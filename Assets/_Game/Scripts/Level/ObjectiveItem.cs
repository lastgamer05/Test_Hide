using UnityEngine;
using ByAWhisker.Core;
using ByAWhisker.Player;

namespace ByAWhisker.Level
{
    /// <summary>
    /// 열쇠 같은 목표물. 닿으면 사라지고 사건을 알린다.
    /// 한 번 주우면 포획당해도 계속 들고 있다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ObjectiveItem : MonoBehaviour
    {
        [Tooltip("주웠을 때 숨길 겉모습. 비우면 이 오브젝트 전체를 끈다.")]
        [SerializeField] private GameObject visual;
        [SerializeField] private float spinDegreesPerSecond = 90f;

        private bool _taken;

        /// <summary>레벨을 만든 쪽이 겉모습을 연결해 준다.</summary>
        public void SetVisual(GameObject target)
        {
            visual = target;
        }

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;
        }

        private void Update()
        {
            if (_taken) return;
            transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_taken) return;
            if (other.GetComponentInParent<PlayerMotor>() == null) return;

            _taken = true;
            GameEvents.RaiseObjectiveTaken();

            if (visual != null) visual.SetActive(false);
            else gameObject.SetActive(false);
        }
    }
}
