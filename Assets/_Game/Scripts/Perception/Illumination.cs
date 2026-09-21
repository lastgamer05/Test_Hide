using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.Perception
{
    /// <summary>
    /// 위치별 밝기를 묻는 창구. 등록된 램프만 보기 때문에 장면을 뒤지지 않는다.
    /// 매 프레임 불리니 할당을 하나도 만들지 않는다.
    /// </summary>
    public static class Illumination
    {
        /// <summary>
        /// 켜진 램프를 모두 훑어서 가장 밝은 값을 0..1로 돌려준다.
        /// 밝기를 더하지 않고 최댓값을 쓰는 이유는, 램프 여러 개가 겹친 곳에서 값이 1로 붙어 버리면
        /// 플레이어가 "여기가 더 어둡다"를 읽을 수 없기 때문이다.
        /// </summary>
        public static float Sample(Vector3 worldPosition, LayerMask blockers)
        {
            IReadOnlyList<LightSource> lights = LightSource.All;
            if (lights == null) return 0f;

            float brightest = 0f;

            // foreach는 인터페이스 열거자를 박싱한다. 인덱스로 돈다.
            for (int i = 0; i < lights.Count; i++)
            {
                LightSource light = lights[i];
                if (light == null || !light.IsOn) continue;
                if (light.radius <= 0f || light.intensity <= 0f) continue;

                Vector3 lightPosition = light.transform.position;
                Vector3 delta = worldPosition - lightPosition;

                // 거리 밖이면 레이캐스트까지 갈 필요가 없다. 제곱으로 먼저 거른다.
                float sqrDistance = delta.sqrMagnitude;
                float radius = light.radius;
                if (sqrDistance >= radius * radius) continue;

                float value = (1f - Mathf.Sqrt(sqrDistance) / radius) * light.intensity;
                if (value <= brightest) continue; // 이미 더 밝은 램프가 있으면 레이캐스트를 아낀다.

                // 벽 뒤는 어둡다. 부분 그림자는 없이 막히면 0으로 본다.
                if (!Sight.HasLineOfSight(lightPosition, worldPosition, blockers)) continue;

                brightest = value;
            }

            return Mathf.Clamp01(brightest);
        }
    }
}
