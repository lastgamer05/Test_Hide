using UnityEngine;
using ByAWhisker.Perception;
using ByAWhisker.Player;

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
        [Tooltip("눈 위치. 비우면 프로필의 eyeHeight를 쓴다.")]
        [SerializeField] private Transform eye;
        [Tooltip("플레이어를 겨누는 높이. 발밑이 아니라 몸통을 본다.")]
        [SerializeField] private float targetHeight = 1f;
        [Tooltip("탐지 거리 끝에서의 의심도 상승 배율. 멀수록 천천히 오른다.")]
        [SerializeField] private float farDetectScale = 0.3f;

        [Header("시작할 때 붙일 대상(선택)")]
        [Tooltip("비워 두면 통합 담당이 Bind로 넣어 준다.")]
        [SerializeField] private Transform initialPlayer;
        [SerializeField] private PlayerExposure initialExposure;

        private Transform _player;
        private PlayerExposure _exposure;

        /// <summary>0..1. 1이면 완전히 들킨 것이다.</summary>
        public float Awareness { get; private set; }
        public bool CanSeePlayer { get; private set; }

        /// <summary>이번 프레임에 소리를 들었다.</summary>
        public bool HeardSomething { get; private set; }

        /// <summary>마지막으로 보거나 들은 자리. 못 본 적이 없으면 자기 위치다.</summary>
        public Vector3 LastKnownPosition { get; private set; }

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
            LastKnownPosition = transform.position;
        }

        private void OnDisable()
        {
            // 기절 중에는 두뇌가 이 컴포넌트를 끈다. 켜질 때까지 옛 판정을 들고 있지 않는다.
            CanSeePlayer = false;
            HeardSomething = false;
        }

        private void Update()
        {
            // 한 프레임짜리 값이라 판정을 새로 하기 전에 먼저 지운다.
            HeardSomething = false;

            if (profile == null || _player == null)
            {
                CanSeePlayer = false;
                return;
            }

            float dt = Time.deltaTime;
            Vector3 targetPoint = _player.position + Vector3.up * targetHeight;

            UpdateSight(targetPoint, dt);
            UpdateHearing();
        }

        private void UpdateSight(Vector3 targetPoint, float dt)
        {
            Vector3 origin = EyePosition;
            float distance = Sight.HorizontalDistance(origin, targetPoint);
            float range = DetectRange();

            // 보이는 조건은 세 가지를 모두 만족할 때다: 시야각 안, 거리 안, 시선이 안 막힘.
            bool visible = distance <= range
                && Sight.InFieldOfView(origin, Forward(), targetPoint, profile.fovDegrees)
                && Sight.HasLineOfSight(origin, targetPoint, blockers);

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

        private void UpdateHearing()
        {
            float noise = _exposure != null ? _exposure.Noise : 0f;
            float hearRadius = noise * profile.hearingMultiplier;
            if (hearRadius <= 0.001f) return;

            // 인간은 귀가 약하다. 벽은 따지지 않고 수평 거리만 본다. M4에서 소리 시스템으로 바뀐다.
            if (Sight.HorizontalDistance(transform.position, _player.position) <= hearRadius)
            {
                HeardSomething = true;
                LastKnownPosition = _player.position;
            }
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
