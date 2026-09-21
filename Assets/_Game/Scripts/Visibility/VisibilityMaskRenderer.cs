using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ByAWhisker.Visibility
{
    /// 가시 폴리곤을 위에서 내려다보는 직교 행렬로 마스크 텍스처에 그리고,
    /// 기억 텍스처에 최댓값으로 누적한다. 전역 셰이더 값으로 넘긴다.
    [DisallowMultipleComponent]
    public class VisibilityMaskRenderer : MonoBehaviour
    {
        // 누적 셰이더의 패스 번호. VisionMemoryAccumulate.shader의 패스 순서와 맞춘다.
        const int PassMaskFill = 0;
        const int PassMemoryMax = 1;

        const string AccumulateShaderName = "Hidden/ByAWhisker/VisionMemoryAccumulate";

        static readonly int IdVisionMask = Shader.PropertyToID("_BW_VisionMask");
        static readonly int IdVisionMemory = Shader.PropertyToID("_BW_VisionMemory");
        static readonly int IdVisionBounds = Shader.PropertyToID("_BW_VisionBounds");
        static readonly int IdPrevMemory = Shader.PropertyToID("_BW_PrevMemory");
        static readonly int IdBlitTexture = Shader.PropertyToID("_BlitTexture");
        static readonly int IdBlitScaleBias = Shader.PropertyToID("_BlitScaleBias");

        [Header("참조")]
        [SerializeField] PlayerVision vision;
        [Tooltip("VisionMemoryAccumulate.shader로 만든 머티리얼. 비우면 Shader.Find로 만든다.")]
        [SerializeField] Material accumulateMaterial;

        [Header("해상도")]
        [Tooltip("레벨 1m당 픽셀 수")]
        [SerializeField, Range(1f, 32f)] float pixelsPerMeter = 4f;
        [SerializeField] int maxTextureSize = 2048;

        [Header("기본 범위")]
        [Tooltip("통합 담당이 Configure를 부르지 않을 때 이 값으로 시작한다.")]
        [SerializeField] bool autoConfigureOnStart = true;
        [SerializeField] Vector3 defaultWorldMin = new Vector3(-64f, 0f, -64f);
        [SerializeField] Vector3 defaultWorldSize = new Vector3(128f, 8f, 128f);

        RenderTexture _mask;
        RenderTexture _memory;
        RenderTexture _scratch;   // 핑퐁용 임시 RT. 미리 만들고 재사용한다.
        CommandBuffer _cmd;
        Material _ownedMaterial;  // 우리가 만든 머티리얼만 우리가 지운다.

        Vector4 _worldBounds;
        Matrix4x4 _view;
        Matrix4x4 _proj;
        bool _configured;
        int _lastDrawnFrame = -1;
        bool _subscribed;

        /// 지금 보이는 영역. 흰색이 보이는 곳
        public RenderTexture MaskTexture => _mask;

        /// 본 적 있는 영역의 누적
        public RenderTexture MemoryTexture => _memory;

        /// (minX, minZ, sizeX, sizeZ)
        public Vector4 WorldBounds => _worldBounds;

        /// Configure가 끝나 텍스처가 준비되었는지
        public bool IsConfigured => _configured;

        void Awake()
        {
            _cmd = new CommandBuffer { name = "BW Visibility Mask" };
            if (vision == null) vision = FindAnyObjectByType<PlayerVision>();
        }

        void Start()
        {
            if (!_configured && autoConfigureOnStart)
                Configure(defaultWorldMin, defaultWorldSize);
        }

        void OnEnable()
        {
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void OnDestroy()
        {
            Unsubscribe();
            ReleaseTexture(ref _mask);
            ReleaseTexture(ref _memory);
            ReleaseTexture(ref _scratch);

            if (_cmd != null) { _cmd.Release(); _cmd = null; }
            if (_ownedMaterial != null)
            {
                if (Application.isPlaying) Destroy(_ownedMaterial);
                else DestroyImmediate(_ownedMaterial);
                _ownedMaterial = null;
            }
        }

        /// 레벨 크기에 맞춰 텍스처와 직교 투영 범위를 잡는다. 통합 담당이 시작할 때 부른다.
        public void Configure(Vector3 worldMin, Vector3 worldSize)
        {
            float sizeX = Mathf.Max(0.01f, worldSize.x);
            float sizeZ = Mathf.Max(0.01f, worldSize.z);
            float sizeY = Mathf.Max(0.01f, worldSize.y);

            _worldBounds = new Vector4(worldMin.x, worldMin.z, sizeX, sizeZ);

            int w = Mathf.Clamp(Mathf.CeilToInt(sizeX * pixelsPerMeter), 8, maxTextureSize);
            int h = Mathf.Clamp(Mathf.CeilToInt(sizeZ * pixelsPerMeter), 8, maxTextureSize);

            AllocateTexture(ref _mask, w, h, "BW_VisionMask");
            AllocateTexture(ref _memory, w, h, "BW_VisionMemory");
            AllocateTexture(ref _scratch, w, h, "BW_VisionMemoryScratch");

            // 새로 만든 RT는 내용이 정해지지 않았으므로 한 번 비운다.
            ClearTexture(_mask);
            ClearTexture(_memory);
            ClearTexture(_scratch);

            // 위에서 내려다보는 카메라. 화면 오른쪽이 월드 +X, 화면 위가 월드 +Z가 된다.
            float centerX = worldMin.x + sizeX * 0.5f;
            float centerZ = worldMin.z + sizeZ * 0.5f;
            float top = worldMin.y + sizeY;
            float camHeight = 500f;

            var camPos = new Vector3(centerX, top + camHeight, centerZ);
            var camRot = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            // 유니티 뷰 행렬은 -Z가 앞이므로 z를 뒤집어 역행렬을 만든다.
            _view = Matrix4x4.TRS(camPos, camRot, new Vector3(1f, 1f, -1f)).inverse;

            float far = sizeY + camHeight * 2f;
            // 위아래를 뒤집어 잡는다. 렌더 텍스처에 그린 결과의 v가 월드 +Z 방향과 같아야
            // 합성 셰이더가 월드 XZ를 그대로 UV로 쓸 수 있다.
            var ortho = Matrix4x4.Ortho(-sizeX * 0.5f, sizeX * 0.5f, sizeZ * 0.5f, -sizeZ * 0.5f, 0.01f, far);
            _proj = GL.GetGPUProjectionMatrix(ortho, true);

            _configured = true;
            PushGlobals();
        }

        /// 재시작 등으로 기억을 지울 때 부른다.
        public void ClearMemory()
        {
            ClearTexture(_memory);
            ClearTexture(_scratch);
        }

        void Subscribe()
        {
            if (_subscribed || vision == null) return;
            vision.PolygonUpdated += OnPolygonUpdated;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || vision == null) return;
            vision.PolygonUpdated -= OnPolygonUpdated;
            _subscribed = false;
        }

        // 폴리곤이 갱신된 그 프레임에 바로 그린다. 실행 순서에 기대지 않기 위해서다.
        void OnPolygonUpdated() => RenderMask();

        void LateUpdate()
        {
            if (!_subscribed) Subscribe();
            // 이벤트가 오지 않은 프레임에도 마스크는 매 프레임 갱신한다.
            if (_lastDrawnFrame != Time.frameCount) RenderMask();
        }

        void RenderMask()
        {
            if (!_configured || vision == null || _cmd == null) return;

            var mat = ResolveMaterial();
            if (mat == null) return;

            var mesh = vision.PolygonMesh;
            _lastDrawnFrame = Time.frameCount;

            _cmd.Clear();

            // 1) 마스크는 매 프레임 지우고 폴리곤을 흰색으로 다시 그린다.
            _cmd.SetRenderTarget(_mask);
            _cmd.ClearRenderTarget(false, true, Color.black);
            if (mesh != null && mesh.vertexCount > 0)
            {
                _cmd.SetViewProjectionMatrices(_view, _proj);
                // 폴리곤 정점은 Origin 기준 로컬이다.
                _cmd.DrawMesh(mesh, Matrix4x4.Translate(vision.Origin), mat, 0, PassMaskFill);
            }

            // 2) 기억은 지우지 않고 max로 누적한다. 임시 RT와 핑퐁한다.
            _cmd.SetGlobalTexture(IdBlitTexture, _mask);
            _cmd.SetGlobalVector(IdBlitScaleBias, new Vector4(1f, 1f, 0f, 0f));
            _cmd.SetGlobalTexture(IdPrevMemory, _memory);
            _cmd.SetRenderTarget(_scratch);
            _cmd.DrawProcedural(Matrix4x4.identity, mat, PassMemoryMax, MeshTopology.Triangles, 3, 1);

            Graphics.ExecuteCommandBuffer(_cmd);

            // 결과가 들어간 임시 RT가 새 기억이 된다.
            var swap = _memory;
            _memory = _scratch;
            _scratch = swap;

            PushGlobals();
        }

        void PushGlobals()
        {
            Shader.SetGlobalTexture(IdVisionMask, _mask);
            Shader.SetGlobalTexture(IdVisionMemory, _memory);
            Shader.SetGlobalVector(IdVisionBounds, _worldBounds);
        }

        Material ResolveMaterial()
        {
            if (accumulateMaterial != null) return accumulateMaterial;
            if (_ownedMaterial != null) return _ownedMaterial;

            var shader = Shader.Find(AccumulateShaderName);
            if (shader == null)
            {
                Debug.LogError($"[VisibilityMaskRenderer] 셰이더를 찾지 못했다: {AccumulateShaderName}. " +
                               "머티리얼을 만들어 accumulateMaterial에 넣어라.", this);
                enabled = false;
                return null;
            }

            _ownedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return _ownedMaterial;
        }

        void AllocateTexture(ref RenderTexture rt, int width, int height, string name)
        {
            if (rt != null && rt.width == width && rt.height == height) return;
            ReleaseTexture(ref rt);

            var format = GraphicsFormat.R8_UNorm;
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render))
                format = GraphicsFormat.R8G8B8A8_UNorm;

            var desc = new RenderTextureDescriptor(width, height, format, 0)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false
            };

            rt = new RenderTexture(desc)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            rt.Create();
        }

        static void ClearTexture(RenderTexture rt)
        {
            if (rt == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = prev;
        }

        static void ReleaseTexture(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            if (Application.isPlaying) Destroy(rt);
            else DestroyImmediate(rt);
            rt = null;
        }
    }
}
