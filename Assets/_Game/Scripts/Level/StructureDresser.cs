using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ByAWhisker.Level
{
    /// <summary>
    /// 바닥과 벽에 우주정거장 옷을 입힌다. 판정은 LevelBuilder의 상자가 그대로 맡고
    /// 여기서는 보이는 것만 만든다. 입힌 것에는 콜라이더가 없다.
    ///
    /// 옷은 레벨 루트 밖의 자기 루트에 둔다. LevelBuilder.Build()가 Clear()로 레벨 루트의
    /// 자식을 전부 지우기 때문에, 그 밑에 두면 다시 지을 때 옷이 같이 사라진다.
    /// LevelBuilder에는 다 지었다는 이벤트가 없어서 격자 참조가 바뀌었는지로 다시 지어진 것을 안다.
    /// </summary>
    [DisallowMultipleComponent]
    public class StructureDresser : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("입힐 레벨. 비우면 같은 오브젝트에서, 그래도 없으면 씬에서 찾는다.")]
        [SerializeField] private LevelBuilder levelBuilder;

        [Header("바닥")]
        [Tooltip("평범한 바닥 타일. 여기서 칸마다 하나를 고른다. 같은 파일을 두 번 넣으면 그만큼 자주 나온다.")]
        [SerializeField] private GameObject[] floorTiles;

        [Tooltip("가끔 섞는 바닥 타일. 너무 자주 나오면 무늬가 소음이 된다.")]
        [SerializeField] private GameObject[] floorAccentTiles;

        [Range(0f, 1f)]
        [SerializeField] private float floorAccentChance = 0.12f;

        [Tooltip("바닥 타일 윗면 높이(m). 판정용 바닥의 윗면이 0이라 발이 뜨거나 묻히지 않게 맞춘다.")]
        [SerializeField] private float floorTopHeight = 0f;

        [Tooltip("타일 두께 배율. 가로세로만 칸에 맞춰 늘리고 두께는 그대로 두어야 옆면이 두꺼운 판처럼 보이지 않는다.")]
        [SerializeField] private float floorThicknessScale = 1f;

        [Tooltip("벽이 아닌 칸 전부에 바닥을 깐다. 판정용 바닥을 끄면 엄폐 칸 밑이 비어 소품 틈으로 허공이 보인다.")]
        [SerializeField] private bool floorUnderCover = true;

        [Header("벽 판")]
        [Tooltip("평범한 벽 판. 걸을 수 있는 칸을 바라보는 면마다 하나를 고른다.")]
        [SerializeField] private GameObject[] wallPanels;

        [Tooltip("가끔 섞는 벽 판(창문, 장식, 현수막). 긴 복도가 한 장의 무늬로 보이지 않게 한다.")]
        [SerializeField] private GameObject[] wallAccentPanels;

        [Range(0f, 1f)]
        [SerializeField] private float wallAccentChance = 0.15f;

        [Tooltip("판의 앞면 방향 보정(도). 코드는 로컬 +Z가 앞이라고 놓는다. 판이 등을 보이면 180을 넣는다. " +
                 "폭은 로컬 X로 재므로 0이나 180만 쓴다.")]
        [SerializeField] private float faceYawOffset = 0f;

        [Tooltip("판 두께 배율. 판은 벽 칸 안쪽으로 붙이므로 두꺼워도 통로를 좁히지 않는다.")]
        [SerializeField] private float panelThicknessScale = 1f;

        [Tooltip("엄폐 칸을 바라보는 벽 면에도 판을 세운다. 엄폐 소품은 칸보다 작아서 그 틈으로 벽 면이 보인다.")]
        [SerializeField] private bool faceCoverCells = true;

        [Header("모서리")]
        [Tooltip("튀어나온 모서리(벽 칸 하나가 두 면을 드러낸 곳). 판 두 장의 끝이 만나는 이음매를 덮는다.")]
        [SerializeField] private GameObject outerCorner;

        [Tooltip("들어간 모서리(빈 칸 하나를 벽 셋이 둘러싼 곳). 비우면 튀어나온 모서리 모델을 쓴다.")]
        [SerializeField] private GameObject innerCorner;

        [Tooltip("모서리 조각의 가로세로(m).")]
        [SerializeField] private float cornerSize = 0.6f;

        [Tooltip("모서리 조각이 판 앞면보다 튀어나오는 양(m). 0이면 판 앞면과 같은 자리에 겹쳐 z 싸움이 난다. " +
                 "콜라이더보다 크게 튀어나오면 벽을 뚫고 걷는 것처럼 보이니 작게 둔다.")]
        [SerializeField] private float cornerProtrusion = 0.06f;

        [Tooltip("모서리 조각 방향 보정(도). L자 모서리가 벽 쪽을 감싸지 않으면 90씩 돌려 맞춘다.")]
        [SerializeField] private float cornerYawOffset = 0f;

        [Header("벽 윗면")]
        [Tooltip("벽 뚜껑 재질. 비우면 판정용 벽 상자의 재질을 쓴다. 쿼터뷰에서 판 사이로 속이 보이지 않게 덮는다.")]
        [SerializeField] private Material wallCapMaterial;

        [Tooltip("뚜껑 두께(m). 판 윗단을 덮을 만큼만.")]
        [SerializeField] private float wallCapThickness = 0.08f;

        [Header("변주")]
        [Tooltip("칸 좌표와 섞는 씨앗. 같은 값이면 몇 번을 다시 지어도 모양이 같다.")]
        [SerializeField] private int seed = 1507;

        /// <summary>옷이 들어간 루트. 레벨 루트 밖에 있다.</summary>
        public Transform DressingRoot { get { return _root; } }

        /// <summary>지금 입혀 둔 옷이 어느 격자의 것인가. 없으면 null.</summary>
        public GridMap DressedGrid { get { return _dressedGrid; } }

        private const int SaltFloorPick = 1;
        private const int SaltFloorAccent = 2;
        private const int SaltFloorTurn = 3;
        private const int SaltWallPick = 11;
        private const int SaltWallAccent = 12;

        private static readonly Vector2Int[] FaceDirections =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0)
        };

        /// 꼭짓점을 둘러싼 네 칸. 순서대로 돌면 이웃한 둘이 변을 하나 나눠 갖는다.
        private static readonly Vector2Int[] CornerQuadrants =
        {
            new Vector2Int(-1, -1),
            new Vector2Int(1, -1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, 1)
        };

        private Transform _root;
        private GridMap _dressedGrid;
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();
        private readonly Dictionary<GameObject, Piece> _pieces = new Dictionary<GameObject, Piece>();
        private readonly Dictionary<Material, List<CombineInstance>> _batches = new Dictionary<Material, List<CombineInstance>>();

        private struct Part
        {
            public Mesh mesh;
            public int subMesh;
            public Material material;
            public Matrix4x4 local;
        }

        /// 모델 하나를 그릴 조각과 크기. 모델마다 한 번만 잰다.
        private sealed class Piece
        {
            public readonly List<Part> parts = new List<Part>();
            public Bounds bounds;
        }

        private void Awake()
        {
            if (levelBuilder == null) levelBuilder = GetComponent<LevelBuilder>();
            if (levelBuilder == null) levelBuilder = FindFirstObjectByType<LevelBuilder>();
        }

        private void Start()
        {
            Dress();
        }

        private void Update()
        {
            // 참조 비교뿐이라 매 프레임 돌려도 할당이 없다. Build()는 매번 새 GridMap을 만든다.
            if (levelBuilder != null && levelBuilder.Grid != null && levelBuilder.Grid != _dressedGrid)
            {
                Dress();
            }
        }

        private void OnDestroy()
        {
            Undress();
            if (_root != null) Discard(_root.gameObject);
            _root = null;
        }

        /// <summary>
        /// 지금 격자에 맞춰 입힌다. 입혀 둔 것이 있으면 지우고 다시 입힌다.
        /// 격자가 아직 없으면 아무것도 하지 않고, Update가 격자가 생기는 것을 기다린다.
        /// </summary>
        public void Dress()
        {
            if (levelBuilder == null || levelBuilder.Grid == null || levelBuilder.Map == null) return;

            Undress();

            GridMap grid = levelBuilder.Grid;
            Transform levelRoot = levelBuilder.LevelRoot;
            EnsureRoot(levelRoot);

            // 판정 상자에서 벽 윗면을 덮을 상자 메시와 기본 재질을 얻는다. 원시 도형을 새로 만들지 않아도 된다.
            Mesh cubeMesh;
            Material wallBoxMaterial;
            FindCollisionBoxMesh(levelRoot, out cubeMesh, out wallBoxMaterial);

            bool floorDressed = DressFloor(grid);
            bool wallsDressed = DressWalls(grid, levelBuilder.Map.wallHeight);
            if (wallsDressed)
            {
                DressCorners(grid, levelBuilder.Map.wallHeight);
                Material capMaterial = wallCapMaterial != null ? wallCapMaterial : wallBoxMaterial;
                DressWallCaps(grid, levelBuilder.Map.wallHeight, cubeMesh, capMaterial);
            }

            CombineBatches();

            // 모델이 비어 입히지 못한 쪽은 판정 상자를 켜 둔다. 끄면 그 자리가 허공이 된다.
            HideCollisionRenderers(levelRoot, floorDressed, wallsDressed);

            _dressedGrid = grid;
        }

        /// <summary>입힌 옷을 지운다. 판정 상자의 렌더러는 되돌리지 않는다 — 곧 다시 입히거나 레벨째 지워진다.</summary>
        public void Undress()
        {
            if (_root != null)
            {
                for (int i = _root.childCount - 1; i >= 0; i--)
                {
                    Discard(_root.GetChild(i).gameObject);
                }
            }

            // 합친 메시는 이 컴포넌트가 만든 것이라 오브젝트를 지워도 남는다. 직접 지워야 새지 않는다.
            for (int i = 0; i < _ownedMeshes.Count; i++)
            {
                if (_ownedMeshes[i] != null) Discard(_ownedMeshes[i]);
            }

            _ownedMeshes.Clear();
            _batches.Clear();
            _dressedGrid = null;
        }

        private void EnsureRoot(Transform levelRoot)
        {
            if (_root == null) _root = new GameObject("StructureDressing").transform;

            // LevelBuilder는 레벨 루트의 로컬 좌표로 상자를 놓는다. 루트가 옮겨져 있어도
            // 옷이 상자와 겹치도록 같은 자세를 따라간다. 자식으로 두지 않는 것은 Clear() 때문이다.
            _root.SetPositionAndRotation(levelRoot.position, levelRoot.rotation);
            _root.localScale = levelRoot.lossyScale;
        }

        private bool DressFloor(GridMap grid)
        {
            float cell = grid.CellSize;
            bool any = false;

            for (int row = 0; row < grid.Rows; row++)
            {
                for (int col = 0; col < grid.Cols; col++)
                {
                    CellType type = grid.At(col, row);
                    if (type == CellType.Wall) continue;
                    if (!floorUnderCover && IsCover(type)) continue;

                    bool accent = Random01(Hash(col, row, SaltFloorAccent)) < floorAccentChance;
                    Piece piece = accent ? PickPiece(floorAccentTiles, Hash(col, row, SaltFloorPick)) : null;
                    if (piece == null) piece = PickPiece(floorTiles, Hash(col, row, SaltFloorPick));
                    if (piece == null) continue;

                    Vector3 size = piece.bounds.size;
                    var scale = new Vector3(
                        SafeDivide(cell, size.x),
                        floorThicknessScale,
                        SafeDivide(cell, size.z));

                    // 칸은 정사각형이라 90도씩 돌려도 자리가 그대로다. 같은 타일이 줄지어 반복돼 보이는 것을 깬다.
                    float yaw = 90f * (Hash(col, row, SaltFloorTurn) % 4u);
                    float bottom = floorTopHeight - size.y * scale.y;
                    AddPiece(piece, grid.CellCenter(col, row), bottom, yaw, scale);
                    any = true;
                }
            }

            return any;
        }

        private bool DressWalls(GridMap grid, float wallHeight)
        {
            float cell = grid.CellSize;
            bool any = false;

            for (int row = 0; row < grid.Rows; row++)
            {
                for (int col = 0; col < grid.Cols; col++)
                {
                    if (grid.At(col, row) != CellType.Wall) continue;

                    for (int d = 0; d < FaceDirections.Length; d++)
                    {
                        Vector2Int dir = FaceDirections[d];
                        if (!IsOpen(grid, col + dir.x, row + dir.y)) continue;

                        // 면마다 따로 굴린다. 같은 벽 칸의 두 면이 늘 같은 판이면 모서리에서 티가 난다.
                        int salt = d * 101;
                        bool accent = Random01(Hash(col, row, SaltWallAccent + salt)) < wallAccentChance;
                        Piece piece = accent ? PickPiece(wallAccentPanels, Hash(col, row, SaltWallPick + salt)) : null;
                        if (piece == null) piece = PickPiece(wallPanels, Hash(col, row, SaltWallPick + salt));
                        if (piece == null) continue;

                        Vector3 size = piece.bounds.size;
                        var scale = new Vector3(
                            SafeDivide(cell, size.x),
                            SafeDivide(wallHeight, size.y),
                            panelThicknessScale);

                        // 판은 벽 칸 안쪽에 붙인다. 앞면이 칸 경계와 딱 맞아야 보이는 벽과 부딪히는 벽이 같다.
                        var outward = new Vector3(dir.x, 0f, dir.y);
                        float thickness = size.z * scale.z;
                        Vector3 center = grid.CellCenter(col, row) + outward * (cell * 0.5f - thickness * 0.5f);

                        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + faceYawOffset;
                        AddPiece(piece, center, 0f, yaw, scale);
                        any = true;
                    }
                }
            }

            return any;
        }

        /// <summary>
        /// 격자 꼭짓점마다 둘러싼 네 칸을 보고 판이 꺾이는 자리에만 모서리 조각을 세운다.
        /// 곧게 이어지는 벽(벽 둘이 한쪽에 나란한 경우)에는 세우지 않는다.
        /// </summary>
        private void DressCorners(GridMap grid, float wallHeight)
        {
            Piece outer = GetPiece(outerCorner);
            Piece inner = GetPiece(innerCorner);
            if (outer == null) outer = inner;
            if (inner == null) inner = outer;
            if (outer == null) return;

            float cell = grid.CellSize;
            float inset = Mathf.Max(0f, cornerSize * 0.5f - cornerProtrusion);

            for (int vr = 0; vr <= grid.Rows; vr++)
            {
                for (int vc = 0; vc <= grid.Cols; vc++)
                {
                    var vertex = new Vector3(vc * cell, 0f, vr * cell);

                    int wallCount = 0;
                    for (int q = 0; q < 4; q++)
                    {
                        if (QuadrantIsWall(grid, vc, vr, q)) wallCount++;
                    }

                    if (wallCount == 0 || wallCount == 4) continue;

                    for (int q = 0; q < 4; q++)
                    {
                        int prev = (q + 3) % 4;
                        int next = (q + 1) % 4;
                        Vector2Int quadrant = CornerQuadrants[q];
                        var toQuadrant = new Vector3(quadrant.x, 0f, quadrant.y);

                        if (QuadrantIsWall(grid, vc, vr, q)
                            && QuadrantIsOpen(grid, vc, vr, prev)
                            && QuadrantIsOpen(grid, vc, vr, next))
                        {
                            // 튀어나온 모서리: 벽 칸 쪽으로 넣되 판 앞면보다 조금 나오게.
                            PlaceCorner(outer, vertex + toQuadrant * inset, q, wallHeight);
                        }
                        else if (wallCount == 3
                                 && QuadrantIsOpen(grid, vc, vr, q)
                                 && QuadrantIsWall(grid, vc, vr, prev)
                                 && QuadrantIsWall(grid, vc, vr, next))
                        {
                            // 들어간 모서리: 빈 칸의 맞은편 벽 칸 쪽으로 넣는다.
                            PlaceCorner(inner, vertex - toQuadrant * inset, q, wallHeight);
                        }
                    }
                }
            }
        }

        private void PlaceCorner(Piece piece, Vector3 center, int quadrant, float wallHeight)
        {
            Vector3 size = piece.bounds.size;
            var scale = new Vector3(
                SafeDivide(cornerSize, size.x),
                SafeDivide(wallHeight, size.y),
                SafeDivide(cornerSize, size.z));
            AddPiece(piece, center, 0f, 90f * quadrant + cornerYawOffset, scale);
        }

        /// <summary>
        /// 벽 칸 윗면을 덮는다. 판은 면에만 서 있어서 쿼터뷰로 내려다보면 판 사이로 벽 속이 비어 보인다.
        /// LevelBuilder처럼 한 행에서 이어진 벽 칸은 뚜껑 하나로 합친다.
        /// </summary>
        private void DressWallCaps(GridMap grid, float wallHeight, Mesh cubeMesh, Material material)
        {
            if (cubeMesh == null || material == null || wallCapThickness <= 0f) return;

            float cell = grid.CellSize;
            for (int row = 0; row < grid.Rows; row++)
            {
                int col = 0;
                while (col < grid.Cols)
                {
                    if (grid.At(col, row) != CellType.Wall)
                    {
                        col++;
                        continue;
                    }

                    int end = col;
                    while (end + 1 < grid.Cols && grid.At(end + 1, row) == CellType.Wall) end++;

                    // 판 윗단과 같은 높이에서 시작하면 둘이 z 싸움을 한다. 조금 내려 판 윗단을 먹게 한다.
                    var center = new Vector3(
                        (col + end + 1) * 0.5f * cell,
                        wallHeight + wallCapThickness * 0.5f - 0.01f,
                        (row + 0.5f) * cell);
                    var size = new Vector3((end - col + 1) * cell, wallCapThickness, cell);

                    AddInstance(material, cubeMesh, 0, Matrix4x4.TRS(center, Quaternion.identity, size));
                    col = end + 1;
                }
            }
        }

        /// <summary>
        /// 모델을 놓는다. 모델의 피벗이 어디든 잰 크기의 가운데가 center(xz)에, 밑면이 bottomY에 오게 한다.
        /// 피벗을 믿지 않는 것은 키트마다 피벗 자리가 조금씩 달라서다.
        /// </summary>
        private void AddPiece(Piece piece, Vector3 center, float bottomY, float yaw, Vector3 scale)
        {
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 position = center - rotation * Vector3.Scale(piece.bounds.center, scale);
            position.y = bottomY - piece.bounds.min.y * scale.y;

            Matrix4x4 placement = Matrix4x4.TRS(position, rotation, scale);
            for (int i = 0; i < piece.parts.Count; i++)
            {
                Part part = piece.parts[i];
                AddInstance(part.material, part.mesh, part.subMesh, placement * part.local);
            }
        }

        private void AddInstance(Material material, Mesh mesh, int subMesh, Matrix4x4 matrix)
        {
            List<CombineInstance> list;
            if (!_batches.TryGetValue(material, out list))
            {
                list = new List<CombineInstance>(256);
                _batches.Add(material, list);
            }

            list.Add(new CombineInstance { mesh = mesh, subMeshIndex = subMesh, transform = matrix });
        }

        /// <summary>
        /// 재질마다 메시 하나로 합친다. 바닥 타일만 600장이 넘고, 런타임에 만든 것은 정적 배칭이
        /// 저절로 먹지 않아 그대로 두면 그리기 호출이 타일 수만큼 나간다.
        /// </summary>
        private void CombineBatches()
        {
            foreach (KeyValuePair<Material, List<CombineInstance>> batch in _batches)
            {
                if (batch.Value.Count == 0) continue;

                var mesh = new Mesh();
                mesh.name = "StructureDressing_" + batch.Key.name;

                // 판과 타일을 다 합치면 정점이 65535를 쉽게 넘는다.
                mesh.indexFormat = IndexFormat.UInt32;
                mesh.CombineMeshes(batch.Value.ToArray(), true, true, false);
                mesh.RecalculateBounds();
                _ownedMeshes.Add(mesh);

                var go = new GameObject(mesh.name);
                go.transform.SetParent(_root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;

                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = batch.Key;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }

            _batches.Clear();
        }

        /// <summary>
        /// 판정용 바닥과 벽 상자의 그림만 끈다. 콜라이더와 레이어는 그대로라 시야, 총알, 내브메시,
        /// 엄폐 판정이 한 치도 달라지지 않는다. 엄폐 상자는 소품 쪽이 맡으니 건드리지 않는다.
        /// </summary>
        private static void HideCollisionRenderers(Transform levelRoot, bool floor, bool walls)
        {
            int floorLayer = LayerMask.NameToLayer(LevelBuilder.FloorLayer);
            int wallLayer = LayerMask.NameToLayer(LevelBuilder.WallLayer);

            for (int i = 0; i < levelRoot.childCount; i++)
            {
                GameObject child = levelRoot.GetChild(i).gameObject;
                bool hide = (floor && floorLayer >= 0 && child.layer == floorLayer)
                            || (walls && wallLayer >= 0 && child.layer == wallLayer);
                if (!hide) continue;

                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = false;
            }
        }

        /// 판정 벽 상자는 CreatePrimitive 상자라 그 메시를 뚜껑에 빌려 쓴다.
        private static void FindCollisionBoxMesh(Transform levelRoot, out Mesh mesh, out Material material)
        {
            mesh = null;
            material = null;

            int wallLayer = LayerMask.NameToLayer(LevelBuilder.WallLayer);
            if (wallLayer < 0) return;

            for (int i = 0; i < levelRoot.childCount; i++)
            {
                Transform child = levelRoot.GetChild(i);
                if (child.gameObject.layer != wallLayer) continue;

                MeshFilter filter = child.GetComponent<MeshFilter>();
                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                if (filter == null || filter.sharedMesh == null || renderer == null) continue;

                mesh = filter.sharedMesh;
                material = renderer.sharedMaterial;
                return;
            }
        }

        private Piece PickPiece(GameObject[] set, uint hash)
        {
            if (set == null || set.Length == 0) return null;

            // 빈 칸이 끼어 있어도 다음 것으로 넘어가 자리가 비지 않게 한다.
            int start = (int)(hash % (uint)set.Length);
            for (int i = 0; i < set.Length; i++)
            {
                Piece piece = GetPiece(set[(start + i) % set.Length]);
                if (piece != null) return piece;
            }

            return null;
        }

        /// <summary>
        /// 모델을 조각과 크기로 풀어 둔다. 인스턴스를 만들지 않고 행렬만 쌓아 합치므로
        /// 수백 개의 게임 오브젝트를 만들었다 지우는 일이 없다.
        /// </summary>
        private Piece GetPiece(GameObject prefab)
        {
            if (prefab == null) return null;

            Piece cached;
            if (_pieces.TryGetValue(prefab, out cached)) return cached;

            var piece = new Piece();

            // 루트의 위치만 뺀다. FBX 루트에 축 보정 회전이 들어 있을 수 있어 회전과 배율은 살린다.
            Matrix4x4 toModel = Matrix4x4.Translate(-prefab.transform.position);
            bool hasBounds = false;

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int f = 0; f < filters.Length; f++)
            {
                Mesh mesh = filters[f].sharedMesh;
                MeshRenderer renderer = filters[f].GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null) continue;

                Matrix4x4 local = toModel * filters[f].transform.localToWorldMatrix;
                Material[] materials = renderer.sharedMaterials;
                if (materials.Length == 0) continue;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material material = materials[Mathf.Min(s, materials.Length - 1)];
                    if (material == null) continue;
                    piece.parts.Add(new Part { mesh = mesh, subMesh = s, material = material, local = local });
                }

                Bounds b = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0 ? b.min.x : b.max.x,
                        (corner & 2) == 0 ? b.min.y : b.max.y,
                        (corner & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 p = local.MultiplyPoint3x4(point);
                    if (!hasBounds)
                    {
                        piece.bounds = new Bounds(p, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        piece.bounds.Encapsulate(p);
                    }
                }
            }

            if (piece.parts.Count == 0 || !hasBounds)
            {
                Debug.LogWarning("StructureDresser: " + prefab.name + "에서 그릴 메시를 찾지 못했다.", this);
                piece = null;
            }

            _pieces.Add(prefab, piece);
            return piece;
        }

        private bool IsOpen(GridMap grid, int col, int row)
        {
            if (!grid.Inside(col, row)) return false;

            CellType type = grid.At(col, row);
            if (type == CellType.Wall) return false;
            return faceCoverCells || !IsCover(type);
        }

        /// 꼭짓점 (vc, vr)을 둘러싼 q번째 칸. 격자 밖은 GridMap이 벽으로 돌려준다.
        private static Vector2Int QuadrantCell(int vc, int vr, int q)
        {
            Vector2Int quadrant = CornerQuadrants[q];
            return new Vector2Int(quadrant.x < 0 ? vc - 1 : vc, quadrant.y < 0 ? vr - 1 : vr);
        }

        private static bool QuadrantIsWall(GridMap grid, int vc, int vr, int q)
        {
            Vector2Int cell = QuadrantCell(vc, vr, q);
            return grid.At(cell.x, cell.y) == CellType.Wall;
        }

        private bool QuadrantIsOpen(GridMap grid, int vc, int vr, int q)
        {
            Vector2Int cell = QuadrantCell(vc, vr, q);
            return IsOpen(grid, cell.x, cell.y);
        }

        private static bool IsCover(CellType type)
        {
            return type == CellType.HighCover || type == CellType.LowCover;
        }

        /// <summary>
        /// 칸 좌표로 정해지는 난수. System.Random을 쓰면 뽑는 순서에 결과가 묶여서
        /// 한 칸의 규칙만 바꿔도 뒤따르는 칸이 전부 바뀐다. 좌표에서 바로 섞으면 칸끼리 독립이다.
        /// </summary>
        private uint Hash(int col, int row, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h = Mix(h + (uint)col * 0x85EBCA77u);
                h = Mix(h + (uint)row * 0xC2B2AE3Du);
                h = Mix(h + (uint)salt * 0x27D4EB2Fu);
                return h;
            }
        }

        private static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        private static float Random01(uint hash)
        {
            return (hash & 0xFFFFFFu) / 16777216f;
        }

        private static float SafeDivide(float value, float by)
        {
            return by > 0.0001f ? value / by : 1f;
        }

        private static void Discard(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
