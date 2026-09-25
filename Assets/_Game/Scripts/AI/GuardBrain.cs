using UnityEngine;
using ByAWhisker.Combat;
using ByAWhisker.Core;
using ByAWhisker.Senses;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 경비의 상태 기계. 판정은 GuardPerception, 이동은 GuardMotor가 하고
    /// 여기서는 "지금 무엇을 할지"만 고른다.
    /// </summary>
    [RequireComponent(typeof(GuardMotor))]
    public class GuardBrain : MonoBehaviour
    {
        // TakeCover를 끝에 붙인 이유는, 중간에 끼우면 씬에 저장된 다른 상태 값이 한 칸씩 밀리기 때문이다.
        public enum State { Patrol, Suspicious, Search, Alert, Attack, Stunned, TakeCover }

        [Header("참조")]
        [SerializeField] private GuardMotor motor;
        [SerializeField] private GuardPerception perception;
        [SerializeField] private PatrolRoute route;
        [Tooltip("사격 담당. 비어 있으면 예전처럼 붙어서 잡는 것만 한다.")]
        [SerializeField] private GuardGunner gunner;
        [Tooltip("엄폐 담당. 비어 있으면 총을 맞아도 예전처럼 벌판에서 쫓는다.")]
        [SerializeField] private GuardCover cover;

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

        [Header("엄폐")]
        [Tooltip("엄폐 자리까지 가는 데 이보다 오래 걸리면 포기하고 다시 쫓는다.")]
        [SerializeField] private float coverTravelTimeout = 4f;
        [Tooltip("한 번 숨은 뒤 다시 숨기까지의 최소 간격. 없으면 총알마다 엄폐물 사이를 왕복한다.")]
        [SerializeField] private float coverCooldown = 6f;
        [Tooltip("총성이 들리는 거리 배율. 총성은 발소리와 달리 프로필의 약한 귀를 거치지 않고 그대로 듣는다.")]
        [SerializeField] private float gunshotHearScale = 1f;

        [Header("외침")]
        [Tooltip("소리를 낼 NoiseEmitter. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private NoiseEmitter noise;

        [Tooltip("발각을 알리는 외침의 크기. 총성에 준해야 동료가 듣고 온다. 0..1")]
        [Range(0f, 1f)]
        [SerializeField] private float shoutLoudness = 1f;

        [Tooltip("NoiseEmitter가 없을 때만 쓰는 반경(m). 붙어 있으면 그쪽의 크기당 반경을 따른다.")]
        [SerializeField] private float shoutRadius = 12f;

        [Tooltip("한 번 외친 뒤 다시 외치기까지의 최소 간격(초). 없으면 Alert를 들락거릴 때마다 외친다.")]
        [SerializeField] private float shoutCooldown = 6f;

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

        private Damageable _body;
        private bool _underFire;      // 이번에 총격을 받았다. 한 프레임짜리 신호라 Update가 소비한다
        private float _coverReadyTime; // 이 시각이 지나야 다시 숨는다
        private float _shoutReadyTime; // 이 시각이 지나야 다시 외친다

        public State Current { get; private set; }
        public event System.Action<State> StateChanged;

        private void Awake()
        {
            // 재시작 때 돌아올 자리를 여기서 기억한다.
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            if (motor == null) motor = GetComponent<GuardMotor>();
            if (perception == null) perception = GetComponent<GuardPerception>();
            if (gunner == null) gunner = GetComponent<GuardGunner>();
            if (cover == null) cover = GetComponent<GuardCover>();
            // 외침도 발소리와 같은 통로로 낸다. 없으면 Shout이 NoiseBus를 직접 부른다.
            if (noise == null) noise = GetComponent<NoiseEmitter>();

            // 콜라이더가 자식에 달려 있어도 맞은 몸은 부모 하나다.
            _body = GetComponentInParent<Damageable>();

            Current = State.Patrol;
        }

        private void OnEnable()
        {
            GameEvents.RunReset += HandleRunReset;

            // 총격을 두 갈래로 잡는다. 맞은 것과 들은 것이다. 빗나간 총알은 Damaged가 오지 않고,
            // 소음기를 단 총은 소리가 작아 못 들을 수 있어서 둘 중 하나로는 모자란다.
            NoiseBus.Emitted += HandleNoise;
            if (_body != null) _body.Damaged += HandleDamaged;
        }

        private void OnDisable()
        {
            GameEvents.RunReset -= HandleRunReset;

            NoiseBus.Emitted -= HandleNoise;
            if (_body != null) _body.Damaged -= HandleDamaged;
        }

        /// <summary>뒤에서 기절당했을 때. 그동안 이동도 판정도 멈춘다.</summary>
        public void Stun(float seconds)
        {
            _stunRemaining = Mathf.Max(_stunRemaining, seconds);
            // 기절 중에 조준선이 남아 있으면 쓰러진 경비가 계속 겨누는 것처럼 보인다.
            if (gunner != null) gunner.Disengage();
            Enter(State.Stunned);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (Current == State.Stunned)
            {
                // 기절 중에 받은 총격은 깨어난 뒤에 쓸 신호가 아니다. 여기서 버린다.
                _underFire = false;
                _stunRemaining -= dt;
                if (_stunRemaining <= 0f) Enter(State.Patrol);
                return;
            }

            if (perception != null) EvaluateEscalation();

            // 의심도로 상태를 올린 다음에 본다. 방금 Alert가 된 경비도 같은 프레임에 숨을 수 있게.
            TryTakeCover();

            _stateTimer += dt;

            switch (Current)
            {
                case State.Patrol: TickPatrol(dt); break;
                case State.Suspicious: TickSuspicious(); break;
                case State.Search: TickSearch(dt); break;
                case State.Alert: TickAlert(dt); break;
                case State.Attack: TickAttack(); break;
                case State.TakeCover: TickTakeCover(); break;
            }
        }

        /// <summary>
        /// 총격을 받았을 때 숨을지 고른다. 신호는 한 프레임짜리라 숨든 안 숨든 여기서 지운다.
        /// 이미 Alert나 Attack일 때만 본다. 아직 아무것도 모르는 경비가 총소리 하나에
        /// 엄폐부터 하면 "어디서 났는지 찾는" 단계가 통째로 사라진다.
        /// </summary>
        private void TryTakeCover()
        {
            bool signal = _underFire;
            _underFire = false;

            if (!signal) return;
            if (cover == null || motor == null || perception == null) return;
            if (Current != State.Alert && Current != State.Attack) return;
            if (Time.time < _coverReadyTime) return;

            // 위협은 마지막으로 안 자리다. Alert까지 왔으면 이 자리가 곧 플레이어다.
            if (!cover.FindCover(perception.LastKnownPosition)) return;

            Enter(State.TakeCover);
        }

        /// <summary>의심도와 소리로 상태를 올린다. 내리는 쪽은 각 상태가 알아서 한다.</summary>
        private void EvaluateEscalation()
        {
            float aware = perception.Awareness;

            if (aware >= alertThreshold)
            {
                // Attack과 TakeCover는 Alert의 하위 행동이라 여기서 끌어내리지 않는다.
                // 숨는 도중에 다시 Alert로 갈아타면 엄폐물 앞에서 한 발짝도 못 움직인다.
                if (Current != State.Alert && Current != State.Attack && Current != State.TakeCover)
                {
                    Enter(State.Alert);
                }
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

            // 쓰러진 몸은 소리와 같은 무게의 단서다. 살아 있는 단서를 쫓는 중이면 그쪽이 먼저라
            // 이 자리에서만 본다. Alert나 Attack을 Search로 끌어내리지도 않는다.
            if (perception.SeesBody && Current != State.Alert && Current != State.Attack && Current != State.TakeCover)
            {
                // 받아들이는 순간 뒤질 자리가 시체 자리로 바뀐다. 같은 몸에 두 번 놀라지는 않는다.
                perception.AcknowledgeBody();
                Enter(State.Search);
                // 이미 Search 중이었다면 Enter가 아무 일도 하지 않으니 여기서 둘러보기를 다시 시작한다.
                _stateTimer = 0f;
                _searchArrived = false;
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

            // 총을 든 경비는 붙잡지 않고 쏜다. 둘 다 돌면 같은 순간에 두 번 죽는 셈이 된다.
            if (InAttackRange() && gunner == null)
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

            // 엄폐물에 붙어 있고 거기서 보이면 나가지 않는다. 애써 숨고는 곧장 걸어 나오면
            // 엄폐가 그저 도는 길이 된다. 안 보이면 더 지킬 것이 없으니 예전처럼 쫓는다.
            if (gunner != null && cover != null && cover.InCover && perception.CanSeePlayer)
            {
                motor.Stop();
                motor.FaceTowards(perception.LastKnownPosition, turnSpeed);
                return;
            }

            // 겨누는 동안과 쏘는 동안은 걷지 않는다. 걸으면서 쏘면 플레이어가 피할 틈이 없다.
            // 연사 구간에서는 IsAiming이 내려가므로 IsFiring도 같이 봐야 한다.
            if (gunner != null && (gunner.IsAiming || gunner.IsFiring))
            {
                motor.Stop();
                motor.FaceTowards(perception.LastKnownPosition, turnSpeed);
                return;
            }

            motor.MoveTo(perception.LastKnownPosition, chaseSpeed);
        }

        /// <summary>
        /// 찾아 둔 자리로 달린다. 여기서는 쫓지 않는다. 총은 GuardGunner가 달리는 중에도 굴리고 있다.
        /// </summary>
        private void TickTakeCover()
        {
            if (motor == null || cover == null || !cover.HasCover)
            {
                Enter(State.Alert);
                return;
            }

            // 목적지를 먼저 준다. 경로가 없는 상태에서 ReachedDestination을 물으면
            // 아직 한 발짝도 안 뗐는데 도착했다고 답한다. TickSearch가 도는 순서와 같다.
            motor.MoveTo(cover.CoverPosition, chaseSpeed);

            if (motor.ReachedDestination)
            {
                motor.Stop();
                // 도착한 프레임에 먼저 위협 쪽으로 돌려 둔다. Alert로 돌아가 겨누기 시작할 때
                // 이미 그쪽을 보고 있어야 등을 보인 채 총을 드는 모양이 안 나온다.
                if (perception != null) motor.FaceTowards(perception.LastKnownPosition, turnSpeed);
                Enter(State.Alert);
            }
            else if (_stateTimer >= coverTravelTimeout)
            {
                // 길이 막혀 빙 돌아가는 중일 수 있다. 그 사이 벌판에 서 있는 셈이라 오래 끌지 않는다.
                Enter(State.Alert);
            }
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
                // 의심도와 함께, 어느 시체에 이미 놀랐는지도 여기서 지워진다. 시체도 같이 되살아나기 때문이다.
                perception.ResetAwareness();
            }

            _patrolIndex = 0;
            _stunRemaining = 0f;

            // 지난 판에서 찾아 둔 자리와 쉬는 시간은 여기서 버린다. 경비는 제자리로 돌아가 있어서
            // 옛 자리는 엉뚱한 방향이고, 남은 쉬는 시간은 첫 총격을 그냥 흘려보낸다.
            if (cover != null) cover.Forget();
            _underFire = false;
            _coverReadyTime = 0f;
            // 지난 판에 외친 것은 없던 일이 된다. 남겨 두면 다시 들킨 첫 순간에 아무도 외치지 않는다.
            _shoutReadyTime = 0f;

            Enter(State.Patrol, true);
        }

        /// <summary>
        /// 맞았다. 총알만 센다. 제압이나 낙하는 숨어서 될 일이 아니다.
        /// 이 게임은 한 대에 죽어서 이 신호가 오는 일이 드물지만, 빗나간 총알에는
        /// 이쪽이 오지 않으므로 아래 소리 쪽과 둘이 짝을 이룬다.
        /// </summary>
        private void HandleDamaged(DamageInfo info)
        {
            if (info.kind != DamageKind.Bullet) return;
            _underFire = true;
        }

        /// <summary>
        /// 총성을 듣는다. GuardPerception의 청각은 종류를 가리지 않고 의심도만 올려서,
        /// "총소리인가"를 여기서 따로 본다. 남의 파일을 고치지 않고도 NoiseBus가 종류를 들고 오기 때문이다.
        /// 발소리와 달리 프로필의 약한 귀(hearingMultiplier)를 거치지 않는다. 총성은 그만큼 크다.
        /// </summary>
        private void HandleNoise(NoiseEvent evt)
        {
            if (evt.kind != NoiseKind.Gunshot) return;
            if (IsOwnNoise(evt.source)) return;
            if (HorizontalDistance(transform.position, evt.position) > evt.radius * gunshotHearScale) return;

            _underFire = true;
        }

        /// <summary>자기 총소리에 놀라 자기 엄폐물로 뛰지 않도록. 총을 든 자식 오브젝트까지 함께 본다.</summary>
        private bool IsOwnNoise(GameObject source)
        {
            if (source == null) return false;
            if (source == gameObject) return true;
            return source.transform.IsChildOf(transform);
        }

        /// <summary>
        /// 발각을 알리는 외침. NoiseKind는 있는 것 중 Object를 쓴다.
        /// Gunshot은 안 된다. 위의 HandleNoise가 총성을 "총격을 받았다"로 읽어서, 외침 한 번에
        /// 사거리 안 동료들이 쏜 사람도 없는데 엄폐물로 뛴다. GuardPerception도 총성만은
        /// 의심도를 단번에 끝까지 올리므로 동료가 찾지도 않고 바로 발각된 것이 된다.
        /// Footstep은 SenseHud와 NoiseRipple이 발소리 색으로 칠해서 플레이어에게 거짓말이 된다.
        /// Bump는 제압당한 몸이 바닥에 닿는 소리로 이미 쓰고 있다.
        /// Object는 아직 아무도 쓰지 않고, 두 표시 모두 "그 밖의 소리" 색으로 보내며,
        /// 듣는 쪽에서는 크기만큼 의심도를 올려 동료가 소리 난 자리로 찾아오게 한다. 그게 우리가 원하는 것이다.
        /// </summary>
        private void Shout()
        {
            if (shoutLoudness <= 0f) return;
            // 엄폐와 Alert 사이를 오가면 Enter(State.Alert)가 거듭 불린다. 그때마다 외치면
            // 한 경비가 비명을 연달아 지르고 동료들이 그 자리에 못 박힌다.
            if (Time.time < _shoutReadyTime) return;
            _shoutReadyTime = Time.time + shoutCooldown;

            // 붙어 있으면 발소리와 같은 통로를 쓴다. 듣는 쪽이 자기 약한 귀로 똑같이 걸러야
            // 외침만 유별나게 멀리 가지 않는다. 반경은 그쪽의 크기당 반경이 정한다.
            if (noise != null)
            {
                noise.EmitOnce(shoutLoudness, NoiseKind.Object);
                return;
            }

            NoiseEvent evt;
            evt.position = transform.position;
            evt.radius = Mathf.Max(0f, shoutRadius);
            evt.loudness = Mathf.Clamp01(shoutLoudness);
            evt.kind = NoiseKind.Object;
            // 주인을 적어 둬야 제 외침을 제가 다시 듣고 놀라지 않는다.
            evt.source = gameObject;
            evt.time = Time.time;

            NoiseBus.Emit(evt);
        }

        private void Enter(State next, bool force = false)
        {
            if (!force && Current == next) return;

            // 엄폐를 마치는 순간부터 쉬는 시간을 센다. 들어갈 때부터 세면 달려가는 시간만큼
            // 간격이 짧아져서, 도착하자마자 옆 엄폐물로 다시 뛰는 일이 생긴다.
            if (Current == State.TakeCover) _coverReadyTime = Time.time + coverCooldown;

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

                case State.Alert:
                    // 들킨 순간 경비가 소리를 지른다. 화면 효과와 달리 이건 세계 안에서 벌어지는 일이라
                    // 동료가 실제로 듣고 오고, 플레이어도 집중 중이면 고리로 어디서 났는지 본다.
                    Shout();
                    break;

                case State.Stunned:
                    if (motor != null) motor.Stop();
                    if (perception != null) perception.enabled = false;
                    break;
            }

            // 총은 쫓는 동안에만 든다. 순찰로 돌아가면 내린다.
            // 숨으러 달리는 동안에도 든 채로 둔다. 엄폐물에 닿고 나서야 총을 드는 경비는
            // 이동 중에 쏠 기회를 통째로 버리는 셈이고, 여기서 내리면 겨누던 진행도도 날아간다.
            if (gunner != null)
            {
                if (next == State.Alert || next == State.TakeCover) gunner.Engage();
                else gunner.Disengage();
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
