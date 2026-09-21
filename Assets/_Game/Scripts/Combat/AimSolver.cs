using UnityEngine;

namespace ByAWhisker.Combat
{
    /// <summary>
    /// 화면의 마우스 위치를 캐릭터가 겨눌 수평 방향으로 바꾼다.
    /// 상태가 없어서 플레이어 조준과 조준선 표시가 같은 함수를 쓴다. 답이 하나여야
    /// "겨눈 곳과 맞는 곳이 다르다"는 불일치가 안 생긴다.
    /// </summary>
    public static class AimSolver
    {
        /// <summary>
        /// 바닥 평면(y = groundY)과 마우스 광선의 교점을 구한다.
        /// 쿼터뷰는 내려보는 각이 고정이라 광선이 늘 평면을 앞쪽에서 만난다. 그래도 카메라를
        /// 눕히거나 화면 밖 좌표가 들어오면 교점이 없을 수 있어서 성공 여부를 따로 돌려준다.
        /// </summary>
        public static bool TryAimPoint(Camera camera, Vector2 screenPoint, float groundY, out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;
            if (camera == null) return false;

            Ray ray = camera.ScreenPointToRay(screenPoint);

            // Plane은 구조체다. 매 프레임 불려도 힙에 아무것도 남기지 않는다.
            var ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));

            float distance;
            // 광선이 평면과 나란하거나 등지고 있으면 false다. 카메라 뒤쪽 교점을 잡지 않는다.
            if (!ground.Raycast(ray, out distance)) return false;

            worldPoint = ray.GetPoint(distance);
            return true;
        }

        /// <summary>
        /// origin에서 그 점을 향하는 수평 방향. 실패하면 fallback을 돌려준다.
        /// y를 버리는 이유는 층이 하나뿐이고 고개를 들거나 숙이는 개념이 없기 때문이다.
        /// 커서가 발밑에 겹치면 방향이 없으니 그때도 fallback으로 간다.
        /// </summary>
        public static Vector3 AimDirection(Vector3 origin, Vector3 aimPoint, Vector3 fallback)
        {
            Vector3 direction = aimPoint - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f) return direction.normalized;

            // fallback도 겨눌 방향으로 쓰이니 같은 규칙으로 눕혀서 돌려준다.
            Vector3 flat = fallback;
            flat.y = 0f;
            return flat.sqrMagnitude > 0.0001f ? flat.normalized : fallback;
        }
    }
}
