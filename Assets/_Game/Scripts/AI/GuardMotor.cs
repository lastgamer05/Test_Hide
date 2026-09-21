using UnityEngine;
using UnityEngine.AI;

namespace ByAWhisker.AI
{
    /// <summary>
    /// NavMeshAgent 래퍼. 두뇌가 에이전트를 직접 만지지 않게 해서
    /// 나중에 이동 방식을 바꿔도 GuardBrain은 그대로 둘 수 있다.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class GuardMotor : MonoBehaviour
    {
        [Tooltip("FaceTowards가 도는 최소 각도 문턱. 너무 작은 방향은 무시한다.")]
        [SerializeField] private float minTurnDirection = 0.01f;

        private NavMeshAgent _agent;

        public Vector3 Position
        {
            get { return transform.position; }
        }

        public Vector3 Forward
        {
            get { return transform.forward; }
        }

        /// <summary>경로를 아직 계산 중이면 남은 거리가 거짓이라 pathPending을 먼저 본다.</summary>
        public bool ReachedDestination
        {
            get
            {
                if (_agent == null || !_agent.isOnNavMesh) return true;
                if (_agent.pathPending) return false;
                return _agent.remainingDistance <= _agent.stoppingDistance;
            }
        }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
        }

        public void MoveTo(Vector3 destination, float speed)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            _agent.speed = speed;
            _agent.isStopped = false;
            // 이동 중에는 에이전트가 진행 방향으로 돌게 두고, FaceTowards가 뺏어간 회전권을 돌려준다.
            _agent.updateRotation = true;
            _agent.SetDestination(destination);
        }

        public void Stop()
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            _agent.ResetPath();
        }

        /// <summary>y를 무시한 수평 방향으로만 돌린다. 위아래로 기울면 캡슐이 누워 보인다.</summary>
        public void FaceTowards(Vector3 worldPosition, float turnSpeed)
        {
            Vector3 direction = worldPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < minTurnDirection) return;

            // 직접 돌리는 동안에는 에이전트의 자동 회전을 꺼야 서로 싸우지 않는다.
            if (_agent != null) _agent.updateRotation = false;

            Quaternion wanted = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float t = 1f - Mathf.Exp(-turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, wanted, t);
        }

        /// <summary>
        /// 재시작용 순간이동. 에이전트를 켠 채 transform만 옮기면 내부 위치와 어긋나서 Warp를 쓴다.
        /// 계약에는 없지만 NavMeshAgent를 여기 가둬 두려고 래퍼에 둔다.
        /// </summary>
        public void Warp(Vector3 position, Quaternion rotation)
        {
            transform.rotation = rotation;

            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
                _agent.velocity = Vector3.zero;
                if (_agent.Warp(position)) return;
            }

            transform.position = position;
        }
    }
}
