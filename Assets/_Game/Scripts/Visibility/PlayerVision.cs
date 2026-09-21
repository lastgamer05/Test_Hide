using UnityEngine;
using ByAWhisker.Player;

namespace ByAWhisker.Visibility
{
    /// <summary>
    /// 캐릭터가 지금 보는 영역을 폴리곤으로 만든다.
    /// 적을 숨길지 정하는 판정과 화면에 그릴 마스크가 같은 규칙을 쓰도록 여기 한곳에 둔다.
    /// </summary>
    public class PlayerVision : MonoBehaviour
    {
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerStance stance;
        [Tooltip("항상 시선을 막는 레이어. 벽과 높은 엄폐")]
        [SerializeField] private LayerMask blockers;
        [Tooltip("앉았을 때만 시선을 막는 레이어. 낮은 엄폐")]
        [SerializeField] private LayerMask lowCover;

        [Header("시야")]
        [SerializeField] private float coneDegrees = 100f;
        [SerializeField] private float radius = 26f;
        [Tooltip("몸 주변은 방향과 무관하게 이만큼 감지한다")]
        [SerializeField] private float nearRadius = 2.4f;
        [SerializeField] private int rayCount = 96;
        [Tooltip("다시 계산하는 간격. 0이면 매 프레임")]
        [SerializeField] private float updateInterval = 0f;

        private Mesh _mesh;
        private Vector3[] _buffer;
        private float _nextUpdate;
        private int _vertexCount;

        public Mesh PolygonMesh { get { return _mesh; } }
        public Vector3 Origin { get; private set; }
        public float Yaw { get; private set; }
        public float Radius { get { return radius; } }

        /// <summary>폴리곤을 다시 만든 프레임에 부른다.</summary>
        public event System.Action PolygonUpdated;

        private void Awake()
        {
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (stance == null) stance = GetComponent<PlayerStance>();

            _mesh = new Mesh();
            _mesh.name = "PlayerVisionPolygon";
            _mesh.MarkDynamic();

            _buffer = new Vector3[1 + Mathf.Max(2, rayCount) + VisionPolygon.NearRayCount];

            Rebuild();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        private void LateUpdate()
        {
            // 이동과 회전이 끝난 뒤에 계산해야 한 프레임 밀리지 않는다.
            if (updateInterval > 0f && Time.time < _nextUpdate) return;
            _nextUpdate = Time.time + updateInterval;

            Rebuild();
        }

        private void Rebuild()
        {
            Vector3 eye = EyePosition();
            Yaw = transform.eulerAngles.y;

            _vertexCount = VisionPolygon.Build(
                eye, Yaw, coneDegrees, radius, nearRadius, rayCount, CurrentBlockers(), _buffer);

            VisionPolygon.FillMesh(_mesh, _buffer, _vertexCount, eye);

            // 메시는 평면에 눕혀 두고, 그릴 때 이 원점으로 옮긴다.
            Origin = new Vector3(eye.x, 0f, eye.z);

            if (PolygonUpdated != null) PolygonUpdated();
        }

        /// <summary>
        /// 각도, 거리, 시선 세 가지로 직접 판정한다.
        /// 폴리곤을 다시 훑는 것보다 싸고, 적이 몇 명이든 레이 하나로 끝난다.
        /// </summary>
        public bool IsVisible(Vector3 worldPoint)
        {
            Vector3 eye = EyePosition();

            Vector3 delta = worldPoint - eye;
            float horizontal = new Vector2(delta.x, delta.z).magnitude;
            if (horizontal > radius) return false;

            // 몸 주변 원 안이면 등 뒤라도 기척으로 안다. 각도 조건을 건너뛴다.
            if (horizontal > nearRadius)
            {
                float angle = Mathf.Abs(Mathf.DeltaAngle(Yaw, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg));
                if (angle > coneDegrees * 0.5f) return false;
            }

            return !Physics.Linecast(eye, worldPoint, CurrentBlockers(), QueryTriggerInteraction.Ignore);
        }

        private Vector3 EyePosition()
        {
            if (motor != null) return motor.EyePosition;

            float height = stance != null ? stance.EyeHeight : 1.55f;
            return transform.position + Vector3.up * height;
        }

        /// <summary>앉으면 낮은 엄폐도 시선을 막는다. 서 있으면 넘겨다본다.</summary>
        private int CurrentBlockers()
        {
            bool crouching = stance != null && stance.IsCrouching;
            return crouching ? (blockers.value | lowCover.value) : blockers.value;
        }

        private void OnDrawGizmosSelected()
        {
            if (_mesh == null || _vertexCount < 3) return;

            Gizmos.color = new Color(0.31f, 0.82f, 0.77f, 0.35f);
            Gizmos.DrawMesh(_mesh, Origin);
        }
    }
}
