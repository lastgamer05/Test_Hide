using UnityEngine;
using ByAWhisker.Player;

namespace ByAWhisker.Cameras
{
    /// <summary>
    /// 거리와 내려보는 각을 고정한 채 대상을 따라가고, 수평으로만 90도씩 돈다.
    /// 시선 앞당김 덕분에 캐릭터가 화면 중앙보다 조금 아래에 놓이고 앞쪽이 넓게 보인다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class QuarterViewCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private PlayerInputReader input;

        [Header("고정 값")]
        [SerializeField] private float distance = 10.5f;
        [SerializeField] private float pitch = 52f;
        [SerializeField] private float fieldOfView = 45f;
        [Tooltip("시작 각도. 45도면 격자 벽이 화면에 대각선으로 보인다.")]
        [SerializeField] private float startYaw = 45f;

        [Header("회전")]
        [SerializeField] private float stepDegrees = 90f;
        [SerializeField] private float rotateSeconds = 0.25f;

        [Header("집중")]
        // 아래 둘은 Q를 누르는 "집중 상태"에 쓰는 값이다. 따라가기의 focusHeight와 이름만 닮았지
        // 하는 일이 다르다. 그쪽은 카메라가 바라보는 점의 높이고, 집중과는 아무 관계가 없다.
        [Tooltip("집중했을 때 다가갈 거리. 평소 distance와 이 값 사이를 SetFocus가 섞는다.")]
        [SerializeField] private float focusDistance = 6.5f;
        [Tooltip("집중했을 때 좁아질 시야각. 평소 fieldOfView와 이 값 사이를 섞는다.")]
        [SerializeField] private float focusFieldOfView = 38f;

        [Header("따라가기")]
        [Tooltip("이동 방향으로 초점을 당기는 거리")]
        [SerializeField] private float lookAhead = 2f;
        [Tooltip("카메라가 바라보는 점의 높이. 발밑이 아니라 몸통을 본다. 위 '집중' 값들과는 무관하다.")]
        [SerializeField] private float focusHeight = 1f;
        [Tooltip("초점을 따라가는 속도. 낮을수록 부드럽다")]
        [SerializeField] private float followLerp = 5f;
        [Tooltip("앞당김 방향이 바뀌는 속도. 낮을수록 방향 전환이 덜 어지럽다")]
        [SerializeField] private float lookAheadTurnLerp = 2.5f;
        [Tooltip("이보다 가까운 초점 차이는 무시한다. 제자리 미세 흔들림을 없앤다")]
        [SerializeField] private float deadZone = 0.35f;
        [Tooltip("켜면 이동 방향을, 끄면 바라보는 방향을 앞당긴다")]
        [SerializeField] private bool leadWithMovement = true;
        [SerializeField] private PlayerMotor motor;

        private Camera _camera;
        private Vector3 _focus;
        private Vector3 _leadDirection;
        private float _currentYaw;
        private float _fromYaw;
        private float _targetYaw;
        private float _rotateTimer = -1f;

        // 지금 집중 정도. 0..1. FocusSense가 매 프레임 넣어 준다.
        private float _focusAmount;

        /// <summary>지금 카메라의 수평 각도. 이동이 화면 기준 방향을 구할 때 쓴다.</summary>
        public float Yaw { get { return _currentYaw; } }

        public Vector3 PlanarForward
        {
            get
            {
                Vector3 forward = Quaternion.Euler(0f, _currentYaw, 0f) * Vector3.forward;
                return forward;
            }
        }

        public Vector3 PlanarRight
        {
            get { return Quaternion.Euler(0f, _currentYaw, 0f) * Vector3.right; }
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.fieldOfView = fieldOfView;

            _currentYaw = startYaw;
            _targetYaw = startYaw;
            _fromYaw = startYaw;

            if (target != null) SnapToTarget();
        }

        private void OnEnable()
        {
            if (input != null) input.CameraRotateRequested += RotateStep;
        }

        private void OnDisable()
        {
            if (input != null) input.CameraRotateRequested -= RotateStep;
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            if (target == null) return;

            if (motor == null) motor = target.GetComponent<PlayerMotor>();
            _leadDirection = Flatten(target.forward);
            SnapToTarget();
        }

        /// <summary>
        /// 집중 정도를 받는다. 0은 평소, 1은 끝까지 집중. 보간은 넣어 주는 쪽이 이미 해 뒀다.
        /// 회전과 마찬가지로 카메라는 입력을 스스로 읽지 않는다 — 여기서는 값을 섞기만 한다.
        /// </summary>
        public void SetFocus(float amount)
        {
            _focusAmount = Mathf.Clamp01(amount);
        }

        /// <summary>한 칸 돌린다. -1은 왼쪽, +1은 오른쪽.</summary>
        public void RotateStep(int direction)
        {
            if (direction == 0) return;

            _fromYaw = _currentYaw;
            _targetYaw += stepDegrees * Mathf.Sign(direction);
            _rotateTimer = 0f;
        }

        /// <summary>보간 없이 지금 위치로 붙는다.</summary>
        public void SnapToTarget()
        {
            _focus = DesiredFocus();
            Apply();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            UpdateYaw();
            UpdateLeadDirection();

            Vector3 desired = DesiredFocus();
            Vector3 gap = desired - _focus;

            // 죽은 구역 안이면 아예 따라가지 않는다. 제자리에서 카메라가 떠는 걸 막는다.
            if (gap.sqrMagnitude > deadZone * deadZone)
            {
                float t = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
                _focus = Vector3.Lerp(_focus, desired, t);
            }

            Apply();
        }

        /// <summary>
        /// 앞당김 방향을 천천히 돌린다. 마우스를 휙 돌려도 카메라는 서서히 따라간다.
        /// </summary>
        private void UpdateLeadDirection()
        {
            Vector3 wanted;

            if (leadWithMovement && motor != null && motor.PlanarVelocity.sqrMagnitude > 0.25f)
            {
                wanted = Flatten(motor.PlanarVelocity);
            }
            else if (leadWithMovement)
            {
                // 멈춰 있으면 방향을 유지한다. 제자리 회전으로는 카메라가 움직이지 않는다.
                wanted = _leadDirection;
            }
            else
            {
                wanted = Flatten(target.forward);
            }

            if (wanted.sqrMagnitude < 0.0001f) return;

            float t = 1f - Mathf.Exp(-lookAheadTurnLerp * Time.deltaTime);
            _leadDirection = Vector3.Slerp(_leadDirection.sqrMagnitude < 0.0001f ? wanted : _leadDirection, wanted, t);
        }

        private void UpdateYaw()
        {
            if (_rotateTimer < 0f)
            {
                _currentYaw = _targetYaw;
                return;
            }

            _rotateTimer += Time.deltaTime;
            float k = rotateSeconds <= 0f ? 1f : Mathf.Clamp01(_rotateTimer / rotateSeconds);
            _currentYaw = Mathf.Lerp(_fromYaw, _targetYaw, Mathf.SmoothStep(0f, 1f, k));

            if (k >= 1f)
            {
                _currentYaw = _targetYaw;
                _rotateTimer = -1f;
            }
        }

        private Vector3 DesiredFocus()
        {
            Vector3 lead = _leadDirection.sqrMagnitude > 0.0001f ? _leadDirection : Flatten(target.forward);
            return target.position + Vector3.up * focusHeight + lead * lookAhead;
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero;
        }

        private void Apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, _currentYaw, 0f);
            transform.rotation = rotation;
            transform.position = _focus - rotation * Vector3.forward * Mathf.Lerp(distance, focusDistance, _focusAmount);

            // 시야각도 같은 자리에서 매 프레임 넣는다. 집중이 풀리는 동안에도 값이 계속 움직이는데,
            // 거리만 따라 움직이면 다가서는 느낌과 좁아지는 느낌이 어긋난다.
            if (_camera != null) _camera.fieldOfView = Mathf.Lerp(fieldOfView, focusFieldOfView, _focusAmount);
        }
    }
}
