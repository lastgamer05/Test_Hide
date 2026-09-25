using UnityEngine;

namespace ByAWhisker.Level
{
    /// <summary>
    /// 문자 지도와 치수를 담는 데이터. 한 줄이 격자 한 행이다.
    /// # 벽, H 높은 엄폐, L 낮은 엄폐, l 램프, P 시작 위치, K 열쇠, X 출구, . 빈 바닥.
    /// </summary>
    [CreateAssetMenu(menuName = "By a Whisker/Level Map", fileName = "LevelMap")]
    public class LevelMapAsset : ScriptableObject
    {
        [TextArea(10, 40)]
        public string rows = string.Empty;

        [Header("치수 (m)")]
        public float cellSize = 2f;
        public float wallHeight = 3.6f;
        public float highCoverHeight = 3f;
        public float lowCoverHeight = 1.5f;

        [Header("머티리얼")]
        public Material floorMaterial;
        public Material wallMaterial;
        public Material highCoverMaterial;
        public Material lowCoverMaterial;
        public Material lampMaterial;
        public Material keyMaterial;
        public Material exitMaterial;

        [Header("램프")]
        public float lampHeight = 3f;
        public float lampRange = 7f;
        [Tooltip("탐지에 쓰는 밝기 반경. 보이는 빛보다 조금 좁게 둔다.")]
        public float lampLitRadius = 5.5f;
        public float lampIntensity = 1f;
        public Color lampColor = new Color(1f, 0.76f, 0.48f);

        /// <summary>지도 문자 하나를 칸 종류로 바꾼다.</summary>
        public static CellType Parse(char symbol)
        {
            switch (symbol)
            {
                case '#': return CellType.Wall;
                case 'H': return CellType.HighCover;
                case 'L': return CellType.LowCover;
                case 'l': return CellType.Lamp;
                case 'P': return CellType.PlayerStart;
                case 'K': return CellType.Key;
                case 'X': return CellType.Exit;
                default: return CellType.Empty;
            }
        }
    }
}
