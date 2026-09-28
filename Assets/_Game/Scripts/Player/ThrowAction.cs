using UnityEngine;
using UnityEngine.Rendering;
using ByAWhisker.Core;
using ByAWhisker.Senses;

namespace ByAWhisker.Player
{
    /// <summary>
    /// 돌을 던진다. 플레이어에 붙는다.
    ///
    /// 이 게임에서 경비를 옮기는 첫 수단이다. 총은 경비를 지우지만 자리를 알리고 시체를 남긴다.
    /// 돌은 아무것도 지우지 못하는 대신 경비를 내가 고른 자리로 불러낸다.
    /// 그래서 값이 있어야 한다 — 개수가 유한하고, 한 번에 하나만 날며, 다 쓰면 다시 총뿐이다.
    ///
    /// 겨누는 방향은 <see cref="PlayerCombat.AimDirection"/>을 읽어 쓴다. 총과 다른 곳을 보면
    /// 플레이어가 겨냥을 두 번 배워야 한다. 대신 거리는 고정이다 — 커서까지의 거리는
    /// PlayerCombat이 내놓지 않고, 그 파일은 우리 담당이 아니다. 늘 같은 거리로 날아가는 쪽이
    /// 오히려 배우기 쉽다: 한 번 익히면 어디까지 닿는지 눈대중이 된다.
    ///
    /// 입력은 이 컴포넌트가 직접 구독한다. PlayerCombat이 총과 제압을 잇는 것처럼 이어 주면
    /// 좋겠지만 그 파일을 고칠 수 없어서, 던지기만은 스스로 <see cref="PlayerInputReader"/>를 듣는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThrowAction : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayerInputReader input;

        [Tooltip("겨누는 방향을 읽어 올 쪽. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayerCombat combat;

        [Tooltip("앉았는지 볼 쪽. 손 높이를 낮추는 데만 쓴다. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayerStance stance;

        [Header("개수")]
        [Tooltip("판을 시작할 때 들고 있는 개수. 다 쓰면 못 던진다. RunReset에서 이 값으로 되돌아간다.")]
        [SerializeField] private int stoneCount = 3;

        [Header("날아가는 것")]
        [Tooltip("손을 떠나는 높이(m). 총구보다 조금 높다. 낮은 엄폐 뒤에서 던지면 엄폐에 맞아야 한다.")]
        [SerializeField] private float handHeight = 1.35f;

        [Tooltip("앉았을 때 손 높이에 곱할 값. 총구가 낮아지는 것과 같은 이유다.")]
        [Range(0.2f, 1f)]
        [SerializeField] private float crouchHandScale = 0.6f;

        [Tooltip("수평으로 나아가는 속도(m/s). 느리면 던진 뒤 경비가 오기 전에 자리를 옮길 틈이 생긴다.")]
        [SerializeField] private float throwSpeed = 13f;

        [Tooltip("막히지 않으면 이만큼 날아가 떨어진다(m). 이 거리가 곧 이 도구의 사거리다.")]
        [SerializeField] private float throwDistance = 13f;

        [Tooltip("포물선의 높이(m). 0이면 직선으로 난다. 살짝 띄우면 던진 것으로 읽힌다.")]
        [SerializeField] private float arcHeight = 1.5f;

        [Tooltip("떨어진 자리에서 바닥에 띄우는 높이(m). 0이면 바닥과 z 싸움이 난다.")]
        [SerializeField] private float groundClearance = 0.12f;

        [Header("부딪힘")]
        [Tooltip("돌을 막는 것이 있는 레이어. 벽과 지형과 엄폐물. 사람은 넣지 않는다 — 돌은 사람을 뚫고 지나가도 된다.")]
        [SerializeField] private LayerMask blockingLayers;

        [Tooltip("벽에 걸리는지 볼 때 쓰는 반지름(m). 돌의 두께쯤이면 된다.")]
        [SerializeField] private float stoneRadius = 0.1f;

        [Header("소리")]
        [Tooltip(
            "떨어질 때 나는 소리 크기. 0..1. 걷는 발소리(0.4)보다 크고 총성(1)보다 작아야 " +
            "도구가 된다. 더 크면 총 대신 쓰는 경보기가 되고, 더 작으면 아무도 오지 않는다.")]
        [Range(0f, 1f)]
        [SerializeField] private float noiseLoudness = 0.6f;

        [Tooltip(
            "이 거리 안에서 들린다(m). 발소리 약 5m와 총성 12m 사이에 둔다. " +
            "경비의 귀는 약해서(hearingMultiplier 0.6) 실제로 알아채는 거리는 이것의 6할쯤이다.")]
        [SerializeField] private float noiseRadius = 9f;

        [Header("보이기")]
        [Tooltip("돌의 크기(m). 반지름이다. 쿼터뷰에서 눈에 걸릴 만큼만.")]
        [SerializeField] private float stoneSize = 0.14f;

        [Tooltip("돌의 색. 어두운 바닥 위에서 읽혀야 하니 밝은 쪽으로 둔다.")]
        [SerializeField] private Color stoneColor = new Color(0.94f, 0.90f, 0.74f, 1f);

        [Tooltip("비우면 코드가 만든다. 프리팹도 애셋도 없이 돌아가야 한다.")]
        [SerializeField] private Material stoneMaterial;

        [Tooltip(
            "반투명 큐에 그린다. 못 본 곳을 검게 칠하는 합성 패스가 반투명보다 먼저 돌기 때문에 " +
            "3000 이상이면 어둠을 넘어 보인다. NoiseRipple과 AwarenessGauge가 그렇게 산다.")]
        [SerializeField] private int renderQueue = 3100;

        /// <summary>
        /// 한 번에 걸릴 수 있는 콜라이더 수. 던지는 자리가 제 몸 안이라 자기 콜라이더가 먼저
        /// 잡히는 일이 흔하다. Weapon.TraceShot과 같은 까닭으로 가장 가까운 하나만 봐서는 안 된다.
        /// </summary>
        private const int MaxHits = 4;

        // 매 프레임 새로 잡지 않으려고 들고 있는다. NonAlloc 스피어캐스트가 여기에 채운다.
        private readonly RaycastHit[] _hits = new RaycastHit[MaxHits];

        // 돌은 하나를 만들어 껐다 켜며 쓴다. 던질 때마다 만들면 판마다 쓰레기가 쌓인다.
        // 씬 루트에 세우는 이유는 플레이어의 자식이면 던진 돌이 플레이어를 따라 움직이기 때문이다.
        private GameObject _stone;
        private MeshRenderer _renderer;
        private Mesh _mesh;

        // 머티리얼은 색을 들고 있지 않아서(색은 정점에 굽는다) 여러 판이 같은 것을 나눠 쓸 수 있다.
        // NoiseRipple, AwarenessGauge와 같은 규칙이다.
        private static Material _sharedMaterial;

        private ByAWhisker.UI.ControlsOverlay _overlay;

        private int _remaining;
        private bool _inFlight;

        // 날아가는 중에만 뜻이 있는 값들. 궤적은 출발점과 방향과 지나온 거리로만 정해진다.
        private Vector3 _origin;
        private Vector3 _direction;
        private float _travelled;
        private float _startY;
        private float _groundY;

        /// <summary>남은 개수. 화면 표시가 읽어 간다.</summary>
        public int Remaining { get { return _remaining; } }

        /// <summary>
        /// 지금 던질 수 있는가. 남은 것이 있고, 앞서 던진 것이 이미 떨어져 있어야 한다.
        /// </summary>
        public bool CanThrow { get { return _remaining > 0 && !_inFlight; } }

        /// <summary>돌이 날아가는 중인가. 화면 표시가 손을 비워 두는 데 쓸 수 있다.</summary>
        public bool InFlight { get { return _inFlight; } }

        /// <summary>
        /// 한 개를 던진다. 던지지 못하는 상태면 아무 일도 하지 않는다.
        /// 실패를 소리 없이 넘기는 것은 이 함수의 몫이고, 왜 안 됐는지 알리는 일은 입력 쪽에서 한다.
        /// </summary>
        public void Throw()
        {
            if (!CanThrow) return;

            Vector3 aim = combat != null ? combat.AimDirection : transform.forward;
            aim.y = 0f;  // 층이 하나라 돌도 수평으로만 날아간다. 총알과 같은 규칙이다.
            if (aim.sqrMagnitude < 0.0001f) aim = transform.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 0.0001f) return;  // 겨눌 곳이 없으면 던질 곳도 없다.

            _remaining--;

            _direction = aim.normalized;
            _startY = transform.position.y + HandHeight();
            _groundY = transform.position.y + Mathf.Max(0f, groundClearance);
            _origin = transform.position;
            _origin.y = _startY;
            _travelled = 0f;
            _inFlight = true;

            if (_stone != null) _stone.transform.position = _origin;
            Show(true);
        }

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (combat == null) combat = GetComponent<PlayerCombat>();
            if (stance == null) stance = GetComponent<PlayerStance>();

            _remaining = Mathf.Max(0, stoneCount);
            BuildStone();
        }

        private void OnEnable()
        {
            GameEvents.RunReset += HandleRunReset;
            if (input != null) input.ThrowRequested += HandleThrowRequested;
        }

        private void OnDisable()
        {
            GameEvents.RunReset -= HandleRunReset;
            if (input != null) input.ThrowRequested -= HandleThrowRequested;

            // 꺼지면 Update가 멈춘다. 접어 두지 않으면 날아가던 돌이 그 자리에 얼어붙는다.
            // 소리는 내지 않는다. 잡히거나 재시작해서 꺼진 것을 경비가 들을 이유가 없다.
            Cancel();
        }

        private void OnDestroy()
        {
            // 돌과 메시는 씬 루트에 따로 서 있다. 주인이 사라지면 같이 치운다.
            if (_stone != null) Destroy(_stone);
            if (_mesh != null) Destroy(_mesh);

            _stone = null;
            _renderer = null;
            _mesh = null;
        }

        private void Update()
        {
            // 날아가는 중이 아니면 이 컴포넌트는 매 프레임 아무 일도 하지 않는다.
            if (!_inFlight) return;
            if (_stone == null)
            {
                Cancel();
                return;
            }

            float total = Mathf.Max(0.01f, throwDistance);

            float step = Mathf.Max(0.01f, throwSpeed) * Time.deltaTime;
            float left = total - _travelled;
            if (step > left) step = left;

            Vector3 from = _stone.transform.position;
            _travelled += step;
            Vector3 to = PositionAt(_travelled, total);

            // 한 프레임에 옮겨 가는 거리가 돌보다 크면 얇은 벽을 그냥 통과한다.
            // 그래서 점 검사가 아니라 지나갈 선분을 훑는다.
            Vector3 delta = to - from;
            float span = delta.magnitude;
            if (span > 0.0001f)
            {
                Vector3 hitPoint;
                if (Sweep(from, delta / span, span, out hitPoint))
                {
                    Land(hitPoint);
                    return;
                }
            }

            _stone.transform.position = to;

            // 여기까지 왔으면 막힌 것 없이 끝까지 날아간 것이다.
            if (_travelled >= total - 0.0001f) Land(to);
        }

        /// <summary>
        /// 지나온 거리로 자리를 낸다. 높이는 손 높이에서 바닥까지 내려가는 선 위에 포물선을 얹는다.
        /// 물리로 던지지 않는 이유는 Rigidbody를 하나 들이면 그것만 다른 규칙으로 움직여서,
        /// 어디까지 날아가는지를 플레이어가 예측할 수 없게 되기 때문이다.
        /// </summary>
        private Vector3 PositionAt(float distance, float total)
        {
            Vector3 point = _origin + _direction * distance;

            float t = Mathf.Clamp01(distance / total);
            // 4t(1-t)는 양 끝이 0이고 가운데가 1인 포물선이다. 손에서 떠날 때와 닿을 때가 선 위에 놓인다.
            point.y = Mathf.Lerp(_startY, _groundY, t) + arcHeight * 4f * t * (1f - t);
            return point;
        }

        /// <summary>
        /// from에서 span만큼 나아가는 사이에 막는 것이 있으면 그 자리를 내놓는다.
        /// 자기 콜라이더는 건너뛴다 — 던지는 자리가 제 몸 안이라 첫 프레임이 늘 자기에게 걸린다.
        /// </summary>
        private bool Sweep(Vector3 from, Vector3 direction, float span, out Vector3 hitPoint)
        {
            hitPoint = from;

            float radius = Mathf.Max(0.01f, stoneRadius);
            int count = Physics.SphereCastNonAlloc(
                from, radius, direction, _hits, span, blockingLayers.value, QueryTriggerInteraction.Ignore);
            if (count <= 0) return false;

            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider hit = _hits[i].collider;
                if (hit == null) continue;
                if (hit.transform.IsChildOf(transform)) continue;

                if (_hits[i].distance >= bestDistance) continue;
                bestDistance = _hits[i].distance;
                best = i;
            }

            if (best < 0) return false;

            // 벽 속에 박힌 자리에 두면 돌이 벽에 반쯤 먹힌 채로 보인다. 반지름만큼 물러선다.
            // 시작하는 자리가 이미 벽 안이면 distance가 0으로 오는데, 그때는 뒤로 물러서지 않는다.
            float back = Mathf.Max(0f, bestDistance - radius);
            hitPoint = from + direction * back;
            return true;
        }

        /// <summary>
        /// 떨어졌다. 여기서만 소리가 난다 — 날아가는 동안 조용한 것이 이 도구의 요점이다.
        /// 소리가 던진 자리에서 나면 경비가 내 쪽으로 오고, 그러면 돌은 총보다 나쁜 물건이 된다.
        /// </summary>
        private void Land(Vector3 at)
        {
            _inFlight = false;
            _travelled = 0f;

            at.y = _groundY;
            if (_stone != null) _stone.transform.position = at;

            // 떨어진 돌은 계속 바닥에 보일 만큼 중요한 물건이 아니다. 소리가 정보고 돌은 그 전달자다.
            // 남겨 두면 다음에 던질 돌과 같은 오브젝트라 어차피 자리를 옮겨야 한다.
            Show(false);

            EmitLandingNoise(at);
        }

        /// <summary>
        /// 날아가던 것을 없던 일로 한다. 소리를 내지 않는 것이 Land와의 차이다.
        /// 개수는 되돌리지 않는다 — 재시작이면 RunReset이 어차피 전부 채운다.
        /// </summary>
        private void Cancel()
        {
            _inFlight = false;
            _travelled = 0f;
            Show(false);
        }

        /// <summary>
        /// 떨어지는 소리. <see cref="NoiseKind"/>는 있는 것 중 <see cref="NoiseKind.Object"/>를 쓴다.
        ///
        /// Gunshot은 안 된다. GuardBrain이 총성을 "내가 사격받는 중"으로 읽어서 경비들이 쏜 사람도
        /// 없는데 엄폐물로 흩어진다. 돌은 경비를 그 자리로 불러내야 하는데 정반대가 된다.
        /// GuardPerception도 총성만은 의심도를 단번에 끝까지 올려서, 뒤지러 오는 대신 바로 발각이 된다.
        /// Footstep은 SenseHud와 NoiseRipple이 발소리 색으로 칠해서 내 발소리와 구별이 안 된다.
        /// Bump는 제압당한 몸이 바닥에 닿는 소리로 이미 쓰고 있어 색이 겹친다.
        /// Object는 GuardBrain의 외침이 쓰는 것과 같고, 듣는 쪽에서 크기만큼 의심도를 올려
        /// 경비가 소리 난 자리로 찾아오게 한다. 그게 정확히 돌이 해야 하는 일이다.
        /// </summary>
        private void EmitLandingNoise(Vector3 at)
        {
            float loudness = Mathf.Clamp01(noiseLoudness);
            if (loudness <= 0f) return;

            NoiseEvent evt;
            evt.position = at;
            evt.radius = Mathf.Max(0f, noiseRadius);
            evt.loudness = loudness;
            evt.kind = NoiseKind.Object;
            // 소리의 주인은 돌이다. 플레이어를 적으면 SenseHud가 "내가 낸 소리"로 보고 호를 지워서,
            // 돌이 어디에 떨어졌는지가 화면에 남지 않는다. 돌은 씬 루트에 따로 서 있어서 걸러지지 않는다.
            evt.source = _stone != null ? _stone : gameObject;
            evt.time = Time.time;

            NoiseBus.Emit(evt);
        }

        /// <summary>
        /// 날아가는 중에 또 누르면 말없이 무시한다. 앞서 던진 것을 중간에 떨어뜨리면
        /// 개수만 줄고 소리는 엉뚱한 자리에서 나고, 둘을 함께 띄우려면 돌 오브젝트를 늘려야 한다.
        /// 날아가는 시간(약 1초)이 그대로 다음 던지기까지의 간격이 되므로 따로 쿨다운을 둘 것도 없다.
        /// </summary>
        private void HandleThrowRequested()
        {
            if (_inFlight) return;

            if (!CanThrow)
            {
                // 다 썼다는 것을 알려 준다. 안 그러면 키가 먹은 건지 개수가 없는 건지 알 수 없다.
                Flash("던질 것이 없다");
                return;
            }

            Throw();

            // 키를 누른 순간에만 도는 자리라 여기 생기는 문자열은 매 프레임 할당이 아니다.
            Flash("돌을 던졌다. 남은 " + _remaining);
        }

        /// <summary>재시작이면 개수가 처음으로 돌아간다. 날아가던 것은 소리 없이 접는다.</summary>
        private void HandleRunReset()
        {
            Cancel();
            _remaining = Mathf.Max(0, stoneCount);
        }

        /// <summary>
        /// 앉아도 일어서지 않는다. 앉은 채로 던지게 두는 쪽을 골랐다 —
        /// 던지기는 경비를 피하려고 쓰는 것인데, 쓰는 순간 일어서면 그 경비에게 들킨다.
        /// 즉 "쓸 수 있을 때는 쓸 필요가 없고, 쓰고 싶을 때는 못 쓴다"가 되어 도구가 죽는다.
        /// 대신 손 높이만 낮춰서 낮은 엄폐 뒤에서 던지면 엄폐에 맞게 한다. 그것이 앉은 값이다.
        /// </summary>
        private float HandHeight()
        {
            bool crouching = stance != null && stance.IsCrouching;
            return crouching ? handHeight * Mathf.Clamp01(crouchHandScale) : handHeight;
        }

        private void Flash(string message)
        {
            if (_overlay == null) _overlay = FindAnyObjectByType<ByAWhisker.UI.ControlsOverlay>();
            if (_overlay != null) _overlay.Flash(message);
        }

        private void Show(bool visible)
        {
            if (_renderer == null) return;

            // enabled 대입도 공짜가 아니다. 이미 그 상태면 건드리지 않는다.
            if (_renderer.enabled != visible) _renderer.enabled = visible;
        }

        private void BuildStone()
        {
            GameObject go = new GameObject(name + "_Stone");
            go.hideFlags = HideFlags.DontSave;

            MeshFilter filter = go.AddComponent<MeshFilter>();
            _mesh = BuildMesh();
            filter.sharedMesh = _mesh;

            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = StoneMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;

            _stone = go;
            _renderer = renderer;
        }

        /// <summary>
        /// 팔면체 하나. 구를 쓰면 정점이 수백 개인데 손톱만 한 돌에 그럴 이유가 없고,
        /// 사각형을 쓰면 내려다보는 각에서 납작한 종이로 보인다. 여덟 면이면 어느 쪽에서 봐도 덩어리다.
        /// 색은 정점에 굽는다. 머티리얼에 칠하면 이 컴포넌트마다 머티리얼 사본이 하나씩 생긴다.
        /// </summary>
        private Mesh BuildMesh()
        {
            float s = Mathf.Max(0.01f, stoneSize);

            Vector3[] vertices =
            {
                new Vector3(0f, s, 0f),
                new Vector3(0f, -s, 0f),
                new Vector3(s, 0f, 0f),
                new Vector3(-s, 0f, 0f),
                new Vector3(0f, 0f, s),
                new Vector3(0f, 0f, -s)
            };

            int[] triangles =
            {
                0, 4, 2,  0, 2, 5,  0, 5, 3,  0, 3, 4,
                1, 2, 4,  1, 5, 2,  1, 3, 5,  1, 4, 3
            };

            Color[] colors = new Color[vertices.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = stoneColor;

            Mesh mesh = new Mesh
            {
                name = "BW_ThrowStone",
                hideFlags = HideFlags.HideAndDontSave
            };

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            // 손으로 정한 경계다. RecalculateBounds는 정점을 한 번 더 훑는데 그럴 값이 아니다.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (s * 2f));
            return mesh;
        }

        private Material StoneMaterial()
        {
            if (stoneMaterial != null) return stoneMaterial;

            // 플레이 모드를 나가면 파괴되어 가짜 null이 된다. 그때 다시 만든다.
            if (_sharedMaterial != null) return _sharedMaterial;

            // Internal-Colored를 먼저 찾는 이유는 이것만 _ZTest를 밖으로 열어 두기 때문이다.
            // 돌은 벽에 가려야 하니 깊이 판정을 켠 채로 두어야 한다. 없으면 NoiseRipple과 같은 차례로 내려간다.
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            Material material = new Material(shader)
            {
                name = "BW_ThrowStone",
                hideFlags = HideFlags.HideAndDontSave
            };

            // 셰이더마다 열어 둔 것이 달라서 있는 것만 건드린다. 없는 이름에 값을 넣어도 조용히 무시된다.
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            // 팔면체는 양면을 다 그려도 값이 싸다. 감는 방향을 잘못 잡아도 사라지지 않게 끈다.
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            // 어둠은 넘지만 벽은 넘지 않는다. 돌은 정보가 아니라 물건이라 벽 뒤에서 보이면 투시가 된다.
            if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)CompareFunction.LessEqual);

            material.renderQueue = renderQueue;

            _sharedMaterial = material;
            return _sharedMaterial;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 어디까지 날아가는지는 눈에 안 보이니 씬에서 확인할 수 있게 겨누는 쪽으로 그려 둔다.
            Vector3 aim = combat != null ? combat.AimDirection : transform.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 0.0001f) aim = transform.forward;

            Vector3 from = transform.position + Vector3.up * handHeight;
            Gizmos.color = new Color(0.94f, 0.90f, 0.74f, 0.6f);
            Gizmos.DrawLine(from, from + aim.normalized * Mathf.Max(0.01f, throwDistance));
        }
#endif
    }
}
