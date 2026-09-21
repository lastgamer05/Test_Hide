using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 순찰 지점 목록. 지점을 자식 오브젝트로 두면 씬에서 눈으로 보고 옮길 수 있다.
    /// 경로를 도는 주체는 GuardBrain이고 여기는 좌표만 돌려준다.
    /// </summary>
    public class PatrolRoute : MonoBehaviour
    {
        /// <summary>지점마다 쉬는 시간. 계약이 공개 필드로 정해 두었다.</summary>
        public float waitSeconds = 1.2f;

        [Tooltip("비워 두면 자식 트랜스폼을 위에서부터 순서대로 쓴다.")]
        [SerializeField] private List<Transform> points = new List<Transform>();
        [SerializeField] private Color gizmoColor = new Color(0.4f, 0.9f, 1f, 1f);

        // 자식에서 모은 결과를 매번 새로 만들지 않으려고 캐시한다.
        private readonly List<Transform> _resolved = new List<Transform>();
        private bool _resolvedReady;

        public int Count
        {
            get { return Resolve().Count; }
        }

        /// <summary>index는 알아서 순환시킨다. 음수도 받는다.</summary>
        public Vector3 PointAt(int index)
        {
            List<Transform> list = Resolve();
            if (list.Count == 0) return transform.position;

            // C#의 %는 음수에서 음수를 내니까 한 번 더 더해서 올린다.
            int wrapped = ((index % list.Count) + list.Count) % list.Count;
            Transform point = list[wrapped];
            return point != null ? point.position : transform.position;
        }

        private List<Transform> Resolve()
        {
            if (_resolvedReady) return _resolved;

            _resolved.Clear();
            if (points != null && points.Count > 0)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    if (points[i] != null) _resolved.Add(points[i]);
                }
            }
            else
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    _resolved.Add(transform.GetChild(i));
                }
            }

            // 에디터에서 지점을 추가하면 다시 모아야 하니 플레이 중에만 굳힌다.
            _resolvedReady = Application.isPlaying;
            return _resolved;
        }

        private void OnDisable()
        {
            _resolvedReady = false;
        }

        private void OnDrawGizmos()
        {
            List<Transform> list = Resolve();
            if (list.Count == 0) return;

            Gizmos.color = gizmoColor;
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 a = PointAt(i);
                Vector3 b = PointAt(i + 1);
                Gizmos.DrawSphere(a, 0.18f);
                if (list.Count > 1) Gizmos.DrawLine(a, b);
            }
        }
    }
}
