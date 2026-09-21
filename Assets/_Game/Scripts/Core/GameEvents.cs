using System;
using UnityEngine;

namespace ByAWhisker.Core
{
    /// <summary>
    /// 게임 전체가 공유하는 사건들. 발행하는 쪽과 듣는 쪽이 서로를 참조하지 않게 한다.
    /// 예를 들어 경비는 포획을 알릴 뿐이고, 누가 어떻게 재시작하는지는 모른다.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>플레이어가 잡혔다. 인자는 잡은 적.</summary>
        public static event Action<GameObject> PlayerCaptured;

        /// <summary>목표물을 주웠다.</summary>
        public static event Action ObjectiveTaken;

        /// <summary>목표물을 들고 출구에 닿았다.</summary>
        public static event Action PlayerEscaped;

        /// <summary>포획 후 재시작. 적도 제자리로 돌아간다.</summary>
        public static event Action RunReset;

        public static void RaisePlayerCaptured(GameObject by)
        {
            if (PlayerCaptured != null) PlayerCaptured(by);
        }

        public static void RaiseObjectiveTaken()
        {
            if (ObjectiveTaken != null) ObjectiveTaken();
        }

        public static void RaisePlayerEscaped()
        {
            if (PlayerEscaped != null) PlayerEscaped();
        }

        public static void RaiseRunReset()
        {
            if (RunReset != null) RunReset();
        }
    }
}
