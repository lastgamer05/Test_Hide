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
        public float lowCoverHeight = 1.15f;

        [Header("머티리얼")]
        public Material floorMaterial;
        public Material wallMaterial;
        public Material highCoverMaterial;
        public Material lowCoverMaterial;

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
