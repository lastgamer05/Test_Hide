using UnityEngine;

namespace ByAWhisker.Level
{
    /// <summary>
    /// 문자 지도를 읽어 바닥과 블록을 만든다.
    /// 가로로 이어진 같은 종류의 칸은 상자 하나로 합친다.
    /// 편집 모드에서는 컴포넌트 메뉴의 Build Level로 실행한다.
    /// </summary>
    public class LevelBuilder : MonoBehaviour
    {
        public const string FloorLayer = "Floor";
        public const string WallLayer = "Wall";
        public const string HighCoverLayer = "HighCover";
        public const string LowCoverLayer = "LowCover";

        [SerializeField] private LevelMapAsset map;
        [Tooltip("생성물이 들어갈 부모. 비우면 이 오브젝트 밑에 만든다.")]
        [SerializeField] private Transform root;
        [SerializeField] private bool buildOnAwake = true;

        public GridMap Grid { get; private set; }
        public Vector3 PlayerStartPosition { get; private set; }
        public LevelMapAsset Map { get { return map; } }

        private void Awake()
        {
            if (buildOnAwake && Grid == null) Build();
        }

        /// <summary>빌드 전에 데이터를 지정한다. 부트스트랩과 에디터 도구가 쓴다.</summary>
        public void Configure(LevelMapAsset mapAsset, Transform parent)
        {
            map = mapAsset;
            if (parent != null) root = parent;
        }

        [ContextMenu("Build Level")]
        public void Build()
        {
            if (map == null)
            {
                Debug.LogError("LevelBuilder: 지도 에셋이 비어 있다.", this);
                return;
            }

            Clear();
            Grid = GridMap.FromAsset(map);

            Transform parent = root != null ? root : transform;
            CreateFloor(parent);
            CreateBlocks(parent);

            Vector2Int start;
            PlayerStartPosition = Grid.TryFindFirst(CellType.PlayerStart, out start)
                ? Grid.CellCenter(start.x, start.y)
                : Grid.CellCenter(1, 1);
        }

        [ContextMenu("Clear Level")]
        public void Clear()
        {
            Transform parent = root != null ? root : transform;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        private void CreateFloor(Transform parent)
        {
            float width = Grid.Cols * Grid.CellSize;
            float depth = Grid.Rows * Grid.CellSize;
            CreateBox(
                "Floor",
                parent,
                new Vector3(width * 0.5f, -0.1f, depth * 0.5f),
                new Vector3(width, 0.2f, depth),
                map.floorMaterial,
                FloorLayer);
        }

        private void CreateBlocks(Transform parent)
        {
            for (int row = 0; row < Grid.Rows; row++)
            {
                int col = 0;
                while (col < Grid.Cols)
                {
                    CellType type = Grid.At(col, row);
                    if (!IsBlock(type))
                    {
                        col++;
                        continue;
                    }

                    int end = col;
                    while (end + 1 < Grid.Cols && Grid.At(end + 1, row) == type) end++;

                    CreateRun(parent, type, col, end, row);
                    col = end + 1;
                }
            }
        }

        private void CreateRun(Transform parent, CellType type, int fromCol, int toCol, int row)
        {
            float cell = Grid.CellSize;
            float width = (toCol - fromCol + 1) * cell;
            float height = HeightOf(type);
            var center = new Vector3(
                (fromCol + toCol + 1) * 0.5f * cell,
                height * 0.5f,
                (row + 0.5f) * cell);

            CreateBox(
                type + "_r" + row + "_c" + fromCol,
                parent,
                center,
                new Vector3(width, height, cell),
                MaterialOf(type),
                LayerOf(type));
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 center, Vector3 size, Material material, string layerName)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = center;
            box.transform.localScale = size;
            box.layer = ResolveLayer(layerName);

            if (material != null)
            {
                var renderer = box.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
            }

            return box;
        }

        private static int ResolveLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0) return layer;

            Debug.LogWarning("LevelBuilder: " + layerName + " 레이어가 없다. 기본 레이어로 만든다.");
            return 0;
        }

        private static bool IsBlock(CellType type)
        {
            return type == CellType.Wall || type == CellType.HighCover || type == CellType.LowCover;
        }

        private float HeightOf(CellType type)
        {
            switch (type)
            {
                case CellType.Wall: return map.wallHeight;
                case CellType.HighCover: return map.highCoverHeight;
                default: return map.lowCoverHeight;
            }
        }

        private Material MaterialOf(CellType type)
        {
            switch (type)
            {
                case CellType.Wall: return map.wallMaterial;
                case CellType.HighCover: return map.highCoverMaterial;
                default: return map.lowCoverMaterial;
            }
        }

        private static string LayerOf(CellType type)
        {
            switch (type)
            {
                case CellType.Wall: return WallLayer;
                case CellType.HighCover: return HighCoverLayer;
                default: return LowCoverLayer;
            }
        }
    }
}
