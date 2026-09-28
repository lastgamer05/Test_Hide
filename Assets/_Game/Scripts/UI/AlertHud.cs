using UnityEngine;
using UnityEngine.UI;
using ByAWhisker.AI;

namespace ByAWhisker.UI
{
    /// <summary>
    /// 기지 경계 단계(StationAlert)를 화면 위 가운데에 작은 딱지로 보여 준다.
    /// 평온(0)일 때는 아무것도 띄우지 않는다. 경계(1)는 호박색, 비상(2)은 붉은색이고, 오르는 순간 한 번 튄다.
    ///
    /// 경비를 쏘면 무언가 나빠진다는 것을 플레이어가 알아야 "피해 가기"를 고른다. 걸음이 빨라지고
    /// 손전등이 길어지는 것만으로는 무엇 때문인지 읽히지 않는다.
    ///
    /// 판정은 하지 않는다. StationAlert.LevelChanged를 듣기만 한다. 캔버스와 글자는 코드로 만든다(SenseHud와 같은 규칙).
    /// 씬에 놓지 않아도 첫 씬이 열린 뒤 스스로 하나 만든다. 씬에 직접 놓아 두면 그쪽을 쓴다.
    /// </summary>
    [DisallowMultipleComponent]
    public class AlertHud : MonoBehaviour
    {
        [Header("캔버스")]
        [Tooltip("크기 값을 적는 기준 해상도. SenseHudStyle과 같은 값으로 둔다.")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);

        [Tooltip("SenseHud 100, ControlsOverlay 110, SpottedEdge 120, SpottedFlash 130. 발각 경고보다는 아래에 둔다.")]
        [SerializeField] private int sortingOrder = 115;

        [Header("딱지")]
        [Tooltip("화면 위 가장자리에서 내려오는 거리. 왼쪽 위의 조작 안내와 높이를 맞춘다.")]
        [SerializeField] private float topMargin = 28f;

        [Tooltip("딱지 크기. 글자 두 자가 넉넉히 들어가는 정도로 작게 둔다. 화면 가운데는 게임이 쓰는 자리다.")]
        [SerializeField] private Vector2 badgeSize = new Vector2(132f, 44f);

        [Tooltip("딱지 바탕색. ControlsOverlay 패널과 같은 어두운 반투명이다.")]
        [SerializeField] private Color panelColor = new Color(0.04f, 0.05f, 0.07f, 0.72f);

        [Tooltip("아래쪽 띠의 두께. 단계 색을 글자 말고 한 번 더 보여 주는 줄이다.")]
        [SerializeField] private float accentHeight = 3f;

        [Tooltip("글자 크기.")]
        [SerializeField] private int fontSize = 26;

        [Header("단계")]
        [Tooltip("1단계에 띄울 글자.")]
        [SerializeField] private string raisedLabel = "경계";

        [Tooltip("1단계 색. 호박색. AwarenessGauge의 의심 색과 같은 계열이라 '누군가 수상하게 여긴다'로 읽힌다.")]
        [SerializeField] private Color raisedColor = new Color(1f, 0.72f, 0.22f, 1f);

        [Tooltip("2단계에 띄울 글자.")]
        [SerializeField] private string emergencyLabel = "비상";

        [Tooltip("2단계 색. SpottedEdge의 쐐기와 같은 붉은색이라 발각 경고와 한 가지 위험으로 읽힌다.")]
        [SerializeField] private Color emergencyColor = new Color(1f, 0.30f, 0.24f, 1f);

        [Header("튀기")]
        [Tooltip("단계가 오른 순간의 배율. 한 번 커졌다가 1로 가라앉는다.")]
        [Range(1f, 3f)] [SerializeField] private float pulseScale = 1.4f;

        [Tooltip("튀었다가 가라앉는 시간(초). SpottedFlash가 시간을 늦추므로 실제 시간으로 센다.")]
        [SerializeField] private float pulseSeconds = 0.7f;

        [Tooltip("튀는 동안 바탕을 단계 색으로 물들이는 정도. 0..1")]
        [Range(0f, 1f)] [SerializeField] private float pulseTint = 0.55f;

        private RectTransform _badge;
        private Image _panel;
        private Image _accent;
        private Text _label;
        private Font _font;

        private int _shownLevel = -1;
        private Color _levelColor;
        private bool _pulsing;
        private float _pulseStart;

        /// <summary>지금 띄운 단계. 씬 연결이 어긋났는지 눈으로 보려고 열어 둔다.</summary>
        public int ShownLevel { get { return _shownLevel; } }

        /// <summary>
        /// 씬에 AlertHud가 없으면 첫 씬이 열린 뒤 하나 만든다. 통합 담당이 씬을 고치지 않아도 되게 하려는 것이다.
        /// 씬을 다시 불러도 사라지지 않게 DontDestroyOnLoad로 둔다. 이 함수는 플레이마다 한 번만 불린다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfMissing()
        {
            if (FindAnyObjectByType<AlertHud>(FindObjectsInactive.Include) != null) return;

            GameObject go = new GameObject("Alert HUD");
            DontDestroyOnLoad(go);
            go.AddComponent<AlertHud>();
        }

        private void Awake()
        {
            _font = ResolveFont();
            Build();
            _badge.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            StationAlert.LevelChanged += HandleLevelChanged;
            // 꺼져 있던 사이에 오른 단계는 튀지 않고 조용히 맞춘다. 이미 지나간 일을 새 사건처럼 알리지 않는다.
            Show(StationAlert.Level, false);
        }

        private void OnDisable()
        {
            StationAlert.LevelChanged -= HandleLevelChanged;
        }

        private void HandleLevelChanged(int level)
        {
            // 오를 때만 튄다. 재시작으로 0이 되는 것은 조용히 사라지면 된다.
            Show(level, level > _shownLevel);
        }

        private void Update()
        {
            if (!_pulsing) return;

            float duration = Mathf.Max(0.01f, pulseSeconds);
            float t = (Time.unscaledTime - _pulseStart) / duration;
            if (t >= 1f)
            {
                _pulsing = false;
                ApplyPulse(0f);
                return;
            }

            // 처음에 가장 크고 점점 가라앉는다. 제곱으로 빼서 끝이 부드럽게 붙게 한다.
            float remain = 1f - t;
            ApplyPulse(remain * remain);
        }

        /// <summary>단계에 맞춰 글자와 색을 바꾼다. 매 프레임이 아니라 바뀔 때만 불린다.</summary>
        private void Show(int level, bool pulse)
        {
            _shownLevel = level;

            if (level <= StationAlert.Calm)
            {
                _pulsing = false;
                _badge.gameObject.SetActive(false);
                return;
            }

            bool emergency = level >= StationAlert.Emergency;
            _levelColor = emergency ? emergencyColor : raisedColor;
            _label.text = emergency ? emergencyLabel : raisedLabel;
            _label.color = _levelColor;
            _accent.color = _levelColor;

            _badge.gameObject.SetActive(true);

            if (pulse)
            {
                _pulsing = true;
                _pulseStart = Time.unscaledTime;
                ApplyPulse(1f);
            }
            else
            {
                _pulsing = false;
                ApplyPulse(0f);
            }
        }

        /// <summary>amount 1이면 가장 크게 튄 모양, 0이면 가라앉은 모양이다.</summary>
        private void ApplyPulse(float amount)
        {
            float scale = Mathf.Lerp(1f, pulseScale, amount);
            _badge.localScale = new Vector3(scale, scale, 1f);

            Color tinted = Color.Lerp(panelColor, _levelColor, pulseTint * amount);
            tinted.a = Mathf.Lerp(panelColor.a, 1f, amount * pulseTint);
            _panel.color = tinted;
        }

        private void Build()
        {
            Canvas canvas = SenseHudBuilder.CreateOverlayCanvas("AlertHud Canvas", transform, sortingOrder, referenceResolution);

            _panel = SenseHudBuilder.CreateImage("Alert Badge", canvas.transform, SenseHudBuilder.SolidSprite, panelColor);
            _badge = _panel.rectTransform;
            // 위 가운데에 매단다. 피벗도 위에 두면 튈 때 아래로만 커져서 화면 밖으로 잘리지 않는다.
            SenseHudBuilder.Anchor(_badge, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            _badge.anchoredPosition = new Vector2(0f, -topMargin);
            _badge.sizeDelta = badgeSize;

            _accent = SenseHudBuilder.CreateImage("Accent", _badge, SenseHudBuilder.SolidSprite, raisedColor);
            RectTransform accentRect = _accent.rectTransform;
            SenseHudBuilder.Anchor(accentRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(0f, Mathf.Max(0f, accentHeight));

            GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(_badge, false);

            _label = textGo.GetComponent<Text>();
            _label.font = _font;
            _label.fontSize = fontSize;
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _label.raycastTarget = false;

            RectTransform labelRect = _label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            // 아래 띠만큼 글자를 살짝 올려 가운데로 보이게 한다.
            labelRect.offsetMin = new Vector2(0f, Mathf.Max(0f, accentHeight));
            labelRect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 한글이 나와야 해서 OS 폰트를 빌린다. ControlsOverlay와 같은 후보 순서를 쓴다.
        /// 없으면 유니티 기본 폰트로 떨어지고, 그 경우 한글은 네모로 보인다.
        /// </summary>
        private static Font ResolveFont()
        {
            string[] candidates = { "Malgun Gothic", "맑은 고딕", "Noto Sans KR", "Arial Unicode MS", "Gulim", "Arial" };
            Font font = Font.CreateDynamicFontFromOSFont(candidates, 24);
            if (font != null) return font;

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
