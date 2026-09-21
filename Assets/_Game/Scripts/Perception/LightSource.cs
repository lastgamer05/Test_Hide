using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.Perception
{
    /// <summary>
    /// 램프에 붙는다. 밝기 계산이 장면을 뒤지지 않도록 스스로 정적 목록에 등록한다.
    /// 목록은 OnEnable에서 넣고 OnDisable에서 뺀다. 그래야 프리팹을 껐다 켜도 유령이 남지 않는다.
    /// </summary>
    public class LightSource : MonoBehaviour
    {
        [Tooltip("이 거리 밖은 완전히 어둡다.")]
        public float radius = 5.5f;

        [Tooltip("램프 중심의 밝기. 0..1")]
        [Range(0f, 1f)] public float intensity = 1f;

        [Tooltip("시작할 때 켜져 있는가.")]
        [SerializeField] private bool startOn = true;

        [Tooltip("같이 껐다 켤 실제 조명. 비워 두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private Light unityLight;

        // 인터페이스로 순회하면 열거자가 박싱되니 내부에서는 구체 List를 그대로 쓴다.
        private static readonly List<LightSource> _all = new List<LightSource>();

        private bool _isOn;

        /// <summary>등록된 램프 전체. 읽기 전용으로만 준다.</summary>
        public static IReadOnlyList<LightSource> All { get { return _all; } }

        /// <summary>꺼진 램프는 밝기 계산에서 빠진다. 보이는 조명도 같이 맞춘다.</summary>
        public bool IsOn
        {
            get { return _isOn; }
            set
            {
                _isOn = value;
                ApplyToUnityLight();
            }
        }

        private void Awake()
        {
            if (unityLight == null) unityLight = GetComponent<Light>();
            _isOn = startOn;
        }

        private void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
            ApplyToUnityLight();
        }

        private void OnDisable()
        {
            _all.Remove(this);
        }

        private void ApplyToUnityLight()
        {
            // 판정과 보이는 그림이 어긋나면 디버깅이 괴로우니 한쪽에서만 상태를 쥔다.
            if (unityLight != null) unityLight.enabled = _isOn;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.4f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
#endif
    }
}
