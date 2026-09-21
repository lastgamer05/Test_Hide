using UnityEngine;
using UnityEngine.Rendering;

namespace ByAWhisker.AI
{
    /// <summary>
    /// 겨누고 있다는 것을 플레이어에게 보여 준다. 조준 시간이 이 게임의 긴장인데
    /// 그게 화면에 안 보이면 그냥 뜬금없는 즉사로 느껴진다.
    /// 표시는 GuardGunner가 굴리는 값(AimProgress, AimPoint)에서만 나온다. 여기서 따로 판정하지 않는다.
    /// </summary>
    public class AimTelegraph : MonoBehaviour
    {
        [Tooltip("비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private GuardGunner gunner;

        [Header("색")]
        [Tooltip("막 겨누기 시작했을 때. 화면이 어두우니 얇고 밝은 색이 잘 보인다.")]
        [SerializeField] private Color aimColor = new Color(1f, 0.72f, 0.32f, 0.55f);

        [Tooltip("조준이 다 찼을 때. 색이 변하는 것만으로 '이제 쏜다'를 읽을 수 있어야 한다.")]
        [SerializeField] private Color readyColor = new Color(1f, 0.32f, 0.24f, 1f);

        [Tooltip("쏜 순간 한 번 번쩍인다. 총성만으로는 어디서 쐈는지 모른다.")]
        [SerializeField] private Color flashColor = new Color(1f, 0.95f, 0.82f, 1f);

        [Tooltip("경비 쪽 끝의 투명도 배율. 대상 쪽이 진해야 선이 어디로 뻗는지 읽힌다.")]
        [Range(0f, 1f)]
        [SerializeField] private float tailAlpha = 0.35f;

        [Header("굵기")]
        [SerializeField] private float aimWidth = 0.02f;
        [SerializeField] private float readyWidth = 0.06f;

        [Tooltip("바닥에서 띄우는 높이. 0이면 바닥과 z 싸움이 나서 선이 지글거린다.")]
        [SerializeField] private float groundOffset = 0.06f;

        [Header("시간")]
        [Tooltip("조준이 풀린 뒤 선이 사라지는 시간. 뚝 끊기면 놓친 건지 쏜 건지 헷갈린다.")]
        [SerializeField] private float fadeSeconds = 0.15f;

        [SerializeField] private float flashSeconds = 0.08f;

        [Tooltip("비우면 코드가 만든다. 프리팹도 애셋도 없이 돌아가야 한다.")]
        [SerializeField] private Material lineMaterial;

        // 경비마다 선은 하나씩이지만 머티리얼은 같은 것을 나눠 쓴다.
        private static Material _sharedMaterial;

        private GameObject _lineObject;
        private LineRenderer _line;
        private GuardGunner _bound;
        private float _fadeLeft;
        private float _flashLeft;
        private Vector3 _lastAimPoint;

        private void Awake()
        {
            if (gunner == null) gunner = GetComponent<GuardGunner>();
            _lastAimPoint = transform.position;

            EnsureLine();
            Subscribe(gunner);
        }

        /// <summary>어느 총을 보여 줄지 정한다. 통합 담당이 씬에서 연결하거나 Awake가 알아서 찾는다.</summary>
        public void Bind(GuardGunner target)
        {
            Subscribe(target);
            gunner = target;
        }

        private void OnDisable()
        {
            // 꺼지면 LateUpdate가 멈춘다. 선을 지우지 않으면 겨누던 자세 그대로 허공에 얼어붙는다.
            _fadeLeft = 0f;
            _flashLeft = 0f;
            Show(false);
        }

        private void OnDestroy()
        {
            Subscribe(null);

            // 선은 경비의 자식이 아니라 씬에 따로 서 있다. 주인이 사라지면 같이 치운다.
            if (_lineObject != null) Destroy(_lineObject);
            _lineObject = null;
            _line = null;
        }

        /// <summary>
        /// 경비가 움직이고 돌아본 뒤에 선을 그어야 한 프레임 밀리지 않는다.
        /// NavMeshAgent는 Update에서 위치를 바꾸니 여기는 LateUpdate여야 한다.
        /// </summary>
        private void LateUpdate()
        {
            if (_line == null) return;

            float dt = Time.deltaTime;
            if (_flashLeft > 0f) _flashLeft -= dt;

            bool aiming = gunner != null && gunner.IsAiming;

            if (aiming)
            {
                _lastAimPoint = gunner.AimPoint;
                _fadeLeft = fadeSeconds;
            }
            else if (_fadeLeft > 0f)
            {
                _fadeLeft -= dt;
            }

            if (!aiming && _fadeLeft <= 0f && _flashLeft <= 0f)
            {
                Show(false);
                return;
            }

            Show(true);

            // 발밑에서 대상 쪽으로 눕힌 선. 위에서 내려다보는 각이라 바닥에 누운 선이 제일 잘 읽히고,
            // 몸통 높이로 그으면 벽이나 상자에 파묻혀 토막 난다.
            Vector3 from = transform.position + Vector3.up * groundOffset;
            Vector3 to = _lastAimPoint;
            to.y = from.y;

            _line.SetPosition(0, from);
            _line.SetPosition(1, to);

            float progress = aiming && gunner != null ? Mathf.Clamp01(gunner.AimProgress) : 1f;
            float flash = flashSeconds > 0.0001f ? Mathf.Clamp01(_flashLeft / flashSeconds) : 0f;
            float fade = aiming || flash > 0f
                ? 1f
                : (fadeSeconds > 0.0001f ? Mathf.Clamp01(_fadeLeft / fadeSeconds) : 0f);

            Color color = Color.Lerp(aimColor, readyColor, progress);
            if (flash > 0f) color = Color.Lerp(color, flashColor, flash);
            color.a *= fade;

            Color tail = color;
            tail.a *= tailAlpha;

            // 머티리얼 색이 아니라 정점 색을 바꾼다. 머티리얼을 건드리면 경비마다 사본이 하나씩 생긴다.
            _line.startColor = tail;
            _line.endColor = color;
            _line.widthMultiplier = Mathf.Lerp(aimWidth, readyWidth, progress) * (1f + flash);
        }

        private void HandleFired()
        {
            _flashLeft = flashSeconds;
        }

        private void Subscribe(GuardGunner next)
        {
            if (_bound == next) return;

            if (_bound != null) _bound.Fired -= HandleFired;
            _bound = next;
            if (_bound != null) _bound.Fired += HandleFired;
        }

        private void EnsureLine()
        {
            if (_line != null) return;

            // 경비의 자식으로 두면 EnemyVisibility가 자식 렌더러를 통째로 끈다.
            // 경비 몸이 안 보이는 상황에서도 겨누는 선은 보여야 경고 구실을 하니 씬 루트에 따로 세운다.
            _lineObject = new GameObject(name + "_AimLine");
            _lineObject.hideFlags = HideFlags.DontSave;

            _line = _lineObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.numCapVertices = 0;
            _line.numCornerVertices = 0;
            // 카메라를 90도 돌려도 굵기가 그대로 보여야 한다.
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.lightProbeUsage = LightProbeUsage.Off;
            _line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _line.sharedMaterial = LineMaterial();
            _line.enabled = false;
        }

        private Material LineMaterial()
        {
            if (lineMaterial != null) return lineMaterial;

            // 플레이 모드를 나가면 파괴되어 가짜 null이 된다. 그때 다시 만든다.
            if (_sharedMaterial != null) return _sharedMaterial;

            // 정점 색과 반투명을 그대로 받아 주는 셰이더가 필요하다. 앞쪽부터 있는 것을 쓴다.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return null;

            _sharedMaterial = new Material(shader)
            {
                name = "BW_AimLine",
                hideFlags = HideFlags.HideAndDontSave
            };
            return _sharedMaterial;
        }

        private void Show(bool visible)
        {
            if (_line == null) return;
            // enabled 대입도 공짜가 아니다. 이미 그 상태면 건드리지 않는다.
            if (_line.enabled != visible) _line.enabled = visible;
        }
    }
}
