using UnityEngine;
using ByAWhisker.Core;
using ByAWhisker.Player;

namespace ByAWhisker.Level
{
    /// <summary>밟으면 다시 시작할 위치가 여기로 바뀐다. 트리거 콜라이더가 필요하다.</summary>
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;

        private bool _used;

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
            if (_used || gameManager == null) return;
            if (other.GetComponentInParent<PlayerMotor>() == null) return;

            gameManager.SetCheckpoint(transform.position);
            _used = true;
        }
    }
}
