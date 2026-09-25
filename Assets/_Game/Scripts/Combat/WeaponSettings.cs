using UnityEngine;

namespace ByAWhisker.Combat
{
    /// <summary>
    /// 총 한 자루의 성능. 플레이어 권총과 경비 소총이 같은 틀을 쓴다.
    /// 숫자를 코드에 박지 않는 원칙대로 에셋만 하나 더 만들면 새 총이 되도록 필드로만 둔다.
    /// </summary>
    [CreateAssetMenu(menuName = "By a Whisker/Weapon Settings", fileName = "WeaponSettings")]
    public class WeaponSettings : ScriptableObject
    {
        [Header("사거리와 정확도")]
        [Tooltip("총알이 닿는 거리(m). 여기까지 아무것도 없으면 빗나간 것으로 친다.")]
        public float range = 22f;

        [Tooltip("한 발의 흐트러짐(도). 수평으로만 벌어진다. 0이면 겨눈 그대로 나간다.")]
        [Range(0f, 30f)]
        public float spreadDegrees = 2f;

        [Header("연사와 장전")]
        [Tooltip("다음 발까지의 최소 간격(초). 한 대에 죽는 게임이라 난사는 막는다.")]
        public float fireInterval = 0.35f;

        [Tooltip("탄창 한 개의 발 수.")]
        public int magazine = 6;

        [Tooltip("탄창 밖에 들고 다니는 예비탄. 음수면 무한이라 장전이 끊기지 않는다. 경비 총은 무한을 쓴다.")]
        public int reserveAmmo = -1;

        [Tooltip("장전에 걸리는 시간(초). 이 동안은 쏠 수 없다.")]
        public float reloadSeconds = 1.6f;

        [Header("소리")]
        [Tooltip("소음기를 달았는가. 달면 총성 크기에 배율이 곱해진다.")]
        public bool suppressed = false;

        [Tooltip("소음기 없이 쐈을 때의 총성 크기. 0..1. NoiseEmitter로 그대로 넘긴다.")]
        [Range(0f, 1f)]
        public float noiseLoudness = 1f;
    }
}
