using System.Collections.Generic;
using UnityEngine;
using ByAWhisker.Combat;
using ByAWhisker.Perception;
using ByAWhisker.Player;
using ByAWhisker.Senses;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 시각과 청각으로 의심도를 올리고 내린다. 판정은 여기서만 하고 행동은 GuardBrain이 정한다.
    /// 대상은 Bind로 한 번 받아 둔다. 매 프레임 찾지 않는다.
    /// </summary>
    public class GuardPerception : MonoBehaviour
    {
        [SerializeField] private SenseProfile profile;
        [Tooltip("시선을 막는 레이어. Wall, HighCover 같은 것들.")]
        [SerializeField] private LayerMask blockers;
        [Tooltip("플레이어가 앉아 있을 때만 시선을 막는 레이어. LowCover.")]
        [SerializeField] private LayerMask lowCover;
        [Tooltip("눈 위치. 비우면 프로필의 eyeHeight를 쓴다.")]
        [SerializeField] private Transform eye;
        [Tooltip("플레이어를 겨누는 높이. 발밑이 아니라 몸통을 본다.")]
        [SerializeField] private float targetHeight = 1f;
        [Tooltip("탐지 거리 끝에서의 의심도 상승 배율. 멀수록 천천히 오른다.")]
        [SerializeField] private float farDetectScale = 0.3f;
        [Tooltip("이 시간 안에 난 소리까지 듣는다. 한 프레임만 보면 발소리를 놓친다.")]
        [SerializeField] private float hearingMemory = 0.35f;

        [Header("총성")]
        [Tooltip("총성 한 번에 오르는 의심도. 발소리처럼 천천히 차면 코앞에서 쏴도 알아채지 못한다.")]
        [SerializeField] private float gunshotAwareness = 0.9f;

        [Header("동료")]
        [Tooltip("플레이어를 보고 있을 때 의심을 나눠 줄 거리(m).")]
        [SerializeField] private float allyAlertRadius = 14f;
        [Tooltip("그 동료에게 나눠 주는 초당 의심도. 본 쪽보다 느리게 차야 눈으로 본 경비가 먼저 움직인다.")]
        [SerializeField] private float allyAlertPerSecond = 0.5f;
        [Tooltip("동료를 훑는 간격(초). 매 프레임 거리를 다시 잴 만큼 급한 판정이 아니다.")]
        [SerializeField] private float allyScanInterval = 0.25f;

        [Header("시체")]
        [Tooltip("쓰러진 몸이 있는 레이어. Enemy와 Player.")]
        [SerializeField] private LayerMask bodyMask;
        [Tooltip("시체를 알아보는 거리(m). 밝기로 늘었다 주는 탐지 거리와 따로 둔다. 아래 주석 참고.")]
        [SerializeField] private float bodyRange = 12f;
        [Tooltip("시체를 겨누는 높이(m). 바닥에 누워 있으니 몸통 높이로 보면 엄폐물을 뚫고 보인다.")]
        [SerializeField] private float bodyHeight = 0.3f;
        [Tooltip("시체를 다시 훑는 간격(초). 매 프레임 물리 질의를 돌릴 만큼 급한 판정이 아니다.")]
        [SerializeField] private float bodyScanInterval = 0.2f;
        [Tooltip("한 번에 살펴볼 몸의 수. 좁은 방에 몇이 겹쳐 쓰러져 있어도 이 정도면 넉넉하다.")]
        [SerializeField] private int maxBodies = 8;

        [Header("시작할 때 붙일 대상(선택)")]
        [Tooltip("비워 두면 통합 담당이 Bind로 넣어 준다.")]
        [SerializeField] private Transform initialPlayer;
        [SerializeField] private PlayerExposure initialExposure;

        private Transform _player;
        private PlayerExposure _exposure;

        // 매 프레임 새로 만들지 않으려고 들고 있는다. NoiseBus가 여기에 채워 준다.
        private readonly List<NoiseEvent> _heard = new List<NoiseEvent>(16);

        // OverlapSphereNonAlloc이 여기에 채운다. TakedownAction이 쓰는 방식과 같다.
        private Collider[] _bodyOverlap;
        private Damageable _self;
        private Damageable _visibleBody;
        private float _nextBodyScan;

        // 한 번 놀란 몸은 여기 적어 둔다. 안 그러면 경비가 시체 앞을 영영 떠나지 못한다.
        private readonly HashSet<int> _handledBodies = new HashSet<int>();

        // 씬의 경비를 한 번 모아 두고 재사용한다. 레벨이 만들어진 뒤로 경비는 늘지 않으므로
        // 매 프레임은커녕 두 번 찾을 일도 없다. BodyCarry가 시체를 모으는 방식과 같다.
        private GuardPerception[] _allies;
        private float _nextAllyScan;
        private float _lastAllySpread;

        // 같은 총성이 hearingMemory 동안 버퍼에 남아 있다. 한 발에 한 번만 놀라려고
        // 마지막으로 반응한 총성의 시각을 적어 둔다.
        private float _lastGunshotTime;

        // 밖에서 들어온 경보. Alarm은 남의 Update에서도 불리는데 이쪽 Update가 그 뒤에 돌면
        // 두뇌가 읽기도 전에 HeardSomething이 지워진다. 그래서 한 프레임 더 들고 있는다.
        private bool _alarmed;

        /// <summary>0..1. 1이면 완전히 들킨 것이다.</summary>
        public float Awareness { get; private set; }
        public bool CanSeePlayer { get; private set; }

        /// <summary>이번 프레임에 소리를 들었다.</summary>
        public bool HeardSomething { get; private set; }

        /// <summary>마지막으로 보거나 들은 자리. 못 본 적이 없으면 자기 위치다.</summary>
        public Vector3 LastKnownPosition { get; private set; }

        /// <summary>아직 놀라지 않은 쓰러진 몸이 지금 보인다.</summary>
        public bool SeesBody { get; private set; }

        /// <summary>그 몸이 누워 있는 자리. 못 본 적이 없으면 자기 위치다.</summary>
        public Vector3 LastBodyPosition { get; private set; }

        public Vector3 EyePosition
        {
            get
            {
                if (eye != null) return eye.position;
                float height = profile != null ? profile.eyeHeight : 1.6f;
                return transform.position + Vector3.up * height;
            }
        }

        private void Awake()
        {
            LastKnownPosition = transform.position;
            LastBodyPosition = transform.position;

            _bodyOverlap = new Collider[Mathf.Max(1, maxBodies)];

            // 자기 몸을 보고 놀라지 않도록 어디까지가 자기인지 한 번 정해 둔다.
            _self = GetComponentInParent<Damageable>();

            if (initialPlayer != null || initialExposure != null) Bind(initialPlayer, initialExposure);
        }

        public void Bind(Transform playerTransform, PlayerExposure exposure)
        {
            _player = playerTransform;
            _exposure = exposure;
        }

        /// <summary>재시작용. 계약에는 없지만 의심도를 0으로 되돌릴 입구가 필요하다.</summary>
        public void ResetAwareness()
        {
            Awareness = 0f;
            CanSeePlayer = false;
            HeardSomething = false;
            SeesBody = false;
            LastKnownPosition = transform.position;
            LastBodyPosition = transform.position;

            // 시체도 함께 되살아나므로 놀란 기억도 지운다. GuardBrain이 RunReset에서 불러 준다.
            // 여기서 직접 RunReset을 구독하지 않는 이유는, 기절 중에는 이 컴포넌트가 꺼져 있어서
            // 정작 재시작 신호를 놓치기 때문이다.
            _handledBodies.Clear();
            _visibleBody = null;
            _nextBodyScan = 0f;

            _alarmed = false;
            _nextAllyScan = 0f;
            _lastAllySpread = 0f;

            // 재시작하면 소리 버스도 비워지지만, 방금 전 총성에 다시 놀라지 않게 기준 시각을 지금으로 당긴다.
            _lastGunshotTime = Time.time;
        }

        /// <summary>
        /// 밖에서 의심도를 올리는 통로. 총성도 동료의 전파도 모두 여기로 모인다.
        /// 올린 자리를 마지막 단서로 삼아야 두뇌가 그쪽을 뒤지러 간다.
        /// </summary>
        public void Alarm(float amount, Vector3 origin)
        {
            if (amount <= 0f) return;

            Awareness = Mathf.Clamp01(Awareness + amount);
            LastKnownPosition = origin;

            // 두뇌는 HeardSomething을 보고 Search로 올린다. 총성이든 동료의 외침이든
            // "무언가 있었다"는 단서인 것은 같다.
            HeardSomething = true;
            _alarmed = true;
        }

        /// <summary>
        /// 두뇌가 이 몸을 단서로 받아들였다. 그 자리를 마지막 단서로 적고,
        /// 같은 몸에 다시 놀라지 않게 기억해 둔다. 안 그러면 경비가 시체 옆에 붙박인다.
        /// </summary>
        public void AcknowledgeBody()
        {
            if (!SeesBody) return;

            if (_visibleBody != null) _handledBodies.Add(_visibleBody.GetInstanceID());
            _visibleBody = null;

            LastKnownPosition = LastBodyPosition;
            SeesBody = false;
        }

        private void OnDisable()
        {
            // 기절 중에는 두뇌가 이 컴포넌트를 끈다. 켜질 때까지 옛 판정을 들고 있지 않는다.
            CanSeePlayer = false;
            HeardSomething = false;
            SeesBody = false;
            _visibleBody = null;
            _nextBodyScan = 0f;
            _alarmed = false;
        }

        private void Update()
        {
            // 한 프레임짜리 값이라 판정을 새로 하기 전에 먼저 지운다.
            // 다만 밖에서 온 경보는 남의 Update에서 들어오므로, 여기서 곧장 지우면 순서에 따라
            // 두뇌가 한 번도 못 보고 지나간다. 그 한 프레임만 살려서 넘긴다.
            HeardSomething = _alarmed;
            _alarmed = false;

            if (profile == null)
            {
                CanSeePlayer = false;
                SeesBody = false;
                return;
            }

            // 시체는 플레이어와 상관없는 단서라 Bind가 안 되어 있어도 본다.
            UpdateBodySight();

            if (_player == null)
            {
                CanSeePlayer = false;
                return;
            }

            float dt = Time.deltaTime;
            // 겨누는 높이는 노출 담당이 자세를 보고 정한다. 없으면 예전대로 고정 높이를 쓴다.
            Vector3 targetPoint = _exposure != null
                ? _exposure.BodyPoint
                : _player.position + Vector3.up * targetHeight;

            UpdateSight(targetPoint, dt);
            UpdateHearing();
            SpreadToAllies();
        }

        private void UpdateSight(Vector3 targetPoint, float dt)
        {
            Vector3 origin = EyePosition;
            float distance = Sight.HorizontalDistance(origin, targetPoint);
            float range = DetectRange();

            // 보이는 조건은 세 가지를 모두 만족할 때다: 시야각 안, 거리 안, 시선이 안 막힘.
            bool visible = distance <= range
                && Sight.InFieldOfView(origin, Forward(), targetPoint, profile.fovDegrees)
                && Sight.HasLineOfSight(origin, targetPoint, SightBlockers());

            CanSeePlayer = visible;

            if (visible)
            {
                LastKnownPosition = _player.position;

                float seconds = Mathf.Max(0.01f, profile.detectSeconds);
                // 거리가 멀수록 천천히 찬다. 코앞이면 detectSeconds 그대로 걸린다.
                float closeness = range > 0.001f ? Mathf.Clamp01(distance / range) : 0f;
                float scale = Mathf.Lerp(1f, farDetectScale, closeness);
                Awareness = Mathf.Clamp01(Awareness + dt / seconds * scale);
            }
            else
            {
                Awareness = Mathf.Clamp01(Awareness - profile.forgetPerSecond * dt);
            }
        }

        /// <summary>
        /// 주변에 쓰러진 몸이 있는지 훑는다. 보이는 조건은 플레이어와 같은 세 가지다.
        /// 간격을 두고 훑는 이유는 물리 질의가 비싸서다. 그 사이에는 지난 결과를 그대로 들고 있는다.
        /// </summary>
        private void UpdateBodySight()
        {
            if (Time.time < _nextBodyScan) return;
            _nextBodyScan = Time.time + Mathf.Max(0.02f, bodyScanInterval);

            Vector3 origin = EyePosition;
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, bodyRange, _bodyOverlap, bodyMask.value, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                Collider hit = _bodyOverlap[i];
                if (hit == null) continue;

                // 콜라이더가 자식에 달려 있을 수 있으니 부모까지 올라가서 찾는다.
                Damageable body = hit.GetComponentInParent<Damageable>();
                if (body == null || body == _self || !IsBody(body)) continue;
                if (_handledBodies.Contains(body.GetInstanceID())) continue;

                Vector3 point = body.transform.position + Vector3.up * bodyHeight;

                // 밝기로 늘었다 주는 DetectRange는 플레이어가 선 자리의 밝기다. 시체 자리와는 상관이 없어서
                // 여기서는 고정 거리를 쓴다.
                if (Sight.HorizontalDistance(origin, point) > bodyRange) continue;
                if (!Sight.InFieldOfView(origin, Forward(), point, profile.fovDegrees)) continue;
                if (!Sight.HasLineOfSight(origin, point, BodyBlockers())) continue;

                // 하나만 보여도 놀라기에 충분하다. 가장 가까운 것을 고르려고 끝까지 훑지 않는다.
                SeesBody = true;
                _visibleBody = body;
                LastBodyPosition = body.transform.position;
                return;
            }

            SeesBody = false;
            _visibleBody = null;
        }

        /// <summary>
        /// 쓰러진 몸인가. 총에 맞아 죽으면 IsAlive가 내려가고 제압당해 기절하면 IsDown이 올라간다.
        /// 경비 눈에는 둘 다 바닥에 누운 몸이라 구분하지 않는다.
        /// </summary>
        private static bool IsBody(Damageable candidate)
        {
            return !candidate.IsAlive || candidate.IsDown;
        }

        /// <summary>
        /// 시체는 바닥에 누워 있어서 낮은 엄폐물 뒤에 들어가면 보이지 않는다.
        /// 플레이어 쪽 규칙(SightBlockers)은 플레이어가 앉았을 때만 LowCover를 세는데,
        /// 시체는 언제나 앉은 것보다 낮으니 여기서는 늘 함께 센다.
        /// </summary>
        private int BodyBlockers()
        {
            return SightBlockers() | lowCover.value;
        }

        /// <summary>
        /// 소리 버스에 쌓인 최근 사건을 훑는다. 인간은 귀가 약해서 사건의 반경을
        /// hearingMultiplier만큼 줄여서 듣고, 벽은 따지지 않는다.
        /// 총성만은 발소리와 다르게 의심도를 단번에 올린다. 벽을 안 따지는 것은 총성도 같다.
        /// </summary>
        private void UpdateHearing()
        {
            Vector3 ear = transform.position;
            int count = NoiseBus.Collect(ear, hearingMemory, _heard);

            float loudest = 0f;
            float newestGunshot = _lastGunshotTime;
            Vector3 gunshotAt = ear;

            for (int i = 0; i < count; i++)
            {
                NoiseEvent evt = _heard[i];

                // 자기 발소리는 듣지 않는다. 안 그러면 순찰만 해도 제 소리에 놀란다.
                // 제 총성도 같은 길로 걸러진다. 쏘는 경비가 제 소리에 놀랄 일은 없다.
                if (evt.source == gameObject) continue;

                float radius = evt.radius * profile.hearingMultiplier;
                if (Sight.HorizontalDistance(ear, evt.position) > radius) continue;

                // 한 발이 hearingMemory 동안 버퍼에 남으므로, 아직 반응하지 않은 총성만 센다.
                // 안 그러면 한 발에 여러 프레임 놀라서 늘 즉시 발각된다.
                if (evt.kind == NoiseKind.Gunshot && evt.time > newestGunshot)
                {
                    newestGunshot = evt.time;
                    gunshotAt = evt.position;
                }

                // 여러 소리가 겹치면 가장 큰 쪽을 향한다.
                if (evt.loudness < loudest) continue;

                loudest = evt.loudness;
                HeardSomething = true;
                LastKnownPosition = evt.position;
            }

            // 발소리 판정 뒤에 둔다. 발소리와 총성이 겹쳐 들렸으면 총성 쪽이 마지막 단서가 되어야 한다.
            if (newestGunshot > _lastGunshotTime)
            {
                _lastGunshotTime = newestGunshot;
                Alarm(gunshotAwareness, gunshotAt);
            }
        }

        /// <summary>
        /// 플레이어를 보고 있으면 가까운 동료도 함께 긴장한다. 혼자 보고 혼자 쫓다 죽는 대신
        /// 주변이 같이 깨어나게 하는 장치다. 나눠 주는 쪽은 자기 의심도를 잃지 않는다.
        /// 간격을 두는 이유는 거리 계산이 아니라 경비 수만큼 도는 고리를 매 프레임 돌리지 않으려는 것이다.
        /// </summary>
        private void SpreadToAllies()
        {
            if (!CanSeePlayer || allyAlertPerSecond <= 0f || allyAlertRadius <= 0f) return;
            if (Time.time < _nextAllyScan) return;

            float interval = Mathf.Max(0.02f, allyScanInterval);
            // 한동안 못 보다가 다시 본 경우 밀린 시간을 한꺼번에 주면 동료가 단번에 깨어난다. 한 간격까지만 친다.
            float elapsed = _lastAllySpread > 0f
                ? Mathf.Min(Time.time - _lastAllySpread, interval)
                : interval;

            _lastAllySpread = Time.time;
            _nextAllyScan = Time.time + interval;

            EnsureAllies();

            float amount = allyAlertPerSecond * elapsed;
            float sqrRadius = allyAlertRadius * allyAlertRadius;

            for (int i = 0; i < _allies.Length; i++)
            {
                GuardPerception ally = _allies[i];
                if (ally == null || ally == this) continue;

                // 쓰러졌거나 기절한 경비는 깨어나지 않는다. 같은 클래스라 남의 _self를 그대로 읽는다.
                if (ally._self != null && IsBody(ally._self)) continue;

                Vector3 delta = ally.transform.position - transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > sqrRadius) continue;

                // 내가 본 자리를 그대로 넘긴다. 동료는 플레이어를 못 봤으니 이 자리가 유일한 단서다.
                ally.Alarm(amount, LastKnownPosition);
            }
        }

        /// <summary>
        /// 경비 목록을 처음 쓸 때 한 번만 모은다. 레벨은 재시작해도 다시 지어지지 않고
        /// 경비도 그 자리에서 되살아나므로 이 목록은 판이 바뀌어도 그대로 쓸 수 있다.
        /// </summary>
        private void EnsureAllies()
        {
            if (_allies != null) return;
            _allies = FindObjectsByType<GuardPerception>(FindObjectsSortMode.None);
        }

        /// <summary>
        /// 앉아 있는 플레이어는 낮은 엄폐 뒤에 숨는다. 서 있으면 넘겨다본다.
        /// 플레이어 시야(PlayerVision)가 쓰는 규칙과 같게 둬서 화면과 판정이 어긋나지 않게 한다.
        /// </summary>
        private int SightBlockers()
        {
            bool crouching = _exposure != null && _exposure.Crouching;
            return crouching ? (blockers.value | lowCover.value) : blockers.value;
        }

        /// <summary>밝은 데 서 있을수록 멀리서도 보인다.</summary>
        private float DetectRange()
        {
            float light = PlayerLight();
            return Mathf.Lerp(profile.rangeDark, profile.rangeLit, Mathf.Clamp01(light));
        }

        private float PlayerLight()
        {
            if (_exposure != null) return _exposure.Light;
            // 노출 컴포넌트가 없으면 직접 재 본다. 씬 연결이 빠져도 완전한 장님은 되지 않게.
            return Illumination.Sample(_player.position, blockers);
        }

        private Vector3 Forward()
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
        }
    }
}
