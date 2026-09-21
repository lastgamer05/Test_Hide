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

        [Header("따라가기")]
        [Tooltip("바라보는 방향으로 초점을 당기는 거리")]
        [SerializeField] private float lookAhead = 2.5f;
        [SerializeField] private float focusHeight = 1f;
        [SerializeField] private float followLerp = 10f;

        private Camera _camera;
        private Vector3 _focus;
        private float _currentYaw;
        private float _fromYaw;
        private float _targetYaw;
        private float _rotateTimer = -1f;

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
            if (target != null) SnapToTarget();
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

            float t = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
            _focus = Vector3.Lerp(_focus, DesiredFocus(), t);

            Apply();
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
            Vector3 facing = target.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f) facing.Normalize();

            return target.position + Vector3.up * focusHeight + facing * lookAhead;
        }

        private void Apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, _currentYaw, 0f);
            transform.rotation = rotation;
            transform.position = _focus - rotation * Vector3.forward * distance;
        }
    }
}
