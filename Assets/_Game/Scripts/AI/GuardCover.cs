using UnityEngine;
using UnityEngine.AI;
using ByAWhisker.Perception;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 몸을 가려 주는 자리를 찾는다. 찾기만 하고 걷지는 않는다.
    /// 걷는 쪽을 GuardMotor에, 언제 숨을지를 GuardBrain에 남겨 두면
    /// "어디가 안전한가"라는 질문 하나만 여기서 답하면 된다.
    /// </summary>
    public class GuardCover : MonoBehaviour
    {
        [Header("찾을 범위")]
        [Tooltip("엄폐물 레이어. HighCover와 LowCover.")]
        [SerializeField] private LayerMask coverMask;
        [Tooltip("이 반경 안의 엄폐물만 본다(m). 멀리 뛰어가는 경비는 그냥 벌판에 서 있는 것과 같다.")]
        [SerializeField] private float searchRadius = 12f;
        [Tooltip("한 번에 살펴볼 엄폐물 수. 배열 크기라 넉넉히 잡되 무한정 늘리지는 않는다.")]
        [SerializeField] private int maxCovers = 12;
        [Tooltip("엄폐물 하나마다 둘레에서 만들어 볼 자리 수. 홀수로 두면 한가운데가 후보에 들어온다.")]
        [SerializeField] private int candidatesPerCover = 5;
        [Tooltip("위협 반대쪽으로 펼치는 부채꼴의 전체 각도. 넓힐수록 옆구리 자리까지 본다.")]
        [SerializeField] private float spreadDegrees = 120f;
        [Tooltip("엄폐물 겉면에서 이만큼 떨어져 선다(m). 0이면 엄폐물 안에 겹쳐 서려고 한다.")]
        [SerializeField] private float standOffset = 0.7f;

        [Header("판정")]
        [Tooltip("시선을 막는 레이어. GuardPerception의 blockers와 같은 것을 넣어야 판정이 어긋나지 않는다.")]
        [SerializeField] private LayerMask blockers;
        [Tooltip("가려야 할 몸통 높이(m). 발밑이 아니라 이 높이가 막혀야 진짜 엄폐다.")]
        [SerializeField] private float bodyHeight = 1f;
        [Tooltip("위협이 쏘는 높이(m). 총구는 바닥이 아니라 어깨에서 나온다.")]
        [SerializeField] private float threatHeight = 1.4f;
        [Tooltip("후보에서 이 거리 안에 NavMesh가 있어야 갈 수 있는 자리로 본다(m).")]
        [SerializeField] private float navSampleRadius = 1f;
        [Tooltip("엄폐 자리에 이만큼 붙으면 숨은 것으로 본다(m).")]
        [SerializeField] private float arriveRadius = 0.8f;

        // 매 프레임 할당을 만들지 않으려고 들고 있는다. GuardPerception의 시체 훑기와 같은 방식이다.
        private Collider[] _covers;
        private Vector3[] _candidates;

        /// <summary>쓸 만한 자리를 찾아 두었는가. FindCover가 실패하면 내려간다.</summary>
        public bool HasCover { get; private set; }

        /// <summary>찾아 둔 자리. HasCover가 false면 자기 위치다.</summary>
        public Vector3 CoverPosition { get; private set; }

        /// <summary>그 자리에 이미 붙어 있는가. 두뇌가 "여기서 버틸지"를 이걸로 고른다.</summary>
        public bool InCover
        {
            get
            {
                if (!HasCover) return false;
                return Sight.HorizontalDistance(transform.position, CoverPosition) <= arriveRadius;
            }
        }

        private void Awake()
        {
            CoverPosition = transform.position;

            _covers = new Collider[Mathf.Max(1, maxCovers)];
            _candidates = new Vector3[Mathf.Max(1, candidatesPerCover)];
        }

        /// <summary>
        /// threat 쪽에서 몸을 가려 주는 가장 가까운 자리를 찾는다. 찾으면 CoverPosition에 적고 true.
        /// 가까운 쪽을 고르는 이유는 총이 날아오는 중이라서다. 더 좋은 자리가 있어도 멀면 가는 길에 맞는다.
        /// </summary>
        public bool FindCover(Vector3 threat)
        {
            HasCover = false;
            CoverPosition = transform.position;

            if (_covers == null) return false;

            Vector3 origin = transform.position;
            Vector3 threatEye = threat;
            threatEye.y += threatHeight;

            int count = Physics.OverlapSphereNonAlloc(
                origin, searchRadius, _covers, coverMask.value, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;
            Vector3 best = origin;

            for (int i = 0; i < count; i++)
            {
                Collider cover = _covers[i];
                if (cover == null) continue;

                int made = BuildCandidates(cover, threat, origin.y);

                for (int c = 0; c < made; c++)
                {
                    NavMeshHit navHit;
                    if (!NavMesh.SamplePosition(_candidates[c], out navHit, navSampleRadius, NavMesh.AllAreas)) continue;

                    // 가려지는가. 위협의 총구에서 이 자리의 몸통까지 선이 막혀야 한다.
                    Vector3 body = navHit.position;
                    body.y += bodyHeight;
                    if (Sight.HasLineOfSight(threatEye, body, blockers)) continue;

                    float distance = Sight.HorizontalDistance(origin, navHit.position);
                    if (distance >= bestDistance) continue;

                    bestDistance = distance;
                    best = navHit.position;
                }
            }

            if (bestDistance == float.MaxValue) return false;

            CoverPosition = best;
            HasCover = true;
            return true;
        }

        /// <summary>
        /// 계약에는 없다. 재시작 때 지난 판의 자리를 들고 있지 않으려고 둔다.
        /// 엄폐물은 그대로라도 경비는 제자리로 돌아가 있어서, 옛 자리는 엉뚱한 방향이 된다.
        /// </summary>
        public void Forget()
        {
            HasCover = false;
            CoverPosition = transform.position;
        }

        /// <summary>
        /// 엄폐물 둘레에서 위협 반대쪽으로 부채꼴로 후보를 뿌린다.
        /// 콜라이더 모양을 따지지 않고 바운즈로 어림하는 이유는, 어차피 아래에서
        /// 시선과 NavMesh로 다시 거르기 때문이다. 여기서 정확할 필요가 없다.
        /// </summary>
        private int BuildCandidates(Collider cover, Vector3 threat, float groundY)
        {
            Bounds bounds = cover.bounds;

            Vector3 center = bounds.center;
            center.y = groundY;

            Vector3 away = center - threat;
            away.y = 0f;
            // 위협이 엄폐물 한가운데에 겹쳐 있으면 반대쪽이 없다. 내가 선 쪽을 뒤로 친다.
            if (away.sqrMagnitude < 0.0001f) away = center - transform.position;
            if (away.sqrMagnitude < 0.0001f) return 0;
            away.Normalize();

            float ring = Mathf.Max(bounds.extents.x, bounds.extents.z) + standOffset;

            int slots = _candidates.Length;
            float step = slots > 1 ? spreadDegrees / (slots - 1) : 0f;
            float start = slots > 1 ? -spreadDegrees * 0.5f : 0f;

            for (int i = 0; i < slots; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, start + step * i, 0f) * away;
                _candidates[i] = center + direction * ring;
            }

            return slots;
        }
    }
}
