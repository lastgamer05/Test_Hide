using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.Level
{
    /// <summary>
    /// 격자 조회 창구. MonoBehaviour가 아니다.
    /// 시야 계산과 적 AI가 모두 이 클래스를 통해 칸을 묻는다.
    /// 행은 지도 문자열의 줄 순서이고 월드의 +Z 방향으로 늘어난다.
    /// </summary>
    public class GridMap
    {
        private readonly CellType[,] _cells;

        public int Cols { get; }
        public int Rows { get; }
        public float CellSize { get; }

        public GridMap(CellType[,] cells, float cellSize)
        {
            _cells = cells;
            Cols = cells.GetLength(0);
            Rows = cells.GetLength(1);
            CellSize = cellSize;
        }

        public static GridMap FromAsset(LevelMapAsset asset)
        {
            string[] lines = asset.rows.Replace("\r", string.Empty).Split('\n');

            var kept = new List<string>();
            foreach (string line in lines)
            {
                if (line.Length > 0) kept.Add(line);
            }

            int rows = kept.Count;
            int cols = 0;
            foreach (string line in kept)
            {
                if (line.Length > cols) cols = line.Length;
            }

            var cells = new CellType[Mathf.Max(cols, 1), Mathf.Max(rows, 1)];
            for (int row = 0; row < rows; row++)
            {
                string line = kept[row];
                for (int col = 0; col < cols; col++)
                {
                    cells[col, row] = col < line.Length ? LevelMapAsset.Parse(line[col]) : CellType.Empty;
                }
            }

            return new GridMap(cells, asset.cellSize);
        }

        public bool Inside(int col, int row)
        {
            return col >= 0 && row >= 0 && col < Cols && row < Rows;
        }

        /// <summary>격자 밖은 벽으로 다룬다. 레벨 경계 밖으로 시선이 새지 않는다.</summary>
        public CellType At(int col, int row)
        {
            return Inside(col, row) ? _cells[col, row] : CellType.Wall;
        }

        public CellType At(Vector3 world)
        {
            return At(ColOf(world.x), RowOf(world.z));
        }

        public int ColOf(float x)
        {
            return Mathf.FloorToInt(x / CellSize);
        }

        public int RowOf(float z)
        {
            return Mathf.FloorToInt(z / CellSize);
        }

        public Vector3 CellCenter(int col, int row)
        {
            return new Vector3((col + 0.5f) * CellSize, 0f, (row + 0.5f) * CellSize);
        }

        public Vector3 WorldSize
        {
            get { return new Vector3(Cols * CellSize, 0f, Rows * CellSize); }
        }

        /// <summary>몸이 지나갈 수 없는 칸인가.</summary>
        public static bool BlocksMovement(CellType type)
        {
            return type == CellType.Wall || type == CellType.HighCover || type == CellType.LowCover;
        }

        /// <summary>시선을 막는 칸인가. 낮은 엄폐는 앉았을 때만 막는다.</summary>
        public bool BlocksSight(int col, int row, bool crouched)
        {
            CellType type = At(col, row);
            if (type == CellType.Wall || type == CellType.HighCover) return true;
            return crouched && type == CellType.LowCover;
        }

        public bool TryFindFirst(CellType type, out Vector2Int cell)
        {
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    if (_cells[col, row] != type) continue;
                    cell = new Vector2Int(col, row);
                    return true;
                }
            }

            cell = default;
            return false;
        }

        public IEnumerable<Vector2Int> CellsOf(CellType type)
        {
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    if (_cells[col, row] == type) yield return new Vector2Int(col, row);
                }
            }
        }
    }
}
