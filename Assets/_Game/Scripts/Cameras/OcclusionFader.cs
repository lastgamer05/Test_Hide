using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.Cameras
{
    /// <summary>
    /// 카메라와 캐릭터 사이를 가리는 구조물을 비춰 준다.
    /// 카메라 거리는 고정이므로 당기지 않고 가리는 쪽을 양보시킨다.
    /// </summary>
    public class OcclusionFader : MonoBehaviour
    {
        public enum Mode
        {
            Hide,
            Transparent
        }

        [SerializeField] private Transform target;
        [SerializeField] private LayerMask occluders;
        [SerializeField] private Mode mode = Mode.Transparent;
        [Tooltip("Transparent 모드에서 갈아 끼울 머티리얼")]
        [SerializeField] private Material fadeMaterial;
        [SerializeField] private float checkInterval = 0.05f;
        [Tooltip("캐릭터 몸 여러 점으로 검사한다. 모서리에서 깜빡이는 걸 줄인다.")]
        [SerializeField] private float[] sampleHeights = { 0.3f, 1f, 1.7f };

        /// 비추기 전 모습. 켜짐 여부까지 기억해야 한다 — 옷을 입힌 곳은 판정 상자의 그림이 꺼져 있어서,
        /// 되돌릴 때 무조건 켜면 옷 위로 판정 상자가 튀어나온다.
        private struct Original
        {
            public Material[] materials;
            public bool enabled;
        }

        private readonly Dictionary<Renderer, Original> _faded = new Dictionary<Renderer, Original>();
        private readonly HashSet<Renderer> _hitThisCheck = new HashSet<Renderer>();
        private readonly List<Renderer> _restoreBuffer = new List<Renderer>();
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private float _nextCheck;

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + checkInterval;

            _hitThisCheck.Clear();
            CollectOccluders();
            ApplyFade();
            RestoreOthers();
        }

        private void CollectOccluders()
        {
            for (int i = 0; i < sampleHeights.Length; i++)
            {
                Vector3 point = target.position + Vector3.up * sampleHeights[i];
                Vector3 delta = point - transform.position;
                float distance = delta.magnitude;
                if (distance <= 0.01f) continue;

                int count = Physics.RaycastNonAlloc(transform.position, delta / distance, _hits, distance - 0.2f, occluders, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                {
                    Collider collider = _hits[h].collider;

                    // 옷을 입힌 상자는 제 그림이 꺼져 있다. 보이는 것은 대리인이 가리키는 옷이다.
                    OcclusionProxy proxy = collider.GetComponent<OcclusionProxy>();
                    if (proxy != null)
                    {
                        IReadOnlyList<Renderer> dressed = proxy.Renderers;
                        for (int r = 0; r < dressed.Count; r++)
                        {
                            if (dressed[r] != null) _hitThisCheck.Add(dressed[r]);
                        }
                    }

                    Renderer renderer = collider.GetComponent<Renderer>();
                    if (renderer != null && renderer.enabled) _hitThisCheck.Add(renderer);
                }
            }
        }

        private void ApplyFade()
        {
            foreach (Renderer renderer in _hitThisCheck)
            {
                if (_faded.ContainsKey(renderer)) continue;

                _faded.Add(renderer, new Original { materials = renderer.sharedMaterials, enabled = renderer.enabled });

                if (mode == Mode.Hide || fadeMaterial == null)
                {
                    renderer.enabled = false;
                    continue;
                }

                var swapped = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < swapped.Length; i++) swapped[i] = fadeMaterial;
                renderer.sharedMaterials = swapped;
            }
        }

        private void RestoreOthers()
        {
            _restoreBuffer.Clear();

            foreach (KeyValuePair<Renderer, Original> pair in _faded)
            {
                if (pair.Key == null || !_hitThisCheck.Contains(pair.Key)) _restoreBuffer.Add(pair.Key);
            }

            for (int i = 0; i < _restoreBuffer.Count; i++)
            {
                Renderer renderer = _restoreBuffer[i];
                if (renderer != null) Restore(renderer, _faded[renderer]);

                _faded.Remove(renderer);
            }
        }

        private void OnDisable()
        {
            // 씬을 떠날 때 원래대로 돌려 둔다. 에디터에서 머티리얼이 바뀐 채 남지 않게.
            foreach (KeyValuePair<Renderer, Original> pair in _faded)
            {
                if (pair.Key != null) Restore(pair.Key, pair.Value);
            }

            _faded.Clear();
        }

        private static void Restore(Renderer renderer, Original original)
        {
            renderer.enabled = original.enabled;
            renderer.sharedMaterials = original.materials;
        }
    }
}
