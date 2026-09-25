using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ByAWhisker.Player;
using ByAWhisker.Senses;

namespace ByAWhisker.UI
{
    /// 감각 표시를 담당한다. 캔버스와 위젯은 코드로 만든다. 프리팹을 쓰지 않는다.
    /// 보여 주는 것은 셋이다. 내가 내는 소음, 내가 받는 빛, 최근에 난 소리의 방향과 거리와 종류.
    /// 전부 힌트다. 정확한 자리를 찍지 않고 방향과 세기만 말해 준다.
    ///
    /// 냄새는 화면이 아니라 월드 바닥에 그린다(ScentMapRenderer). 시야가 11m로 줄면서
    /// 화면에서 소리가 차지할 자리가 커졌고, 소리는 원래 화면 말고는 보여 줄 데가 없다.
    [DisallowMultipleComponent]
    public class SenseHud : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("색과 크기. 비우면 기본값으로 하나 만들어 쓴다.")]
        [SerializeField] SenseHudStyle style;

        [Tooltip("다른 HUD 위에 그리려면 값을 올린다.")]
        [SerializeField] int sortingOrder = 100;

        [Header("갱신")]
        [Tooltip("소리를 다시 읽는 간격. 매 프레임 읽을 이유가 없다. 그리는 것은 매 프레임 한다.")]
        [SerializeField, Range(0.02f, 0.5f)] float refreshInterval = 0.12f;

        [Tooltip("통합 담당이 Bind를 안 불렀을 때 시작하면서 스스로 찾아본다.")]
        [SerializeField] bool autoBindOnStart = true;

        // 소리 한 번에 몇 개까지 훑을지. 링 버퍼보다 넉넉하면 되고, 리스트는 한 번만 만든다.
        const int NoiseBufferCapacity = 64;

        /// 호 하나가 들고 있는 것. 화면 각도와 거리는 카메라도 나도 움직이니 매 프레임 다시 구한다.
        struct ArcSlot
        {
            public Vector3 position;
            public float startTime;
            public float loudness;
            public NoiseKind kind;
            public float angle;    // 합치기 판정에만 쓰는 값
            public float weight;
        }

        Transform _player;
        GameObject _playerRoot;
        Camera _camera;
        PlayerExposure _exposure;

        Canvas _canvas;
        RectTransform _root;
        RectTransform _barGroup;
        RectTransform _noiseFill;
        RectTransform _lightFill;
        RectTransform _centerGroup;

        Image[] _arcs;
        RectTransform[] _arcRects;
        ArcSlot[] _arcSlots;
        int _arcCount;

        List<NoiseEvent> _noiseBuffer;

        float _shownNoise;
        float _shownLight;
        float _drawnNoise = -1f;
        float _drawnLight = -1f;
        float _refreshTimer;
        bool _ownsStyle;
        bool _built;

        /// 따라다닐 대상과 방향을 재는 기준 카메라를 준다. 통합 담당이 시작할 때 부른다.
        public void Bind(Transform player, Camera viewCamera)
        {
            _player = player;
            _playerRoot = player != null ? player.gameObject : null;
            if (viewCamera != null) _camera = viewCamera;
        }

        /// 냄새 격자를 받던 자리. 이제 HUD는 냄새를 그리지 않아서 아무것도 하지 않는다.
        /// 씬 연결(GameBootstrap)이 이미 부르고 있어 서명만 남겨 둔다.
        public void SetScentField(ScentField field)
        {
        }

        /// 소음과 빛 수치를 읽어 올 곳을 준다. 비우면 막대가 숨는다.
        public void SetExposure(PlayerExposure exposure)
        {
            _exposure = exposure;

            if (_barGroup != null) SenseHudBuilder.SetVisible(_barGroup, _exposure != null);
        }

        void Awake()
        {
            ResolveStyle();
            Build();
        }

        void Start()
        {
            if (!autoBindOnStart) return;

            // 씬 연결을 빠뜨려도 화면이 비지 않도록 한 번만 찾아본다. 매 프레임 찾지는 않는다.
            if (_exposure == null) SetExposure(FindAnyObjectByType<PlayerExposure>());
            if (_player == null && _exposure != null) Bind(_exposure.transform, _camera);
            if (_camera == null) _camera = Camera.main;
        }

        void OnDestroy()
        {
            // 우리가 만든 기본 스타일만 우리가 지운다. 꽂아 준 에셋은 건드리지 않는다.
            if (!_ownsStyle || style == null) return;

            if (Application.isPlaying) Destroy(style);
            else DestroyImmediate(style);

            style = null;
        }

        void Update()
        {
            if (!_built) return;

            UpdateBars();

            if (_player == null || _camera == null)
            {
                HideAllArcs();
                return;
            }

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = refreshInterval;
                RefreshNoise();
            }

            // 카메라가 90도 돌아가는 중에도 방향이 어긋나지 않게 자리는 매 프레임 다시 잡는다.
            ApplyArcs();
        }

        void ResolveStyle()
        {
            if (style != null) return;

            style = SenseHudStyle.CreateDefault();
            _ownsStyle = true;
        }

        void Build()
        {
            _canvas = SenseHudBuilder.CreateOverlayCanvas("SenseHud Canvas", transform, sortingOrder, style.referenceResolution);
            _root = (RectTransform)_canvas.transform;

            BuildBars();
            BuildCenterGroup();

            _built = true;

            SenseHudBuilder.SetVisible(_barGroup, _exposure != null);
        }

        void BuildBars()
        {
            _barGroup = SenseHudBuilder.CreateRect("Bars", _root);
            SenseHudBuilder.Anchor(_barGroup, Vector2.zero, Vector2.zero, Vector2.zero);
            _barGroup.anchoredPosition = style.barMargin;
            _barGroup.sizeDelta = Vector2.zero;

            // 나란히 놓으면 오른쪽으로, 쌓으면 위로 두 번째 막대를 민다.
            Vector2 step = style.barsVertical
                ? new Vector2(0f, style.barSize.y + style.barSpacing)
                : new Vector2(style.barSize.x + style.barSpacing, 0f);

            _noiseFill = BuildBar("Noise", Vector2.zero, style.noiseColor);
            _lightFill = BuildBar("Light", step, style.lightColor);
        }

        /// 막대 하나는 배경 한 장과 채움 한 장이다. 채움은 왼쪽에 붙여 두고 가로 크기만 바꾼다.
        RectTransform BuildBar(string name, Vector2 offset, Color fillColor)
        {
            Image back = SenseHudBuilder.CreateImage(name + " Back", _barGroup, SenseHudBuilder.SolidSprite, style.barBackColor);
            RectTransform backRect = back.rectTransform;
            SenseHudBuilder.Anchor(backRect, Vector2.zero, Vector2.zero, Vector2.zero);
            backRect.anchoredPosition = offset;
            backRect.sizeDelta = style.barSize;

            Image fill = SenseHudBuilder.CreateImage(name + " Fill", backRect, SenseHudBuilder.SolidSprite, fillColor);
            RectTransform fillRect = fill.rectTransform;
            SenseHudBuilder.Anchor(fillRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(0f, 0f);

            return fillRect;
        }

        void BuildCenterGroup()
        {
            _centerGroup = SenseHudBuilder.CreateRect("Center", _root);
            SenseHudBuilder.Anchor(_centerGroup, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            _centerGroup.anchoredPosition = Vector2.zero;
            _centerGroup.sizeDelta = Vector2.zero;

            int arcCount = Mathf.Max(1, style.maxArcs);
            _arcs = new Image[arcCount];
            _arcRects = new RectTransform[arcCount];
            _arcSlots = new ArcSlot[arcCount];

            for (int i = 0; i < arcCount; i++)
            {
                Image arc = SenseHudBuilder.CreateImage("Noise Arc " + i, _centerGroup, SenseHudBuilder.FadedBarSprite, Color.clear);
                SenseHudBuilder.Anchor(arc.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                arc.rectTransform.sizeDelta = new Vector2(style.arcLengthQuiet, style.arcThicknessFar);
                arc.gameObject.SetActive(false);

                _arcs[i] = arc;
                _arcRects[i] = arc.rectTransform;
            }

            _noiseBuffer = new List<NoiseEvent>(NoiseBufferCapacity);
        }

        void UpdateBars()
        {
            if (_exposure == null || _noiseFill == null) return;

            float noise = Mathf.Clamp01(_exposure.Noise);
            float light = Mathf.Clamp01(_exposure.Light);

            // 프레임 레이트가 흔들려도 같은 속도로 붙도록 지수 보간을 쓴다. PlayerExposure와 같은 방식이다.
            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, style.barFollowSpeed) * Time.deltaTime);
            _shownNoise = Mathf.Lerp(_shownNoise, noise, t);
            _shownLight = Mathf.Lerp(_shownLight, light, t);

            ApplyBar(_noiseFill, _shownNoise, ref _drawnNoise);
            ApplyBar(_lightFill, _shownLight, ref _drawnLight);
        }

        /// 값이 거의 안 바뀌었으면 건드리지 않는다. RectTransform을 만지면 캔버스가 다시 짜인다.
        void ApplyBar(RectTransform fill, float value, ref float drawn)
        {
            if (fill == null) return;
            if (drawn >= 0f && Mathf.Abs(drawn - value) < style.barEpsilon) return;

            drawn = value;
            fill.sizeDelta = new Vector2(style.barSize.x * value, 0f);
        }

        void RefreshNoise()
        {
            _arcCount = 0;

            Vector3 origin = _player.position;

            // 종류마다 남는 시간이 달라서 가장 긴 쪽으로 걷어 온 뒤 하나씩 다시 잰다.
            int count = NoiseBus.Collect(origin, style.LongestFadeSeconds(), _noiseBuffer);
            if (count > _noiseBuffer.Count) count = _noiseBuffer.Count;

            for (int i = 0; i < count; i++)
            {
                NoiseEvent evt = _noiseBuffer[i];

                // 내가 낸 소리는 이미 아는 정보다. 화면을 어지럽히기만 한다.
                if (IsOwnSource(evt.source)) continue;

                float life = style.FadeSecondsFor(evt.kind);
                float age = Time.time - evt.time;
                if (age < 0f) age = 0f;
                if (age >= life) continue;

                Vector3 delta = evt.position - origin;

                Vector2 dir;
                if (!TryScreenDirection(delta, out dir)) continue;

                float loudness = Mathf.Clamp01(evt.loudness);
                // 가까운 소리가 더 급한 정보다. 거리를 가중치에 넣지 않으면 멀리서 난 총성이
                // 등 뒤 발소리를 밀어내고 자리를 차지한다.
                float near = 1f - Distance01(delta);
                float weight = loudness * (1f - age / life) * Mathf.Lerp(style.arcFarWeightScale, 1f, near);
                if (weight <= 0.001f) continue;

                AddArc(evt.position, evt.time, loudness, evt.kind, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, weight);
            }
        }

        /// 비슷한 방향의 소리는 하나로 합친다. 발소리가 이어지면 호가 줄줄이 뜨기 때문이다.
        void AddArc(Vector3 position, float time, float loudness, NoiseKind kind, float angle, float weight)
        {
            for (int i = 0; i < _arcCount; i++)
            {
                // 종류가 다르면 합치지 않는다. 총성이 발소리에 먹히면 가장 급한 정보를 놓친다.
                if (_arcSlots[i].kind != kind) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(angle, _arcSlots[i].angle)) > style.arcMergeDegrees) continue;

                // 같은 방향이면 더 센 쪽만 남긴다.
                if (weight > _arcSlots[i].weight) SetArc(i, position, time, loudness, kind, angle, weight);
                return;
            }

            if (_arcCount < _arcSlots.Length)
            {
                SetArc(_arcCount, position, time, loudness, kind, angle, weight);
                _arcCount++;
                return;
            }

            // 자리가 다 찼으면 가장 약한 것을 밀어낸다.
            int weakest = 0;
            for (int i = 1; i < _arcCount; i++)
            {
                if (_arcSlots[i].weight < _arcSlots[weakest].weight) weakest = i;
            }

            if (weight > _arcSlots[weakest].weight) SetArc(weakest, position, time, loudness, kind, angle, weight);
        }

        void SetArc(int index, Vector3 position, float time, float loudness, NoiseKind kind, float angle, float weight)
        {
            _arcSlots[index].position = position;
            _arcSlots[index].startTime = time;
            _arcSlots[index].loudness = loudness;
            _arcSlots[index].kind = kind;
            _arcSlots[index].angle = angle;
            _arcSlots[index].weight = weight;
        }

        void ApplyArcs()
        {
            Vector3 origin = _player.position;

            for (int i = 0; i < _arcs.Length; i++)
            {
                if (i >= _arcCount)
                {
                    SenseHudBuilder.SetVisible(_arcs[i], false);
                    continue;
                }

                NoiseKind kind = _arcSlots[i].kind;
                float life = style.FadeSecondsFor(kind);
                float age = Time.time - _arcSlots[i].startTime;
                if (age < 0f) age = 0f;

                float fade = 1f - age / life;
                Vector3 delta = _arcSlots[i].position - origin;

                Vector2 dir;
                if (fade <= 0f || !TryScreenDirection(delta, out dir))
                {
                    SenseHudBuilder.SetVisible(_arcs[i], false);
                    continue;
                }

                SenseHudBuilder.SetVisible(_arcs[i], true);

                float loudness = _arcSlots[i].loudness;
                // 소리가 난 자리는 그대로여도 내가 움직이니 거리는 매 프레임 다시 잰다.
                float far = Distance01(delta);
                RectTransform rect = _arcRects[i];

                // 가까운 소리는 몸에 붙고 먼 소리는 화면 가장자리로 간다. 방향만 알던 것에 거리가 붙는다.
                rect.anchoredPosition = dir * Mathf.Lerp(style.arcRadiusNear, style.arcRadiusFar, far);
                // 막대는 가로로 누워 있으니 방향각에 90도를 더해 원의 접선으로 세운다.
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f);

                // 갓 난 소리는 잠깐 커졌다 가라앉는다. 가만히 떠 있기만 하면 새로 난 것을 놓친다.
                float pop = 1f + Mathf.Max(0f, style.arcPopScale - 1f) *
                    (1f - Mathf.Clamp01(age / Mathf.Max(0.01f, style.arcPopSeconds)));
                float emphasis = style.ArcEmphasis(kind) * pop;

                float length = Mathf.Lerp(style.arcLengthQuiet, style.arcLengthLoud, loudness) * emphasis;
                float thickness = Mathf.Lerp(style.arcThicknessNear, style.arcThicknessFar, far) * emphasis;
                SetSize(rect, length, thickness);

                Color color = style.ArcColor(kind);
                // 제곱으로 줄여야 끝물에 오래 붙어 있지 않고 깔끔하게 사라진다.
                color.a = fade * fade * style.arcMaxAlpha
                    * Mathf.Lerp(style.arcQuietAlphaScale, 1f, loudness)
                    * Mathf.Lerp(1f, style.arcFarAlphaScale, far);
                SetColor(_arcs[i], color);
            }
        }

        void HideAllArcs()
        {
            if (_arcs == null) return;
            for (int i = 0; i < _arcs.Length; i++) SenseHudBuilder.SetVisible(_arcs[i], false);
            _arcCount = 0;
        }

        /// 수평 거리를 0..1로 바꾼다. 층이 하나뿐이라 높이는 거리로 치지 않는다. NoiseBus와 같은 규칙이다.
        float Distance01(Vector3 delta)
        {
            delta.y = 0f;
            return Mathf.Clamp01(delta.magnitude / Mathf.Max(0.5f, style.arcDistanceRange));
        }

        /// 소리를 낸 것이 나 자신인지. 발밑 소리 내는 자식 오브젝트까지 같이 걸러야 한다.
        bool IsOwnSource(GameObject source)
        {
            if (source == null) return false;
            if (_playerRoot == null) return false;
            if (source == _playerRoot) return true;

            return source.transform.IsChildOf(_player);
        }

        /// 월드 수평 방향을 화면 방향으로 바꾼다.
        /// 카메라를 내려다보는 각으로 고정해 놓았으니 카메라 축에 투영하면 그대로 화면 방향이 된다.
        /// 이러면 카메라를 90도 돌려도, 화면 밖 지점이어도 방향이 어긋나지 않는다.
        bool TryScreenDirection(Vector3 worldDirection, out Vector2 screenDirection)
        {
            screenDirection = Vector2.zero;

            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.0001f || _camera == null) return false;

            Transform cam = _camera.transform;
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

        static void SetSize(RectTransform rect, float width, float height)
        {
            Vector2 size = rect.sizeDelta;
            if (Mathf.Abs(size.x - width) < 0.5f && Mathf.Abs(size.y - height) < 0.5f) return;

            rect.sizeDelta = new Vector2(width, height);
        }

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
