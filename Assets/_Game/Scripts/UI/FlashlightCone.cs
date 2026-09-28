using UnityEngine;
using UnityEngine.Rendering;

namespace ByAWhisker.UI
{
    /// <summary>
    /// 경비 손전등의 판정 부채꼴을 바닥에 그린다. 경비의 Flashlight 자식(스포트라이트 + LightSource)에 붙는다.
    ///
    /// 각도도 사거리도 스스로 들고 있지 않다. 전부 같은 오브젝트의 LightSource에서 읽는다.
    /// 숫자를 한 벌 더 두면 언젠가 반드시 어긋나고, 어긋난 그림은 "빔 밖인 줄 알았는데 들킨다"가 된다.
    /// 그때 플레이어는 자기가 뭘 잘못했는지 알 수 없다. 이 컴포넌트가 있는 이유가 그것뿐이다.
    ///
    /// URP 스포트라이트는 그대로 둔다. 조명은 분위기를 맡고, 이 부채꼴이 판정을 맡는다.
    /// 판정은 하지 않는다. LightSource를 읽기만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FlashlightCone : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("각도와 사거리를 읽어 올 곳. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private Perception.LightSource source;

        [Header("모양")]
        [Tooltip("부채꼴을 나눌 조각 수. 적으면 55도 부채꼴의 호가 꺾여 보인다.")]
        [Range(6, 128)]
        [SerializeField] private int segments = 24;

        [Tooltip(
            "부채꼴을 그릴 높이(월드 y). 층이 하나뿐이라 바닥 높이를 하나로 둘 수 있다. " +
            "0으로 두면 바닥과 z 싸움이 나서 빔이 지글거리니 살짝 띄운다. M10의 소리 고리와 같은 이유다.")]
        [SerializeField] private float groundHeight = 0.06f;

        [Header("색")]
        [Tooltip("빔 색. 손전등 조명색과 맞춰 두면 바닥의 부채꼴과 벽에 걸린 빛이 한 물건으로 읽힌다.")]
        [SerializeField] private Color beamColor = new Color(1f, 0.94f, 0.72f, 0.5f);

        [Tooltip("전체 진하기. 경비 열둘이 동시에 그리므로 진하면 화면이 빛으로 덮여 어둠이 뜻을 잃는다.")]
        [Range(0f, 1f)]
        [SerializeField] private float strength = 0.55f;

        [Tooltip(
            "양 옆 각으로 갈수록 옅어지는 정도. 0이면 가장자리가 칼처럼 끊기고, 1이면 끝에서 완전히 사라진다. " +
            "1로 두지 않는 이유는 옆 경계가 안 보이면 판정 경계를 보여 주려던 목적 자체가 없어지기 때문이다.")]
        [Range(0f, 1f)]
        [SerializeField] private float edgeSoftness = 0.55f;

        [Tooltip(
            "사거리 끝에 남길 진하기. Illumination의 밝기는 사거리에서 정확히 0이라 여기도 0이 맞지만, " +
            "0으로 두면 빔이 어디서 끝나는지 눈으로 잴 수 없다. 테두리만 겨우 보일 만큼 남긴다.")]
        [Range(0f, 1f)]
        [SerializeField] private float rimAlpha = 0.12f;

        [Header("그리기")]
        [Tooltip("비우면 코드가 만든다. 프리팹도 애셋도 없이 돌아가야 한다.")]
        [SerializeField] private Material coneMaterial;

        [Tooltip(
            "반투명 큐에 그린다. 못 본 곳을 검게 칠하는 합성 패스가 반투명보다 먼저 돌기 때문에 " +
            "3000 이상이면 어둠을 넘어 보인다. AwarenessGauge와 NoiseRipple이 그렇게 산다.")]
        [SerializeField] private int renderQueue = 3100;

        [Tooltip(
            "깊이 판정을 켤지. 기본은 켠다 — Illumination도 벽에 막히면 어둡다고 보니, 벽을 뚫고 보이는 " +
            "부채꼴은 빛이 없는 자리에 빛을 그리는 거짓말이 된다. AwarenessGauge가 이 필드를 둔 것은 " +
            "게이지가 투시가 되지 않게 하려는 것이고, 여기는 판정과 어긋나지 않게 하려는 것이다.")]
        [SerializeField] private bool depthTest = true;

        // 경비마다 메시는 따로지만 머티리얼은 같은 것을 나눠 쓴다. AwarenessGauge, NoiseRipple과 같은 규칙이다.
        // 큐와 깊이 판정은 공유물의 성질이라 먼저 만드는 쪽의 값으로 정해진다.
        private static Material _sharedMaterial;

        /// 시야 마스크로 자르는 전용 셰이더. 빌드에 들어가도록 Always Included Shaders에 올려 둔다.
        public const string ConeShaderName = "ByAWhisker/FlashlightCone";

        private GameObject _object;
        private MeshRenderer _renderer;
        private Mesh _mesh;

        // 한 번 잡고 계속 재사용한다. 매 프레임 new Mesh는 물론이고 배열도 새로 잡지 않는다.
        private Vector3[] _vertices;
        private Color[] _colors;
        private int[] _triangles;

        private int _segments;

        // 지금 구워 둔 모양. LightSource의 값이 바뀔 때만 정점을 다시 채운다.
        // -1로 시작해서 첫 LateUpdate가 반드시 한 번 채우게 한다.
        private float _sweep = -1f;
        private float _radius = -1f;
        private float _intensity = -1f;

        /// <summary>지금 부채꼴이 떠 있는가. 씬 설정이 어긋났는지 눈으로 보려고 열어 둔다.</summary>
        public bool Visible { get { return _renderer != null && _renderer.enabled; } }

        /// <summary>지금 그려 둔 부채꼴의 각(도). 판정과 같은 값이어야 한다.</summary>
        public float Sweep { get { return _sweep; } }

        /// <summary>지금 그려 둔 부채꼴의 사거리(m). 판정과 같은 값이어야 한다.</summary>
        public float Radius { get { return _radius; } }

        /// <summary>
        /// 읽어 갈 빛을 밖에서 꽂아 준다. 통합 담당이 씬에서 연결하거나 Awake가 알아서 찾는다.
        /// null을 넘긴 자리는 건드리지 않는다.
        /// </summary>
        public void Bind(Perception.LightSource light)
        {
            if (light != null) source = light;
        }

        private void Awake()
        {
            if (source == null) source = GetComponent<Perception.LightSource>();

            BuildMesh();
            BuildObject();
        }

        private void OnDisable()
        {
            // 손전등 오브젝트가 꺼지면 LateUpdate가 멈춘다. 지우지 않으면 마지막 부채꼴이 바닥에 얼어붙는다.
            Show(false);
        }

        private void OnDestroy()
        {
            // 부채꼴은 손전등의 자식이 아니라 씬에 따로 서 있다. 주인이 사라지면 같이 치운다.
            if (_object != null) Destroy(_object);
            _object = null;
            _renderer = null;

            // 메시는 경비마다 하나씩 만든 것이라 우리가 지운다. 머티리얼은 공유물이라 두고 간다.
            if (_mesh != null) Destroy(_mesh);
            _mesh = null;
        }

        /// <summary>
        /// 경비가 움직이고 돈 뒤에 따라붙어야 부채꼴이 한 프레임 밀리지 않는다.
        /// NavMeshAgent가 Update에서 위치와 방향을 바꾸니 여기는 LateUpdate여야 한다.
        /// </summary>
        private void LateUpdate()
        {
            if (_renderer == null) return;

            // 램프처럼 손전등도 꺼진다. 매 프레임 묻는다 — 꺼진 손전등 앞에 부채꼴이 남아 있으면
            // 플레이어는 없는 빛을 피해 돌아가게 된다.
            if (!Lit())
            {
                Show(false);
                return;
            }

            // 각이 0인 LightSource는 사방을 비추는 램프다(Covers가 언제나 참을 준다).
            // 손전등에 붙일 컴포넌트지만 그 경우에도 판정 그대로 꽉 찬 원을 그린다.
            float sweep = source.coneAngle > 0f ? source.coneAngle : 360f;
            float radius = source.radius;

            if (sweep != _sweep || radius != _radius)
            {
                _sweep = sweep;
                _radius = radius;
                FillVertices(sweep, radius);
            }

            if (source.intensity != _intensity)
            {
                _intensity = source.intensity;
                FillColors(_intensity);
            }

            Vector3 center = transform.position;
            // 손전등은 가슴 높이에 있지만 부채꼴은 바닥에 눕는다. 위에서 내려다보는 각이라
            // 바닥에 누운 쪽이 "저기까지 비친다"로 읽히고, 판정도 높이를 따지지 않는다.
            center.y = groundHeight;
            _object.transform.position = center;

            // 메시는 로컬 XZ 평면에 +Z를 가운데로 두고 구워 두었다. 여기서는 돌리기만 한다.
            // 월드 좌표로 매 프레임 정점을 다시 채우는 쪽이 코드는 짧지만, 경비 열둘이 각자
            // 조각 수만큼 sin/cos를 돌고 메시 버퍼를 다시 올린다. 로컬 고정이면 그 일이 아예 없다.
            //
            // forward를 수평으로 눌러 쓰는 것은 LightSource.Covers가 y를 버린 뒤 각을 재기 때문이다.
            // 손전등이 조금이라도 아래를 보고 있으면 기울어진 부채꼴은 판정과 다른 자리를 가리킨다.
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
            {
                _object.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }

            Show(true);
        }

        /// <summary>
        /// 지금 이 빛이 무언가를 비추고 있는가. Illumination이 거르는 조건을 그대로 쓴다.
        /// 컴포넌트를 끄면 LightSource가 제 정적 목록에서 스스로 빠지므로 enabled까지 봐야 조건이 같아진다.
        /// </summary>
        private bool Lit()
        {
            if (source == null) return false;
            if (!source.enabled || !source.IsOn) return false;
            if (source.radius <= 0f || source.intensity <= 0f) return false;
            return true;
        }

        /// <summary>
        /// 부채꼴의 호를 다시 찍는다. 조각 수는 그대로 두므로 삼각형 배열은 Awake에서 한 번 올린 것을 끝까지 쓴다.
        /// 각도나 사거리가 바뀔 때만 불린다 — 평소에는 프레임마다 아무 일도 하지 않는다.
        /// </summary>
        private void FillVertices(float sweep, float radius)
        {
            float half = sweep * 0.5f * Mathf.Deg2Rad;
            float step = half * 2f / _segments;

            // 0번은 손전등이 선 자리. 부채꼴의 꼭짓점이다.
            _vertices[0] = Vector3.zero;

            for (int i = 0; i <= _segments; i++)
            {
                float a = -half + step * i;
                int v = i + 1;

                // +Z가 부채꼴의 가운데다. 눕히고 돌리는 일은 트랜스폼이 맡으므로 y는 늘 0이다.
                _vertices[v].x = Mathf.Sin(a) * radius;
                _vertices[v].y = 0f;
                _vertices[v].z = Mathf.Cos(a) * radius;
            }

            _mesh.SetVertices(_vertices);
        }

        /// <summary>
        /// 정점 색으로 옅어짐을 만든다. 머티리얼 색을 건드리면 경비마다 사본이 하나씩 생긴다.
        ///
        /// 꼭짓점과 호, 두 줄만 있으면 된다. Illumination의 밝기는 거리에 정비례해서 떨어지고
        /// (1 - 거리/사거리), 정점 색은 삼각형 안에서 선형으로 섞이므로 두 줄이 그 감쇠와 정확히 같다.
        /// 중간 고리를 더 넣어도 같은 그림이 나온다.
        /// </summary>
        private void FillColors(float intensity)
        {
            Color color = beamColor;
            float center = Mathf.Clamp01(beamColor.a * strength * Mathf.Clamp01(intensity));

            color.a = center;
            _colors[0] = color;

            for (int i = 0; i <= _segments; i++)
            {
                // 가운데 0, 양 끝 1. 옆으로 갈수록 옅어져야 빔처럼 읽힌다.
                float t = Mathf.Abs((float)i / _segments * 2f - 1f);
                color.a = center * rimAlpha * Mathf.Lerp(1f, 1f - edgeSoftness, t);
                _colors[i + 1] = color;
            }

            _mesh.SetColors(_colors);
        }

        private void BuildMesh()
        {
            _segments = Mathf.Clamp(segments, 6, 128);

            // 꼭짓점 하나 + 호의 정점들.
            int vertexCount = _segments + 2;
            _vertices = new Vector3[vertexCount];
            _colors = new Color[vertexCount];
            _triangles = new int[_segments * 3];

            for (int i = 0; i < _segments; i++)
            {
                int t = i * 3;

                // 꼭짓점에서 호의 두 점으로. 감는 방향은 따지지 않는다. 머티리얼에서 뒷면 잘라내기를 끄기 때문이다.
                _triangles[t] = 0;
                _triangles[t + 1] = i + 1;
                _triangles[t + 2] = i + 2;
            }

            _mesh = new Mesh();
            _mesh.name = "BW_FlashlightCone";
            _mesh.hideFlags = HideFlags.HideAndDontSave;
            // 각도나 사거리가 바뀌면 정점을 다시 올린다고 미리 알려 둔다.
            _mesh.MarkDynamic();

            // 삼각형을 올리려면 정점이 먼저 있어야 한다. 모양은 첫 LateUpdate가 채운다.
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_triangles, 0, false);
        }

        private void BuildObject()
        {
            // 손전등의 자식으로 두면 둘이 문제가 된다. 하나, EnemyVisibility가 경비의 자식 렌더러를
            // 통째로 끄는데 바닥의 빛은 든 사람이 안 보여도 보여야 한다. 둘, 손전등이 아래를 보게
            // 기울면 부채꼴도 같이 기운다. 씬 루트에 세우고 자리와 방향을 여기서 준다.
            _object = new GameObject(name + "_FlashlightCone");
            _object.hideFlags = HideFlags.DontSave;

            MeshFilter filter = _object.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            _renderer = _object.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = ConeMaterial();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _renderer.enabled = false;
        }

        private Material ConeMaterial()
        {
            if (coneMaterial != null) return coneMaterial;

            // 플레이 모드를 나가면 파괴되어 가짜 null이 된다. 그때 다시 만든다.
            if (_sharedMaterial != null) return _sharedMaterial;

            // 전용 셰이더를 먼저 찾는다. 이것만 플레이어 시야 마스크로 부채꼴을 자른다 — 반투명 큐는
            // 어둠 위에 그려지므로, 자르지 않으면 못 보는 곳의 빛이 떠서 벽 너머 경비의 자리를 다 알려 준다.
            // 없으면 Internal-Colored(_ZTest를 밖으로 열어 둔 것)부터 AwarenessGauge와 같은 차례로 내려간다.
            Shader shader = Shader.Find(ConeShaderName);
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            Material material = new Material(shader)
            {
                name = "BW_FlashlightCone",
                hideFlags = HideFlags.HideAndDontSave
            };

            // 셰이더마다 열어 둔 것이 달라서 있는 것만 건드린다. 없는 이름에 값을 넣어도 조용히 무시된다.
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            // 바닥에 누운 면이라 카메라가 돌면 뒷면이 보인다. 잘라내면 그때 부채꼴이 사라진다.
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            if (material.HasProperty("_ZTest"))
            {
                material.SetInt("_ZTest", (int)(depthTest ? CompareFunction.LessEqual : CompareFunction.Always));
            }

            // 어둠을 칠하는 합성 패스는 반투명보다 먼저 돈다. 여기에 그려야 어둠을 넘어 보인다.
            material.renderQueue = renderQueue;

            _sharedMaterial = material;
            return _sharedMaterial;
        }

        private void Show(bool visible)
        {
            if (_renderer == null) return;
            // enabled 대입도 공짜가 아니다. 이미 그 상태면 건드리지 않는다.
            if (_renderer.enabled != visible) _renderer.enabled = visible;
        }
    }
}
