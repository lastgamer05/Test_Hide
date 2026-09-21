using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ByAWhisker.Player;
using ByAWhisker.Senses;

namespace ByAWhisker.UI
{
    /// 감각 표시를 담당한다. 캔버스와 위젯은 코드로 만든다. 프리팹을 쓰지 않는다.
    /// 보여 주는 것은 넷이다. 내가 내는 소음, 내가 받는 빛, 최근에 난 소리의 방향, 근처 냄새 흔적.
    /// 전부 힌트다. 정확한 자리를 찍지 않고 방향과 세기만 말해 준다.
    [DisallowMultipleComponent]
    public class SenseHud : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("색과 크기. 비우면 기본값으로 하나 만들어 쓴다.")]
        [SerializeField] SenseHudStyle style;

        [Tooltip("다른 HUD 위에 그리려면 값을 올린다.")]
        [SerializeField] int sortingOrder = 100;

        [Header("갱신")]
        [Tooltip("소리와 냄새를 다시 읽는 간격. 매 프레임 읽을 이유가 없다. 그리는 것은 매 프레임 한다.")]
        [SerializeField, Range(0.02f, 0.5f)] float refreshInterval = 0.12f;

        [Tooltip("통합 담당이 Bind를 안 불렀을 때 시작하면서 스스로 찾아본다.")]
        [SerializeField] bool autoBindOnStart = true;

        // 소리 한 번에 몇 개까지 훑을지. 링 버퍼보다 넉넉하면 되고, 리스트는 한 번만 만든다.
        const int NoiseBufferCapacity = 64;

        /// 호 하나가 들고 있는 것. 화면 각도는 카메라가 돌 수 있으니 매 프레임 다시 구한다.
        struct ArcSlot
        {
            public Vector3 position;
            public float startTime;
            public float loudness;
            public float angle;    // 합치기 판정에만 쓰는 값
            public float weight;
        }

        /// 냄새 점 하나가 들고 있는 것. 자리는 플레이어 기준 오프셋으로 들고 있는다.
        struct DotSlot
        {
            public Vector3 offset;    // 플레이어 기준 수평 오프셋
            public Vector3 lean;      // 짙어지는 쪽 수평 방향
            public float distance;
            public float strength;
        }

        Transform _player;
        GameObject _playerRoot;
        Camera _camera;
        ScentField _scentField;
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

        Image[] _dots;
        RectTransform[] _dotRects;
        DotSlot[] _dotSlots;
        int _dotCount;

        List<NoiseEvent> _noiseBuffer;
        Vector3[] _scentOffsets;

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

        /// 냄새 격자를 준다. 비우면 냄새 점은 그냥 안 뜬다.
        public void SetScentField(ScentField field)
        {
            _scentField = field;
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
            if (_scentField == null) SetScentField(FindAnyObjectByType<ScentField>());
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
                HideAllDots();
                return;
            }

            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer <= 0f)
            {
                _refreshTimer = refreshInterval;
                RefreshNoise();
                RefreshScent();
            }

            // 카메라가 90도 돌아가는 중에도 방향이 어긋나지 않게 자리는 매 프레임 다시 잡는다.
            ApplyArcs();
            ApplyDots();
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
                arc.rectTransform.sizeDelta = new Vector2(style.arcLengthQuiet, style.arcThickness);
                arc.gameObject.SetActive(false);

                _arcs[i] = arc;
                _arcRects[i] = arc.rectTransform;
            }

            int dotCount = Mathf.Max(1, style.maxScentDots);
            _dots = new Image[dotCount];
            _dotRects = new RectTransform[dotCount];
            _dotSlots = new DotSlot[dotCount];

            for (int i = 0; i < dotCount; i++)
            {
                Image dot = SenseHudBuilder.CreateImage("Scent Dot " + i, _centerGroup, SenseHudBuilder.SoftDotSprite, Color.clear);
                SenseHudBuilder.Anchor(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                dot.rectTransform.sizeDelta = new Vector2(style.scentDotSizeMin, style.scentDotSizeMin);
                dot.gameObject.SetActive(false);

                _dots[i] = dot;
                _dotRects[i] = dot.rectTransform;
            }

            // 점은 소리 호보다 아래에 깔린다. 호가 가려지면 경고를 놓친다.
            for (int i = 0; i < _arcs.Length; i++) _arcRects[i].SetAsLastSibling();

            _noiseBuffer = new List<NoiseEvent>(NoiseBufferCapacity);
            BuildScentOffsets();
        }

        /// 냄새를 찍어 볼 자리를 미리 만들어 둔다. 월드 기준이라 카메라가 돌아도 표본이 흔들리지 않는다.
        void BuildScentOffsets()
        {
            int rings = Mathf.Max(1, style.scentRings);
            int perRing = Mathf.Max(3, style.scentSamplesPerRing);
            float radius = Mathf.Max(0.1f, style.scentSampleRadius);

            _scentOffsets = new Vector3[1 + rings * perRing];
            _scentOffsets[0] = Vector3.zero;   // 발밑

            int index = 1;
            for (int r = 1; r <= rings; r++)
            {
                float ringRadius = radius * r / rings;
                // 고리마다 반 칸씩 돌려 놓는다. 그래야 표본이 한 줄로 몰리지 않는다.
                float twist = (r % 2 == 0) ? Mathf.PI / perRing : 0f;

                for (int k = 0; k < perRing; k++)
                {
                    float a = twist + Mathf.PI * 2f * k / perRing;
                    _scentOffsets[index++] = new Vector3(Mathf.Cos(a) * ringRadius, 0f, Mathf.Sin(a) * ringRadius);
                }
            }
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

            float maxAge = Mathf.Max(0.05f, style.arcFadeSeconds);
            Vector3 origin = _player.position;

            int count = NoiseBus.Collect(origin, maxAge, _noiseBuffer);
            if (count > _noiseBuffer.Count) count = _noiseBuffer.Count;

            for (int i = 0; i < count; i++)
            {
                NoiseEvent evt = _noiseBuffer[i];

                // 내가 낸 소리는 이미 아는 정보다. 화면을 어지럽히기만 한다.
                if (IsOwnSource(evt.source)) continue;

                float age = Time.time - evt.time;
                if (age < 0f) age = 0f;
                if (age >= maxAge) continue;

                Vector2 dir;
                if (!TryScreenDirection(evt.position - origin, out dir)) continue;

                float loudness = Mathf.Clamp01(evt.loudness);
                float weight = loudness * (1f - age / maxAge);
                if (weight <= 0.001f) continue;

                AddArc(evt.position, evt.time, loudness, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, weight);
            }
        }

        /// 비슷한 방향의 소리는 하나로 합친다. 발소리가 이어지면 호가 줄줄이 뜨기 때문이다.
        void AddArc(Vector3 position, float time, float loudness, float angle, float weight)
        {
            for (int i = 0; i < _arcCount; i++)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(angle, _arcSlots[i].angle)) > style.arcMergeDegrees) continue;

                // 같은 방향이면 더 센 쪽만 남긴다.
                if (weight > _arcSlots[i].weight) SetArc(i, position, time, loudness, angle, weight);
                return;
            }

            if (_arcCount < _arcSlots.Length)
            {
                SetArc(_arcCount, position, time, loudness, angle, weight);
                _arcCount++;
                return;
            }

            // 자리가 다 찼으면 가장 약한 것을 밀어낸다.
            int weakest = 0;
            for (int i = 1; i < _arcCount; i++)
            {
                if (_arcSlots[i].weight < _arcSlots[weakest].weight) weakest = i;
            }

            if (weight > _arcSlots[weakest].weight) SetArc(weakest, position, time, loudness, angle, weight);
        }

        void SetArc(int index, Vector3 position, float time, float loudness, float angle, float weight)
        {
            _arcSlots[index].position = position;
            _arcSlots[index].startTime = time;
            _arcSlots[index].loudness = loudness;
            _arcSlots[index].angle = angle;
            _arcSlots[index].weight = weight;
        }

        void ApplyArcs()
        {
            float maxAge = Mathf.Max(0.05f, style.arcFadeSeconds);
            Vector3 origin = _player.position;

            for (int i = 0; i < _arcs.Length; i++)
            {
                if (i >= _arcCount)
                {
                    SenseHudBuilder.SetVisible(_arcs[i], false);
                    continue;
                }

                float fade = 1f - (Time.time - _arcSlots[i].startTime) / maxAge;
                Vector2 dir;
                if (fade <= 0f || !TryScreenDirection(_arcSlots[i].position - origin, out dir))
                {
                    SenseHudBuilder.SetVisible(_arcs[i], false);
                    continue;
                }

                SenseHudBuilder.SetVisible(_arcs[i], true);

                float loudness = _arcSlots[i].loudness;
                RectTransform rect = _arcRects[i];

                rect.anchoredPosition = dir * style.arcRadius;
                // 막대는 가로로 누워 있으니 방향각에 90도를 더해 원의 접선으로 세운다.
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f);

                float length = Mathf.Lerp(style.arcLengthQuiet, style.arcLengthLoud, loudness);
                SetSize(rect, length, style.arcThickness);

                Color color = Color.Lerp(style.arcQuietColor, style.arcLoudColor, loudness);
                // 제곱으로 줄여야 끝물에 오래 붙어 있지 않고 깔끔하게 사라진다.
                color.a = fade * fade * style.arcMaxAlpha * Mathf.Lerp(0.4f, 1f, loudness);
                SetColor(_arcs[i], color);
            }
        }

        void RefreshScent()
        {
            _dotCount = 0;

            if (_scentField == null || !_scentField.IsConfigured) return;

            Vector3 origin = _player.position;
            float radius = Mathf.Max(0.1f, style.scentSampleRadius);
            float halfLife = Mathf.Max(0.1f, style.scentAgeHalfLife);

            for (int i = 0; i < _scentOffsets.Length && _dotCount < _dots.Length; i++)
            {
                ScentReading reading;
                if (!_scentField.TrySample(origin + _scentOffsets[i], out reading)) continue;

                float strength = Mathf.Clamp01(reading.strength);
                if (strength < style.scentMinStrength) continue;

                // 묵은 흔적은 흐려진다. 새 흔적과 옛 흔적이 같아 보이면 추적이 안 된다.
                strength *= Mathf.Pow(0.5f, Mathf.Max(0f, reading.age) / halfLife);
                if (strength < style.scentMinStrength) continue;

                int slot = _dotCount++;
                _dotSlots[slot].offset = _scentOffsets[i];
                _dotSlots[slot].lean = reading.gradient;
                _dotSlots[slot].distance = Mathf.Clamp01(_scentOffsets[i].magnitude / radius);
                _dotSlots[slot].strength = strength;

                float size = Mathf.Lerp(style.scentDotSizeMin, style.scentDotSizeMax, strength);
                SetSize(_dotRects[slot], size, size);

                Color color = style.OwnerColor(reading.ownerId);
                color.a = strength * style.scentMaxAlpha;
                SetColor(_dots[slot], color);
            }
        }

        void ApplyDots()
        {
            for (int i = 0; i < _dots.Length; i++)
            {
                if (i >= _dotCount)
                {
                    SenseHudBuilder.SetVisible(_dots[i], false);
                    continue;
                }

                Vector2 dir;
                Vector2 position = Vector2.zero;

                // 실제 거리에 비례해 찍으면 냄새 지도가 되어 버린다. 가까운 띠 안으로 눌러 넣는다.
                if (TryScreenDirection(_dotSlots[i].offset, out dir))
                {
                    position = dir * Mathf.Lerp(style.scentRadiusNear, style.scentRadiusFar, _dotSlots[i].distance);
                }

                Vector2 lean;
                if (TryScreenDirection(_dotSlots[i].lean, out lean))
                {
                    // 짙어지는 쪽으로 조금 민다. 냄새가 어디서 오는지만 알려 주는 정도로.
                    position += lean * (style.scentGradientLean * _dotSlots[i].strength);
                }

                SenseHudBuilder.SetVisible(_dots[i], true);
                _dotRects[i].anchoredPosition = position;
            }
        }

        void HideAllArcs()
        {
            if (_arcs == null) return;
            for (int i = 0; i < _arcs.Length; i++) SenseHudBuilder.SetVisible(_arcs[i], false);
            _arcCount = 0;
        }

        void HideAllDots()
        {
            if (_dots == null) return;
            for (int i = 0; i < _dots.Length; i++) SenseHudBuilder.SetVisible(_dots[i], false);
            _dotCount = 0;
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
