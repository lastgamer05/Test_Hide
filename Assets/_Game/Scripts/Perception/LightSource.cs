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

        [Tooltip("퍼지는 각도(도). 0이면 사방으로 퍼지는 램프, 0보다 크면 앞쪽만 비추는 손전등이다.")]
        [Range(0f, 180f)] public float coneAngle = 0f;

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

        /// <summary>
        /// 그 자리가 이 빛의 부채꼴 안에 드는가. 각도가 0이면 사방을 비추므로 언제나 참이다.
        /// 위아래는 따지지 않는다. 층이 하나뿐이라 수평으로만 보면 된다.
        /// </summary>
        public bool Covers(Vector3 worldPosition)
        {
            if (coneAngle <= 0f) return true;

            Vector3 to = worldPosition - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return true;   // 발밑은 언제나 밝다

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return true;

            float half = coneAngle * 0.5f;
            return Vector3.Angle(forward.normalized, to.normalized) <= half;
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
            if (coneAngle <= 0f)
            {
                Gizmos.DrawWireSphere(transform.position, radius);
                return;
            }

            // 손전등은 비추는 쪽을 알아야 자리를 잡을 수 있다. 부채꼴의 양 끝과 가운데만 그린다.
            float half = coneAngle * 0.5f;
            Vector3 forward = transform.forward;
            Gizmos.DrawLine(transform.position, transform.position + Quaternion.Euler(0f, -half, 0f) * forward * radius);
            Gizmos.DrawLine(transform.position, transform.position + Quaternion.Euler(0f, half, 0f) * forward * radius);
            Gizmos.DrawLine(transform.position, transform.position + forward * radius);
        }
#endif
    }
}
