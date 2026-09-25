using ByAWhisker.Core;
using ByAWhisker.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace ByAWhisker.Senses
{
    /// <summary>
    /// 소리가 난 자리에서 고리가 퍼졌다 사라진다.
    /// 화면 가장자리의 호는 방향만 알려 주지만, 바닥에 퍼지는 고리는 어디서 났는지를 그대로 알려 준다.
    /// 소리의 radius까지 퍼지므로 고리가 나를 삼키면 나도 그 소리를 들을 자리에 있다는 뜻이다.
    ///
    /// NoiseBus.Emitted는 정적 이벤트다. OnEnable에서 넣고 OnDisable에서 반드시 뺀다.
    /// 풀어 두지 않으면 플레이를 멈춰도 죽은 오브젝트가 계속 불려 유령 고리가 뜬다.
    ///
    /// 집중(FocusSense)과 무관하게 늘 보인다. 듣는 것은 눈을 감아도 들린다.
    /// </summary>
    [DisallowMultipleComponent]
    public class NoiseRipple : MonoBehaviour
    {
        [Header("퍼짐")]
        [Tooltip("고리가 제 반경까지 퍼지는 데 걸리는 시간(초). 길면 소리가 언제 났는지 흐려지고, 짧으면 눈에 안 들어온다.")]
        [SerializeField] private float expandSeconds = 0.8f;

        [Tooltip("퍼지기 시작하는 반경(m). 0이면 첫 프레임이 점이라 어디서 났는지 읽히기 전에 지나간다.")]
        [SerializeField] private float startRadius = 0.35f;

        [Tooltip("퍼지는 감속. 1이면 등속, 크면 처음에 확 튀어 나갔다 느려진다. 소리가 퍼지는 느낌은 뒤로 갈수록 느려야 산다.")]
        [Range(1f, 4f)]
        [SerializeField] private float expandEase = 2.2f;

        [Header("모양")]
        [Tooltip("고리 한 개의 선분 수. 적으면 큰 총성 고리가 다각형으로 보인다.")]
        [Range(8, 128)]
        [SerializeField] private int segments = 48;

        [Tooltip("갓 퍼질 때 선 굵기(m).")]
        [SerializeField] private float startWidth = 0.16f;

        [Tooltip("다 퍼졌을 때 선 굵기(m). 얇아지면서 사라져야 잦아드는 것으로 읽힌다.")]
        [SerializeField] private float endWidth = 0.04f;

        [Tooltip("바닥에서 띄우는 높이(m). 0이면 바닥과 z 싸움이 나서 고리가 지글거린다.")]
        [SerializeField] private float groundOffset = 0.05f;

        [Header("색")]
        [Tooltip("발소리. SenseHud의 호 색과 맞춰 두면 가장자리 호와 바닥 고리가 같은 소리로 읽힌다.")]
        [SerializeField] private Color footstepColor = new Color(0.55f, 0.78f, 0.85f, 0.85f);

        [Tooltip("총성. 발소리와 한눈에 갈려야 해서 반대쪽 색을 쓴다.")]
        [SerializeField] private Color gunshotColor = new Color(0.95f, 0.42f, 0.30f, 1f);

        [Tooltip("부딪힘과 소품 소리. 둘은 급한 정보가 아니라 한 색으로 묶는다.")]
        [SerializeField] private Color otherColor = new Color(0.86f, 0.78f, 0.48f, 0.9f);

        [Tooltip("크기 0인 소리의 투명도 배율. 작은 소리까지 진하게 뜨면 화면이 고리로 덮인다.")]
        [Range(0f, 1f)]
        [SerializeField] private float quietAlpha = 0.4f;

        [Header("풀")]
        [Tooltip("동시에 띄울 고리 수. 다 차면 가장 오래된 것을 빼앗아 쓴다. 소리마다 오브젝트를 만들지 않는다.")]
        [Range(1, 64)]
        [SerializeField] private int maxRipples = 16;

        [Header("집중")]
        [Tooltip("집중하고 있을 때만 고리를 띄운다. 늘 보이면 화면이 고리로 덮여 아무 정보가 없는 것과 같다.")]
        [SerializeField] private bool requireFocus = true;
        [Tooltip("집중 상태를 읽을 쪽. 비우면 씬에서 한 번 찾는다.")]
        [SerializeField] private ByAWhisker.Player.FocusSense focus;

        [Header("솎아내기")]
        [Tooltip("이보다 작은 소리는 고리를 만들지 않는다. 발소리마다 고리가 뜨면 너무 잦다.")]
        [Range(0f, 1f)]
        [SerializeField] private float minLoudness = 0.35f;
        [Tooltip("이 거리 안에서 이 시간 안에 난 소리는 하나로 친다. 같은 자리에서 걷는 발소리가 겹겹이 쌓이지 않게 한다.")]
        [SerializeField] private float mergeRadius = 2.5f;
        [SerializeField] private float mergeSeconds = 0.5f;

        [Header("내 소리")]
        [Tooltip(
            "플레이어 자신이 낸 소리도 고리로 보일지. 기본은 켠다 — 내가 지금 얼마나 멀리까지 들리는지가 " +
            "이 게임에서 제일 중요한 정보이고, 발밑에서 퍼지는 고리가 그 반경을 그대로 보여 준다. " +
            "SenseHud의 호가 내 소리를 거르는 것은 호가 방향만 말해서 내 소리에 아무 정보가 없기 때문이다.")]
        [SerializeField] private bool showOwnNoise = true;

        [Tooltip("내 소리를 가려낼 기준. showOwnNoise를 끌 때만 쓴다. 비우면 씬에서 찾는다.")]
        [SerializeField] private Transform player;

        [Header("그리기")]
        [Tooltip("비우면 코드가 만든다. 프리팹도 애셋도 없이 돌아가야 한다.")]
        [SerializeField] private Material lineMaterial;

        [Tooltip("반투명보다 뒤에 그려 벽에 가리지 않게 한다. 소리는 벽을 돌아 들리니 벽을 뚫고 보이는 것이 맞다.")]
        [SerializeField] private int renderQueue = 3100;

        /// <summary>퍼지는 중인 고리 하나. 매 프레임 다시 그려야 해서 값만 들고 있는다.</summary>
        private struct Slot
        {
            public bool active;
            public Vector3 center;
            public float radius;    // 다 퍼졌을 때의 반경. 소리의 radius 그대로다
            public float startTime;
            public Color color;
        }

        // 고리마다 선은 하나씩이지만 머티리얼은 같은 것을 나눠 쓴다. AimTelegraph와 같은 규칙이다.
        private static Material _sharedMaterial;

        private GameObject[] _objects;
        private LineRenderer[] _lines;
        private Slot[] _slots;

        // 단위 원을 Awake에서 한 번 굽고 매 프레임 반경만 곱한다. sin/cos를 프레임마다 다시 돌 이유가 없다.
        private Vector3[] _unitCircle;

        // SetPositions에 넘길 버퍼. 고리마다 따로 잡으면 개수만큼 배열이 생기고, 매번 잡으면 프레임마다 쓰레기가 된다.
        private Vector3[] _points;

        // 다음에 쓸 자리. 순서대로만 나눠 주므로 여기가 곧 가장 오래된 고리다.
        private int _next;

        // 이번 프레임의 집중 세기. 매 프레임 한 번만 읽어 고리마다 다시 묻지 않는다.
        private float _focusFade = 1f;

        private int _activeCount;
        private GameObject _playerRoot;

        /// <summary>지금 퍼지고 있는 고리 수. 풀이 모자란지 눈으로 보려고 열어 둔다.</summary>
        public int ActiveCount { get { return _activeCount; } }

        /// <summary>내 소리를 가려낼 기준을 밖에서 꽂아 준다. showOwnNoise를 끌 때만 뜻이 있다.</summary>
        public void Bind(Transform playerRoot)
        {
            player = playerRoot;
            _playerRoot = playerRoot != null ? playerRoot.gameObject : null;
        }

        /// <summary>떠 있는 고리를 전부 지운다. 재시작하면 지난 판의 소리가 남아 있으면 안 된다.</summary>
        public void ClearAll()
        {
            if (_slots == null) return;

            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].active = false;
                Show(i, false);
            }

            _activeCount = 0;
            _next = 0;
        }

        private void Awake()
        {
            BuildCircle();
            BuildPool();
            ResolvePlayer();
        }

        private void OnEnable()
        {
            NoiseBus.Emitted += HandleNoise;
            GameEvents.RunReset += HandleRunReset;
        }

        private void OnDisable()
        {
            // 정적 이벤트라 여기서 풀지 않으면 이 컴포넌트가 죽어도 계속 불린다. NoiseBus의 경고가 이것이다.
            NoiseBus.Emitted -= HandleNoise;
            GameEvents.RunReset -= HandleRunReset;

            // 꺼지면 LateUpdate가 멈춘다. 지우지 않으면 퍼지던 고리가 그 모양 그대로 바닥에 얼어붙는다.
            ClearAll();
        }

        private void OnDestroy()
        {
            // 고리는 씬 루트에 따로 서 있다. 주인이 사라지면 같이 치운다.
            if (_objects == null) return;

            for (int i = 0; i < _objects.Length; i++)
            {
                if (_objects[i] != null) Destroy(_objects[i]);
                _objects[i] = null;
            }

            _lines = null;
        }

        /// <summary>
        /// 소리가 날 때마다 불린다. 여기서는 자리만 잡아 둔다.
        /// 그리는 것은 LateUpdate가 한 번에 한다 — 한 프레임에 발소리가 여럿 나도 일이 한 번으로 모인다.
        /// </summary>
        private void HandleNoise(NoiseEvent evt)
        {
            if (_slots == null) return;
            if (evt.radius <= 0.01f) return;
            if (evt.loudness < minLoudness) return;
            if (!showOwnNoise && IsOwnSource(evt.source)) return;

            // 집중하지 않으면 자리조차 잡지 않는다. 집중을 켜는 순간 지난 소리가 한꺼번에
            // 떠오르면 어디가 방금 난 소리인지 알 수 없다.
            if (requireFocus && FocusAmount() <= 0.001f) return;

            // 같은 자리에서 방금 난 소리는 하나로 친다. 걸어가는 발소리가 한 발짝마다 고리를 만들면
            // 화면이 고리로 덮여서 정작 총성 하나를 놓친다.
            float now = evt.time > 0f ? evt.time : Time.time;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].active) continue;
                if (now - _slots[i].startTime > mergeSeconds) continue;

                Vector3 d = _slots[i].center - evt.position;
                d.y = 0f;
                if (d.sqrMagnitude <= mergeRadius * mergeRadius) return;
            }

            int index = _next;
            _next = (_next + 1) % _slots.Length;

            // 이미 퍼지고 있던 고리를 빼앗는 경우에는 활성 수가 그대로다.
            if (!_slots[index].active) _activeCount++;

            _slots[index].active = true;
            _slots[index].center = evt.position;
            _slots[index].radius = evt.radius;
            // Time.time이 아니라 사건의 시각을 쓴다. 지난 프레임에 난 소리가 이제야 그려져도 나이가 어긋나지 않는다.
            _slots[index].startTime = evt.time > 0f ? evt.time : Time.time;
            _slots[index].color = ColorFor(evt.kind, evt.loudness);
        }

        /// <summary>
        /// 소리를 낸 것들이 다 움직인 뒤에 그려야 고리가 한 프레임 밀리지 않는다.
        /// 고리 자체는 난 자리에 박혀 있지만 소리는 Update에서 나므로 여기는 LateUpdate여야 한다.
        /// </summary>
        private void LateUpdate()
        {
            if (_slots == null || _activeCount <= 0) return;

            float now = Time.time;
            float life = Mathf.Max(0.05f, expandSeconds);

            // 집중을 놓으면 퍼지던 고리도 같이 잦아든다. 손을 뗐는데 고리만 남아 있으면
            // 무엇이 집중의 결과인지 읽히지 않는다.
            _focusFade = requireFocus ? FocusAmount() : 1f;
            if (_focusFade <= 0.001f)
            {
                ClearAll();
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].active) continue;

                float t = (now - _slots[i].startTime) / life;
                if (t >= 1f)
                {
                    _slots[i].active = false;
                    _activeCount--;
                    Show(i, false);
                    continue;
                }

                if (t < 0f) t = 0f;
                Draw(i, t);
            }
        }

        private void Draw(int index, float t)
        {
            LineRenderer line = _lines[index];
            if (line == null) return;

            // 뒤로 갈수록 느려지는 곡선. 소리가 확 퍼졌다 잦아드는 모양이 이쪽이다.
            float eased = 1f - Mathf.Pow(1f - t, expandEase);
            float radius = Mathf.Lerp(startRadius, Mathf.Max(startRadius, _slots[index].radius), eased);

            Vector3 center = _slots[index].center;
            // 바닥에 눕힌다. 위에서 내려다보는 각이라 누운 고리가 제일 잘 읽히고, 높이가 다르면 자리가 어긋나 보인다.
            center.y += groundOffset;

            for (int p = 0; p < _points.Length; p++)
            {
                _points[p] = center + _unitCircle[p] * radius;
            }

            line.SetPositions(_points);

            Color color = _slots[index].color;
            // 끝에서 뚝 끊기지 않게 투명도를 t로 눌러 준다. 굵기까지 같이 얇아져야 잦아드는 것으로 읽힌다.
            color.a *= (1f - t) * _focusFade;

            // 머티리얼 색이 아니라 정점 색을 바꾼다. 머티리얼을 건드리면 고리마다 사본이 하나씩 생긴다.
            line.startColor = color;
            line.endColor = color;
            line.widthMultiplier = Mathf.Lerp(startWidth, endWidth, t);

            Show(index, true);
        }

        /// <summary>집중 세기. 참조가 없으면 늘 켜진 것으로 본다.</summary>
        private float FocusAmount()
        {
            if (focus == null) focus = Object.FindFirstObjectByType<ByAWhisker.Player.FocusSense>();
            return focus != null ? focus.Amount : 1f;
        }

        private Color ColorFor(NoiseKind kind, float loudness)
        {
            Color color;
            switch (kind)
            {
                case NoiseKind.Gunshot: color = gunshotColor; break;
                case NoiseKind.Footstep: color = footstepColor; break;
                default: color = otherColor; break;
            }

            // 작은 소리는 옅게. 앉아서 기어가는 발소리와 뛰는 발소리가 같은 진하기면 자세를 바꿀 이유가 없어진다.
            color.a *= Mathf.Lerp(quietAlpha, 1f, Mathf.Clamp01(loudness));
            return color;
        }

        /// <summary>소리를 낸 것이 나 자신인지. 발밑에서 소리 내는 자식 오브젝트까지 같이 걸러야 한다.</summary>
        private bool IsOwnSource(GameObject source)
        {
            if (source == null || _playerRoot == null) return false;
            if (source == _playerRoot) return true;

            return source.transform.IsChildOf(_playerRoot.transform);
        }

        private void HandleRunReset()
        {
            ClearAll();
        }

        private void BuildCircle()
        {
            int count = Mathf.Clamp(segments, 8, 128);

            _unitCircle = new Vector3[count];
            _points = new Vector3[count];

            float step = Mathf.PI * 2f / count;
            for (int i = 0; i < count; i++)
            {
                float a = step * i;
                // 바닥에 눕힐 것이므로 y는 0이다. 높이는 그릴 때 중심에 한 번만 더한다.
                _unitCircle[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
        }

        private void BuildPool()
        {
            int count = Mathf.Clamp(maxRipples, 1, 64);

            _objects = new GameObject[count];
            _lines = new LineRenderer[count];
            _slots = new Slot[count];

            Material material = LineMaterial();

            for (int i = 0; i < count; i++)
            {
                // 이 컴포넌트의 자식으로 두면 붙인 오브젝트가 움직일 때 고리도 따라 움직인다.
                // 고리는 소리가 난 자리에 박혀 있어야 하니 씬 루트에 따로 세운다.
                GameObject go = new GameObject(name + "_Ripple" + i);
                go.hideFlags = HideFlags.DontSave;

                LineRenderer line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                // 원은 닫혀야 한다. loop를 쓰면 시작점을 한 번 더 넣지 않아도 된다.
                line.loop = true;
                line.positionCount = _points.Length;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                // 바닥에 누운 띠로 보여야 한다. View로 두면 카메라를 향해 서서 고리가 아니라 벽처럼 보인다.
                line.alignment = LineAlignment.TransformZ;
                // 띠가 바라보는 쪽은 이 트랜스폼의 +Z다. 위를 보게 눕혀야 내려다보는 카메라에 고리로 보인다.
                go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                line.textureMode = LineTextureMode.Stretch;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = LightProbeUsage.Off;
                line.reflectionProbeUsage = ReflectionProbeUsage.Off;
                line.sharedMaterial = material;
                line.enabled = false;

                _objects[i] = go;
                _lines[i] = line;
            }
        }

        private Material LineMaterial()
        {
            if (lineMaterial != null) return lineMaterial;

            // 플레이 모드를 나가면 파괴되어 가짜 null이 된다. 그때 다시 만든다.
            if (_sharedMaterial != null) return _sharedMaterial;

            // Internal-Colored를 먼저 찾는 이유는 이것만 _ZTest를 밖으로 열어 두기 때문이다.
            // 벽을 뚫고 보여야 해서 깊이 판정을 끌 수 있어야 한다. 없으면 AimTelegraph와 같은 차례로 내려간다.
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            Material material = new Material(shader)
            {
                name = "BW_NoiseRipple",
                hideFlags = HideFlags.HideAndDontSave
            };

            // 셰이더마다 열어 둔 것이 달라서 있는 것만 건드린다. 없는 이름에 값을 넣어도 조용히 무시된다.
            if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            // 깊이 판정을 끈다. 소리는 벽을 돌아 들리니 벽 너머 고리도 보이는 쪽이 맞다.
            if (material.HasProperty("_ZTest")) material.SetInt("_ZTest", (int)CompareFunction.Always);

            material.renderQueue = renderQueue;

            _sharedMaterial = material;
            return _sharedMaterial;
        }

        private void ResolvePlayer()
        {
            if (player != null)
            {
                _playerRoot = player.gameObject;
                return;
            }

            // 내 소리를 거를 때만 필요하다. 보여 줄 것이면 찾을 이유가 없다.
            if (showOwnNoise) return;

            // PlayerMotor는 플레이어에만 붙는다. 태그에 기대면 씬 설정이 어긋났을 때 조용히 틀린다.
            PlayerMotor motor = FindAnyObjectByType<PlayerMotor>();
            if (motor == null) return;

            player = motor.transform;
            _playerRoot = motor.gameObject;
        }

        private void Show(int index, bool visible)
        {
            LineRenderer line = _lines != null ? _lines[index] : null;
            if (line == null) return;

            // enabled 대입도 공짜가 아니다. 이미 그 상태면 건드리지 않는다.
            if (line.enabled != visible) line.enabled = visible;
        }
    }
}
