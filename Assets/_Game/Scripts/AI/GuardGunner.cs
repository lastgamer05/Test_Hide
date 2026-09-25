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
    /// 겨누기 → 연사 → 회복의 한 바퀴다. 매서워지는 쪽은 연사뿐이고, 앞의 예고는 건드리지 않는다.
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

        [Header("연사")]
        [Tooltip("연사 한 번에 더 주는 여유 시간(초). 예상 시간(발 수 x 간격)에 이만큼을 더한 것이 상한이고, 넘기면 남은 발을 버리고 회복으로 간다. 총 쪽 fireInterval이 길어 발이 밀릴 때 연사가 끝나지 않는 것을 막는 안전장치다.")]
        [SerializeField] private float burstGraceSeconds = 1f;

        private Transform _target;

        // 자세에 따라 겨누는 높이가 달라진다. Bind에서 한 번 찾아 두고 매 프레임 GetComponent를 부르지 않는다.
        private PlayerExposure _targetExposure;

        private bool _engaged;
        private float _aimTimer;      // 지금까지 겨눈 시간
        private float _lostTimer;     // 대상을 못 보고 흐른 시간
        private float _recoverTimer;  // 다음 조준까지 남은 시간
        private int _burstLeft;       // 이번 연사에 남은 발 수
        private float _shotTimer;     // 다음 발까지 남은 시간
        private float _burstTimeLeft; // 이번 연사에 남은 상한 시간

        public bool IsAiming { get; private set; }

        /// <summary>연사 중인가. 겨누기가 끝난 다음의 짧은 구간이라 IsAiming과 동시에 참이 되지 않는다.</summary>
        public bool IsFiring { get; private set; }

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
                if (IsAiming || IsFiring) ClearAim();
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
                    // 쏘던 중이었으면 남은 발은 버린다. 이미 없는 대상 자리에 탄창을 쏟는 경비는 우스워 보인다.
                    if (IsFiring) EndBurst();
                    ClearAim();
                    return;
                }
            }

            // 무기는 스스로 재장전하지 않는다. 빈 탄창을 채우라고 시키는 것은 쏘는 쪽 몫이다.
            if (weapon.Ammo <= 0 && !weapon.IsReloading) weapon.Reload();

            // 연사는 겨누기와 회복 사이에 끼어 있다. 시작된 뒤에는 조준을 다시 세지 않고 남은 발만 마저 쏜다.
            if (IsFiring)
            {
                TickBurst(dt);
                return;
            }

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

            // 탄이 없거나 재장전 중이면 겨눈 채로 기다린다. 진행도는 1에 멈춰 있어서
            // 플레이어에게는 "언제 터질지 모르는 상태"로 보인다.
            if (!weapon.CanFire) return;

            // 조준이 다 찼다. 여기서부터 방아쇠는 연사가 맡는다. 같은 프레임에 첫 발이 나가야
            // 예고가 끝나는 순간과 총성이 어긋나지 않는다.
            BeginBurst();
            TickBurst(dt);
        }

        /// <summary>보이고, 사거리 안이다. 둘 중 하나라도 아니면 조준은 차지 않는다.</summary>
        private bool HasShot()
        {
            // 보이지 않으면 쏘지 않는다. 감각이 없는 경비는 영영 쏘지 못하는 쪽이 안전하다.
            if (perception == null || !perception.CanSeePlayer) return false;

            return Sight.HorizontalDistance(transform.position, AimPoint) <= settings.fireRange;
        }

        /// <summary>겨누기가 끝났다. 여기서부터는 조준을 다시 세지 않는다.</summary>
        private void BeginBurst()
        {
            IsFiring = true;
            IsAiming = false;
            AimProgress = 0f;
            _aimTimer = 0f;

            _burstLeft = Mathf.Max(1, settings.burstCount);
            _shotTimer = 0f;
            _burstTimeLeft = BurstTimeout();
        }

        private void TickBurst(float dt)
        {
            // 탄창이 비어 장전이 걸리면 연사는 거기서 끝난다. 장전을 기다렸다 이어 쏘면
            // 몇 초 전에 끝난 예고로 총알이 나가는 꼴이 된다. 다시 겨누는 편이 정직하다.
            if (weapon.IsReloading)
            {
                EndBurst();
                return;
            }

            // 총이 아직 안 된다고 하면(제 fireInterval이 남았다) 간격을 다시 세지 않고 그냥 둔다.
            // _shotTimer가 0 아래에 머무르니 다음 프레임에 곧장 다시 두드린다.
            _shotTimer -= dt;
            if (_shotTimer <= 0f && Shoot())
            {
                _burstLeft--;
                if (_burstLeft <= 0)
                {
                    EndBurst();
                    return;
                }

                _shotTimer = Mathf.Max(0f, settings.burstInterval);
            }

            // 총 쪽 fireInterval이 burstInterval보다 길면 발이 계속 밀린다. 상한이 없으면 그대로
            // 연사에 매달려 회복도 조준도 못 한다. 남은 발을 버리더라도 반드시 끝낸다.
            // 상한을 한 발 두드려 본 뒤에 보는 것은 여유를 0으로 잡아도 첫 발은 나가게 하려는 것이다.
            _burstTimeLeft -= dt;
            if (_burstTimeLeft <= 0f) EndBurst();
        }

        /// <summary>한 발. 총이 거절하면 false.</summary>
        private bool Shoot()
        {
            Vector3 origin = FireOrigin();
            Vector3 direction = AimPoint - origin;
            if (direction.sqrMagnitude < 0.0001f) return false;

            if (!weapon.TryFire(origin, direction.normalized)) return false;

            if (Fired != null) Fired();
            return true;
        }

        /// <summary>연사를 끝내고 회복으로 넘긴다. 다 쏘고 끝나든 중간에 끊기든 쉬는 틈은 똑같이 준다.</summary>
        private void EndBurst()
        {
            CancelBurst();
            _recoverTimer = Mathf.Max(0f, settings.recoverSeconds);
        }

        /// <summary>연사 상태만 지운다. 회복은 걸지 않는다. 전투 자체가 끝날 때 쓴다.</summary>
        private void CancelBurst()
        {
            IsFiring = false;
            _burstLeft = 0;
            _shotTimer = 0f;
            _burstTimeLeft = 0f;
        }

        /// <summary>
        /// 연사 한 번에 허용하는 시간. 실제로 발을 미루는 쪽은 둘 중 긴 간격이라 그걸로 잡고,
        /// 간격이 0이어도 상한이 0이 되지 않도록 여유를 더한다.
        /// </summary>
        private float BurstTimeout()
        {
            float step = Mathf.Max(0f, settings.burstInterval);
            if (weapon.Settings != null) step = Mathf.Max(step, weapon.Settings.fireInterval);

            int shots = Mathf.Max(1, settings.burstCount);
            return shots * step + Mathf.Max(0f, burstGraceSeconds);
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

            // 조준이 지워졌는데 연사만 남아 있으면 겨누지 않은 총이 계속 나간다.
            CancelBurst();
        }

        private void HandleRunReset()
        {
            Disengage();
            _recoverTimer = 0f;
            if (weapon != null) weapon.RefillAmmo();
        }
    }
}
