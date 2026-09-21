using UnityEngine;

namespace ByAWhisker.Senses
{
    /// <summary>소리의 종류. 듣는 쪽이 발소리와 총성을 다르게 대할 수 있게 나눠 둔다.</summary>
    public enum NoiseKind
    {
        Footstep,
        Bump,
        Object,
        Gunshot
    }

    /// <summary>
    /// 한 번 난 소리. 클래스가 아니라 구조체인 이유는 발소리가 초당 여러 번 나기 때문이다.
    /// 값으로 오가면 링 버퍼에 그대로 눕고 매 프레임 쓰레기를 만들지 않는다.
    /// </summary>
    public struct NoiseEvent
    {
        public Vector3 position;
        public float radius;     // 이 거리 안에서 들린다 (m)
        public float loudness;   // 0..1
        public NoiseKind kind;
        public GameObject source;
        public float time;       // Time.time
    }
}
