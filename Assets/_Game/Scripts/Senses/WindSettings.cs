using UnityEngine;

namespace ByAWhisker.Senses
{
    /// <summary>
    /// 레벨 전체에 부는 바람. 냄새가 어느 쪽으로 흘러갈지를 정한다.
    /// 값은 코드에 박지 않고 에셋으로 두어야 레벨마다 바람을 바꿔 가며 맞춰 볼 수 있다.
    /// </summary>
    [CreateAssetMenu(menuName = "ByAWhisker/Wind Settings", fileName = "WindSettings")]
    public class WindSettings : ScriptableObject
    {
        [Tooltip("바람이 부는 쪽. 수평 성분만 쓰고 정규화해서 쓴다.")]
        public Vector3 direction = new Vector3(1f, 0f, 0f);

        [Tooltip("바람 세기. m/s. 0이면 냄새가 제자리에서 번지기만 한다.")]
        public float speed = 1.2f;

        [Tooltip("돌풍의 크기. 0이면 일정하게 분다. 1이면 세기가 0배에서 2배까지 흔들린다.")]
        [Range(0f, 1f)] public float gustStrength = 0.35f;

        [Tooltip("돌풍 한 번이 도는 데 걸리는 시간. 초.")]
        public float gustPeriod = 4f;

        /// <summary>
        /// 돌풍을 섞은 지금의 바람. 결과는 속도가 곱해진 m/s 벡터다.
        /// 난수 대신 사인 두 개를 겹쳐 쓴다. 매 프레임 상태를 들고 있지 않아도 되고,
        /// 재시작해도 같은 시각이면 같은 바람이 나와서 디버깅할 때 재현이 된다.
        /// </summary>
        public Vector3 SampleAt(float time)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            float lengthSqr = flat.sqrMagnitude;
            if (lengthSqr < 1e-8f || speed <= 0f) return Vector3.zero;

            flat /= Mathf.Sqrt(lengthSqr);

            float gust = 0f;
            if (gustStrength > 0f && gustPeriod > 0.0001f)
            {
                // 주기가 어긋난 사인 둘을 더해서 같은 모양이 대놓고 반복되는 느낌을 지운다. 결과는 -1..1이다.
                float phase = time / gustPeriod * (Mathf.PI * 2f);
                gust = Mathf.Sin(phase) * 0.6f + Mathf.Sin(phase * 2.3f) * 0.4f;
            }

            // 방향도 같이 흔든다. 세기만 흔들면 냄새 자취가 자로 그은 직선이 되어 바람이 분다는 느낌이 없다.
            Vector3 swayed = Quaternion.AngleAxis(gust * gustStrength * 20f, Vector3.up) * flat;

            float now = speed * (1f + gustStrength * gust);
            if (now < 0f) now = 0f;   // 돌풍이 세면 순간적으로 음수가 된다. 바람이 거꾸로 불게 두지 않는다.

            return swayed * now;
        }
    }
}
