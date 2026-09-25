using UnityEngine;
using UnityEngine.Rendering;
using ByAWhisker.AI;
using ByAWhisker.Combat;

namespace ByAWhisker.UI
{
    /// <summary>
    /// 경비 머리 위에 의심도가 부채꼴로 차오른다. 0이면 아무것도 없고, 차오를수록 각이 넓어지고 색이 바뀐다.
    /// 들키는 것이 사고가 아니라 예고된 결과여야 숨는 놀이가 된다. 화면 구석의 목록으로는
    /// "저 경비가 나를 의심한다"가 읽히지 않아서 월드에 띄운다.
    ///
    /// 판정은 하지 않는다. GuardPerception.Awareness를 읽기만 한다.
    /// 문턱은 GuardBrain의 상태 문턱(Suspicious 0.3 / Search 0.6)과 맞춰 둔다.
    /// 색이 바뀌는 자리와 경비가 움직이기 시작하는 자리가 어긋나면 게이지가 거짓말을 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class AwarenessGauge : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("비우면 같은 오브젝트에서, 없으면 부모까지 올라가서 찾는다.")]
        [SerializeField] private GuardPerception perception;

        [Tooltip("쓰러졌는지 볼 쪽. 비우면 부모까지 올라가서 찾는다.")]
        [SerializeField] private Damageable body;

        [Tooltip("빌보드가 바라볼 카메라. 비우면 Camera.main을 한 번만 찾는다.")]
        [SerializeField] private Camera viewCamera;

        [Header("자리")]
        [Tooltip("머리 위로 띄우는 높이(m). 몸에 겹치면 게이지인지 장식인지 읽히지 않는다.")]
        [SerializeField] private float heightOffset = 2.1f;

        [Tooltip("여기에 트랜스폼을 꽂으면 그 자리에 뜬다. 비우면 경비 위치에 heightOffset을 더한다.")]
        [SerializeField] private Transform anchor;

        [Header("모양")]
        [Tooltip("의심도 1일 때의 각(도). 360이면 꽉 찬 고리가 된다.")]
        [Range(30f, 360f)]
        [SerializeField] private float sweepDegrees = 360f;

        [Tooltip("안쪽 반지름(m). 0으로 두면 원판이 되는데, 도넛 쪽이 얼마나 찼는지 한눈에 읽힌다.")]
        [SerializeField] private float innerRadius = 0.22f;

        [Tooltip("바깥쪽 반지름(m).")]
        [SerializeField] private float outerRadius = 0.35f;

        [Tooltip("부채꼴을 나눌 조각 수. 적으면 다 찬 고리가 다각형으로 보인다.")]
        [Range(6, 128)]
        [SerializeField] private int segments = 48;

        [Header("색")]
        [Tooltip("아직 낌새만 챈 상태. 희미해야 열둘이 동시에 떠도 화면을 덮지 않는다.")]
        [SerializeField] private Color calmColor = new Color(0.86f, 0.90f, 0.94f, 0.35f);

        [Tooltip("의심하기 시작한 상태. GuardBrain이 Suspicious로 넘어가는 지점이다.")]
        [SerializeField] private Color warnColor = new Color(1f, 0.84f, 0.30f, 0.80f);

        [Tooltip("뒤지러 오는 상태. AimTelegraph의 '이제 쏜다' 색과 맞춰 둬야 같은 위험으로 읽힌다.")]
        [SerializeField] private Color alertColor = new Color(1f, 0.32f, 0.24f, 1f);

        [Tooltip("여기서 노랑으로 바뀐다. GuardBrain의 suspiciousThreshold와 같아야 한다.")]
        [Range(0f, 1f)]
        [SerializeField] private float warnThreshold = 0.3f;

        [Tooltip("여기서 빨강으로 바뀐다. GuardBrain의 searchThreshold와 같아야 한다.")]
        [Range(0f, 1f)]
        [SerializeField] private float alertThreshold = 0.6f;

        [Header("솎아내기")]
        [Tooltip("이보다 낮은 의심도면 아예 끈다. 경비가 열둘이라 늘 떠 있으면 화면이 지저분하다.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float hideBelow = 0.02f;

        [Header("그리기")]
        [Tooltip("비우면 코드가 만든다. 프리팹도 애셋도 없이 돌아가야 한다.")]
        [SerializeField] private Material gaugeMaterial;

        [Tooltip(
            "반투명 큐에 그린다. 어둠을 칠하는 합성 패스가 반투명보다 먼저 돌기 때문에 " +
            "3000 이상이면 어둠을 넘어 보인다. M10의 소리 고리가 그렇게 산다.")]
        [SerializeField] private int renderQueue = 3100;

        [Tooltip(
            "깊이 판정을 켤지. 켜면 벽 뒤 경비의 게이지는 벽에 가린다. 끄면 벽을 뚫고 다 보이는데, " +
            "그러면 게이지가 의심의 표시가 아니라 경비 위치를 알려 주는 투시가 된다. 기본은 켠다.")]
        [SerializeField] private bool depthTest = true;

        // 경비마다 메시는 따로지만 머티리얼은 같은 것을 나눠 쓴다. AimTelegraph, NoiseRipple과 같은 규칙이다.
        // 큐와 깊이 판정은 공유물의 성질이라 먼저 만드는 쪽의 값으로 정해진다.
        private static Material _sharedMaterial;

        private GameObject _object;
        private MeshRenderer _renderer;
        private Mesh _mesh;

        // 한 번 잡고 계속 재사용한다. 매 프레임 new Mesh는 물론이고 배열도 새로 잡지 않는다.
        private Vector3[] _vertices;
        private Color[] _colors;
        private int[] _triangles;

        private Camera _camera;
        private int _segments;

        // 지금 칠해 둔 색. 문턱을 넘을 때만 정점 색을 다시 올린다.
        private Color _color;
        private bool _colorSet;

        /// <summary>지금 게이지가 떠 있는가. 씬 설정이 어긋났는지 눈으로 보려고 열어 둔다.</summary>
        public bool Visible { get { return _renderer != null && _renderer.enabled; } }

        /// <summary>
        /// 무엇을 보여 줄지 밖에서 꽂아 준다. 통합 담당이 씬에서 연결하거나 Awake가 알아서 찾는다.
        /// null을 넘긴 자리는 건드리지 않는다. 셋 중 하나만 바꿔 끼울 수 있어야 한다.
        /// </summary>
        public void Bind(GuardPerception target, Damageable owner, Camera view)
        {
            if (target != null) perception = target;
            if (owner != null) body = owner;
            if (view != null)
            {
                viewCamera = view;
                _camera = view;
            }
        }

        private void Awake()
        {
            if (perception == null) perception = GetComponent<GuardPerception>();
            if (perception == null) perception = GetComponentInParent<GuardPerception>();

            // 콜라이더나 감각이 자식에 달려 있을 수 있으니 부모까지 올라가서 찾는다. GuardPerception이 하는 것과 같다.
            if (body == null) body = GetComponentInParent<Damageable>();

            _camera = viewCamera;

            BuildMesh();
            BuildObject();
        }

        private void Start()
        {
            // 카메라는 여기서 한 번만 찾는다. 매 프레임 Camera.main을 묻지 않는다.
            // Awake가 아니라 Start인 이유는 부트스트랩이 카메라를 세운 뒤여야 찾히기 때문이다.
            if (_camera == null) _camera = Camera.main;
        }

        private void OnDisable()
        {
            // 기절하면 두뇌가 이 컴포넌트까지 끈다. 지우지 않으면 마지막 의심도가 머리 위에 얼어붙는다.
            Show(false);
        }

        private void OnDestroy()
        {
            // 게이지는 경비의 자식이 아니라 씬에 따로 서 있다. 주인이 사라지면 같이 치운다.
            if (_object != null) Destroy(_object);
            _object = null;
            _renderer = null;

            // 메시는 경비마다 하나씩 만든 것이라 우리가 지운다. 머티리얼은 공유물이라 두고 간다.
            if (_mesh != null) Destroy(_mesh);
            _mesh = null;
        }

        /// <summary>
        /// 경비가 움직인 뒤에 따라붙어야 게이지가 한 프레임 밀리지 않는다.
        /// NavMeshAgent는 Update에서 위치를 바꾸니 여기는 LateUpdate여야 한다.
        /// </summary>
        private void LateUpdate()
        {
            if (_renderer == null) return;

            float aware = perception != null ? Mathf.Clamp01(perception.Awareness) : 0f;

            // 쓰러진 경비의 남은 의심도가 떠 있으면 시체가 아직 나를 보는 것처럼 읽힌다.
            if (aware <= hideBelow || _camera == null || IsDown())
            {
                Show(false);
                return;
            }

            Vector3 center = anchor != null
                ? anchor.position
                : transform.position + Vector3.up * heightOffset;

            _object.transform.position = center;

            // 카메라를 정면으로 보게 세운다. 바닥에 눕히지 않는 이유가 둘이다.
            // 하나, 쿼터뷰라 카메라가 52도쯤 내려다보는데 누운 고리는 그만큼 눌려서 찬 각을 읽기 어렵다.
            // 둘, 바닥에는 이미 소리 고리와 냄새가 깔려 있어서 거기에 또 그리면 무엇이 무엇인지 섞인다.
            // LookAt이 아니라 카메라의 회전을 그대로 쓴다. 그래야 화면 가장자리의 경비도 기울지 않고,
            // 카메라가 90도씩 돌아도 게이지 모양은 늘 같게 읽힌다.
            _object.transform.rotation = _camera.transform.rotation;

            FillMesh(aware);
            ApplyColor(aware);
            Show(true);
        }

        /// <summary>쓰러졌는가. 총에 맞아 죽었든 제압당해 기절했든 게이지를 띄울 일은 없다.</summary>
        private bool IsDown()
        {
            return body != null && (!body.IsAlive || body.IsDown);
        }

        /// <summary>
        /// 채워진 각만큼 부채꼴을 다시 만든다. 정점 수는 그대로 두고 조각을 채워진 각에 전부 나눠 담는다.
        /// 각이 줄 때 조각을 빼는 쪽이 자연스러워 보이지만, 그러면 삼각형 배열을 매번 다시 올려야 한다.
        /// 각만 줄이면 인덱스는 Awake에서 한 번 올린 것을 끝까지 쓴다.
        /// </summary>
        private void FillMesh(float amount)
        {
            float filled = sweepDegrees * Mathf.Deg2Rad * amount;
            float step = filled / _segments;

            float inner = Mathf.Min(innerRadius, outerRadius);
            float outer = Mathf.Max(innerRadius, outerRadius);

            for (int i = 0; i <= _segments; i++)
            {
                // 12시에서 시작해 시계 방향으로 감는다. 시계처럼 돌아야 얼마나 찼는지 바로 읽힌다.
                float a = Mathf.PI * 0.5f - step * i;
                float c = Mathf.Cos(a);
                float s = Mathf.Sin(a);

                int v = i * 2;
                // 메시는 로컬 공간의 XY 평면에 눕혀 둔다. 카메라를 보는 일은 트랜스폼이 맡는다.
                _vertices[v].x = c * inner;
                _vertices[v].y = s * inner;
                _vertices[v].z = 0f;

                _vertices[v + 1].x = c * outer;
                _vertices[v + 1].y = s * outer;
                _vertices[v + 1].z = 0f;
            }

            _mesh.SetVertices(_vertices);
        }

        /// <summary>
        /// 문턱을 넘을 때만 정점 색을 올린다. 매 프레임 올리면 조각 수만큼 색을 다시 밀어 넣는 셈이다.
        /// 머티리얼 색이 아니라 정점 색을 쓴다. 머티리얼을 건드리면 경비마다 사본이 하나씩 생긴다.
        /// </summary>
        private void ApplyColor(float amount)
        {
            Color next = calmColor;
            if (amount >= alertThreshold) next = alertColor;
            else if (amount >= warnThreshold) next = warnColor;

            if (_colorSet && next == _color) return;

            _color = next;
            _colorSet = true;

            for (int i = 0; i < _colors.Length; i++) _colors[i] = next;
            _mesh.SetColors(_colors);
        }

        private void BuildMesh()
        {
            _segments = Mathf.Clamp(segments, 6, 128);

            int vertexCount = (_segments + 1) * 2;
            _vertices = new Vector3[vertexCount];
            _colors = new Color[vertexCount];
            _triangles = new int[_segments * 6];

            for (int i = 0; i < _segments; i++)
            {
                int v = i * 2;
                int t = i * 6;

                // 안쪽/바깥쪽 정점이 번갈아 들어 있으니 네 점으로 띠 한 칸을 만든다.
                // 감는 방향은 따지지 않는다. 머티리얼에서 뒷면 잘라내기를 끄기 때문이다.
                _triangles[t] = v;
                _triangles[t + 1] = v + 1;
                _triangles[t + 2] = v + 3;
                _triangles[t + 3] = v;
                _triangles[t + 4] = v + 3;
                _triangles[t + 5] = v + 2;
            }

            _mesh = new Mesh();
            _mesh.name = "BW_AwarenessGauge";
            _mesh.hideFlags = HideFlags.HideAndDontSave;
            // 정점이 매 프레임 바뀐다고 미리 알려 둔다.
            _mesh.MarkDynamic();

            // 삼각형을 올리려면 정점이 먼저 있어야 한다. 모양은 첫 LateUpdate가 채운다.
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_triangles, 0, false);
        }

        private void BuildObject()
        {
            // 경비의 자식으로 두면 EnemyVisibility가 자식 렌더러를 통째로 끈다.
            // 경비 몸이 안 보이는 순간에도 의심은 보여야 경고 구실을 하니 씬 루트에 따로 세운다.
            // AimTelegraph가 겨누는 선을 따로 세우는 것과 같은 이유다.
            _object = new GameObject(name + "_AwarenessGauge");
            _object.hideFlags = HideFlags.DontSave;

            MeshFilter filter = _object.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            _renderer = _object.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = GaugeMaterial();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _renderer.enabled = false;
        }

        private Material GaugeMaterial()
        {
            if (gaugeMaterial != null) return gaugeMaterial;

            // 플레이 모드를 나가면 파괴되어 가짜 null이 된다. 그때 다시 만든다.
            if (_sharedMaterial != null) return _sharedMaterial;

            // Internal-Colored를 먼저 찾는 이유는 이것만 _ZTest를 밖으로 열어 두기 때문이다.
            // 벽에 가릴지 말지를 고를 수 있어야 한다. 없으면 NoiseRipple과 같은 차례로 내려간다.
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            Material material = new Material(shader)
            {
                name = "BW_AwarenessGauge",
                hideFlags = HideFlags.HideAndDontSave
            };

            // 셰이더마다 열어 둔 것이 달라서 있는 것만 건드린다. 없는 이름에 값을 넣어도 조용히 무시된다.
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            // 빌보드라 카메라 쪽을 보고 있지만, 카메라가 돌아가는 중에는 뒷면이 잠깐 비친다.
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
