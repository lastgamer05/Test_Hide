using UnityEngine;

namespace ByAWhisker.UI
{
    /// 감각 표시의 색과 크기를 담는 데이터. 코드에 숫자를 박지 않으려고 따로 뺐다.
    /// 에셋을 안 만들어도 돌아가야 해서 CreateDefault로 같은 값을 만들 수 있게 둔다.
    [CreateAssetMenu(menuName = "By a Whisker/Sense Hud Style", fileName = "SenseHudStyle")]
    public class SenseHudStyle : ScriptableObject
    {
        [Header("캔버스")]
        [Tooltip("크기 값을 적는 기준 해상도. 실제 화면은 이 비율로 늘어난다.")]
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);

        [Header("막대 · 자리")]
        [Tooltip("화면 왼쪽 아래에서 띄우는 여백.")]
        public Vector2 barMargin = new Vector2(36f, 36f);

        [Tooltip("막대 하나의 크기.")]
        public Vector2 barSize = new Vector2(190f, 10f);

        [Tooltip("두 막대 사이 간격.")]
        public float barSpacing = 14f;

        [Tooltip("켜면 두 막대를 위아래로 쌓는다. 끄면 나란히 놓는다.")]
        public bool barsVertical = false;

        [Tooltip("막대가 값을 따라가는 속도. 소음은 계단처럼 튀어서 눈으로 볼 때만 부드럽게 만든다.")]
        public float barFollowSpeed = 11f;

        [Header("막대 · 색")]
        [Tooltip("빈 막대. 어두운 화면에서도 자리를 알 만큼만 밝게.")]
        public Color barBackColor = new Color(0.05f, 0.06f, 0.08f, 0.62f);

        [Tooltip("소음 막대. 소리는 차가운 색으로 둔다.")]
        public Color noiseColor = new Color(0.46f, 0.74f, 0.80f, 0.92f);

        [Tooltip("빛 막대. 빛은 따뜻한 색으로 둔다.")]
        public Color lightColor = new Color(0.86f, 0.76f, 0.44f, 0.92f);

        [Tooltip("값이 이만큼 안 바뀌면 막대를 다시 그리지 않는다.")]
        public float barEpsilon = 0.004f;

        [Header("소리 방향 호")]
        [Tooltip("동시에 띄우는 호의 최대 개수. 위젯 풀 크기이기도 하다.")]
        [Range(1, 16)] public int maxArcs = 6;

        [Tooltip("화면 가운데에서 호까지의 거리.")]
        public float arcRadius = 210f;

        [Tooltip("호의 두께.")]
        public float arcThickness = 7f;

        [Tooltip("조용한 소리의 호 길이.")]
        public float arcLengthQuiet = 60f;

        [Tooltip("시끄러운 소리의 호 길이.")]
        public float arcLengthLoud = 150f;

        [Tooltip("이 초가 지나면 호가 사라진다. NoiseBus.Collect의 maxAge로도 쓴다.")]
        public float arcFadeSeconds = 2.4f;

        [Tooltip("조용한 소리의 색.")]
        public Color arcQuietColor = new Color(0.55f, 0.78f, 0.85f, 1f);

        [Tooltip("시끄러운 소리의 색. 클수록 붉게 가서 총성이 눈에 띈다.")]
        public Color arcLoudColor = new Color(0.90f, 0.49f, 0.36f, 1f);

        [Tooltip("갓 난 소리의 최대 불투명도. 1까지 올리면 잠입 화면에서 너무 튄다.")]
        [Range(0f, 1f)] public float arcMaxAlpha = 0.72f;

        [Tooltip("이 각도 안에 있는 소리는 한 호로 합친다. 발소리가 줄줄이 뜨는 걸 막는다.")]
        public float arcMergeDegrees = 22f;

        [Header("냄새 점")]
        [Tooltip("동시에 띄우는 점의 최대 개수. 위젯 풀 크기이기도 하다.")]
        [Range(1, 64)] public int maxScentDots = 16;

        [Tooltip("플레이어 주변 몇 m까지 찍어 볼지.")]
        public float scentSampleRadius = 6f;

        [Tooltip("찍어 볼 고리 수. 0번 고리는 발밑 한 점이다.")]
        [Range(1, 5)] public int scentRings = 2;

        [Tooltip("고리 하나에 몇 방향을 찍을지.")]
        [Range(3, 16)] public int scentSamplesPerRing = 8;

        [Tooltip("이보다 옅은 냄새는 표시하지 않는다.")]
        [Range(0f, 1f)] public float scentMinStrength = 0.06f;

        [Tooltip("발밑 냄새가 찍히는 화면 반지름.")]
        public float scentRadiusNear = 40f;

        [Tooltip("가장 먼 표본이 찍히는 화면 반지름. 실제 거리와 비례하지 않게 눌러 놓는다.")]
        public float scentRadiusFar = 160f;

        [Tooltip("옅은 냄새 점의 지름.")]
        public float scentDotSizeMin = 26f;

        [Tooltip("짙은 냄새 점의 지름.")]
        public float scentDotSizeMax = 64f;

        [Tooltip("가장 짙을 때의 불투명도. 점은 어디까지나 힌트라 흐리게 둔다.")]
        [Range(0f, 1f)] public float scentMaxAlpha = 0.34f;

        [Tooltip("짙어지는 쪽으로 점을 이만큼 민다. 냄새가 오는 방향을 읽게 해 준다.")]
        public float scentGradientLean = 22f;

        [Tooltip("이 초만큼 묵은 냄새는 절반쯤 흐려진다. 새 흔적과 옛 흔적을 구분한다.")]
        public float scentAgeHalfLife = 12f;

        [Tooltip("주인별 점 색. 개체가 팔레트보다 많으면 돌려 쓴다.")]
        public Color[] scentOwnerColors = DefaultOwnerColors();

        /// 주인 번호로 색을 고른다. 번호가 음수여도 같은 주인은 늘 같은 색이 나와야 한다.
        public Color OwnerColor(int ownerId)
        {
            if (scentOwnerColors == null || scentOwnerColors.Length == 0) return Color.white;

            int index = ownerId % scentOwnerColors.Length;
            if (index < 0) index += scentOwnerColors.Length;
            return scentOwnerColors[index];
        }

        /// 스타일 에셋을 안 꽂아도 HUD가 뜨게 하려고 기본값 인스턴스를 만들어 준다.
        /// 만든 쪽이 파괴까지 책임진다.
        public static SenseHudStyle CreateDefault()
        {
            SenseHudStyle style = CreateInstance<SenseHudStyle>();
            style.name = "SenseHudStyle (기본값)";
            style.hideFlags = HideFlags.HideAndDontSave;
            return style;
        }

        static Color[] DefaultOwnerColors()
        {
            // 어두운 배경에서 서로 구분되면서도 튀지 않는 색으로 고른다.
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
