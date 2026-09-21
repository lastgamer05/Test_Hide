using UnityEngine;
using ByAWhisker.Data;

namespace ByAWhisker.Player
{
    /// <summary>
    /// 화면 기준으로 움직이고 마우스 쪽을 바라본다.
    /// 카메라에서 받는 값은 각도 하나뿐이라 서로 얽히지 않는다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private MovementSettings settings;
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerStance stance;
        [Tooltip("이동 방향의 기준이 되는 카메라. 비우면 메인 카메라를 쓴다.")]
        [SerializeField] private Camera viewCamera;
        [Tooltip("켜면 이동 방향을 바라본다. 끄면 마우스 쪽을 바라본다. 켜면 제자리 둘러보기와 엄폐 엿보기가 불가능해진다.")]
        [SerializeField] private bool faceMovementDirection;

        private CharacterController _controller;
        private Vector3 _planarVelocity;
        private float _verticalVelocity;

        /// <summary>지금 내는 소리의 크기. M4의 소리 시스템이 읽어 간다.</summary>
        public float NoiseLevel { get; private set; }

        public float CurrentSpeed { get { return _planarVelocity.magnitude; } }
        public bool IsRunning { get; private set; }

        /// <summary>수평 속도 벡터. 카메라가 이동 방향을 앞당길 때 쓴다.</summary>
        public Vector3 PlanarVelocity { get { return _planarVelocity; } }

        /// <summary>눈 위치. M3의 시야 계산이 읽어 간다.</summary>
        public Vector3 EyePosition
        {
            get { return transform.position + Vector3.up * (stance != null ? stance.EyeHeight : 1.55f); }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (viewCamera == null) viewCamera = Camera.main;
        }

        private void OnEnable()
        {
            if (input != null) input.CrouchToggled += OnCrouchToggled;
        }

        private void OnDisable()
        {
            if (input != null) input.CrouchToggled -= OnCrouchToggled;
        }

        private void OnCrouchToggled()
        {
            if (stance != null) stance.Toggle();
        }

        private void Update()
        {
            if (settings == null || input == null) return;
            if (viewCamera == null) viewCamera = Camera.main;

            MoveStep();
            FaceCursor();
            UpdateNoise();
        }

        private void MoveStep()
        {
            Vector3 forward = Flatten(viewCamera != null ? viewCamera.transform.forward : Vector3.forward);
            Vector3 right = Flatten(viewCamera != null ? viewCamera.transform.right : Vector3.right);

            Vector2 move = input.Move;
            Vector3 wish = forward * move.y + right * move.x;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            bool crouching = stance != null && stance.IsCrouching;
            IsRunning = input.RunHeld && !crouching && wish.sqrMagnitude > 0.01f;

            float targetSpeed = crouching ? settings.crouchSpeed : (IsRunning ? settings.runSpeed : settings.walkSpeed);
            Vector3 targetVelocity = wish * targetSpeed;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVelocity, settings.acceleration * Time.deltaTime);

            _verticalVelocity = _controller.isGrounded ? -2f : _verticalVelocity - 9.81f * Time.deltaTime;

            Vector3 motion = _planarVelocity;
            motion.y = _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);
        }

        private void FaceCursor()
        {
            if (faceMovementDirection)
            {
                FaceMovement();
                return;
            }

            if (viewCamera == null) return;

            Ray ray = viewCamera.ScreenPointToRay(input.PointerPosition);
            var ground = new Plane(Vector3.up, new Vector3(0f, transform.position.y + 1f, 0f));

            float distance;
            if (!ground.Raycast(ray, out distance)) return;

            Vector3 target = ray.GetPoint(distance);
            Vector3 direction = Flatten(target - transform.position);
            if (direction.sqrMagnitude < 0.01f) return;

            Quaternion wanted = Quaternion.LookRotation(direction);
            float t = 1f - Mathf.Exp(-settings.turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, wanted, t);
        }

        /// <summary>이동 방향을 바라본다. 멈추면 마지막 방향을 유지한다.</summary>
        private void FaceMovement()
        {
            Vector3 direction = Flatten(_planarVelocity);
            if (direction.sqrMagnitude < 0.01f) return;

            Quaternion wanted = Quaternion.LookRotation(direction);
            float t = 1f - Mathf.Exp(-settings.turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, wanted, t);
        }

        private void UpdateNoise()
        {
            bool moving = _planarVelocity.sqrMagnitude > 0.05f;
            if (!moving)
            {
                NoiseLevel = 0f;
                return;
            }

            bool crouching = stance != null && stance.IsCrouching;
            NoiseLevel = crouching ? settings.crouchNoise : (IsRunning ? settings.runNoise : settings.walkNoise);
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }
    }
}
