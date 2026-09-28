using System;
using UnityEngine;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 기지 경계 단계(StationAlert)가 경비를 얼마나 바꾸는지 담는 데이터.
    /// 판정 코드는 StationAlert와 GuardBrain에 있고, 여기에는 "몇 배로"만 둔다.
    /// 에셋이 없어도 돌아가도록 필드 기본값을 그대로 게임 기본값으로 쓴다(Defaults 참고).
    ///
    /// 단계가 올라도 경비가 플레이어의 자리를 알게 되지는 않는다. 바뀌는 것은 걸음, 손전등, 눈의 날카로움뿐이다.
    /// 시체 하나에 기지 전체가 늑대의 위치를 아는 것은 벌이 아니라 반칙이다.
    /// </summary>
    [CreateAssetMenu(menuName = "By a Whisker/Station Alert Settings", fileName = "StationAlertSettings")]
    public class StationAlertSettings : ScriptableObject
    {
        /// <summary>한 단계에서 경비에게 곱하는 배율 묶음. 1이면 평소 그대로다.</summary>
        [Serializable]
        public struct Tier
        {
            [Tooltip("순찰 걸음 배율. 쫓거나 뒤지는 속도는 이미 빨라서 건드리지 않는다.")]
            public float patrolSpeedScale;

            [Tooltip("순찰 지점에서 쉬는 시간 배율. 작을수록 한자리에 오래 서 있지 않아 틈이 줄어든다.")]
            public float waitScale;

            [Tooltip("손전등 반경 배율. 판정(LightSource.radius)과 보이는 스포트라이트(Light.range)를 함께 늘린다.")]
            public float flashlightRangeScale;

            [Tooltip("발각까지 걸리는 시간 배율(SenseProfile.detectSeconds에 곱한다). 작을수록 빨리 알아챈다.")]
            public float detectTimeScale;

            [Tooltip("단서 자리에 도착한 뒤 둘러보는 시간 배율. 긴장한 경비는 쉽게 포기하지 않는다.")]
            public float searchTimeScale;

            public static Tier Make(float patrol, float wait, float flashlight, float detect, float search)
            {
                Tier tier;
                tier.patrolSpeedScale = patrol;
                tier.waitScale = wait;
                tier.flashlightRangeScale = flashlight;
                tier.detectTimeScale = detect;
                tier.searchTimeScale = search;
                return tier;
            }
        }

        [Header("단계별 효과")]
        [Tooltip("0단계, 평온. 보통은 모두 1로 둔다.")]
        public Tier calm = Tier.Make(1f, 1f, 1f, 1f, 1f);

        [Tooltip("1단계, 경계. 누군가 쓰러진 동료를 발견했다.")]
        public Tier raised = Tier.Make(1.25f, 0.6f, 1.2f, 0.85f, 1.3f);

        [Tooltip("2단계, 비상. 누군가 늑대를 똑똑히 봤거나 시체가 둘째로 발견됐다.")]
        public Tier emergency = Tier.Make(1.45f, 0.35f, 1.35f, 0.7f, 1.6f);

        [Header("올리는 조건")]
        [Tooltip("서로 다른 몸이 이만큼 발견되면 비상이 된다. 같은 몸을 여럿이 봐도 하나로 센다.")]
        [Min(1)] public int bodiesForEmergency = 2;

        [Tooltip("몸을 처음 발견한 경비가 소리친다. 들은 동료는 그 경비 자리(시체 쪽)로 찾아온다. 플레이어 자리는 알리지 않는다.")]
        public bool shoutOnBody = true;

        /// <summary>단계에 맞는 배율. 범위를 벗어나면 가장 가까운 단계로 붙인다.</summary>
        public Tier TierFor(int level)
        {
            if (level <= StationAlert.Calm) return calm;
            if (level == StationAlert.Raised) return raised;
            return emergency;
        }

        private static StationAlertSettings _defaults;

        /// <summary>
        /// 에셋을 안 만들었을 때 쓰는 코드 기본값. 필드 초기값이 곧 기본값이다.
        /// 씬에 저장되지 않게 DontSave를 건다. 플레이 모드를 나가면 파괴되어 가짜 null이 되므로 그때 다시 만든다.
        /// </summary>
        public static StationAlertSettings Defaults
        {
            get
            {
                if (_defaults == null)
                {
                    _defaults = CreateInstance<StationAlertSettings>();
                    _defaults.name = "StationAlertSettings (기본값)";
                    _defaults.hideFlags = HideFlags.DontSave;
                }
                return _defaults;
            }
        }
    }
}
