using UnityEngine;
using ByAWhisker.Combat;

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

        /// <summary>몸을 들고 있으면 두 손이 막힌다. 쏘지도, 쏘려고 몸을 돌리지도 않는다.</summary>
        private bool Carrying { get { return carry != null && carry.IsCarrying; } }

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
            carry.Toggle();

            // 집기도 내려놓기도 한순간에 끝나서 글자가 없으면 키가 먹었는지 알 수 없다.
            if (_overlay == null) _overlay = FindAnyObjectByType<ByAWhisker.UI.ControlsOverlay>();
            if (_overlay == null) return;

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
