using UnityEngine;
using ByAWhisker.Core;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 경비의 상태 기계. 판정은 GuardPerception, 이동은 GuardMotor가 하고
    /// 여기서는 "지금 무엇을 할지"만 고른다.
    /// </summary>
    [RequireComponent(typeof(GuardMotor))]
    public class GuardBrain : MonoBehaviour
    {
        public enum State { Patrol, Suspicious, Search, Alert, Attack, Stunned }

        [Header("참조")]
        [SerializeField] private GuardMotor motor;
        [SerializeField] private GuardPerception perception;
        [SerializeField] private PatrolRoute route;

        [Header("속도")]
        [SerializeField] private float patrolSpeed = 1.6f;
        [SerializeField] private float searchSpeed = 2.4f;
        [SerializeField] private float chaseSpeed = 3.8f;
        [SerializeField] private float turnSpeed = 8f;

        [Header("의심도 문턱")]
        [SerializeField] private float suspiciousThreshold = 0.3f;
        [SerializeField] private float searchThreshold = 0.6f;
        [Tooltip("의심도 1. 부동소수점이라 살짝 낮게 잡는다.")]
        [SerializeField] private float alertThreshold = 0.999f;

        [Header("시간과 거리")]
        [SerializeField] private float searchSeconds = 5f;
        [Tooltip("단서 지점까지 가는 데 이보다 오래 걸리면 포기한다.")]
        [SerializeField] private float searchTravelTimeout = 12f;
        [SerializeField] private float alertLoseSeconds = 4f;
        [SerializeField] private float attackRange = 1.8f;
        [Tooltip("공격 예고 시간. 이 사이에 도망치면 놓친다.")]
        [SerializeField] private float attackWindup = 0.75f;
        [SerializeField] private float lookAroundInterval = 1.2f;

        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private float _stateTimer;    // 지금 상태에 들어온 뒤 흐른 시간
        private float _lostTimer;     // Alert에서 플레이어를 못 본 시간
        private float _waitTimer;     // 순찰 지점에서 쉬는 시간
        private float _lookTimer;     // 둘러보기 방향을 바꿀 시간
        private float _stunRemaining;
        private int _patrolIndex;
        private bool _searchArrived;
        private Vector3 _lookTarget;

        public State Current { get; private set; }
        public event System.Action<State> StateChanged;

        private void Awake()
        {
            // 재시작 때 돌아올 자리를 여기서 기억한다.
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            if (motor == null) motor = GetComponent<GuardMotor>();
            if (perception == null) perception = GetComponent<GuardPerception>();

            Current = State.Patrol;
        }

        private void OnEnable()
        {
            GameEvents.RunReset += HandleRunReset;
        }

        private void OnDisable()
        {
            GameEvents.RunReset -= HandleRunReset;
        }

        /// <summary>뒤에서 기절당했을 때. 그동안 이동도 판정도 멈춘다.</summary>
        public void Stun(float seconds)
        {
            _stunRemaining = Mathf.Max(_stunRemaining, seconds);
            Enter(State.Stunned);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (Current == State.Stunned)
            {
                _stunRemaining -= dt;
                if (_stunRemaining <= 0f) Enter(State.Patrol);
                return;
            }

            if (perception != null) EvaluateEscalation();

            _stateTimer += dt;

            switch (Current)
            {
                case State.Patrol: TickPatrol(dt); break;
                case State.Suspicious: TickSuspicious(); break;
                case State.Search: TickSearch(dt); break;
                case State.Alert: TickAlert(dt); break;
                case State.Attack: TickAttack(); break;
            }
        }

        /// <summary>의심도와 소리로 상태를 올린다. 내리는 쪽은 각 상태가 알아서 한다.</summary>
        private void EvaluateEscalation()
        {
            float aware = perception.Awareness;

            if (aware >= alertThreshold)
            {
                // Attack은 Alert의 하위 행동이라 여기서 끌어내리지 않는다.
                if (Current != State.Alert && Current != State.Attack) Enter(State.Alert);
                return;
            }

            if (aware >= searchThreshold || perception.HeardSomething)
            {
                if (Current == State.Patrol || Current == State.Suspicious)
                {
                    Enter(State.Search);
                }
                else if (Current == State.Search && perception.HeardSomething)
                {
                    // 새 단서를 들었으니 둘러보기를 처음부터 다시 한다.
                    _stateTimer = 0f;
                    _searchArrived = false;
                }
                return;
            }

            if (aware >= suspiciousThreshold && Current == State.Patrol) Enter(State.Suspicious);
        }

        private void TickPatrol(float dt)
        {
            if (motor == null || route == null || route.Count == 0) return;

            if (_waitTimer > 0f)
            {
                _waitTimer -= dt;
                if (_waitTimer <= 0f)
                {
                    _patrolIndex++;
                    motor.MoveTo(route.PointAt(_patrolIndex), patrolSpeed);
                }
                return;
            }

            motor.MoveTo(route.PointAt(_patrolIndex), patrolSpeed);
            if (motor.ReachedDestination)
            {
                motor.Stop();
                _waitTimer = Mathf.Max(0.01f, route.waitSeconds);
            }
        }

        private void TickSuspicious()
        {
            if (motor != null && perception != null)
            {
                motor.FaceTowards(perception.LastKnownPosition, turnSpeed);
            }

            if (perception != null && perception.Awareness < suspiciousThreshold) Enter(State.Patrol);
        }

        private void TickSearch(float dt)
        {
            if (motor == null || perception == null)
            {
                Enter(State.Patrol);
                return;
            }

            if (!_searchArrived)
            {
                motor.MoveTo(perception.LastKnownPosition, searchSpeed);
                if (motor.ReachedDestination)
                {
                    _searchArrived = true;
                    _stateTimer = 0f;   // 도착한 뒤부터 둘러보는 시간을 센다
                    _lookTimer = 0f;
                    motor.Stop();
                }
                else if (_stateTimer >= searchTravelTimeout)
                {
                    Enter(State.Patrol);
                }
                return;
            }

            LookAround(dt);
            if (_stateTimer >= searchSeconds) Enter(State.Patrol);
        }

        /// <summary>제자리에서 방향을 바꿔 가며 훑어본다.</summary>
        private void LookAround(float dt)
        {
            _lookTimer -= dt;
            if (_lookTimer <= 0f)
            {
                _lookTimer = lookAroundInterval;
                float angle = Random.Range(-110f, 110f);
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * motor.Forward;
                _lookTarget = motor.Position + direction * 3f;
            }

            motor.FaceTowards(_lookTarget, turnSpeed);
        }

        private void TickAlert(float dt)
        {
            if (motor == null || perception == null)
            {
                Enter(State.Patrol);
                return;
            }

            if (perception.CanSeePlayer) _lostTimer = 0f;
            else _lostTimer += dt;

            if (InAttackRange())
            {
                Enter(State.Attack);
                return;
            }

            // 4초 동안 못 보면 마지막 자리를 뒤진다.
            if (_lostTimer >= alertLoseSeconds)
            {
                Enter(State.Search);
                return;
            }

            motor.MoveTo(perception.LastKnownPosition, chaseSpeed);
        }

        private void TickAttack()
        {
            if (motor != null && perception != null)
            {
                motor.FaceTowards(perception.LastKnownPosition, turnSpeed);
            }

            if (_stateTimer < attackWindup) return;

            // 예고가 끝난 순간에도 사거리 안이면 잡는다. 빠져나갔으면 다시 쫓는다.
            if (InAttackRange()) GameEvents.RaisePlayerCaptured(gameObject);

            Enter(State.Alert);
        }

        /// <summary>
        /// 플레이어 트랜스폼을 직접 들고 있지 않으니, 보이는 동안 갱신되는
        /// LastKnownPosition을 현재 위치로 본다.
        /// </summary>
        private bool InAttackRange()
        {
            if (perception == null || motor == null || !perception.CanSeePlayer) return false;
            return HorizontalDistance(motor.Position, perception.LastKnownPosition) <= attackRange;
        }

        private void HandleRunReset()
        {
            if (motor != null) motor.Warp(_spawnPosition, _spawnRotation);

            if (perception != null)
            {
                perception.enabled = true;
                perception.ResetAwareness();
            }

            _patrolIndex = 0;
            _stunRemaining = 0f;
            Enter(State.Patrol, true);
        }

        private void Enter(State next, bool force = false)
        {
            if (!force && Current == next) return;

            // 기절에서 깨어나면 감각을 다시 켠다.
            if (Current == State.Stunned && next != State.Stunned && perception != null)
            {
                perception.enabled = true;
            }

            Current = next;
            _stateTimer = 0f;
            _lostTimer = 0f;
            _waitTimer = 0f;
            _lookTimer = 0f;
            _searchArrived = false;

            switch (next)
            {
                case State.Patrol:
                    if (motor != null && route != null && route.Count > 0)
                    {
                        motor.MoveTo(route.PointAt(_patrolIndex), patrolSpeed);
                    }
                    break;

                case State.Suspicious:
                case State.Attack:
                    if (motor != null) motor.Stop();
                    break;

                case State.Stunned:
                    if (motor != null) motor.Stop();
                    if (perception != null) perception.enabled = false;
                    break;
            }

            if (StateChanged != null) StateChanged(next);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
