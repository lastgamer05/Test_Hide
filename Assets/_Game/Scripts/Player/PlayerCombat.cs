using UnityEngine;
using ByAWhisker.Combat;
using ByAWhisker.Core;

namespace ByAWhisker.Player
{
    /// <summary>
    /// 입력을 총과 제압에 잇는다. 판정은 각자 하고 여기서는 언제 부를지만 정한다.
    /// 조준은 마우스가 가리키는 바닥 점을 향한다. 카메라 기준 이동과 같은 규칙이다.
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerStance stance;
        [SerializeField] private Weapon weapon;
        [SerializeField] private TakedownAction takedown;
        [SerializeField] private BodyCarry carry;
        [Tooltip("총구 섬광으로 몸이 드러나는 쪽. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayerExposure exposure;
        [Tooltip("총구 높이. 눈보다 조금 아래다.")]
        [SerializeField] private float muzzleHeight = 1.2f;
        [Tooltip("조준 평면의 높이. 발밑에서 이만큼 위다. PlayerMotor가 커서를 볼 때 쓰는 평면과 같아야 몸과 총알이 같은 곳을 본다.")]
        [SerializeField] private float aimPlaneHeight = 1f;
        [Tooltip("쏠 때 몸을 조준 방향으로 돌린다. 등 뒤로 쏘는 그림을 막는다.")]
        [SerializeField] private bool turnToAim = true;

        private Camera _camera;
        private ByAWhisker.UI.ControlsOverlay _overlay;

        /// <summary>지금 겨누고 있는 수평 방향. 화면 표시나 애니메이션이 읽어 갈 수 있다.</summary>
        public Vector3 AimDirection { get; private set; }

        /// <summary>
        /// 두 손이 막힌 상태. 몸을 들고 있거나 상자 안에 숨어 있으면 쏘지도, 쏘려고 몸을 돌리지도 않는다.
        /// 상자 안에서 총구 섬광이 터지면 숨은 뜻이 없어진다.
        /// </summary>
        private bool Carrying { get { return carry != null && (carry.IsCarrying || carry.IsHiding); } }

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (stance == null) stance = GetComponent<PlayerStance>();
            if (weapon == null) weapon = GetComponent<Weapon>();
            if (takedown == null) takedown = GetComponent<TakedownAction>();
            if (carry == null) carry = GetComponent<BodyCarry>();
            if (exposure == null) exposure = GetComponent<PlayerExposure>();

            AimDirection = transform.forward;

            // 구독을 OnEnable이 아니라 Awake에서 한다. 플레이어가 총에 맞으면 Damageable이 이 컴포넌트를
            // 끄는데, 그러면 OnDisable에서 구독이 풀려 재시작 신호를 놓친다. Lamp와 같은 이유다.
            GameEvents.RunReset += HandleRunReset;
        }

        private void OnDestroy()
        {
            GameEvents.RunReset -= HandleRunReset;
        }

        /// <summary>
        /// 재시작하면 탄약도 판을 시작할 때로 돌린다. 경비 총은 GuardGunner가 같은 일을 한다.
        /// 주운 탄약(AmmoPickup)도 재시작에 제자리로 돌아오므로, 이걸 안 하면 잡혔다 다시 줍기를
        /// 되풀이해 탄을 무한히 모을 수 있다. 탄이 세 발뿐인 게임에서 그 구멍은 치명적이다.
        /// </summary>
        private void HandleRunReset()
        {
            if (weapon != null) weapon.RefillAmmo();
        }

        private void OnEnable()
        {
            if (input == null) return;
            input.ReloadRequested += OnReload;
            input.TakedownRequested += OnTakedown;
            input.CarryRequested += OnCarry;
            if (weapon != null) weapon.Fired += OnWeaponFired;
        }

        private void OnDisable()
        {
            if (input == null) return;
            input.ReloadRequested -= OnReload;
            input.TakedownRequested -= OnTakedown;
            input.CarryRequested -= OnCarry;
            if (weapon != null) weapon.Fired -= OnWeaponFired;
        }

        private void Update()
        {
            if (input == null) return;

            UpdateAim();

            if (input.FireHeld && !Carrying && weapon != null)
            {
                // 연사 간격은 총이 알아서 지킨다. 여기서는 누르고 있다는 것만 전한다.
                weapon.TryFire(MuzzlePosition(), AimDirection);
            }
        }

        private void UpdateAim()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            Vector3 point;
            float planeY = transform.position.y + aimPlaneHeight;
            if (!AimSolver.TryAimPoint(_camera, input.PointerPosition, planeY, out point)) return;

            AimDirection = AimSolver.AimDirection(MuzzlePosition(), point, AimDirection);

            // 쏘는 동안에만 돌린다. 평소에도 돌리면 이동 방향과 시선이 계속 싸운다.
            if (turnToAim && input.FireHeld && !Carrying)
            {
                transform.rotation = Quaternion.LookRotation(AimDirection, Vector3.up);
            }
        }

        private Vector3 MuzzlePosition()
        {
            // 앉으면 눈이 낮아지듯 총구도 낮아진다. 낮은 엄폐 뒤에서 쏘면 엄폐에 맞아야 한다.
            float height = stance != null && stance.IsCrouching ? muzzleHeight * 0.6f : muzzleHeight;
            return transform.position + Vector3.up * height;
        }

        private void OnReload()
        {
            if (weapon != null) weapon.Reload();
        }

        private void OnTakedown()
        {
            if (takedown == null) return;

            bool done = takedown.TryTakedown();

            // 실패도 알려 준다. 안 그러면 키가 먹은 건지 자리가 틀린 건지 알 수 없다.
            if (_overlay == null) _overlay = FindAnyObjectByType<ByAWhisker.UI.ControlsOverlay>();
            if (_overlay != null) _overlay.Flash(done ? "제압!" : "등 뒤로 더 가까이");
        }

        private void OnCarry()
        {
            if (carry == null) return;

            bool dropping = carry.IsCarrying;
            bool nearContainer = carry.HasContainer || carry.IsHiding;
            carry.Toggle();

            // 집기도 내려놓기도 한순간에 끝나서 글자가 없으면 키가 먹었는지 알 수 없다.
            if (_overlay == null) _overlay = FindAnyObjectByType<ByAWhisker.UI.ControlsOverlay>();
            if (_overlay == null) return;

            // 상자가 얽힌 경우는 BodyCarry가 자기 사정에 맞는 글을 이미 띄웠다.
            // 여기서 또 띄우면 한 프레임 뒤에 그것을 덮어써 엉뚱한 안내가 남는다.
            if (nearContainer) return;

            if (dropping) _overlay.Flash("내려놓았다");
            else _overlay.Flash(carry.IsCarrying ? "몸을 들었다" : "쓰러진 몸 쪽으로 더 가까이");
        }

        /// <summary>
        /// 쏜 자리가 드러난다. 어둠 속 탐지 거리가 5m뿐이라, 섬광이 없으면 소음기 권총이
        /// 사거리 11m 안에서 아무 대가 없이 이긴다.
        /// </summary>
        private void OnWeaponFired(Vector3 hitPoint)
        {
            if (exposure != null) exposure.FlashFromMuzzle();
        }
    }
}
