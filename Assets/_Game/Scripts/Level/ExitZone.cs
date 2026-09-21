using UnityEngine;
using ByAWhisker.Core;
using ByAWhisker.Player;

namespace ByAWhisker.Level
{
    /// <summary>목표물을 든 채로 닿으면 한 판이 끝난다.</summary>
    [RequireComponent(typeof(Collider))]
    public class ExitZone : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;

        /// <summary>레벨을 만든 쪽이 연결해 준다.</summary>
        public void Bind(GameManager manager)
        {
            gameManager = manager;
        }

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (gameManager == null) return;
            if (other.GetComponentInParent<PlayerMotor>() == null) return;

            // 열쇠가 없으면 아무 일도 없다. 판정은 GameManager가 한 번 더 한다.
            if (!gameManager.HasObjective) return;

            GameEvents.RaisePlayerEscaped();
        }
    }
}
