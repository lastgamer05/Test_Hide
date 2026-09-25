using UnityEngine;
using UnityEngine.UI;
using ByAWhisker.AI;
using ByAWhisker.Combat;
using ByAWhisker.Player;

namespace ByAWhisker.UI
{
    /// 나를 보고 있는 경비가 어느 쪽에 있는지 화면 가장자리의 쐐기로 계속 알려 준다.
    /// 시야가 11m로 좁아 나를 본 경비는 대개 화면 밖에 있고, 겨누는 동작 없이 0.6초면 총알이 온다.
    /// 어디를 피해야 하는지 모르는 채로 죽으면 어려운 게임이 아니라 불공평한 게임이 된다.
    ///
    /// 판정은 하지 않는다. GuardPerception이 이미 내린 결론(CanSeePlayer, Awareness)을 읽기만 한다.
    /// 캔버스와 그림은 코드로 만든다. 프리팹을 두지 않는 것은 SenseHud와 같은 규칙이고,
    /// 방향을 화면 각으로 바꾸는 계산도 SenseHud의 소리 호와 같은 것을 쓴다.
    [DisallowMultipleComponent]
    public class SpottedEdge : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("방향을 재는 기준이 되는 나. 비우면 시작할 때 한 번 찾는다.")]
        [SerializeField] Transform player;

        [Tooltip("화면 각을 재는 카메라. 비우면 시작할 때 Camera.main을 쓴다.")]
        [SerializeField] Camera viewCamera;

        [Tooltip("통합 담당이 Bind를 안 불렀을 때 시작하면서 스스로 찾아본다.")]
        [SerializeField] bool autoBindOnStart = true;

        [Header("캔버스")]
        [Tooltip("크기 값을 적는 기준 해상도. SenseHudStyle과 같은 값으로 둔다.")]
        [SerializeField] Vector2 referenceResolution = new Vector2(1920f, 1080f);

        [Tooltip("SenseHud가 100, ControlsOverlay가 110을 쓴다. 발각 경고는 그 둘에 가리면 안 되니 위에 올린다.")]
        [SerializeField] int sortingOrder = 120;

        [Header("쐐기")]
        [Tooltip("동시에 띄울 쐐기의 최대 개수. 위젯 풀 크기이기도 하다. 경비가 열둘이라 다 띄우면 테두리가 꽉 찬다.")]
        [Range(1, 12)] [SerializeField] int maxWedges = 5;

        [Tooltip(
            "화면 테두리에서 안쪽으로 띄우는 거리. 쐐기는 타원을 돌기 때문에 모서리로는 가지 않는다 — " +
            "모서리에는 감각 막대(왼쪽 아래)와 조작 안내(왼쪽 위)와 상태(오른쪽 아래)가 있다. " +
            "가로를 더 크게 잡은 것은 그 셋이 전부 좌우 끝에 붙어 있기 때문이다.")]
        [SerializeField] Vector2 edgeInset = new Vector2(230f, 150f);

        [Tooltip("쐐기 하나의 크기. x가 길이(뾰족한 쪽), y가 밑변의 폭이다.")]
        [SerializeField] Vector2 wedgeSize = new Vector2(78f, 46f);

        [Tooltip("쐐기 색. 비네트와 같은 붉은 계열로 둬야 둘이 한 가지 경고로 읽힌다.")]
        [SerializeField] Color wedgeColor = new Color(1f, 0.30f, 0.24f, 1f);

        [Tooltip("이제 막 보이기 시작했을 때의 불투명도. 옅어도 눈에 걸려야 피할 틈이 생긴다.")]
        [Range(0f, 1f)] [SerializeField] float wedgeCalmAlpha = 0.34f;

        [Tooltip("완전히 들켰을 때의 불투명도.")]
        [Range(0f, 1f)] [SerializeField] float wedgeAlertAlpha = 0.95f;

        [Tooltip("보이는 의심도가 이보다 낮으면 쐐기를 끈다. 다 사라진 쐐기를 켠 채 두지 않으려는 것이다.")]
        [Range(0f, 0.5f)] [SerializeField] float hideBelow = 0.02f;

        [Header("비네트")]
        [Tooltip("테두리를 물들이는 색.")]
        [SerializeField] Color vignetteColor = new Color(0.86f, 0.10f, 0.08f, 1f);

        [Tooltip("누가 나를 보기 시작한 순간의 불투명도. 아직 의심만 하는 단계에서도 옅게 비쳐야 예고가 된다.")]
        [Range(0f, 1f)] [SerializeField] float vignetteCalmAlpha = 0.12f;

        [Tooltip("의심도가 1일 때의 불투명도. 화면 가운데는 비워 두므로 여기까지는 올려도 게임이 가려지지 않는다.")]
        [Range(0f, 1f)] [SerializeField] float vignetteAlertAlpha = 0.52f;

        [Tooltip("비네트가 물들기 시작하는 반지름(화면 반폭 기준). 이 안쪽은 투명하다.")]
        [Range(0f, 1.4f)] [SerializeField] float vignetteInner = 0.52f;

        [Tooltip("비네트가 가장 진해지는 반지름. 1보다 크게 둬야 네 귀퉁이만 꽉 차고 변은 덜 진하다.")]
        [Range(0.1f, 2f)] [SerializeField] float vignetteOuter = 1.25f;

        [Header("따라가기")]
        [Tooltip(
            "보이는 의심도가 실제 값을 따라가는 속도. 경비가 시야를 들락거릴 때 쐐기와 비네트가 " +
            "깜빡이지 않게 한다. PlayerExposure, FocusSense와 같은 지수 보간이다.")]
        [SerializeField] float followSpeed = 7f;

        // 쐐기 그림의 해상도. 늘려도 화면에서는 부드러워지기만 하고 값이 달라지지 않아서 상수로 둔다.
        const int WedgeTextureWidth = 64;
        const int WedgeTextureHeight = 40;
        const int VignetteResolution = 64;

        /// 경비 하나를 지켜본 결과. 보이는 의심도는 경비마다 따로 이어져야 해서 위젯이 아니라 여기 붙인다.
        struct Watcher
        {
            public GuardPerception perception;
            public Damageable body;
            public float shown;
        }

        Canvas _canvas;
        RectTransform _root;
        RectTransform _wedgeGroup;
        Image _vignette;

        Image[] _wedges;
        RectTransform[] _wedgeRects;

        // 이번 프레임에 쐐기로 그릴 것들. 자리를 다투면 의심도가 높은 쪽이 남는다.
        float[] _slotAngle;
        float[] _slotAmount;
        int _slotCount;

        Watcher[] _watchers;

        Sprite _wedgeSprite;
        Sprite _vignetteSprite;

        float _shownVignette;
        bool _built;

        /// 방향을 재는 기준과 화면 각을 재는 카메라를 준다. 통합 담당이 시작할 때 부른다.
        public void Bind(Transform playerTransform, Camera view)
        {
            if (playerTransform != null) player = playerTransform;
            if (view != null) viewCamera = view;
        }

        /// 씬의 경비를 다시 모은다. 평소에는 한 번이면 되지만, 레벨을 다시 지으면 밖에서 불러 준다.
        public void Rescan()
        {
            GuardPerception[] found = FindObjectsByType<GuardPerception>(FindObjectsSortMode.None);

            _watchers = new Watcher[found.Length];
            for (int i = 0; i < found.Length; i++)
            {
                _watchers[i].perception = found[i];
                // 콜라이더나 감각이 자식에 달려 있을 수 있으니 부모까지 올라가서 찾는다. AwarenessGauge와 같다.
                _watchers[i].body = found[i].GetComponentInParent<Damageable>();
                _watchers[i].shown = 0f;
            }
        }

        /// 지금 몇 개의 쐐기가 떠 있는가. 씬 연결이 어긋났는지 눈으로 보려고 열어 둔다.
        public int VisibleWedges { get { return _slotCount; } }

        /// 지금 비네트가 따르고 있는 값(0..1). 나를 보는 경비들 중 가장 높은 의심도다.
        public float Exposure { get { return _shownVignette; } }

        void Awake()
        {
            Build();
        }

        void Start()
        {
            // 경비는 레벨이 만들어진 뒤로 늘지 않는다. 여기서 한 번 모아 두고 매 프레임 찾지 않는다.
            if (_watchers == null) Rescan();

            if (!autoBindOnStart) return;

            if (player == null)
            {
                PlayerExposure exposure = FindAnyObjectByType<PlayerExposure>();
                if (exposure != null) player = exposure.transform;
            }

            if (viewCamera == null) viewCamera = Camera.main;
        }

        void OnDestroy()
        {
            // 우리가 만든 그림은 우리가 지운다. 저장 대상이 아니라 두고 가면 에디터에 남는다.
            DestroySprite(ref _wedgeSprite);
            DestroySprite(ref _vignetteSprite);
        }

        /// 경비는 Update에서 움직인다(NavMeshAgent). 그 뒤에 자리를 읽어야 쐐기가 한 프레임 밀리지 않는다.
        void LateUpdate()
        {
            if (!_built) return;

            if (player == null || viewCamera == null || _watchers == null)
            {
                FadeOut();
                return;
            }

            Collect();
            ApplyWedges();
            ApplyVignette();
        }

        void Build()
        {
            _canvas = SenseHudBuilder.CreateOverlayCanvas("SpottedEdge Canvas", transform, sortingOrder, referenceResolution);
            _root = (RectTransform)_canvas.transform;

            _wedgeSprite = BuildWedgeSprite();
            _vignetteSprite = BuildVignetteSprite(vignetteInner, vignetteOuter);

            // 비네트를 먼저 만든다. 먼저 만든 것이 먼저 그려져서 쐐기가 그 위에 올라간다.
            BuildVignette();
            BuildWedges();

            _built = true;
        }

        void BuildVignette()
        {
            _vignette = SenseHudBuilder.CreateImage("Vignette", _root, _vignetteSprite, Color.clear);

            // 화면 전체로 늘린다. 비율이 달라져도 네 귀퉁이가 늘 같은 만큼 물든다.
            RectTransform rect = _vignette.rectTransform;
            SenseHudBuilder.Anchor(rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _vignette.gameObject.SetActive(false);
        }

        void BuildWedges()
        {
            _wedgeGroup = SenseHudBuilder.CreateRect("Wedges", _root);
            SenseHudBuilder.Anchor(_wedgeGroup, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _wedgeGroup.anchoredPosition = Vector2.zero;
            _wedgeGroup.sizeDelta = Vector2.zero;

            int count = Mathf.Max(1, maxWedges);
            _wedges = new Image[count];
            _wedgeRects = new RectTransform[count];
            _slotAngle = new float[count];
            _slotAmount = new float[count];

            for (int i = 0; i < count; i++)
            {
                Image wedge = SenseHudBuilder.CreateImage("Spotted Wedge " + i, _wedgeGroup, _wedgeSprite, Color.clear);
                SenseHudBuilder.Anchor(wedge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                wedge.rectTransform.sizeDelta = wedgeSize;
                wedge.gameObject.SetActive(false);

                _wedges[i] = wedge;
                _wedgeRects[i] = wedge.rectTransform;
            }
        }

        /// 경비를 한 바퀴 돌며 보이는 의심도를 잇고, 그릴 자리를 고른다. 배열은 미리 잡아 두어 할당이 없다.
        void Collect()
        {
            _slotCount = 0;

            Vector3 origin = player.position;
            // 프레임 레이트가 흔들려도 같은 속도로 붙도록 지수 보간을 쓴다. PlayerExposure, FocusSense와 같은 방식이다.
            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, followSpeed) * Time.deltaTime);

            for (int i = 0; i < _watchers.Length; i++)
            {
                GuardPerception perception = _watchers[i].perception;
                if (perception == null) continue;

                // 쓰러진 경비의 남은 판정이 떠 있으면 시체가 아직 나를 보는 것처럼 읽힌다. AwarenessGauge와 같은 규칙이다.
                Damageable body = _watchers[i].body;
                bool down = body != null && (!body.IsAlive || body.IsDown);

                float target = !down && perception.CanSeePlayer ? Mathf.Clamp01(perception.Awareness) : 0f;

                float shown = Mathf.Lerp(_watchers[i].shown, target, t);
                _watchers[i].shown = shown;

                if (shown <= hideBelow) continue;

                Vector2 dir;
                if (!TryScreenDirection(perception.EyePosition - origin, out dir)) continue;

                AddSlot(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, shown);
            }
        }

        /// 자리가 다 차면 가장 옅은 것을 밀어낸다. 가장 급한 위협이 먼저 남아야 한다.
        void AddSlot(float angle, float amount)
        {
            if (_slotCount < _slotAngle.Length)
            {
                _slotAngle[_slotCount] = angle;
                _slotAmount[_slotCount] = amount;
                _slotCount++;
                return;
            }

            int weakest = 0;
            for (int i = 1; i < _slotCount; i++)
            {
                if (_slotAmount[i] < _slotAmount[weakest]) weakest = i;
            }

            if (amount <= _slotAmount[weakest]) return;

            _slotAngle[weakest] = angle;
            _slotAmount[weakest] = amount;
        }

        void ApplyWedges()
        {
            // 캔버스의 실제 크기를 읽는다. 기준 해상도를 그대로 쓰면 화면 비율이 다를 때 쐐기가 테두리에서 뜬다.
            Vector2 half = _root.rect.size * 0.5f;
            float radiusX = Mathf.Max(20f, half.x - edgeInset.x);
            float radiusY = Mathf.Max(20f, half.y - edgeInset.y);

            for (int i = 0; i < _wedges.Length; i++)
            {
                if (i >= _slotCount)
                {
                    SenseHudBuilder.SetVisible(_wedges[i], false);
                    continue;
                }

                SenseHudBuilder.SetVisible(_wedges[i], true);

                float angle = _slotAngle[i];
                float radians = angle * Mathf.Deg2Rad;

                // 사각형 테두리를 따라가면 쐐기가 네 모서리에 몰리는데, 그 자리가 막대와 안내가 있는 곳이다.
                // 타원을 돌면 모서리를 자연히 비켜가면서도 방향은 그대로 읽힌다.
                RectTransform rect = _wedgeRects[i];
                rect.anchoredPosition = new Vector2(Mathf.Cos(radians) * radiusX, Mathf.Sin(radians) * radiusY);

                // 그림은 오른쪽 끝이 뾰족하게 그려져 있다. 방향각 그대로 돌리면 뾰족한 쪽이 경비를 가리킨다.
                rect.localRotation = Quaternion.Euler(0f, 0f, angle);

                Color color = wedgeColor;
                color.a = Mathf.Lerp(wedgeCalmAlpha, wedgeAlertAlpha, _slotAmount[i]);
                SetColor(_wedges[i], color);
            }
        }

        void ApplyVignette()
        {
            // 여럿이 보고 있으면 가장 많이 확신한 쪽이 위험의 크기다. 보이는 값들은 이미 보간되어 있어
            // 여기서 또 부드럽게 만들 필요가 없다.
            float highest = 0f;
            for (int i = 0; i < _slotCount; i++)
            {
                if (_slotAmount[i] > highest) highest = _slotAmount[i];
            }

            _shownVignette = highest;
            ShowVignette(highest);
        }

        void ShowVignette(float amount)
        {
            if (_vignette == null) return;

            if (amount <= hideBelow)
            {
                SenseHudBuilder.SetVisible(_vignette, false);
                return;
            }

            SenseHudBuilder.SetVisible(_vignette, true);

            Color color = vignetteColor;
            color.a = Mathf.Lerp(vignetteCalmAlpha, vignetteAlertAlpha, amount);
            SetColor(_vignette, color);
        }

        /// 대상을 잃었을 때. 그 자리에 얼어붙은 쐐기를 남기지 않는다.
        void FadeOut()
        {
            _slotCount = 0;
            _shownVignette = 0f;

            if (_wedges != null)
            {
                for (int i = 0; i < _wedges.Length; i++) SenseHudBuilder.SetVisible(_wedges[i], false);
            }

            SenseHudBuilder.SetVisible(_vignette, false);

            if (_watchers == null) return;
            for (int i = 0; i < _watchers.Length; i++) _watchers[i].shown = 0f;
        }

        /// 월드 수평 방향을 화면 방향으로 바꾼다. 카메라가 Z/X로 90도씩 돌아가므로 월드 방향을 그대로 쓰면
        /// 쐐기가 엉뚱한 데를 가리킨다. 내려다보는 각이 고정이라 카메라 축에 투영하면 그대로 화면 방향이 된다.
        /// SenseHud가 소리 방향을 그릴 때 쓰는 계산과 같다. 화면 밖 지점이어도 어긋나지 않는다.
        bool TryScreenDirection(Vector3 worldDirection, out Vector2 screenDirection)
        {
            screenDirection = Vector2.zero;

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f) return false;

            Transform cam = viewCamera.transform;
            float x = Vector3.Dot(worldDirection, cam.right);
            float y = Vector3.Dot(worldDirection, cam.up);

            // 카메라가 거의 수평이면 위쪽 축의 수평 성분이 사라진다. 그때는 앞 방향으로 대신 잰다.
            if (Mathf.Abs(y) < 0.0001f && Mathf.Abs(x) < 0.0001f)
            {
                y = Vector3.Dot(worldDirection, cam.forward);
                if (Mathf.Abs(y) < 0.0001f) return false;
            }

            Vector2 dir = new Vector2(x, y);
            float length = dir.magnitude;
            if (length < 0.0001f) return false;

            screenDirection = dir / length;
            return true;
        }

        /// 오른쪽 끝이 뾰족한 삼각형. 호로 그리면 방향이 아니라 넓이로 읽혀서 쐐기로 만든다.
        /// 밑동을 흐리게 빼서 가장 진한 자리가 뾰족한 쪽에 오게 한다. 눈이 먼저 잡는 곳이 가리키는 곳이어야 한다.
        static Sprite BuildWedgeSprite()
        {
            const int w = WedgeTextureWidth;
            const int h = WedgeTextureHeight;
            // 비스듬한 변이 계단으로 보이지 않게 두 픽셀에 걸쳐 흐린다.
            const float feather = 2f;

            Texture2D tex = NewTexture(w, h, "BW_SpottedWedge");
            Color32[] pixels = new Color32[w * h];
            float halfHeight = h * 0.5f;

            for (int y = 0; y < h; y++)
            {
                float dy = Mathf.Abs(y + 0.5f - halfHeight);

                for (int x = 0; x < w; x++)
                {
                    // 0이 밑동, 1이 꼭짓점. 폭은 꼭짓점으로 갈수록 곧게 줄어든다.
                    float t = (x + 0.5f) / w;
                    float half = (1f - t) * halfHeight;

                    float a = half <= 0f ? 0f : Mathf.Clamp01((half - dy) / feather);
                    a *= Mathf.SmoothStep(0f, 1f, t);

                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            return NewSprite(tex, "BW_SpottedWedge");
        }

        /// 가운데는 비고 가장자리로 갈수록 진해지는 사각 그림. 화면에 늘려 깔면 비네트가 된다.
        /// 가운데를 비워 두는 것이 요점이다. 들킨 순간에 가장 봐야 할 것이 화면 가운데이기 때문이다.
        static Sprite BuildVignetteSprite(float inner, float outer)
        {
            int size = VignetteResolution;
            float near = Mathf.Max(0f, inner);
            float far = Mathf.Max(near + 0.01f, outer);

            Texture2D tex = NewTexture(size, size, "BW_SpottedVignette");
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                // 화면에 늘리면 이 원이 화면 비율만큼 눌린 타원이 된다. 비네트가 바라는 모양 그대로다.
                float dy = (y + 0.5f) / size * 2f - 1f;

                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - near) / (far - near)));

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            return NewSprite(tex, "BW_SpottedVignette");
        }

        static Texture2D NewTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = name,
                // 씬을 다시 로드해도 살아 있어야 해서 저장 대상에서 뺀 채로 붙잡아 둔다. SenseHudBuilder와 같다.
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        static Sprite NewSprite(Texture2D tex, string name)
        {
            Sprite sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);

            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        /// 스프라이트와 그 밑의 텍스처를 함께 지운다. 스프라이트만 지우면 텍스처가 남는다.
        static void DestroySprite(ref Sprite sprite)
        {
            if (sprite == null) return;

            Texture2D tex = sprite.texture;

            if (Application.isPlaying)
            {
                Destroy(sprite);
                if (tex != null) Destroy(tex);
            }
            else
            {
                DestroyImmediate(sprite);
                if (tex != null) DestroyImmediate(tex);
            }

            sprite = null;
        }

        /// 매 프레임 불리므로 값이 거의 같으면 건드리지 않는다. 색을 넣으면 캔버스가 다시 짜인다.
        static void SetColor(Image image, Color color)
        {
            Color current = image.color;
            if (Mathf.Abs(current.r - color.r) < 0.004f &&
                Mathf.Abs(current.g - color.g) < 0.004f &&
                Mathf.Abs(current.b - color.b) < 0.004f &&
                Mathf.Abs(current.a - color.a) < 0.004f) return;

            image.color = color;
        }
    }
}
