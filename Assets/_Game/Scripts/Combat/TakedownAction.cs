using UnityEngine;
using ByAWhisker.AI;
using ByAWhisker.Senses;

namespace ByAWhisker.Combat
{
    /// <summary>
    /// 뒤에서 조용히 제압한다. 플레이어에 붙는다.
    /// 판정은 셋뿐이다. 등 뒤인가, 가까운가, 상대가 나를 못 보고 있는가.
    /// 마주 본 상대를 소리 없이 잡을 수는 없으니 그때는 실패한다. 총을 꺼낼지 도망칠지는 플레이어가 정한다.
    /// </summary>
    public class TakedownAction : MonoBehaviour
    {
        [Header("판정")]
        [Tooltip("제압할 수 있는 것이 있는 레이어. Enemy.")]
        [SerializeField] private LayerMask targetLayers;

        [Tooltip("이 거리 안이어야 한다(m). 수평 거리로만 잰다.")]
        [SerializeField] private float range = 1.4f;

        [Tooltip("대상의 등 뒤로 벌어진 부채꼴(도). 100이면 등 뒤 좌우 50도씩이다.")]
        [Range(0f, 360f)]
        [SerializeField] private float backAngle = 100f;

        [Tooltip("한 번에 살펴볼 후보 수. 좁은 통로에 몇이 겹쳐 서 있어도 이 정도면 넉넉하다.")]
        [SerializeField] private int maxCandidates = 8;

        [Tooltip("대상을 다시 찾는 간격(초). 매 프레임 훑을 필요가 없다.")]
        [SerializeField] private float scanInterval = 0.1f;

        [Header("결과")]
        [Tooltip("끄면 기절시키고 켜면 죽인다. 어느 쪽이든 몸은 그 자리에 남는다.")]
        [SerializeField] private bool lethal;

        [Tooltip("맞은 자리로 칠 높이(m). 발밑이 아니라 목덜미쯤을 적어 둔다.")]
        [SerializeField] private float hitHeight = 1.2f;

        [Header("소리")]
        [Tooltip("제압할 때 나는 소리 크기. 0..1. 총성보다 한참 작지만 옆방 경비는 알아챈다.")]
        [Range(0f, 1f)]
        [SerializeField] private float noiseLoudness = 0.22f;

        [Tooltip("소리를 낼 곳. 비우면 이 오브젝트의 것을 쓴다.")]
        [SerializeField] private NoiseEmitter noise;

        // OverlapSphereNonAlloc이 여기에 채운다. 매번 새로 만들면 프레임마다 쓰레기가 쌓인다.
        private Collider[] _overlap;

        private Damageable _self;
        private Damageable _target;
        private float _nextScan;

        /// <summary>지금 제압할 수 있는 상대가 있는가. HUD가 이걸 보고 안내를 띄운다.</summary>
        public bool HasTarget { get { return _target != null; } }

        /// <summary>그 상대. 없으면 null.</summary>
        public Damageable Target { get { return _target; } }

        private void Awake()
        {
            _overlap = new Collider[Mathf.Max(1, maxCandidates)];
            if (noise == null) noise = GetComponent<NoiseEmitter>();

            // 자기 자신을 제압하지 않도록 어디까지가 자기인지 한 번 정해 둔다. Weapon이 쓰는 기준과 같다.
            _self = GetComponentInParent<Damageable>();
        }

        private void OnDisable()
        {
            // 죽거나 재시작으로 꺼질 때 옛 대상을 들고 있지 않는다. 다시 켜지면 처음부터 찾는다.
            _target = null;
            _nextScan = 0f;
        }

        private void Update()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + scanInterval;

            _target = FindTarget();
        }

        /// <summary>성공하면 true.</summary>
        public bool TryTakedown()
        {
            // 누른 순간의 상황으로 판정한다. 0.1초 전에 훑어 둔 자리로 잡으면
            // 이미 돌아선 상대를 등 뒤에서 잡는 일이 생긴다.
            _nextScan = Time.time + scanInterval;
            _target = FindTarget();

            Damageable target = _target;
            if (target == null) return false;

            Vector3 targetPosition = target.transform.position;

            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) direction.Normalize();
            else direction = transform.forward;

            DamageInfo info;
            info.attacker = gameObject;
            info.point = targetPosition + Vector3.up * hitHeight;
            info.direction = direction;
            info.kind = DamageKind.Takedown;
            info.lethal = lethal;

            // 이미 쓰러진 상대면 Apply가 거절한다. 그때는 소리도 내지 않는다.
            if (!target.Apply(info)) return false;

            // 제압은 조용하지만 무음은 아니다. 몸이 바닥에 닿는 소리 정도로 둔다.
            if (noise != null) noise.EmitOnce(noiseLoudness, NoiseKind.Bump);

            _target = null;
            return true;
        }

        /// <summary>범위 안에서 제압 조건을 모두 만족하는 것 중 가장 가까운 하나를 고른다.</summary>
        private Damageable FindTarget()
        {
            Vector3 here = transform.position;
            int count = Physics.OverlapSphereNonAlloc(here, range, _overlap, targetLayers.value, QueryTriggerInteraction.Ignore);

            Damageable best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider hit = _overlap[i];
                if (hit == null) continue;

                // 콜라이더가 자식에 달려 있을 수 있으니 부모까지 올라가서 찾는다.
                Damageable candidate = hit.GetComponentInParent<Damageable>();
                if (candidate == null || candidate == _self) continue;

                float distance = HorizontalDistance(here, candidate.transform.position);
                if (distance >= bestDistance) continue;
                if (!CanTakeDown(candidate, here)) continue;

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        private bool CanTakeDown(Damageable candidate, Vector3 from)
        {
            if (!candidate.IsAlive || candidate.IsDown) return false;

            Transform target = candidate.transform;

            Vector3 toAttacker = from - target.position;
            toAttacker.y = 0f;
            // 완전히 겹쳐 서 있으면 등인지 앞인지 가릴 방법이 없다. 그럴 땐 실패로 둔다.
            if (toAttacker.sqrMagnitude < 0.0001f) return false;
            if (toAttacker.magnitude > range) return false;

            // 구는 높이도 함께 잡으니 위층 아래층이 생기면 여기서 한 번 더 걸러야 한다. 지금은 층이 하나다.
            Vector3 back = -Forward(target);
            if (Vector3.Angle(back, toAttacker) > backAngle * 0.5f) return false;

            // 마주 본 상대를 조용히 잡을 수는 없다. 기절 중이면 GuardPerception이 스스로 false를 들고 있다.
            GuardPerception perception = candidate.GetComponent<GuardPerception>();
            if (perception != null && perception.CanSeePlayer) return false;

            return true;
        }

        /// <summary>수평 정면. 캡슐이 조금 기울어도 등 뒤 판정이 흔들리지 않게 y를 버린다.</summary>
        private static Vector3 Forward(Transform target)
        {
            Vector3 forward = target.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : target.forward;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 제압 거리는 눈에 안 보이니 씬에서 확인할 수 있게 그려 둔다.
            Gizmos.color = new Color(0.9f, 0.4f, 0.35f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
#endif
    }
}
