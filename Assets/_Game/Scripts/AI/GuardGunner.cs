using UnityEngine;
using ByAWhisker.Combat;
using ByAWhisker.Core;
using ByAWhisker.Perception;
using ByAWhisker.Player;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 경비의 사격. 겨누기와 쏘기의 상태는 여기서만 굴리고 GuardBrain은 Engage/Disengage로 켜고 끄기만 한다.
    /// 한 대 맞으면 끝나는 게임이라 예고 없는 사격은 불합리하게 느껴진다.
    /// 그래서 순서를 뒤집을 수 없게 만들었다. 반드시 aimSeconds만큼 겨눈 뒤에야 방아쇠로 넘어간다.
    /// </summary>
    public class GuardGunner : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("언제 겨누고 언제 포기하는지. 비어 있으면 이 경비는 총을 쓰지 않는다.")]
        [SerializeField] private GuardCombatSettings settings;
        [Tooltip("비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private Weapon weapon;
        [SerializeField] private GuardPerception perception;

        [Tooltip("총구. 비우면 GuardPerception의 눈 위치에서 쏜다. 눈에서 쏘면 '보인다'는 판정과 총알이 지나는 길이 같아진다.")]
        [SerializeField] private Transform muzzle;

        [Header("겨누는 높이")]
        [Tooltip("대상에 PlayerExposure가 없을 때 쓸 높이. 발밑이 아니라 몸통을 겨눈다.")]
        [SerializeField] private float targetHeight = 1f;

        private Transform _target;

        // 자세에 따라 겨누는 높이가 달라진다. Bind에서 한 번 찾아 두고 매 프레임 GetComponent를 부르지 않는다.
        private PlayerExposure _targetExposure;

        private bool _engaged;
        private float _aimTimer;      // 지금까지 겨눈 시간
        private float _lostTimer;     // 대상을 못 보고 흐른 시간
        private float _recoverTimer;  // 다음 조준까지 남은 시간

        public bool IsAiming { get; private set; }

        /// <summary>0..1. 1이면 곧 쏜다.</summary>
        public float AimProgress { get; private set; }

        /// <summary>
        /// 계약에는 없다. AimTelegraph가 선을 그으려면 어디를 겨누는지 알아야 해서 내놓는다.
        /// 표시와 판정이 같은 값을 보게 해서 "선은 저기 있는데 총알은 여기로" 같은 어긋남을 막는다.
        /// </summary>
        public Vector3 AimPoint { get; private set; }

        /// <summary>계약에는 없다. 발사한 순간을 표시 쪽에 알리는 데만 쓴다.</summary>
        public event System.Action Fired;

        private void Awake()
        {
            if (weapon == null) weapon = GetComponent<Weapon>();
            if (perception == null) perception = GetComponent<GuardPerception>();

            AimPoint = transform.position;

            // 셋 중 하나라도 빠지면 이 경비는 영영 쏘지 않는다. 조용히 죽어 있으면 씬 연결이 빠진 걸
            // 찾기 어려우니 여기서 딱 한 번만 알린다. 매 프레임 경고는 콘솔을 못 쓰게 만든다.
            if (settings == null || weapon == null || perception == null)
            {
                Debug.LogWarning("GuardGunner: 설정, Weapon, GuardPerception 중 빠진 것이 있어 사격하지 않는다.", this);
            }
        }

        private void OnEnable()
        {
            GameEvents.RunReset += HandleRunReset;
        }

        private void OnDisable()
        {
            GameEvents.RunReset -= HandleRunReset;

            // 기절하거나 재시작으로 꺼질 때 겨누던 상태를 들고 있지 않는다. 안 그러면 조준선이 허공에 남는다.
            _engaged = false;
            ClearAim();
        }

        /// <summary>겨눌 대상을 받아 둔다. 매 프레임 찾지 않는다.</summary>
        public void Bind(Transform target)
        {
            _target = target;
            _targetExposure = target != null ? target.GetComponent<PlayerExposure>() : null;
        }

        /// <summary>두뇌가 전투를 시작할 때.</summary>
        public void Engage()
        {
            if (_engaged) return;
            _engaged = true;

            // 새로 붙잡은 대상은 처음부터 겨눈다. 아까 겨누다 만 진행도를 물려받으면 즉사처럼 느껴진다.
            _aimTimer = 0f;
            _lostTimer = 0f;
            AimProgress = 0f;
        }

        /// <summary>대상을 잃거나 재시작할 때.</summary>
        public void Disengage()
        {
            _engaged = false;
            ClearAim();
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // 재장전 대기와 달리 회복은 전투를 쉬는 동안에도 흘러야 한다. 켜고 끄기를 반복해서
            // 회복을 건너뛰는 일이 없도록 다른 조건보다 먼저 센다.
            if (_recoverTimer > 0f) _recoverTimer -= dt;

            if (!_engaged || settings == null || weapon == null || perception == null || _target == null)
            {
                if (IsAiming) ClearAim();
                return;
            }

            AimPoint = TargetPoint();

            bool acquired = HasShot();

            if (acquired)
            {
                _lostTimer = 0f;
            }
            else
            {
                _lostTimer += dt;
                if (_lostTimer >= Mathf.Max(0f, settings.loseAimSeconds))
                {
                    // 완전히 따돌렸다. 다시 보이면 조준은 처음부터다.
                    ClearAim();
                    return;
                }
            }

            // 무기는 스스로 재장전하지 않는다. 빈 탄창을 채우라고 시키는 것은 쏘는 쪽 몫이다.
            if (weapon.Ammo <= 0 && !weapon.IsReloading) weapon.Reload();

            if (_recoverTimer > 0f)
            {
                // 쏜 직후에는 겨누지 않는다. 표시도 꺼져야 "한 발 쏘고 숨 고르는" 틈이 읽힌다.
                IsAiming = false;
                AimProgress = 0f;
                _aimTimer = 0f;
                return;
            }

            IsAiming = true;

            // 보이는 동안에만 조준이 찬다. 놓친 사이에는 그대로 멈춰 있다가 다시 보이면 이어서 찬다.
            // 벽 뒤에서 조준이 차면 나오는 순간 총알을 맞는데, 그건 예고가 아니라 함정이다.
            float aimSeconds = Mathf.Max(0.01f, settings.aimSeconds);
            if (acquired) _aimTimer = Mathf.Min(_aimTimer + dt, aimSeconds);
            AimProgress = _aimTimer / aimSeconds;

            if (!acquired) return;
            if (_aimTimer < aimSeconds) return;

            TryShoot();
        }

        /// <summary>보이고, 사거리 안이다. 둘 중 하나라도 아니면 조준은 차지 않는다.</summary>
        private bool HasShot()
        {
            // 보이지 않으면 쏘지 않는다. 감각이 없는 경비는 영영 쏘지 못하는 쪽이 안전하다.
            if (perception == null || !perception.CanSeePlayer) return false;

            return Sight.HorizontalDistance(transform.position, AimPoint) <= settings.fireRange;
        }

        private void TryShoot()
        {
            // 탄이 없거나 재장전 중이면 겨눈 채로 기다린다. 진행도는 1에 멈춰 있어서
            // 플레이어에게는 "언제 터질지 모르는 상태"로 보인다.
            if (!weapon.CanFire) return;

            Vector3 origin = FireOrigin();
            Vector3 direction = AimPoint - origin;
            if (direction.sqrMagnitude < 0.0001f) return;

            if (!weapon.TryFire(origin, direction.normalized)) return;

            _aimTimer = 0f;
            AimProgress = 0f;
            IsAiming = false;
            _recoverTimer = Mathf.Max(0f, settings.recoverSeconds);

            if (Fired != null) Fired();
        }

        private Vector3 FireOrigin()
        {
            if (muzzle != null) return muzzle.position;
            if (perception != null) return perception.EyePosition;
            return transform.position + Vector3.up * targetHeight;
        }

        /// <summary>앉은 플레이어는 낮게 겨눈다. 노출 담당이 이미 자세별 몸통 높이를 알고 있다.</summary>
        private Vector3 TargetPoint()
        {
            if (_targetExposure != null) return _targetExposure.BodyPoint;
            return _target.position + Vector3.up * targetHeight;
        }

        private void ClearAim()
        {
            _aimTimer = 0f;
            _lostTimer = 0f;
            AimProgress = 0f;
            IsAiming = false;
        }

        private void HandleRunReset()
        {
            Disengage();
            _recoverTimer = 0f;
            if (weapon != null) weapon.RefillAmmo();
        }
    }
}
