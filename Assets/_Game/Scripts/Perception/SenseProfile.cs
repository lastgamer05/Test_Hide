using UnityEngine;

namespace ByAWhisker.Perception
{
    /// <summary>
    /// 한 종류의 감각 능력을 담는 데이터. 인간, 경비견, 플레이어가 같은 틀을 쓴다.
    /// 동물 적을 넣을 때 새 코드 없이 에셋만 하나 더 만들면 되도록 필드로만 둔다.
    /// </summary>
    [CreateAssetMenu(menuName = "By a Whisker/Sense Profile", fileName = "SenseProfile")]
    public class SenseProfile : ScriptableObject
    {
        [Header("시각")]
        [Tooltip("시야각 전체. 좌우 절반씩 나눠 쓴다.")]
        [Range(1f, 360f)] public float fovDegrees = 100f;

        [Tooltip("어두운 곳에서의 탐지 거리.")]
        public float rangeDark = 5f;

        [Tooltip("밝은 곳에서의 탐지 거리. 실제 거리는 플레이어 빛 노출로 두 값 사이를 보간한다.")]
        public float rangeLit = 16f;

        [Header("청각과 후각")]
        [Tooltip("인간은 귀가 약해서 1보다 작다. 경비견은 1보다 크다.")]
        public float hearingMultiplier = 0.6f;

        [Tooltip("경비견용. M2의 인간 경비는 false다.")]
        public bool canSmell = false;

        [Header("의심도")]
        [Tooltip("정면에서 완전히 발각되기까지 걸리는 시간.")]
        public float detectSeconds = 1f;

        [Tooltip("시야 밖일 때 초당 줄어드는 의심도.")]
        public float forgetPerSecond = 0.25f;

        [Tooltip("시선을 쏘는 눈높이.")]
        public float eyeHeight = 1.6f;
    }
}
