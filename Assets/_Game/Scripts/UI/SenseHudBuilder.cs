using UnityEngine;
using UnityEngine.UI;

namespace ByAWhisker.UI
{
    /// 캔버스와 위젯을 코드로 만드는 정적 도우미. 프리팹도 스프라이트 애셋도 쓰지 않는다.
    /// 필요한 그림은 작은 텍스처로 한 번만 만들어 두고 모든 HUD가 같은 것을 나눠 쓴다.
    public static class SenseHudBuilder
    {
        const int SoftDotResolution = 64;
        const int FadedBarResolution = 64;

        static Sprite _solid;
        static Sprite _softDot;
        static Sprite _fadedBar;

        /// 꽉 찬 사각형. 막대에 쓴다.
        public static Sprite SolidSprite
        {
            get
            {
                // 플레이 모드를 나가면 텍스처가 파괴되어 가짜 null이 된다. 그때 다시 만든다.
                if (_solid == null) _solid = BuildSolid();
                return _solid;
            }
        }

        /// 가운데가 진하고 가장자리로 갈수록 사라지는 둥근 점. 냄새 표시에 쓴다.
        public static Sprite SoftDotSprite
        {
            get
            {
                if (_softDot == null) _softDot = BuildSoftDot();
                return _softDot;
            }
        }

        /// 양 끝이 흐려지는 가로 막대. 돌려서 쓰면 짧은 호처럼 보인다.
        /// 진짜 호 메시를 만들면 길이를 바꿀 때마다 메시를 다시 짜야 해서 이 방식을 골랐다.
        public static Sprite FadedBarSprite
        {
            get
            {
                if (_fadedBar == null) _fadedBar = BuildFadedBar();
                return _fadedBar;
            }
        }

        /// 화면 위에 겹쳐 그리는 캔버스를 만든다. HUD는 누를 일이 없어서 레이캐스터를 달지 않는다.
        public static Canvas CreateOverlayCanvas(string name, Transform parent, int sortingOrder, Vector2 referenceResolution)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));

            // 레이어 번호를 코드에 적지 않는다. 이름을 못 찾으면 기본 레이어로 둔다.
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0) go.layer = uiLayer;

            RectTransform rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution.x > 0f && referenceResolution.y > 0f
                ? referenceResolution
                : new Vector2(1920f, 1080f);
            // 가로세로를 반씩 본다. 화면 비율이 달라져도 막대와 점 크기가 비슷하게 남는다.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// 그림 없는 빈 칸. 위젯을 묶어 두는 데 쓴다.
        public static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);
            return rt;
        }

        /// 그림 한 장. 입력을 가로채지 않도록 레이캐스트 대상에서 뺀다.
        public static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rt = (RectTransform)go.transform;
            if (parent != null) rt.SetParent(parent, false);

            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;

            return image;
        }

        /// 앵커와 피벗을 한 번에 맞춘다. 세 줄을 매번 적지 않으려고 묶었다.
        public static void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            if (rt == null) return;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
        }

        /// 색은 그대로 두고 투명도만 바꾼다. 매 프레임 불리므로 값이 같으면 건드리지 않는다.
        public static void SetAlpha(Graphic graphic, float alpha)
        {
            if (graphic == null) return;

            Color c = graphic.color;
            if (Mathf.Abs(c.a - alpha) < 0.002f) return;

            c.a = alpha;
            graphic.color = c;
        }

        /// 켜고 끄기. 이미 그 상태면 아무 일도 하지 않는다. SetActive는 공짜가 아니다.
        public static void SetVisible(Component widget, bool visible)
        {
            if (widget == null) return;
            GameObject go = widget.gameObject;
            if (go.activeSelf != visible) go.SetActive(visible);
        }

        static Sprite BuildSolid()
        {
            const int size = 4;
            Texture2D tex = NewTexture(size, size, "BW_HudSolid");

            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            return NewSprite(tex, "BW_HudSolid");
        }

        static Sprite BuildSoftDot()
        {
            int size = SoftDotResolution;
            Texture2D tex = NewTexture(size, size, "BW_HudSoftDot");

            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 중심에서의 거리를 0..1로 본다. 가장자리에서 정확히 0이 되어야 테두리가 안 생긴다.
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = r >= 1f ? 0f : Mathf.SmoothStep(0f, 1f, 1f - r);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            return NewSprite(tex, "BW_HudSoftDot");
        }

        static Sprite BuildFadedBar()
        {
            int w = FadedBarResolution;
            const int h = 4;
            Texture2D tex = NewTexture(w, h, "BW_HudFadedBar");

            Color32[] pixels = new Color32[w * h];

            for (int x = 0; x < w; x++)
            {
                // 가운데가 1, 양 끝이 0. 호의 끝이 뚝 끊기지 않고 어둠에 녹는다.
                float t = (x + 0.5f) / w;
                float a = Mathf.SmoothStep(0f, 1f, 1f - Mathf.Abs(t * 2f - 1f));
                Color32 c = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));

                for (int y = 0; y < h; y++) pixels[y * w + x] = c;
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            return NewSprite(tex, "BW_HudFadedBar");
        }

        static Texture2D NewTexture(int width, int height, string name)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = name,
                // 씬을 다시 로드해도 살아 있어야 해서 저장 대상에서 뺀 채로 붙잡아 둔다.
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return tex;
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
    }
}
