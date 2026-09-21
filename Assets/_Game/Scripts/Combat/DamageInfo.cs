using UnityEngine;

namespace ByAWhisker.Combat
{
    /// <summary>
    /// 무엇에 맞았는가. 피해량이 아니라 종류만 구분한다.
    /// 이 게임에는 체력이 없어서 "얼마나 아픈가"를 물을 일이 없고,
    /// 맞은 쪽이 어떤 몸을 남길지와 어떤 소리를 낼지만 갈라지면 된다.
    /// </summary>
    public enum DamageKind
    {
        Bullet,
        Takedown,
        Fall
    }

    /// <summary>
    /// 한 번의 피격. 클래스가 아니라 구조체인 이유는 NoiseEvent와 같다.
    /// 값으로 오가면 총을 쏠 때마다 쓰레기가 쌓이지 않는다.
    /// 필드는 이 다섯 개뿐이다. 부르는 쪽이 선언만 하고 하나씩 채우는 식으로 쓰기 때문에,
    /// 여기에 필드를 하나라도 더하면 A와 B의 기존 호출부가 "할당되지 않은 필드"로 깨진다.
    /// </summary>
    public struct DamageInfo
    {
        public GameObject attacker;
        public Vector3 point;       // 맞은 자리
        public Vector3 direction;   // 날아온 방향
        public DamageKind kind;
        public bool lethal;         // 이 게임은 거의 항상 true다
    }
}
