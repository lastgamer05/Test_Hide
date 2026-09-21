using UnityEngine;

namespace ByAWhisker.Perception
{
    /// <summary>
    /// 시선 판정 도구 모음. 상태가 없어서 적과 플레이어와 밝기 계산이 모두 같은 함수를 쓴다.
    /// "아는 것과 그리는 것을 나눈다"는 원칙대로 여기서 나온 답이 유일한 정답이다.
    /// </summary>
    public static class Sight
    {
        /// <summary>
        /// 두 점 사이가 뚫려 있는가. 체크포인트나 출구 같은 트리거 볼륨이 시야를 막으면 곤란하니 무시한다.
        /// </summary>
        public static bool HasLineOfSight(Vector3 from, Vector3 to, LayerMask blockers)
        {
            return !Physics.Linecast(from, to, blockers.value, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 수평 평면에서만 각도를 잰다. 고개를 들거나 숙이는 개념이 없어서 y는 판정에서 뺀다.
        /// fovDegrees는 전체 각이라 절반과 비교한다.
        /// </summary>
        public static bool InFieldOfView(Vector3 origin, Vector3 forward, Vector3 target, float fovDegrees)
        {
            Vector3 toTarget = target - origin;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.0001f) return true; // 겹쳐 있으면 방향이 없다. 보인다고 본다.

            Vector3 flatForward = forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f) return false; // 바라보는 방향이 없으면 판정할 수 없다.

            return Vector3.Angle(flatForward, toTarget) <= fovDegrees * 0.5f;
        }

        /// <summary>y를 무시한 거리. 층이 하나뿐이라 높이 차이는 거리로 치지 않는다.</summary>
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
