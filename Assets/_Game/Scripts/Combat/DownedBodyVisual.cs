using UnityEngine;
using ByAWhisker.Core;

namespace ByAWhisker.Combat
{
    /// <summary>
    /// 쓰러진 몸을 눕히고 색을 죽인다. Damageable은 판정만 바꾸고 모습은 그대로 두기 때문에,
    /// 제압하고도 화면에서는 아무 일이 없는 것처럼 보였다.
    /// </summary>
    [RequireComponent(typeof(Damageable))]
    public class DownedBodyVisual : MonoBehaviour
    {
        [SerializeField] private Damageable damageable;
        [Tooltip("넘어지는 데 걸리는 시간.")]
        [SerializeField] private float fallSeconds = 0.4f;
        [Tooltip("누웠을 때 발밑 기준 몸이 뜨는 높이. 모델의 반지름쯤이어야 바닥에 파묻히지 않는다.")]
        [SerializeField] private float restHeight = 0.34f;
        [Tooltip("죽은 몸과 기절한 몸의 색. 기절은 조금 덜 어둡게 해서 구분한다.")]
        [SerializeField] private Color deadTint = new Color(0.22f, 0.20f, 0.24f, 1f);
        [SerializeField] private Color downedTint = new Color(0.34f, 0.38f, 0.48f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;

        private Vector3 _standPosition;
        private Quaternion _standRotation;

        private Quaternion _fallFrom;
        private Quaternion _fallTo;
        private Vector3 _fallFromPosition;
        private Vector3 _fallToPosition;
        private float _fallTimer = -1f;
        private bool _fell;

        private void Awake()
        {
            if (damageable == null) damageable = GetComponent<Damageable>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (damageable != null) damageable.Damaged += OnDamaged;
            GameEvents.RunReset += OnRunReset;
        }

        private void OnDisable()
        {
            // Damageable이 쓰러진 몸의 컴포넌트를 끄기 때문에 이 컴포넌트도 같이 꺼질 수 있다.
            // 그때 구독만 풀고 눕힌 자세는 그대로 둔다. 되살릴 때 RunReset이 원래대로 돌린다.
            if (damageable != null) damageable.Damaged -= OnDamaged;
            GameEvents.RunReset -= OnRunReset;
        }

        private void OnDamaged(DamageInfo info)
        {
            // 서 있던 자세를 기억해 둔다. 되살아날 때 여기로 돌아온다.
            _standPosition = transform.position;
            _standRotation = transform.rotation;

            // 맞은 방향으로 넘어진다. 총에 맞으면 날아온 쪽의 반대로, 제압은 앞으로 고꾸라진다.
            Vector3 push = info.direction;
            push.y = 0f;
            if (push.sqrMagnitude < 0.0001f) push = transform.forward;
            push.Normalize();

            Vector3 axis = Vector3.Cross(Vector3.up, push);
            if (axis.sqrMagnitude < 0.0001f) axis = transform.right;

            _fallFrom = transform.rotation;
            _fallTo = Quaternion.AngleAxis(90f, axis.normalized) * transform.rotation;
            _fallFromPosition = transform.position;
            // 넘어지면서 조금 앞으로 쏠린다. 제자리에서 도는 것보다 덜 어색하다.
            // 발밑이 기준점이고 몸은 그 위에 있다. 누우면 몸이 기준점 높이로 내려오므로
            // 기준점을 조금 올려야 바닥에 파묻히지 않는다.
            _fallToPosition = transform.position + push * 0.25f + Vector3.up * restHeight;
            _fallTimer = 0f;
            _fell = true;

            Tint(info.lethal ? deadTint : downedTint);
        }

        private void Update()
        {
            if (_fallTimer < 0f) return;

            _fallTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_fallTimer / Mathf.Max(0.01f, fallSeconds));

            // 처음에 빠르게 기울고 끝에서 느려진다. 뻣뻣하게 도는 것보다 넘어지는 것처럼 보인다.
            float eased = 1f - (1f - t) * (1f - t);

            transform.rotation = Quaternion.Slerp(_fallFrom, _fallTo, eased);
            transform.position = Vector3.Lerp(_fallFromPosition, _fallToPosition, eased);

            if (t >= 1f) _fallTimer = -1f;
        }

        private void OnRunReset()
        {
            _fallTimer = -1f;
            if (!_fell) return;

            // Damageable.Revive가 컴포넌트를 되살리지만 자세와 색은 우리 몫이다.
            transform.SetPositionAndRotation(_standPosition, _standRotation);
            Tint(null);
            _fell = false;
        }

        /// <summary>색을 덮어쓴다. null이면 원래 머티리얼 색으로 돌아간다.</summary>
        private void Tint(Color? color)
        {
            if (_renderers == null) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null) continue;

                renderer.GetPropertyBlock(_block);
                if (color.HasValue)
                {
                    // URP Lit은 _BaseColor를 쓰고 기본 셰이더는 _Color를 쓴다. 둘 다 넣어 둔다.
                    _block.SetColor(BaseColorId, color.Value);
                    _block.SetColor(ColorId, color.Value);
                }
                else
                {
                    _block.Clear();
                }
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}
