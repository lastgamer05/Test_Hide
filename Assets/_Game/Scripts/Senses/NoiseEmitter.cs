using UnityEngine;
using UnityEngine.AI;
using ByAWhisker.AI;
using ByAWhisker.Player;

namespace ByAWhisker.Senses
{
    /// <summary>
    /// 움직이는 것에 붙어 발소리를 낸다. 플레이어에게도 경비에게도 같은 것을 붙인다.
    /// 속도는 이미 있는 이동 컴포넌트에서 읽기만 한다. 이쪽에서 이동을 건드리지 않는다.
    /// </summary>
    public class NoiseEmitter : MonoBehaviour
    {
        [Header("발소리")]
        [Tooltip("이만큼 걸을 때마다 한 발 소리가 난다(m).")]
        [SerializeField] private float strideLength = 0.9f;

        [Tooltip("걷기 한 발의 크기. 자세 배율이 여기에 곱해진다. 0..1")]
        [Range(0f, 1f)]
        [SerializeField] private float footstepLoudness = 0.4f;

        [Tooltip("크기 1인 소리가 들리는 거리(m). 실제 반경은 크기에 비례한다.")]
        [SerializeField] private float radiusPerLoudness = 12f;

        [Header("자세 배율")]
        [SerializeField] private float crouchScale = 0.35f;
        [SerializeField] private float walkScale = 1f;
        [SerializeField] private float runScale = 1.9f;

        [Header("속도 판정")]
        [Tooltip("이보다 느리면 멈춘 것으로 본다(m/s).")]
        [SerializeField] private float moveThreshold = 0.15f;

        [Tooltip("PlayerMotor가 없을 때 달리기로 볼 속도(m/s). 경비는 이 값으로 가른다.")]
        [SerializeField] private float runSpeedThreshold = 5f;

        [Tooltip("마지막 소리 크기가 0으로 잦아드는 시간(초). 표시용 값이라 발을 뗄 때마다 뚝뚝 끊기지 않게 한다.")]
        [SerializeField] private float loudnessFade = 0.35f;

        /// <summary>속도를 어디서 읽을지. Awake에서 한 번 정하고 매 프레임 찾지 않는다.</summary>
        private enum SpeedSource
        {
            None,
            PlayerMotor,
            Controller,
            Agent
        }

        private SpeedSource _source;
        private PlayerMotor _playerMotor;
        private PlayerStance _stance;
        private CharacterController _controller;
        private NavMeshAgent _agent;

        private float _travelled;
        private Vector3 _lastPosition;
        private float _lastEmitted;
        private float _fadeLeft;

        /// <summary>가장 최근에 낸 소리의 크기. 조금씩 잦아든다. HUD가 읽어 간다.</summary>
        public float LastLoudness { get; private set; }

        private void Awake()
        {
            ResolveSpeedSource();
            _lastPosition = transform.position;
        }

        private void OnEnable()
        {
            // 순간이동이나 부활로 자리가 확 바뀌었을 때 그 거리를 발소리로 치지 않는다.
            _lastPosition = transform.position;
        }

        private void OnDisable()
        {
            // 기절하거나 재시작으로 꺼질 때 옛 걸음과 옛 소리 크기를 들고 있지 않는다.
            _travelled = 0f;
            _lastPosition = transform.position;
            _fadeLeft = 0f;
            _lastEmitted = 0f;
            LastLoudness = 0f;
        }

        private void ResolveSpeedSource()
        {
            _playerMotor = GetComponent<PlayerMotor>();
            if (_playerMotor != null)
            {
                // 앉기는 플레이어에게만 있다. 없으면 배율이 서기로 남는다.
                _stance = GetComponent<PlayerStance>();
                _source = SpeedSource.PlayerMotor;
                return;
            }

            // GuardMotor에는 속도를 내주는 멤버가 없다. 기존 파일은 고치지 않기로 했으니
            // 경비면 GuardMotor가 감싸고 있는 NavMeshAgent를 직접 읽는다.
            _agent = GetComponent<NavMeshAgent>();
            if (GetComponent<GuardMotor>() != null && _agent != null)
            {
                _source = SpeedSource.Agent;
                return;
            }

            _controller = GetComponent<CharacterController>();
            if (_controller != null)
            {
                _source = SpeedSource.Controller;
                return;
            }

            if (_agent != null)
            {
                _source = SpeedSource.Agent;
                return;
            }

            // 속도를 알 길이 없으면 조용히 아무 일도 하지 않는다. 경고를 띄우면 소품마다 콘솔이 더러워진다.
            _source = SpeedSource.None;
        }

        private void Update()
        {
            if (_source == SpeedSource.None) return;

            float dt = Time.deltaTime;
            FadeLoudness(dt);

            float speed = CurrentSpeed();

            // 시간이 아니라 실제로 옮겨 간 거리로 센다. 시간 기준이면 느리게 걸을 때 소리가
            // 과하게 나고, 속도 기준이면 벽에 밀착해 제자리걸음을 할 때도 발소리가 난다.
            Vector3 here = transform.position;
            float moved = Horizontal(here - _lastPosition);
            _lastPosition = here;

            if (speed < moveThreshold) return;
            _travelled += moved;

            float stride = Mathf.Max(0.05f, strideLength);
            if (_travelled < stride) return;

            _travelled -= stride;
            EmitOnce(footstepLoudness * StanceScale(speed), NoiseKind.Footstep);
        }

        /// <summary>소리를 한 번 낸다. 부딪힘이나 총성처럼 발걸음이 아닌 소리는 밖에서 이걸 부른다.</summary>
        public void EmitOnce(float loudness, NoiseKind kind)
        {
            loudness = Mathf.Clamp01(loudness);
            if (loudness <= 0f) return;

            NoiseEvent evt;
            evt.position = transform.position;
            evt.radius = loudness * radiusPerLoudness;
            evt.loudness = loudness;
            evt.kind = kind;
            // 자기가 낸 소리를 스스로 듣고 놀라지 않도록 듣는 쪽이 걸러 낼 수 있게 주인을 적어 둔다.
            evt.source = gameObject;
            evt.time = Time.time;

            NoiseBus.Emit(evt);

            _lastEmitted = loudness;
            _fadeLeft = loudnessFade;
            LastLoudness = loudness;
        }

        private float CurrentSpeed()
        {
            switch (_source)
            {
                case SpeedSource.PlayerMotor:
                    return _playerMotor.CurrentSpeed;
                case SpeedSource.Controller:
                    return Horizontal(_controller.velocity);
                case SpeedSource.Agent:
                    return Horizontal(_agent.velocity);
                default:
                    return 0f;
            }
        }

        private float StanceScale(float speed)
        {
            if (_stance != null && _stance.IsCrouching) return crouchScale;

            // 플레이어는 달리기 여부를 스스로 안다. 경비는 그런 개념이 없어서 속도로 가른다.
            bool running = _playerMotor != null ? _playerMotor.IsRunning : speed >= runSpeedThreshold;
            return running ? runScale : walkScale;
        }

        private void FadeLoudness(float dt)
        {
            if (_fadeLeft <= 0f) return;

            _fadeLeft -= dt;
            float t = loudnessFade > 0.0001f ? Mathf.Clamp01(_fadeLeft / loudnessFade) : 0f;
            LastLoudness = _lastEmitted * t;
        }

        /// <summary>중력으로 생기는 수직 속도를 빼야 서 있을 때 걷는 것으로 치지 않는다.</summary>
        private static float Horizontal(Vector3 velocity)
        {
            velocity.y = 0f;
            return velocity.magnitude;
        }
    }
}
