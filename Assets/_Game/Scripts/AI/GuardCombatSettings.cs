using UnityEngine;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 경비가 어떻게 싸우는지를 담는 데이터. 총 자체의 성능은 WeaponSettings에 있고
    /// 여기에는 "언제 겨누고 언제 포기하는가"만 둔다. 같은 총을 든 경비라도
    /// 에셋만 바꿔서 느슨한 경비와 매서운 경비를 나눌 수 있게 하려는 분리다.
    /// </summary>
    [CreateAssetMenu(menuName = "By a Whisker/Guard Combat Settings", fileName = "GuardCombatSettings")]
    public class GuardCombatSettings : ScriptableObject
    {
        [Header("거리")]
        [Tooltip("이 거리 안이면 쏜다. 시야 탐지 거리보다 짧게 둬야 '보자마자 총'이 되지 않는다.")]
        public float fireRange = 12f;

        [Header("시간")]
        [Tooltip("겨누는 시간. 플레이어가 숨거나 먼저 쏠 틈이다. 이 게임의 긴장이 여기서 나온다.")]
        public float aimSeconds = 1.2f;

        [Tooltip("쏘고 나서 다음 조준까지. 0이면 연사가 되어 피할 틈이 사라진다.")]
        public float recoverSeconds = 1f;

        [Tooltip("대상을 놓치고도 이만큼은 겨눈 채로 버틴다. 기둥 뒤로 잠깐 숨은 것과 완전히 따돌린 것을 가른다.")]
        public float loseAimSeconds = 0.8f;
    }
}
