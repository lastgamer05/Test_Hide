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
        public Vector3 KeyPosition { get; private set; }
        public Vector3 ExitPosition { get; private set; }
        public int LampCount { get; private set; }
        public LevelMapAsset Map { get { return map; } }

        /// <summary>생성물이 들어간 부모. 부트스트랩이 여기서 체크포인트와 출구를 찾는다.</summary>
        public Transform LevelRoot { get { return root != null ? root : transform; } }

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

            CreateMarkers(parent);
        }

        /// <summary>램프, 열쇠, 출구, 체크포인트를 놓는다.</summary>
        private void CreateMarkers(Transform parent)
        {
            LampCount = 0;
            KeyPosition = Vector3.zero;
            ExitPosition = Vector3.zero;

            foreach (Vector2Int cell in Grid.CellsOf(CellType.Lamp))
            {
                CreateLamp(parent, Grid.CellCenter(cell.x, cell.y));
                LampCount++;
            }

            Vector2Int keyCell;
            if (Grid.TryFindFirst(CellType.Key, out keyCell))
            {
                KeyPosition = Grid.CellCenter(keyCell.x, keyCell.y);
                CreateKey(parent, KeyPosition);
                CreateCheckpoint(parent, KeyPosition, "Checkpoint_Key");
            }

            Vector2Int exitCell;
            if (Grid.TryFindFirst(CellType.Exit, out exitCell))
            {
                ExitPosition = Grid.CellCenter(exitCell.x, exitCell.y);
                CreateExit(parent, ExitPosition);
            }

            CreateCheckpoint(parent, PlayerStartPosition, "Checkpoint_Start");
        }

        private void CreateLamp(Transform parent, Vector3 cellCenter)
        {
            var lamp = new GameObject("Lamp");
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = cellCenter + Vector3.up * map.lampHeight;

            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = map.lampRange;
            light.color = map.lampColor;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;

            var source = lamp.AddComponent<Perception.LightSource>();
            source.radius = map.lampLitRadius;
            source.intensity = map.lampIntensity;

            GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb";
            DestroyCollider(bulb);
            bulb.transform.SetParent(lamp.transform, false);
            bulb.transform.localScale = Vector3.one * 0.32f;
            if (map.lampMaterial != null) bulb.GetComponent<MeshRenderer>().sharedMaterial = map.lampMaterial;
        }

        private void CreateKey(Transform parent, Vector3 cellCenter)
        {
            var key = new GameObject("Key");
            key.transform.SetParent(parent, false);
            key.transform.localPosition = cellCenter + Vector3.up * 0.9f;

            SphereCollider trigger = key.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.7f;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            DestroyCollider(visual);
            visual.transform.SetParent(key.transform, false);
            visual.transform.localScale = new Vector3(0.45f, 0.45f, 0.18f);
            if (map.keyMaterial != null) visual.GetComponent<MeshRenderer>().sharedMaterial = map.keyMaterial;

            ObjectiveItem item = key.AddComponent<ObjectiveItem>();
            item.SetVisual(visual);
        }

        private void CreateExit(Transform parent, Vector3 cellCenter)
        {
            var exit = new GameObject("Exit");
            exit.transform.SetParent(parent, false);
            exit.transform.localPosition = cellCenter;

            BoxCollider trigger = exit.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(Grid.CellSize, 3f, Grid.CellSize);
            trigger.center = new Vector3(0f, 1.5f, 0f);

            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Pad";
            DestroyCollider(pad);
            pad.transform.SetParent(exit.transform, false);
            pad.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            pad.transform.localScale = new Vector3(Grid.CellSize * 0.9f, 0.06f, Grid.CellSize * 0.9f);
            if (map.exitMaterial != null) pad.GetComponent<MeshRenderer>().sharedMaterial = map.exitMaterial;

            exit.AddComponent<ExitZone>();
        }

        private void CreateCheckpoint(Transform parent, Vector3 cellCenter, string name)
        {
            var point = new GameObject(name);
            point.transform.SetParent(parent, false);
            point.transform.localPosition = cellCenter;

            SphereCollider trigger = point.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 1.2f;

            point.AddComponent<Checkpoint>();
        }

        private static void DestroyCollider(GameObject target)
        {
            Collider collider = target.GetComponent<Collider>();
            if (collider == null) return;

            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
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
