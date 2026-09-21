using UnityEngine;

namespace ByAWhisker.Visibility
{
    /// <summary>
    /// 적은 플레이어가 지금 보고 있을 때만 그린다.
    /// 기억 텍스처에는 사람이 남지 않으므로, 시야를 벗어난 적은 화면에서 사라진다.
    /// </summary>
    public class EnemyVisibility : MonoBehaviour
    {
        [SerializeField] private PlayerVision vision;
        [Tooltip("판정 기준점. 비우면 이 오브젝트 위치에 높이를 더해 쓴다.")]
        [SerializeField] private Transform samplePoint;
        [SerializeField] private float sampleHeight = 1.2f;
        [Tooltip("판정 간격. 매 프레임 레이캐스트를 하지 않는다.")]
        [SerializeField] private float checkInterval = 0.05f;
        [Tooltip("시야에서 벗어난 뒤 사라지기까지의 유예. 경계에서 깜빡이는 걸 막는다.")]
        [SerializeField] private float hideDelay = 0.2f;

        private Renderer[] _renderers;
        private float _nextCheck;
        private float _hideAt;
        private bool _visible = true;

        public bool Visible { get { return _visible; } }

        /// <summary>부트스트랩이 플레이어 시야를 연결해 준다.</summary>
        public void Bind(PlayerVision playerVision)
        {
            vision = playerVision;
        }

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void Update()
        {
            if (vision == null) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + checkInterval;

            Vector3 point = samplePoint != null
                ? samplePoint.position
                : transform.position + Vector3.up * sampleHeight;

            if (vision.IsVisible(point))
            {
                _hideAt = 0f;
                SetVisible(true);
                return;
            }

            // 보이지 않게 된 순간부터 유예를 센다.
            if (_hideAt <= 0f) _hideAt = Time.time + hideDelay;
            if (Time.time >= _hideAt) SetVisible(false);
        }

        private void SetVisible(bool value)
        {
            if (_visible == value) return;
            _visible = value;

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null) _renderers[i].enabled = value;
            }
        }
    }
}
