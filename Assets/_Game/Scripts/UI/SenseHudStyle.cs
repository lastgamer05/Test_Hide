using UnityEngine;
using ByAWhisker.Senses;

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

        [Header("소리 방향 호 · 자리")]
        [Tooltip("동시에 띄우는 호의 최대 개수. 위젯 풀 크기이기도 하다.")]
        [Range(1, 16)] public int maxArcs = 6;

        [Tooltip("발밑에서 난 소리가 찍히는 화면 반지름. 가까운 소리일수록 몸에 붙는다.")]
        public float arcRadiusNear = 96f;

        [Tooltip("가장 먼 소리가 찍히는 화면 반지름.")]
        public float arcRadiusFar = 250f;

        [Tooltip("이 거리(m)에서 호가 바깥 고리에 닿는다. 시야보다 넉넉해야 안 보이는 소리가 거리로 읽힌다.")]
        public float arcDistanceRange = 18f;

        [Header("소리 방향 호 · 굵기와 길이")]
        [Tooltip("코앞에서 난 소리의 호 두께.")]
        public float arcThicknessNear = 12f;

        [Tooltip("멀리서 난 소리의 호 두께. 가까울수록 굵어야 거리가 한눈에 들어온다.")]
        public float arcThicknessFar = 5f;

        [Tooltip("조용한 소리의 호 길이.")]
        public float arcLengthQuiet = 60f;

        [Tooltip("시끄러운 소리의 호 길이.")]
        public float arcLengthLoud = 150f;

        [Tooltip("갓 난 소리가 이 초 동안 잠깐 커졌다 가라앉는다. 새 소리를 눈이 먼저 잡게 한다.")]
        public float arcPopSeconds = 0.18f;

        [Tooltip("튀어오를 때의 크기 배수.")]
        public float arcPopScale = 1.4f;

        [Header("소리 방향 호 · 시간")]
        [Tooltip("발소리와 부딪는 소리가 이 초가 지나면 사라진다.")]
        public float arcFadeSeconds = 2.4f;

        [Tooltip("총성이 남는 시간의 배수. 총성은 한참 뒤에도 '저기서 쐈다'가 쓸모 있는 정보다.")]
        public float arcGunshotFadeScale = 1.8f;

        [Header("소리 방향 호 · 색과 종류")]
        [Tooltip("발소리. 잠입 중 가장 자주 뜨므로 차분한 색으로 둔다.")]
        public Color arcFootstepColor = new Color(0.55f, 0.78f, 0.85f, 1f);

        [Tooltip("총성. 하나만 떠도 바로 눈에 박혀야 한다.")]
        public Color arcGunshotColor = new Color(0.95f, 0.42f, 0.30f, 1f);

        [Tooltip("부딪는 소리와 물건 소리. 발소리와 총성 사이쯤으로 둔다.")]
        public Color arcOtherColor = new Color(0.86f, 0.78f, 0.48f, 1f);

        [Tooltip("총성 호의 굵기와 길이 배수. 색만으로는 곁눈으로 볼 때 안 갈라진다.")]
        public float arcGunshotScale = 1.45f;

        [Header("소리 방향 호 · 진하기")]
        [Tooltip("갓 난 소리의 최대 불투명도. 1까지 올리면 잠입 화면에서 너무 튄다.")]
        [Range(0f, 1f)] public float arcMaxAlpha = 0.72f;

        [Tooltip("가장 조용한 소리의 불투명도 배수. 작은 소리를 배경으로 밀어 둔다.")]
        [Range(0f, 1f)] public float arcQuietAlphaScale = 0.4f;

        [Tooltip("가장 먼 소리의 불투명도 배수. 거리를 자리뿐 아니라 진하기로도 말해 준다.")]
        [Range(0f, 1f)] public float arcFarAlphaScale = 0.45f;

        [Tooltip("이 각도 안에 있는 같은 종류의 소리는 한 호로 합친다. 발소리가 줄줄이 뜨는 걸 막는다.")]
        public float arcMergeDegrees = 22f;

        [Tooltip("가장 먼 소리가 자리를 다툴 때 쓰는 가중치 배수. 코앞의 소리가 먼저 남아야 한다.")]
        [Range(0f, 1f)] public float arcFarWeightScale = 0.4f;

        /// 소리 종류로 색을 고른다. 종류를 늘려도 기본색이 나오게 둔다.
        public Color ArcColor(NoiseKind kind)
        {
            switch (kind)
            {
                case NoiseKind.Gunshot: return arcGunshotColor;
                case NoiseKind.Footstep: return arcFootstepColor;
                default: return arcOtherColor;
            }
        }

        /// 종류별 굵기·길이 배수. 총성만 키운다.
        public float ArcEmphasis(NoiseKind kind)
        {
            return kind == NoiseKind.Gunshot ? Mathf.Max(0.1f, arcGunshotScale) : 1f;
        }

        /// 종류별로 호가 남는 시간.
        public float FadeSecondsFor(NoiseKind kind)
        {
            float baseSeconds = Mathf.Max(0.05f, arcFadeSeconds);
            return kind == NoiseKind.Gunshot ? baseSeconds * Mathf.Max(1f, arcGunshotFadeScale) : baseSeconds;
        }

        /// NoiseBus.Collect에 넘길 나이. 가장 오래 남는 종류에 맞춰야 총성이 일찍 잘리지 않는다.
        public float LongestFadeSeconds()
        {
            return Mathf.Max(0.05f, arcFadeSeconds) * Mathf.Max(1f, arcGunshotFadeScale);
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
    }
}
