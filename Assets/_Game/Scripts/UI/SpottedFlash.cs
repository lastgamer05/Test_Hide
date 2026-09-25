using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using ByAWhisker.AI;
using ByAWhisker.Core;

namespace ByAWhisker.UI
{
    /// <summary>
    /// 들킨 그 순간을 한 방으로 알린다. 느낌표 · 화면 번쩍임 · 짧은 시간 지연이 함께 터진다.
    /// 경비가 겨누지 않고 쏘게 되면서 보인 뒤 0.6초면 총알이 온다. 무엇 때문에 죽었는지
    /// 알 수 없으면 어려운 게임이 아니라 불공평한 게임이 된다.
    ///
    /// 신호는 GuardBrain.StateChanged가 Alert로 들어오는 순간이다. 판정은 하지 않는다.
    /// 씬의 경비를 Awake에서 한 번만 모으고, 구독은 OnEnable에서 걸고 OnDisable에서 푼다.
    /// 경비가 열둘이라 매 프레임 찾거나 매번 구독을 다시 거는 것은 그만큼이 그대로 낭비다.
    ///
    /// Time.timeScale을 건드리는 유일한 곳이다. 되돌리는 타이머는 반드시 unscaled로 센다 —
    /// 느려진 시간으로 세면 타이머도 같이 느려져서 영영 끝나지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpottedFlash : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("느낌표가 바라볼 카메라. 비우면 Start에서 Camera.main을 한 번만 찾는다.")]
        [SerializeField] private Camera viewCamera;

        [Header("솎아내기")]
        [Tooltip(
            "한 방과 다음 한 방 사이의 최소 간격(초). 경비 열둘이 잇따라 Alert로 들어오면 " +
            "번쩍임이 겹겹이 쌓여 화면이 하얗게 남고, 지연도 계속 다시 걸려 시간이 안 돌아온다.")]
        [SerializeField] private float minInterval = 0.55f;

        [Header("느낌표")]
        [Tooltip("머리 위로 띄우는 높이(m). 의심 게이지(2.1m)보다 위에 둬야 둘이 겹쳐 읽히지 않는다.")]
        [SerializeField] private float markHeight = 2.7f;

        [Tooltip("느낌표 높이(m). 이 값에 아래 배율이 곱해진다.")]
        [SerializeField] private float markSize = 0.85f;

        [Tooltip("튀어오른 꼭대기에서의 배율. 1보다 커야 한 번 튀었다가 가라앉는 것으로 읽힌다.")]
        [Range(0.2f, 4f)]
        [SerializeField] private float markPopScale = 1.45f;

        [Tooltip("가라앉아 사라질 때의 배율.")]
        [Range(0.1f, 3f)]
        [SerializeField] private float markSettleScale = 0.95f;

        [Tooltip("느낌표가 떠 있는 시간(초).")]
        [SerializeField] private float markSeconds = 0.7f;

        [Tooltip("그중 커지는 데 쓰는 비율. 작을수록 튀어오르는 맛이 세고 남은 시간은 가라앉는 데 쓴다.")]
        [Range(0.05f, 0.9f)]
        [SerializeField] private float markRise = 0.22f;

        [Tooltip("느낌표 색. AwarenessGauge의 alertColor와 맞춰 두면 같은 위험으로 읽힌다.")]
        [SerializeField] private Color markColor = new Color(1f, 0.32f, 0.24f, 1f);

        [Tooltip(
            "반투명 큐에 그린다. 못 본 곳을 검게 칠하는 전면 패스가 반투명보다 먼저 돌기 때문에 " +
            "3000 이상이면 어둠을 넘어 보인다. AwarenessGauge와 NoiseRipple이 그렇게 산다.")]
        [SerializeField] private int markRenderQueue = 3100;

        [Tooltip(
            "깊이 판정을 켤지. 기본은 끈다 — 이 표시는 한순간 스쳐 지나가서 투시로 쓸 수 없고, " +
            "벽 모서리에 가려 못 보고 지나가면 표시를 붙인 뜻 자체가 없어진다. " +
            "늘 떠 있는 의심 게이지가 깊이 판정을 켜 두는 것과는 반대 이유다.")]
        [SerializeField] private bool markDepthTest = false;

        [Tooltip("비우면 코드가 만든다. 프리팹도 애셋도 없이 돌아가야 한다.")]
        [SerializeField] private Material markMaterial;

        [Header("화면 번쩍임")]
        [Tooltip("덮는 색. 붉은 기가 도는 흰색이라야 '피'가 아니라 '한 대 맞은 순간'으로 읽힌다.")]
        [SerializeField] private Color flashColor = new Color(1f, 0.82f, 0.78f, 1f);

        [Tooltip("가장 진할 때의 투명도. 너무 높으면 정작 총알을 피해야 할 순간에 화면이 안 보인다.")]
        [Range(0f, 1f)]
        [SerializeField] private float flashStrength = 0.35f;

        [Tooltip("덮이는 데 걸리는 시간(초). 짧아야 '번쩍'이 된다.")]
        [SerializeField] private float flashInSeconds = 0.04f;

        [Tooltip("사라지는 데 걸리는 시간(초).")]
        [SerializeField] private float flashOutSeconds = 0.26f;

        [Tooltip("캔버스 정렬 순서. 조작 안내(110)보다 위에 둬서 무엇에도 가리지 않게 한다.")]
        [SerializeField] private int sortingOrder = 130;

        [Tooltip("캔버스 기준 해상도. SenseHud와 맞춰 둔다.")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

        [Header("시간 지연")]
        [Tooltip("지연 중의 시간 배율.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float slowScale = 0.35f;

        [Tooltip("지연이 이어지는 시간(초). 실제 시간으로 센다. 길면 조작이 먹통이 된 것처럼 느껴진다.")]
        [SerializeField] private float slowSeconds = 0.15f;

        [Tooltip("지연이 끝나면 돌아갈 시간 배율. 보통은 1이다. 여기 말고는 아무도 timeScale을 건드리지 않는다.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float normalTimeScale = 1f;

        // 느낌표 메시는 이 컴포넌트 하나뿐이라 머티리얼도 하나면 된다. 그래도 정적으로 잡는 이유는
        // 플레이 모드를 나갈 때 파괴되는 임시 머티리얼을 AwarenessGauge, NoiseRipple과 같은 규칙으로 다루기 위해서다.
        private static Material _sharedMaterial;

        private GuardBrain[] _guards;

        // 경비마다 델리게이트를 하나씩 미리 만들어 둔다. StateChanged는 누가 보냈는지를 알려 주지 않아서
        // 어느 경비 머리 위에 띄울지 알려면 구독할 때 그 경비를 같이 묶어 두는 수밖에 없다.
        // 여기서 한 번 만들어 두면 걸고 풀 때 같은 델리게이트를 쓰므로 구독이 새는 일도 없다.
        private System.Action<GuardBrain.State>[] _handlers;
        private bool _subscribed;

        private GameObject _object;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Vector3[] _vertices;
        private Color[] _colors;

        private Image _flashImage;
        private Camera _camera;

        private Transform _markTarget;
        private float _markAge;
        private bool _markOn;

        // 지금 올려 둔 느낌표 투명도. 바뀔 때만 정점 색을 다시 올린다.
        private float _markAlpha = -1f;

        private float _flashAge;
        private bool _flashOn;

        private float _slowRemaining;
        private float _lastTriggerTime = float.NegativeInfinity;

        /// <summary>번쩍임이 떠 있는가. 씬 설정이 어긋났는지 눈으로 보려고 열어 둔다.</summary>
        public bool IsFlashing { get { return _flashOn; } }

        /// <summary>시간을 늦춰 둔 중인가.</summary>
        public bool IsSlowing { get { return _slowRemaining > 0f; } }

        /// <summary>구독해 둔 경비 수. 통합 담당이 열둘을 다 잡았는지 확인할 자리다.</summary>
        public int GuardCount { get { return _guards != null ? _guards.Length : 0; } }

        /// <summary>
        /// 한 방을 터뜨린다. guard는 느낌표를 띄울 대상이고, 비어 있으면 화면 효과만 돈다.
        /// 재사용 간격 안이면 아무 일도 하지 않는다 — 겹쳐 쌓이는 것을 막는 곳이 여기 한 군데다.
        /// </summary>
        public void Trigger(Transform guard)
        {
            // 지연 중에는 Time.time이 느리게 흐른다. 간격도 실제 시간으로 재야 한다.
            float now = Time.unscaledTime;
            if (now - _lastTriggerTime < minInterval) return;
            _lastTriggerTime = now;

            _markTarget = guard;
            _markAge = 0f;
            _markOn = guard != null;

            _flashAge = 0f;
            _flashOn = true;

            if (slowSeconds > 0f)
            {
                _slowRemaining = slowSeconds;
                Time.timeScale = slowScale;
            }
        }

        /// <summary>떠 있는 것을 즉시 지우고 시간을 되돌린다. 재시작과 꺼질 때가 쓴다.</summary>
        public void ClearNow()
        {
            _markOn = false;
            _markTarget = null;
            ShowMark(false);

            _flashOn = false;
            ShowFlash(false);

            // 느려진 채로 남으면 다음 판이 통째로 느리다. 지우는 자리에서는 반드시 같이 되돌린다.
            _slowRemaining = 0f;
            Time.timeScale = normalTimeScale;
        }

        private void Awake()
        {
            _camera = viewCamera;

            CollectGuards();
            BuildMesh();
            BuildObject();
            BuildFlash();
        }

        private void Start()
        {
            // 카메라는 여기서 한 번만 찾는다. 부트스트랩이 카메라를 세운 뒤여야 찾히므로 Awake가 아니다.
            if (_camera == null) _camera = Camera.main;
        }

        private void OnEnable()
        {
            Subscribe();
            GameEvents.RunReset += HandleRunReset;
        }

        private void OnDisable()
        {
            Unsubscribe();
            GameEvents.RunReset -= HandleRunReset;

            // 꺼지면 LateUpdate가 멈춘다. 지우지 않으면 번쩍임이 화면에 얼어붙고,
            // 무엇보다 플레이를 멈춘 에디터가 느린 시간에 갇힌다.
            ClearNow();
        }

        private void OnDestroy()
        {
            // 느낌표는 씬 루트에 따로 서 있다. 주인이 사라지면 같이 치운다.
            if (_object != null) Destroy(_object);
            _object = null;
            _renderer = null;

            if (_mesh != null) Destroy(_mesh);
            _mesh = null;
        }

        /// <summary>
        /// 경비가 움직인 뒤에 따라붙어야 느낌표가 한 프레임 밀리지 않는다.
        /// NavMeshAgent는 Update에서 위치를 바꾸니 여기는 LateUpdate여야 한다.
        /// </summary>
        private void LateUpdate()
        {
            TickMark();
            TickFlash();
            TickSlow();
        }

        /// <summary>씬에 놓인 경비를 한 번만 모은다. 경비는 실행 중에 늘지 않는다.</summary>
        private void CollectGuards()
        {
            _guards = FindObjectsByType<GuardBrain>(FindObjectsSortMode.None);
            _handlers = new System.Action<GuardBrain.State>[_guards.Length];

            for (int i = 0; i < _guards.Length; i++)
            {
                // 지역 변수에 받아 둬야 저마다 제 경비를 묶는다. 반복 변수를 그대로 잡으면 전부 마지막 경비가 된다.
                Transform who = _guards[i].transform;
                _handlers[i] = state => HandleStateChanged(state, who);
            }
        }

        private void Subscribe()
        {
            if (_subscribed || _guards == null) return;

            for (int i = 0; i < _guards.Length; i++)
            {
                if (_guards[i] == null) continue;
                _guards[i].StateChanged += _handlers[i];
            }

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _guards == null) return;

            for (int i = 0; i < _guards.Length; i++)
            {
                // 경비가 먼저 파괴됐을 수 있다. 가짜 null이라 여기서 걸러야 한다.
                if (_guards[i] == null) continue;
                _guards[i].StateChanged -= _handlers[i];
            }

            _subscribed = false;
        }

        /// <summary>
        /// Alert로 들어가는 순간이 곧 "들켰다"는 신호다. GuardBrain의 Enter는 상태가 실제로 바뀔 때만
        /// 이 이벤트를 보내므로, Alert에 머무는 동안 다시 불릴 일은 없다.
        /// </summary>
        private void HandleStateChanged(GuardBrain.State state, Transform who)
        {
            if (state != GuardBrain.State.Alert) return;
            Trigger(who);
        }

        private void HandleRunReset()
        {
            ClearNow();
        }

        private void TickMark()
        {
            if (!_markOn || _renderer == null) return;

            // 지연 중에도 느낌표는 같은 길이로 떠 있어야 한다. 화면 효과의 길이는 실제 시간으로 정한다.
            _markAge += Time.unscaledDeltaTime;

            if (_markTarget == null || _camera == null || markSeconds <= 0f || _markAge >= markSeconds)
            {
                _markOn = false;
                ShowMark(false);
                return;
            }

            float u = _markAge / markSeconds;
            float rise = Mathf.Clamp(markRise, 0.05f, 0.9f);

            float scale;
            float alpha;
            if (u < rise)
            {
                // 뒤로 갈수록 느려지게 커진다. 등속으로 커지면 튀어오르는 맛이 없다.
                float k = u / rise;
                k = 1f - (1f - k) * (1f - k);
                scale = markPopScale * k;
                alpha = 1f;
            }
            else
            {
                float k = (u - rise) / (1f - rise);
                scale = Mathf.Lerp(markPopScale, markSettleScale, k);
                // 끝에서 몰아서 사라진다. 처음부터 고르게 흐려지면 표시가 실제보다 짧게 느껴진다.
                alpha = 1f - k * k;
            }

            Transform t = _object.transform;
            t.position = _markTarget.position + Vector3.up * markHeight;
            // 카메라의 회전을 그대로 쓴다. LookAt과 달리 화면 가장자리의 경비도 기울지 않고,
            // 카메라가 90도씩 돌아도 느낌표는 늘 똑바로 선다. AwarenessGauge와 같은 규칙이다.
            t.rotation = _camera.transform.rotation;
            t.localScale = Vector3.one * (markSize * scale);

            ApplyMarkAlpha(alpha);
            ShowMark(true);
        }

        private void TickFlash()
        {
            if (!_flashOn || _flashImage == null) return;

            _flashAge += Time.unscaledDeltaTime;

            float alpha;
            if (_flashAge < flashInSeconds)
            {
                alpha = flashStrength * (flashInSeconds > 0f ? _flashAge / flashInSeconds : 1f);
            }
            else
            {
                float k = flashOutSeconds > 0f ? (_flashAge - flashInSeconds) / flashOutSeconds : 1f;
                if (k >= 1f)
                {
                    _flashOn = false;
                    ShowFlash(false);
                    return;
                }

                alpha = flashStrength * (1f - k);
            }

            SenseHudBuilder.SetAlpha(_flashImage, alpha);
            ShowFlash(true);
        }

        private void TickSlow()
        {
            if (_slowRemaining <= 0f) return;

            // 반드시 unscaled로 센다. 느려진 시간으로 세면 타이머까지 같이 느려져서 영영 안 끝난다.
            _slowRemaining -= Time.unscaledDeltaTime;
            if (_slowRemaining > 0f) return;

            _slowRemaining = 0f;
            Time.timeScale = normalTimeScale;
        }

        /// <summary>
        /// 느낌표 한 글자를 메시로 만든다. 막대 네 점과 점 네 점, 삼각형 넷이 전부다.
        /// 글자 비율은 높이 1을 기준으로 굽고 크기는 트랜스폼이 바꾼다 — 그래야 매 프레임 정점을 다시 올리지 않는다.
        /// </summary>
        private void BuildMesh()
        {
            const float barTop = 0.5f;
            const float barBottom = -0.05f;
            const float barTopHalf = 0.11f;
            const float barBottomHalf = 0.07f;   // 아래로 갈수록 좁힌다. 손으로 그은 획처럼 보이게
            const float dotTop = -0.19f;
            const float dotBottom = -0.41f;
            const float dotHalf = 0.09f;

            _vertices = new Vector3[8];
            _colors = new Color[8];

            // 로컬 XY 평면에 눕혀 둔다. 카메라를 보는 일은 트랜스폼이 맡는다.
            _vertices[0] = new Vector3(-barTopHalf, barTop, 0f);
            _vertices[1] = new Vector3(barTopHalf, barTop, 0f);
            _vertices[2] = new Vector3(-barBottomHalf, barBottom, 0f);
            _vertices[3] = new Vector3(barBottomHalf, barBottom, 0f);
            _vertices[4] = new Vector3(-dotHalf, dotTop, 0f);
            _vertices[5] = new Vector3(dotHalf, dotTop, 0f);
            _vertices[6] = new Vector3(-dotHalf, dotBottom, 0f);
            _vertices[7] = new Vector3(dotHalf, dotBottom, 0f);

            // 감는 방향은 따지지 않는다. 머티리얼에서 뒷면 잘라내기를 끄기 때문이다.
            int[] triangles = { 0, 1, 3, 0, 3, 2, 4, 5, 7, 4, 7, 6 };

            _mesh = new Mesh();
            _mesh.name = "BW_SpottedMark";
            _mesh.hideFlags = HideFlags.HideAndDontSave;

            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(triangles, 0, false);

            // 첫 프레임에 색이 올라간다. 여기서는 배열만 잡아 둔다.
            for (int i = 0; i < _colors.Length; i++) _colors[i] = markColor;
            _mesh.SetColors(_colors);
        }

        private void BuildObject()
        {
            // 경비의 자식으로 두면 EnemyVisibility가 자식 렌더러를 통째로 끈다.
            // 몸이 어둠에 지워진 순간에도 느낌표는 보여야 경고 구실을 하니 씬 루트에 따로 세운다.
            _object = new GameObject(name + "_SpottedMark");
            _object.hideFlags = HideFlags.DontSave;

            MeshFilter filter = _object.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            _renderer = _object.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = MarkMaterial();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            // 미리 만들어 두고 껐다 켜기만 한다. 들킬 때마다 오브젝트를 만들지 않는다.
            _renderer.enabled = false;
        }

        private Material MarkMaterial()
        {
            if (markMaterial != null) return markMaterial;

            // 플레이 모드를 나가면 파괴되어 가짜 null이 된다. 그때 다시 만든다.
            if (_sharedMaterial != null) return _sharedMaterial;

            // Internal-Colored를 먼저 찾는 이유는 이것만 _ZTest를 밖으로 열어 두기 때문이다.
            // 벽에 가릴지 말지를 고를 수 있어야 한다. 없으면 AwarenessGauge와 같은 차례로 내려간다.
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            Material material = new Material(shader)
            {
                name = "BW_SpottedMark",
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
                material.SetInt("_ZTest", (int)(markDepthTest ? CompareFunction.LessEqual : CompareFunction.Always));
            }

            // 어둠을 칠하는 합성 패스는 반투명보다 먼저 돈다. 여기에 그려야 어둠을 넘어 보인다.
            material.renderQueue = markRenderQueue;

            _sharedMaterial = material;
            return _sharedMaterial;
        }

        /// <summary>
        /// 화면을 덮을 그림 한 장. 캔버스도 그림도 코드로 만든다 — 이 프로젝트는 UI 프리팹을 두지 않는다.
        /// 들킬 때마다 만들지 않고 처음에 한 장 만들어 두고 껐다 켠다.
        /// </summary>
        private void BuildFlash()
        {
            Canvas canvas = SenseHudBuilder.CreateOverlayCanvas(
                "SpottedFlash Canvas", transform, sortingOrder, referenceResolution);

            _flashImage = SenseHudBuilder.CreateImage(
                "Spotted Flash", canvas.transform, SenseHudBuilder.SolidSprite, WithAlpha(flashColor, 0f));

            RectTransform rect = _flashImage.rectTransform;
            SenseHudBuilder.Anchor(rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _flashImage.enabled = false;
        }

        /// <summary>
        /// 정점 색으로 투명도를 올린다. 머티리얼을 건드리면 공유물에 사본이 생기고,
        /// 정점이 여덟뿐이라 값이 바뀔 때만 올리면 사실상 공짜다.
        /// </summary>
        private void ApplyMarkAlpha(float alpha)
        {
            float a = markColor.a * Mathf.Clamp01(alpha);
            if (Mathf.Abs(a - _markAlpha) < 0.004f) return;

            _markAlpha = a;

            Color c = WithAlpha(markColor, a);
            for (int i = 0; i < _colors.Length; i++) _colors[i] = c;
            _mesh.SetColors(_colors);
        }

        private void ShowMark(bool visible)
        {
            if (_renderer == null) return;
            // enabled 대입도 공짜가 아니다. 이미 그 상태면 건드리지 않는다.
            if (_renderer.enabled != visible) _renderer.enabled = visible;
        }

        private void ShowFlash(bool visible)
        {
            if (_flashImage == null) return;
            if (_flashImage.enabled != visible) _flashImage.enabled = visible;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
