using System;
using System.Collections.Generic;
using UnityEngine;
using ByAWhisker.Core;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 기지 전체의 경계 단계. 경비를 죽이면 대가가 남게 하려는 장치다.
    /// 0 평온 → 1 경계(쓰러진 몸 발견) → 2 비상(늑대를 똑똑히 봄, 또는 둘째 몸 발견).
    ///
    /// 한 판 안에서는 오르기만 하고 내리지 않는다. 기지가 한 번 뒤숭숭해지면 다시 느슨해지지 않아야
    /// "다 쏘고 지나가기"보다 "아무도 모르게 지나가기"가 이득이 된다. 재시작(GameEvents.RunReset)에만 0으로 돌아간다.
    ///
    /// 정적으로 둔 이유는 NoiseBus와 같다. 씬에 아무것도 놓지 않아도 돌아가고, 올리는 쪽(GuardBrain)과
    /// 듣는 쪽(경비, AlertHud)이 서로를 몰라도 된다.
    ///
    /// 단계는 기지의 분위기일 뿐 정보가 아니다. 단계가 올라도 발견하지 못한 경비는 플레이어의 자리를 모른다.
    /// Raise에 넘기는 자리는 표시와 디버깅용으로만 남긴다.
    /// </summary>
    public static class StationAlert
    {
        public const int Calm = 0;
        public const int Raised = 1;
        public const int Emergency = 2;
        public const int MaxLevel = Emergency;

        /// <summary>지금 단계. 0..MaxLevel.</summary>
        public static int Level { get; private set; }

        /// <summary>마지막으로 단계를 올린 자리. 올린 적이 없으면 원점이다.</summary>
        public static Vector3 LastRaisePosition { get; private set; }

        /// <summary>이번 판에 발견된 서로 다른 몸의 수.</summary>
        public static int BodiesFound { get { return _bodies.Count; } }

        /// <summary>
        /// 단계가 바뀔 때 새 단계를 준다. 오를 때와 재시작으로 0이 될 때 불린다.
        /// 구독은 OnEnable에서 넣고 OnDisable에서 빼라. 죽은 오브젝트가 계속 불리면 곤란하다.
        /// </summary>
        public static event Action<int> LevelChanged;

        // 이미 센 몸. 같은 시체를 경비 셋이 차례로 봐도 한 구로 쳐야 "둘째 시체"가 뜻을 가진다.
        private static readonly HashSet<int> _bodies = new HashSet<int>();

        private static StationAlertSettings _settings;
        private static bool _resourcesTried;

        /// <summary>
        /// 단계별 효과와 올리는 조건. 부트스트랩이 에셋을 넣어 줄 수 있다.
        /// 비어 있으면 Resources/StationAlertSettings를 한 번 찾아보고, 그것도 없으면 코드 기본값을 쓴다.
        /// 씬 연결이 빠져도 경계가 통째로 사라지지 않게 하려는 것이다.
        /// </summary>
        public static StationAlertSettings Settings
        {
            get
            {
                if (_settings != null) return _settings;

                if (!_resourcesTried)
                {
                    _resourcesTried = true;
                    _settings = Resources.Load<StationAlertSettings>("StationAlertSettings");
                    if (_settings != null) return _settings;
                }

                return StationAlertSettings.Defaults;
            }
            set { _settings = value; }
        }

        /// <summary>
        /// 적어도 atLeast 단계까지 올린다. 이미 그 이상이면 아무 일도 없다. 내리는 길은 없다.
        /// 올렸으면 참을 돌려준다.
        /// </summary>
        public static bool Raise(int atLeast, Vector3 where)
        {
            int next = Mathf.Clamp(atLeast, Calm, MaxLevel);
            if (next <= Level) return false;

            Level = next;
            LastRaisePosition = where;

            if (LevelChanged != null) LevelChanged(Level);
            return true;
        }

        /// <summary>
        /// 경비가 쓰러진 몸 하나를 받아들였다. 처음 보는 몸이면 세고 단계를 올린다.
        /// bodyId는 몸의 GetInstanceID다. 새로 센 몸이면 참을 돌려준다. 외칠지 정하는 데 쓴다.
        /// </summary>
        public static bool ReportBody(int bodyId, Vector3 where)
        {
            if (!_bodies.Add(bodyId)) return false;

            int needed = Mathf.Max(1, Settings.bodiesForEmergency);
            Raise(_bodies.Count >= needed ? Emergency : Raised, where);
            return true;
        }

        /// <summary>재시작. 시체도 경비도 되살아나므로 센 것도 모두 지운다.</summary>
        public static void ResetLevel()
        {
            bool changed = Level != Calm;

            Level = Calm;
            LastRaisePosition = Vector3.zero;
            _bodies.Clear();

            if (changed && LevelChanged != null) LevelChanged(Level);
        }

        private static void HandleRunReset()
        {
            ResetLevel();
        }

        /// <summary>
        /// 플레이 모드를 다시 시작해도 남지 않게 정적 상태를 되돌리고 재시작 신호를 건다.
        /// 도메인 리로드를 끄면 지난 판의 단계와 구독자가 그대로 살아 있다. NoiseBus와 같은 이유다.
        /// 한 번 빼고 다시 거는 것은 도메인 리로드가 꺼져 있을 때 두 번 걸리지 않게 하려는 것이다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LevelChanged = null;
            Level = Calm;
            LastRaisePosition = Vector3.zero;
            _bodies.Clear();

            _settings = null;
            _resourcesTried = false;

            GameEvents.RunReset -= HandleRunReset;
            GameEvents.RunReset += HandleRunReset;
        }
    }
}
