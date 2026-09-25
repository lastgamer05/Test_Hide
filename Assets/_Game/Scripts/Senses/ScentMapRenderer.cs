using System.Collections.Generic;
using ByAWhisker.Core;
using UnityEngine;

namespace ByAWhisker.Senses
{
    /// <summary>
    /// ScentField의 활성 칸을 텍스처 한 장으로 옮기고 전역 셰이더 값으로 넘긴다.
    /// 자취는 이미 격자에 남고 있었고 보이지 않았을 뿐이라, 그리는 쪽만 따로 붙인다.
    ///
    /// 칸 하나가 픽셀 하나다. 그래서 칸 번호가 곧 픽셀 번호가 된다. 둘 다 row * cols + col이고,
    /// SetPixels32의 0번은 왼쪽 아래라 픽셀 v가 월드 +Z와 같은 방향으로 늘어난다.
    /// VisibilityMaskRenderer가 직교 행렬을 뒤집어 잡은 것과 같은 규칙이라 셰이더가 두 지도를 같은 UV로 읽는다.
    ///
    /// 갱신 비용은 ScentField와 같은 이유로 활성 칸 수를 따른다. 빈 격자를 훑지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScentMapRenderer : MonoBehaviour
    {
        // 셋이서 약속한 전역 이름. 바꾸면 합성 셰이더가 자취를 못 찾아 화면에서 냄새가 사라진다.
        private static readonly int IdScentMap = Shader.PropertyToID("_BW_ScentMap");
        private static readonly int IdScentBounds = Shader.PropertyToID("_BW_ScentBounds");

        private static readonly Color32 Empty = new Color32(0, 0, 0, 0);

        // 알파 0짜리 1x1. Texture2D.blackTexture는 알파가 1이라 "온 바닥에 냄새가 가득"으로 읽힌다.
        // 여럿이 나눠 쓰고 아무도 파괴하지 않는다. 한쪽이 지우면 남은 쪽이 파괴된 텍스처를 읽게 된다.
        private static Texture2D _blank;

        [Header("참조")]
        [Tooltip("옮겨 올 냄새 격자. 비우면 씬에서 찾는다.")]
        [SerializeField] private ScentField field;

        [Header("갱신")]
        [Tooltip("텍스처를 다시 올리는 간격. 초. 필드의 갱신 간격과 비슷하게 둔다. 더 자주 올려도 값이 안 바뀐다.")]
        [SerializeField] private float updateInterval = 0.2f;

        [Header("색")]
        [Tooltip("이 초만큼 묵으면 밝기가 절반이 된다. 새 자취와 옛 자취를 가르는 것이 이 값 하나다.")]
        // 자취는 25초쯤 남는데 반감기가 12초면 끝이 1/4도 안 남아 꼬리가 먼저 검어진다.
        // 그러면 자취가 사라지기 전에 "어느 쪽으로 갔는가"부터 사라져서, 남은 칸이 길이만 알려 준다.
        // 수명의 8할쯤으로 잡으면 가장 오래된 끝도 4할 남짓으로 버티면서 머리와는 두 배 넘게 벌어진다.
        [SerializeField] private float ageHalfLife = 20f;

        [Tooltip("주인별 색. 개체가 팔레트보다 많으면 돌려 쓴다. SenseHudStyle의 점 색과 맞춰 두면 둘이 같은 것으로 읽힌다.")]
        [SerializeField] private Color[] ownerColors = DefaultOwnerColors();

        private Texture2D _texture;
        private Color32[] _pixels;

        // 지난번에 칠한 픽셀 번호. 되돌릴 때 이것만 지우면 비용이 격자 크기가 아니라 활성 칸 수를 따른다.
        private List<int> _painted;

        private int _cols;
        private int _rows;
        private float _timer;

        private void Awake()
        {
            if (field == null) field = FindAnyObjectByType<ScentField>();

            // 격자가 잡히기 전에도 셰이더는 매 프레임 이 값을 읽는다. 아무것도 넣지 않으면
            // 지난 판이나 다른 씬이 남긴 텍스처를 그대로 읽는다.
            PushPlaceholder();
        }

        private void OnEnable()
        {
            GameEvents.RunReset += HandleRunReset;
        }

        private void OnDisable()
        {
            GameEvents.RunReset -= HandleRunReset;
        }

        private void OnDestroy()
        {
            DestroyTexture();
            // 파괴한 텍스처를 전역에 남겨 두면 셰이더가 엉뚱한 기본 텍스처를 읽는다.
            PushPlaceholder();
        }

        private void Update()
        {
            if (field == null || !field.IsConfigured) return;

            _timer += Time.deltaTime;
            if (_timer < updateInterval) return;
            _timer = 0f;

            if (!EnsureTexture()) return;
            Repaint();
        }

        /// <summary>
        /// 격자 크기가 달라졌으면 텍스처를 다시 만든다.
        /// Configure는 레벨 크기가 정해지는 순간 다시 불리고 그때 칸 수가 바뀌므로, 만들어 두고 잊으면 안 된다.
        /// </summary>
        private bool EnsureTexture()
        {
            int cols = field.GridCols;
            int rows = field.GridRows;
            if (cols <= 0 || rows <= 0) return false;

            if (_texture != null && _cols == cols && _rows == rows) return true;

            DestroyTexture();

            _cols = cols;
            _rows = rows;

            // 밉맵은 만들지 않는다. 멀리서 볼 때 자취가 뭉개지면 어느 쪽이 최근인지가 먼저 사라진다.
            // linear로 두어 팔레트 색이 감마 변환 없이 그대로 나간다. HUD가 같은 색을 그대로 쓰므로
            // 한쪽만 변환하면 화면의 자취와 HUD의 점이 다른 색으로 보인다.
            _texture = new Texture2D(cols, rows, TextureFormat.RGBA32, false, true)
            {
                name = "BW_ScentMap",
                // 칸 하나가 한 픽셀이라 Point로 두면 격자 무늬가 바닥에 그대로 드러난다.
                filterMode = FilterMode.Bilinear,
                // 격자 밖은 냄새가 없는 곳이다. 반대편 가장자리가 말려 들어오면 안 된다.
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            _pixels = new Color32[cols * rows];
            if (_painted == null) _painted = new List<int>(1024);
            _painted.Clear();

            // 새 배열은 이미 0이지만 텍스처 쪽 내용은 정해지지 않았다. 한 번 올려서 맞춘다.
            Upload();
            return true;
        }

        private void Repaint()
        {
            // 지난번에 칠한 자리만 되돌린다. 배열을 통째로 지우면 활성 칸이 열 개뿐이어도
            // 비용이 격자 크기를 따라간다. 자취가 몇 줄뿐인 평소가 바로 그 경우다.
            ClearPainted();

            IReadOnlyList<int> active = field.ActiveCells;
            int count = active != null ? active.Count : 0;
            int limit = _pixels.Length;

            // 인스펙터에서 0을 넣으면 0으로 나눈다.
            float halfLife = ageHalfLife > 0.01f ? ageHalfLife : 0.01f;

            for (int i = 0; i < count; i++)
            {
                int index = active[i];
                if (index < 0 || index >= limit) continue;

                float strength;
                int ownerId;
                float age;
                if (!field.TryReadCell(index, out strength, out ownerId, out age)) continue;

                // 반감기로 떨어뜨린다. 선형으로 깎으면 자취 끝이 칼로 자른 듯 끊겨서
                // "어느 쪽으로 갔는가"가 아니라 "어디까지 갔는가"만 읽힌다.
                float fresh = Mathf.Pow(0.5f, age / halfLife);
                Color color = OwnerColor(ownerId);

                // 세기는 알파에 그대로 넣는다. 여기서 한 번 더 누르면 셰이더의 _ScentGlow와 겹쳐서
                // 밝기를 어느 쪽에서 맞춰야 하는지 알 수 없게 된다.
                _pixels[index] = new Color32(
                    ToByte(color.r * fresh),
                    ToByte(color.g * fresh),
                    ToByte(color.b * fresh),
                    ToByte(strength));

                _painted.Add(index);
            }

            Upload();
        }

        private void ClearPainted()
        {
            if (_painted == null || _pixels == null) return;

            for (int i = 0; i < _painted.Count; i++)
            {
                int index = _painted[i];
                if (index >= 0 && index < _pixels.Length) _pixels[index] = Empty;
            }

            _painted.Clear();
        }

        private void Upload()
        {
            if (_texture == null || _pixels == null) return;

            _texture.SetPixels32(_pixels);
            // 밉맵이 없으니 다시 계산할 것도 없다.
            _texture.Apply(false);
            PushGlobals();
        }

        private void PushGlobals()
        {
            Shader.SetGlobalTexture(IdScentMap, _texture);
            // (minX, minZ, sizeX, sizeZ). _BW_VisionBounds와 같은 규칙이라 셰이더가 UV를 한 번만 만들면 된다.
            Shader.SetGlobalVector(IdScentBounds, field != null ? field.WorldBounds : new Vector4(0f, 0f, 1f, 1f));
        }

        /// <summary>
        /// 텍스처가 아직 없거나 이미 사라졌을 때 셰이더가 읽을 것을 넣어 둔다.
        /// 넓이를 0으로 두면 셰이더가 UV를 만들며 0으로 나누므로 1로 둔다.
        /// </summary>
        private void PushPlaceholder()
        {
            Shader.SetGlobalTexture(IdScentMap, Blank());
            Shader.SetGlobalVector(IdScentBounds, new Vector4(0f, 0f, 1f, 1f));
        }

        /// <summary>
        /// 지난 판의 자취가 한 프레임이라도 남으면 안 된다. 다음 갱신을 기다리지 않고 그 자리에서 비운다.
        /// ScentField.Clear와 어느 쪽이 먼저 불리든 결과가 같아지는 것도 이 덕이다.
        /// </summary>
        private void HandleRunReset()
        {
            _timer = 0f;
            ClearPainted();
            Upload();
        }

        /// <summary>
        /// 주인 번호로 색을 고른다. SenseHudStyle.OwnerColor와 같은 규칙이라 HUD와 바닥이 같은 색으로 묶인다.
        /// 번호는 대개 InstanceID라 음수가 온다. 나머지를 그대로 쓰면 색인이 음수가 되어 터진다.
        /// </summary>
        private Color OwnerColor(int ownerId)
        {
            if (ownerColors == null || ownerColors.Length == 0) return Color.white;

            int index = ownerId % ownerColors.Length;
            if (index < 0) index += ownerColors.Length;
            return ownerColors[index];
        }

        private static byte ToByte(float value)
        {
            float clamped = value < 0f ? 0f : (value > 1f ? 1f : value);
            return (byte)(clamped * 255f + 0.5f);
        }

        private static Texture2D Blank()
        {
            if (_blank == null)
            {
                _blank = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
                {
                    name = "BW_ScentMap (빈 것)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _blank.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                _blank.Apply(false);
            }

            return _blank;
        }

        private void DestroyTexture()
        {
            if (_texture == null) return;

            if (Application.isPlaying) Destroy(_texture);
            else DestroyImmediate(_texture);

            _texture = null;
            _pixels = null;
            _cols = 0;
            _rows = 0;
            if (_painted != null) _painted.Clear();
        }

        private static Color[] DefaultOwnerColors()
        {
            // SenseHudStyle의 기본 팔레트와 같은 색이다. 둘이 갈라지면 HUD의 점과 바닥의 자취가
            // 서로 다른 개체처럼 보인다.
            return new[]
            {
                new Color(0.58f, 0.80f, 0.62f, 1f),   // 이끼
                new Color(0.80f, 0.63f, 0.78f, 1f),   // 흐린 자주
                new Color(0.84f, 0.74f, 0.52f, 1f),   // 마른 풀
                new Color(0.55f, 0.72f, 0.88f, 1f),   // 새벽 하늘
                new Color(0.86f, 0.56f, 0.50f, 1f)    // 벽돌
            };
        }
    }
}
