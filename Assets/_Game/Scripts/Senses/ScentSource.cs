using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.Senses
{
    /// <summary>
    /// 냄새를 남기는 것에 붙는다. 사람, 시체, 기름통 같은 것들.
    /// 스스로 ScentField를 찾지 않는다. 필드 쪽이 이 정적 목록을 훑는다.
    /// M2의 LightSource와 같은 방식이다. 그래야 필드가 여러 개든 없든 이쪽은 신경 쓸 것이 없다.
    /// 목록은 OnEnable에서 넣고 OnDisable에서 뺀다. 껐다 켠 프리팹이 유령으로 남지 않는다.
    /// </summary>
    public class ScentSource : MonoBehaviour
    {
        [Tooltip("지금 내뿜는 세기. 0..1. 죽은 것은 낮게, 기름통은 높게 둔다.")]
        [Range(0f, 1f)] [SerializeField] private float strength = 1f;

        [Tooltip("여러 개체를 한 주인으로 묶고 싶을 때만 채운다. 0이면 오브젝트마다 다른 값이 자동으로 붙는다.")]
        [SerializeField] private int ownerIdOverride = 0;

        // 인터페이스로 순회하면 열거자가 박싱되니 내부에서는 구체 List를 그대로 쓴다.
        private static readonly List<ScentSource> _all = new List<ScentSource>();

        private int _ownerId;

        /// <summary>등록된 냄새 원천 전체. 읽기 전용으로만 준다.</summary>
        public static IReadOnlyList<ScentSource> All { get { return _all; } }

        /// <summary>
        /// 개체 구분용. 같은 오브젝트는 늘 같은 값을 돌려준다.
        /// Awake에서 정하지 않고 처음 물을 때 정한다. 필드가 Awake보다 먼저 훑어도 0이 새어 나가지 않는다.
        /// </summary>
        public int OwnerId
        {
            get
            {
                if (_ownerId == 0)
                {
                    _ownerId = ownerIdOverride != 0 ? ownerIdOverride : gameObject.GetInstanceID();
                }

                return _ownerId;
            }
        }

        /// <summary>0..1. 지금 내뿜는 세기.</summary>
        public float Strength { get { return strength; } }

        /// <summary>
        /// 세기를 바꾼다. 앉으면 줄이고 뛰면 늘리는 식으로 다른 쪽에서 불러 준다.
        /// 범위를 여기서 자른다. 0..1 밖의 값이 들어오면 격자 한 칸이 순식간에 포화된다.
        /// </summary>
        public void SetStrength(float value)
        {
            strength = Mathf.Clamp01(value);
        }

        private void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
        }

        private void OnDisable()
        {
            _all.Remove(this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 냄새는 눈에 안 보이니 씬에서 어디가 원천인지 표시해 둔다. 반지름은 세기를 눈대중으로 본 것이다.
            Gizmos.color = new Color(0.5f, 0.85f, 0.4f, 0.45f);
            Gizmos.DrawWireSphere(transform.position, 0.25f + strength);
        }
#endif
    }
}
