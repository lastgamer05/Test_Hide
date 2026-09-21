using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using ByAWhisker.Combat;
using ByAWhisker.Player;

namespace ByAWhisker.UI
{
    /// <summary>
    /// 조작 키 안내와 지금 상태를 글자로 보여 준다.
    /// 캔버스와 글자는 코드로 만든다. 폰트 애셋을 두지 않으려고 OS 폰트를 빌려 쓴다.
    /// </summary>
    public class ControlsOverlay : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("비우면 씬에서 찾는다.")]
        [SerializeField] private PlayerStance stance;
        [SerializeField] private Weapon weapon;
        [SerializeField] private TakedownAction takedown;

        [Header("보이기")]
        [Tooltip("시작할 때 키 안내를 펼쳐 둘지. 접어도 Tab으로 다시 편다.")]
        [SerializeField] private bool openOnStart = true;
        [SerializeField] private Key toggleKey = Key.Tab;

        [Header("모양")]
        [SerializeField] private int titleSize = 20;
        [SerializeField] private int bodySize = 17;
        [SerializeField] private Vector2 margin = new Vector2(28f, 28f);
        [SerializeField] private Vector2 panelSize = new Vector2(330f, 320f);
        [SerializeField] private Color panelColor = new Color(0.04f, 0.05f, 0.07f, 0.72f);
        [SerializeField] private Color titleColor = new Color(0.78f, 0.88f, 0.95f, 1f);
        [SerializeField] private Color bodyColor = new Color(0.72f, 0.78f, 0.84f, 0.95f);
        [SerializeField] private Color statusColor = new Color(0.95f, 0.86f, 0.62f, 1f);
        [SerializeField] private int sortingOrder = 110;

        [Header("감각 막대 이름표")]
        [Tooltip("첫 막대 이름표의 왼쪽 아래 자리. SenseHudStyle의 barMargin과 맞춘다.")]
        [SerializeField] private Vector2 barLabelOrigin = new Vector2(36f, 68f);
        [Tooltip("두 이름표 사이 간격. barSize.x + barSpacing과 맞춘다.")]
        [SerializeField] private float barLabelSpacing = 278f;

        // 키와 설명을 한 줄씩. 바꿀 일이 잦아서 코드 한곳에 모아 둔다.
        private static readonly string[] Lines =
        {
            "WASD  이동",
            "Shift  달리기",
            "C  앉기 (낮은 엄폐 뒤에 숨는다)",
            "마우스  조준",
            "좌클릭  사격",
            "R  재장전",
            "F  뒤에서 제압",
            "Z / X  카메라 90도 회전",
            "Tab  이 안내 접기"
        };

        private GameObject _panel;
        private Text _status;
        private Font _font;
        private bool _open;

        private string _flashText;
        private float _flashUntil;

        /// <summary>제압처럼 한순간에 끝나는 일을 글자로 알린다. 안 그러면 뭐가 일어났는지 모른다.</summary>
        public void Flash(string message, float seconds = 1.6f)
        {
            _flashText = message;
            _flashUntil = Time.time + seconds;
        }

        private void Awake()
        {
            if (stance == null) stance = FindAnyObjectByType<PlayerStance>();
            if (weapon == null)
            {
                PlayerCombat combat = FindAnyObjectByType<PlayerCombat>();
                if (combat != null) weapon = combat.GetComponent<Weapon>();
            }
            if (takedown == null) takedown = FindAnyObjectByType<TakedownAction>();

            _font = ResolveFont();
            Build();
            SetOpen(openOnStart);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame) SetOpen(!_open);

            if (_status != null) _status.text = StatusText();
        }

        /// <summary>
        /// 한글이 나와야 해서 OS 폰트를 빌린다. 빌드에 폰트 애셋을 넣지 않아도 되고,
        /// 없으면 유니티 기본 폰트로 떨어진다. 그 경우 한글은 네모로 보인다.
        /// </summary>
        private static Font ResolveFont()
        {
            string[] candidates = { "Malgun Gothic", "맑은 고딕", "Noto Sans KR", "Arial Unicode MS", "Gulim", "Arial" };
            Font font = Font.CreateDynamicFontFromOSFont(candidates, 24);
            if (font != null) return font;

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void Build()
        {
            GameObject canvasGo = new GameObject("Controls Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _panel = MakePanel(canvasGo.transform);
            _status = MakeStatus(canvasGo.transform);
            MakeBarLabels(canvasGo.transform);
        }

        /// <summary>
        /// 감각 막대 위에 이름을 붙인다. SenseHud는 글자를 쓰지 않아서 어느 쪽이 소음이고
        /// 어느 쪽이 빛인지 알 길이 없었다. 자리는 SenseHudStyle의 막대 배치와 맞춰 둔다.
        /// </summary>
        private void MakeBarLabels(Transform parent)
        {
            MakeBarLabel(parent, "소음", barLabelOrigin.x);
            MakeBarLabel(parent, "빛", barLabelOrigin.x + barLabelSpacing);
        }

        private void MakeBarLabel(Transform parent, string label, float x)
        {
            Text text = MakeText(parent, "Bar Label " + label, bodySize - 2, bodyColor, FontStyle.Normal);

            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, barLabelOrigin.y);
            rect.sizeDelta = new Vector2(120f, 22f);

            text.text = label;
        }

        /// <summary>왼쪽 위에 키 목록을 세운다. 화면 가운데는 게임이 쓰는 자리라 비워 둔다.</summary>
        private GameObject MakePanel(Transform parent)
        {
            GameObject panel = new GameObject("Key Guide", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(margin.x, -margin.y);
            rect.sizeDelta = panelSize;

            panel.GetComponent<Image>().color = panelColor;

            Text title = MakeText(panel.transform, "Title", titleSize, titleColor, FontStyle.Bold);
            SetLine(title.rectTransform, 12f, 26f);
            title.text = "조작";

            for (int i = 0; i < Lines.Length; i++)
            {
                Text line = MakeText(panel.transform, "Line " + i, bodySize, bodyColor, FontStyle.Normal);
                SetLine(line.rectTransform, 46f + i * 28f, 24f);
                line.text = Lines[i];
            }

            return panel;
        }

        /// <summary>오른쪽 아래에 탄약과 자세. 화면을 보다가 눈만 내리면 읽히는 자리다.</summary>
        private Text MakeStatus(Transform parent)
        {
            Text status = MakeText(parent, "Status", bodySize + 3, statusColor, FontStyle.Bold);
            status.alignment = TextAnchor.LowerRight;

            RectTransform rect = status.rectTransform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-margin.x, margin.y);
            rect.sizeDelta = new Vector2(360f, 64f);

            return status;
        }

        private Text MakeText(Transform parent, string name, int size, Color color, FontStyle style)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return text;
        }

        /// <summary>패널 위쪽에서 offset만큼 내려온 자리에 한 줄을 놓는다.</summary>
        private void SetLine(RectTransform rect, float offset, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.offsetMin = new Vector2(16f, 0f);
            rect.offsetMax = new Vector2(-16f, 0f);
            rect.anchoredPosition = new Vector2(16f, -offset);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        private string StatusText()
        {
            string posture = stance != null && stance.IsCrouching ? "앉음" : "섬";

            string line = posture;
            if (weapon != null) line += weapon.IsReloading ? "    재장전 중" : "    탄약 " + weapon.Ammo;

            // 제압할 수 있는 순간을 알려 준다. 이게 없으면 등 뒤에 섰는지 플레이어가 알 수 없다.
            if (takedown != null && takedown.HasTarget) line = "F  제압 가능\n" + line;

            if (Time.time < _flashUntil) line = _flashText + "\n" + line;
            return line;
        }

        private void SetOpen(bool open)
        {
            _open = open;
            if (_panel != null) _panel.SetActive(open);
        }
    }
}
