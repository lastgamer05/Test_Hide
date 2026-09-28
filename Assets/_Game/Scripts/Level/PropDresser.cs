using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.Level
{
    /// <summary>
    /// 엄폐물, 숨는 상자, 열쇠, 출구, 램프에 우주정거장 옷을 입힌다. StructureDresser의 짝이다.
    /// 판정은 LevelBuilder의 상자와 씬의 Container가 그대로 맡고 여기서는 보이는 것만 만든다.
    /// 입힌 것에는 콜라이더가 없다.
    ///
    /// 옷은 레벨 루트 밖의 자기 루트에 둔다. LevelBuilder.Build()가 Clear()로 레벨 루트의
    /// 자식을 전부 지우기 때문에, 그 밑에 두면 다시 지을 때 옷이 같이 사라진다.
    /// LevelBuilder에는 다 지었다는 이벤트가 없어서 격자 참조가 바뀌었는지로 다시 지어진 것을 안다.
    ///
    /// 엄폐물은 LevelBuilder가 가로로 이어진 칸을 상자 하나로 합쳐 두었지만 옷은 칸마다 따로 입힌다.
    /// 한 덩어리로 늘린 모델은 벽처럼 읽혀서 "무더기 뒤에 숨는다"는 느낌이 나지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PropDresser : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("입힐 레벨. 비우면 같은 오브젝트에서, 그래도 없으면 씬에서 찾는다.")]
        [SerializeField] private LevelBuilder levelBuilder;

        [Header("높은 엄폐 H")]
        [Tooltip("칸 하나를 통째로 채우는 모델. 예: structure-barrier-high, container-tall.")]
        [SerializeField] private GameObject[] highSolidModels;

        [Tooltip("칸을 넷으로 나눠 쌓는 모델. 예: container-tall, container-wide.")]
        [SerializeField] private GameObject[] highPileModels;

        [Tooltip("통째로 채울 확률. 나머지는 무더기로 쌓는다.")]
        [Range(0f, 1f)]
        [SerializeField] private float highSolidChance = 0.35f;

        [Tooltip("무더기 한 줄에 몇 개를 쌓는가.")]
        [Min(1)]
        [SerializeField] private int highPileLayers = 2;

        [Tooltip("무더기 줄마다 윗면을 이만큼(비율)까지 낮춘다. 3m 윗면은 눈높이보다 한참 위라 조금 낮춰도 판정과 어긋나 보이지 않는다.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float highTopJitter = 0.12f;

        [Header("낮은 엄폐 L")]
        [Tooltip("칸 하나를 통째로 채우는 모델. 예: structure-barrier.")]
        [SerializeField] private GameObject[] lowSolidModels;

        [Tooltip("칸을 넷으로 나눠 쌓는 모델. 예: container, container-flat.")]
        [SerializeField] private GameObject[] lowPileModels;

        [Tooltip("통째로 채울 확률. 나머지는 무더기로 쌓는다.")]
        [Range(0f, 1f)]
        [SerializeField] private float lowSolidChance = 0.4f;

        [Tooltip("무더기 한 줄에 몇 개를 쌓는가.")]
        [Min(1)]
        [SerializeField] private int lowPileLayers = 2;

        [Tooltip("무더기 줄마다 윗면을 이만큼(비율)까지 낮춘다. 낮은 엄폐는 앉은 늑대(1.45m)를 가리는 선이라 " +
                 "많이 낮추면 가려지는데 안 가려 보인다. 1.5m에서 0.03이면 1.455m까지만 내려간다.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float lowTopJitter = 0.03f;

        [Header("칸 채우기")]
        [Tooltip("옷이 칸에서 차지하는 비율. 1을 넘기면 판정 상자 밖으로 삐져나와 걸을 수 있는 곳을 막은 것처럼 보인다.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float cellFill = 0.94f;

        [Tooltip("무더기 조각이 제 몫(칸의 1/4)에서 차지하는 비율. 틈이 조금 있어야 무더기로 읽힌다.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float pileFill = 0.9f;

        [Tooltip("무더기 위층을 층마다 이만큼(비율)씩 좁힌다. 똑같은 기둥이 서 있는 것보다 쌓은 것처럼 보인다.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float pileTaper = 0.08f;

        [Header("숨는 상자")]
        [Tooltip("Container가 붙은 높은 엄폐 칸을 채울 몸통. 예: container-wide.")]
        [SerializeField] private GameObject[] hideModels;

        [Tooltip("몸통 위에 얹을 열린 상자. 쿼터뷰에서 위가 보이니 뚜껑이 열린 것이 \"들어갈 수 있다\"는 표시가 된다. 예: container-flat-open.")]
        [SerializeField] private GameObject hideTopModel;

        [Tooltip("열린 상자의 높이(m). 몸통과 합쳐 높은 엄폐 높이가 되게 몸통을 줄인다.")]
        [SerializeField] private float hideTopHeight = 0.8f;

        [Tooltip("열린 상자가 몸통 윗면에서 차지하는 비율.")]
        [Range(0.3f, 1f)]
        [SerializeField] private float hideTopFill = 0.85f;

        [Header("열쇠 K")]
        [Tooltip("열쇠 자리의 소품. 주우면 통째로 사라진다. 예: table-display, computer.")]
        [SerializeField] private GameObject[] keyModels;

        [Tooltip("소품의 가장 긴 가로 길이 상한(m). 키트 비율(kitScale)로 키운 것이 이보다 크면 이만큼으로 줄인다.")]
        [SerializeField] private float keyFootprint = 1.4f;

        [Header("출구 X")]
        [Tooltip("출구 자리의 문. 예: door-double, wall-door-wide.")]
        [SerializeField] private GameObject[] exitModels;

        [Tooltip("문 너비(m). 칸(2m)보다 조금 좁게 둬야 옆 칸 옷과 붙지 않는다.")]
        [SerializeField] private float exitWidth = 1.8f;

        [Tooltip("문 높이 상한(m). 너비에 맞춰 키운 문이 벽보다 높아지지 않게 한다.")]
        [SerializeField] private float exitMaxHeight = 3f;

        [Tooltip("문을 돌려 세울 방향을 정할 때 벽을 찾아볼 칸 수. 가장 가까운 벽과 나란히 세운다.")]
        [Min(1)]
        [SerializeField] private int exitWallScan = 8;

        [Header("램프 l")]
        [Tooltip("멀쩡할 때의 등 모양. 깨지면 꺼진다. 예: pipe-ring-colored.")]
        [SerializeField] private GameObject lampOnModel;

        [Tooltip("깨진 뒤의 등 모양. 비우면 깨졌을 때 아무것도 남지 않는다. 예: pipe-ring.")]
        [SerializeField] private GameObject lampOffModel;

        [Tooltip("멀쩡한 등 모양에 씌울 재질. 비우면 키트 재질 그대로다. 예전 전구 재질을 꽂으면 빛나 보인다.")]
        [SerializeField] private Material lampGlowMaterial;

        [Tooltip("등 모양의 가장 긴 변(m).")]
        [SerializeField] private float lampSize = 0.7f;

        [Tooltip("램프 오브젝트 자리에서 등 모양 한가운데까지(m). 램프는 천장 높이에 있다.")]
        [SerializeField] private Vector3 lampOffset = Vector3.zero;

        [Header("공통")]
        [Tooltip("키트 모델의 1이 우리 몇 m인가. 칸이 2m라 2다. 열쇠처럼 제 비율이 있는 소품은 이보다 키우지 않는다.")]
        [SerializeField] private float kitScale = 2f;

        [Tooltip("모델 고르기와 돌리기에 섞는 수. 바꾸면 모양이 통째로 달라지고, 그대로 두면 다시 지어도 같다.")]
        [SerializeField] private int seed = 1511;

        [Tooltip("움직이지 않는 옷(엄폐물, 숨는 상자, 출구)을 합쳐 그린다. 런타임에 만든 것이라 정적 배칭이 저절로 먹지 않는다.")]
        [SerializeField] private bool batchStatic = true;

        // 소금. 같은 칸에서 여러 가지를 고를 때 서로 다른 난수가 나오게 한다.
        private const int SaltHighSolid = 101;
        private const int SaltLowSolid = 201;
        private const int SaltSolidPick = 11;
        private const int SaltSolidTurn = 12;
        private const int SaltPileTop = 21;
        private const int SaltPilePick = 31;
        private const int SaltPileTurn = 51;
        private const int SaltHidePick = 301;
        private const int SaltHideTurn = 302;
        private const int SaltHideTopTurn = 303;
        private const int SaltKeyPick = 401;
        private const int SaltKeyTurn = 402;
        private const int SaltExitPick = 501;
        private const int SaltExitTurn = 502;
        private const int SaltLampTurn = 601;

        /// <summary>
        /// 램프 하나에 입힌 것. 깨졌는지를 따로 들고 있다가 달라졌을 때만 켜고 끈다.
        /// 매 프레임 SetActive를 부르지 않으려는 것이다.
        /// </summary>
        private sealed class LampDress
        {
            public Lamp lamp;
            public GameObject on;
            public GameObject off;
            public bool broken;
        }

        private Transform _root;
        private Transform _staticRoot;
        private Transform _dynamicRoot;
        private GridMap _dressedGrid;

        private GameObject _keyDress;
        private GameObject _keyTracked;

        private readonly List<LampDress> _lamps = new List<LampDress>(16);
        private readonly HashSet<int> _lampCells = new HashSet<int>();
        private readonly HashSet<int> _hideCells = new HashSet<int>();

        // 모델마다 한 번만 잰다. 같은 모델을 수백 번 놓으니 매번 메시를 훑으면 입히는 데 오래 걸린다.
        private readonly Dictionary<GameObject, Bounds> _measured = new Dictionary<GameObject, Bounds>();

        private readonly List<MeshRenderer> _rendererBuffer = new List<MeshRenderer>(8);
        private readonly List<Collider> _colliderBuffer = new List<Collider>(4);

        /// <summary>입힌 것이 들어간 루트. 레벨 루트 밖에 있다.</summary>
        public Transform DressingRoot { get { return _root; } }

        /// <summary>마지막으로 입힌 격자. LevelBuilder의 격자와 다르면 다시 지어진 것이다.</summary>
        public GridMap DressedGrid { get { return _dressedGrid; } }

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

            SyncLamps();
            SyncKey();
        }

        private void OnDestroy()
        {
            Undress();
            if (_root != null) Discard(_root.gameObject);
            _root = null;
            _staticRoot = null;
            _dynamicRoot = null;
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
            LevelMapAsset map = levelBuilder.Map;
            Transform levelRoot = levelBuilder.LevelRoot;
            EnsureRoot(levelRoot);

            DressLevelObjects(levelRoot, grid);

            // 숨는 상자가 먼저 칸을 차지해야 한다. 그 칸은 엄폐 무더기 대신 상자로 입힌다.
            DressHideContainers(grid, map.highCoverHeight);

            bool highDressed = DressCover(grid, CellType.HighCover, highSolidModels, highPileModels,
                highSolidChance, highPileLayers, highTopJitter, map.highCoverHeight, SaltHighSolid);

            // 낮은 엄폐는 지도 높이를 넘기지 않는다. 그 높이가 앉은 늑대를 가리는 선이라
            // 옷이 더 높으면 "안 숨었는데 숨은 것처럼", 더 낮으면 그 반대로 보인다.
            bool lowDressed = DressCover(grid, CellType.LowCover, lowSolidModels, lowPileModels,
                lowSolidChance, lowPileLayers, lowTopJitter, map.lowCoverHeight, SaltLowSolid);

            // 모델이 비어 입히지 못한 쪽은 판정 상자를 켜 둔다. 끄면 그 자리가 허공이 된다.
            HideCoverRenderers(levelRoot, highDressed, lowDressed);

            // 엄폐를 상자 그대로 보이게 두더라도 숨는 상자 칸만은 가린다. 안 가리면 열린 상자 옷이
            // 판정 상자 속에 묻혀, 숨을 수 있는 자리가 다른 기둥과 똑같아 보인다.
            if (!highDressed) HideHideCellBoxes(levelRoot, grid);

            // 편집 모드에서 합치면 합친 메시가 씬에 남는다. 플레이 중에만 합친다.
            if (batchStatic && Application.isPlaying && _staticRoot.childCount > 0)
            {
                StaticBatchingUtility.Combine(_staticRoot.gameObject);
            }

            _dressedGrid = grid;
            SyncKey();
        }

        /// <summary>입힌 옷을 지운다. 판정 상자의 렌더러는 되돌리지 않는다 — 곧 다시 입히거나 레벨째 지워진다.</summary>
        public void Undress()
        {
            DiscardChildren(_staticRoot);
            DiscardChildren(_dynamicRoot);

            _lamps.Clear();
            _lampCells.Clear();
            _hideCells.Clear();
            _keyDress = null;
            _keyTracked = null;
            _dressedGrid = null;
        }

        private void EnsureRoot(Transform levelRoot)
        {
            if (_root == null)
            {
                _root = new GameObject("PropDressing").transform;

                // 합쳐 그릴 것과 켜고 끌 것을 나눠 둔다. 정적 배칭으로 합친 것은 SetActive로 끄면
                // 합친 메시 안의 제 몫만 빠지긴 하지만, 합치는 쪽에서 아예 빼 두는 편이 헷갈리지 않는다.
                _staticRoot = new GameObject("Static").transform;
                _staticRoot.SetParent(_root, false);
                _dynamicRoot = new GameObject("Dynamic").transform;
                _dynamicRoot.SetParent(_root, false);
            }

            // LevelBuilder는 레벨 루트의 로컬 좌표로 상자를 놓는다. 루트가 옮겨져 있어도
            // 옷이 상자와 겹치도록 같은 자세를 따라간다. 자식으로 두지 않는 것은 Clear() 때문이다.
            _root.SetPositionAndRotation(levelRoot.position, levelRoot.rotation);
            _root.localScale = levelRoot.lossyScale;
        }

        /// <summary>
        /// 레벨 루트 밑에서 열쇠, 출구, 램프를 찾아 입히고 그 겉모습을 끈다.
        /// 뒤에서부터 도는 이유: Build()가 같은 프레임에 다시 불리면 Destroy()로 지운 옛 자식이
        /// 프레임 끝까지 남아 있다. 새로 만든 것은 그 뒤에 붙으니 뒤에서부터 보면 새것을 먼저 만난다.
        /// 옛것의 렌더러를 끄는 것은 어차피 사라질 것이라 해가 없다.
        /// </summary>
        private void DressLevelObjects(Transform levelRoot, GridMap grid)
        {
            bool lampReady = lampOnModel != null;
            bool keyReady = HasAny(keyModels);
            bool exitReady = HasAny(exitModels);
            bool keyDone = false;
            bool exitDone = false;

            for (int i = levelRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = levelRoot.GetChild(i);

                Lamp lamp = child.GetComponent<Lamp>();
                if (lamp != null)
                {
                    if (!lampReady) continue;

                    // 전구 렌더러만 끈다. Light, LightSource, 맞을 기둥은 판정이라 손대지 않는다.
                    HideRenderers(child);
                    DressLamp(lamp, grid);
                    continue;
                }

                ObjectiveItem item = child.GetComponent<ObjectiveItem>();
                if (item != null)
                {
                    if (!keyReady) continue;

                    GameObject visual = HideRenderers(child);
                    if (!keyDone) DressKey(item, visual, grid);
                    keyDone = true;
                    continue;
                }

                ExitZone exit = child.GetComponent<ExitZone>();
                if (exit != null)
                {
                    if (!exitReady) continue;

                    HideRenderers(child);
                    if (!exitDone) DressExit(exit, grid);
                    exitDone = true;
                }
            }
        }

        private void DressLamp(Lamp lamp, GridMap grid)
        {
            Vector3 local = _root.InverseTransformPoint(lamp.transform.position);
            int col = grid.ColOf(local.x);
            int row = grid.RowOf(local.z);

            // 같은 칸에 옛 램프와 새 램프가 함께 있으면 먼저 만난 새것만 입힌다.
            if (!_lampCells.Add(CellIndex(grid, col, row))) return;

            Vector3 center = local + lampOffset;
            int turns = (int)(Hash(col, row, SaltLampTurn) & 3u);

            var dress = new LampDress();
            dress.lamp = lamp;
            dress.on = PlaceSized(lampOnModel, _dynamicRoot, center, turns, lampSize, 0.5f);

            if (lampGlowMaterial != null) ApplyMaterial(dress.on, lampGlowMaterial);
            if (lampOffModel != null) dress.off = PlaceSized(lampOffModel, _dynamicRoot, center, turns, lampSize, 0.5f);

            dress.broken = lamp.IsBroken;
            ApplyLamp(dress);
            _lamps.Add(dress);
        }

        /// <summary>
        /// 열쇠 소품. 판정용 열쇠는 제자리에서 돌지만 소품은 바닥에 가만히 둔다.
        /// 주우면 ObjectiveItem이 겉모습(Visual)을 끄는데, 그 켜짐을 그대로 따라가게 붙잡아 둔다.
        /// 겉모습이 없으면 ObjectiveItem은 오브젝트 전체를 끄니 그때는 열쇠 자체를 따라간다.
        /// </summary>
        private void DressKey(ObjectiveItem item, GameObject visual, GridMap grid)
        {
            Vector3 local = _root.InverseTransformPoint(item.transform.position);
            int col = grid.ColOf(local.x);
            int row = grid.RowOf(local.z);

            GameObject model = Pick(keyModels, Hash(col, row, SaltKeyPick));
            if (model == null) return;

            int turns = (int)(Hash(col, row, SaltKeyTurn) & 3u);
            Bounds bounds = Measure(model);
            float longest = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = Mathf.Min(kitScale, keyFootprint / longest);

            var floor = new Vector3(local.x, 0f, local.z);
            _keyDress = Place(model, _dynamicRoot, floor, turns, Vector3.one * scale, 0f);
            _keyTracked = visual != null ? visual : item.gameObject;
        }

        /// <summary>출구 문. 가장 가까운 벽과 나란히 세워, 벽에 난 문처럼 읽히게 한다.</summary>
        private void DressExit(ExitZone exit, GridMap grid)
        {
            Vector3 local = _root.InverseTransformPoint(exit.transform.position);
            int col = grid.ColOf(local.x);
            int row = grid.RowOf(local.z);

            GameObject model = Pick(exitModels, Hash(col, row, SaltExitPick));
            if (model == null) return;

            // 키트의 문은 가로가 x, 두께가 z다. 돌리는 방향에 따라 문짝이 벽과 나란해진다.
            Bounds bounds = Measure(model);
            float scale = Mathf.Min(exitWidth / bounds.size.x, exitMaxHeight / bounds.size.y);
            int turns = ExitTurns(grid, col, row);

            var floor = new Vector3(local.x, 0f, local.z);
            Place(model, _staticRoot, floor, turns, Vector3.one * scale, 0f);
        }

        /// <summary>
        /// 가장 가까운 벽이 x쪽이면 문을 90도 돌려 문짝을 그 벽과 나란히 한다.
        /// 벽이 안 보이면 칸 좌표로 정한다. 어느 쪽이든 다시 지어도 같다.
        /// </summary>
        private int ExitTurns(GridMap grid, int col, int row)
        {
            for (int d = 1; d <= exitWallScan; d++)
            {
                if (grid.At(col + d, row) == CellType.Wall || grid.At(col - d, row) == CellType.Wall) return 1;
                if (grid.At(col, row + d) == CellType.Wall || grid.At(col, row - d) == CellType.Wall) return 0;
            }

            return (int)(Hash(col, row, SaltExitTurn) & 1u);
        }

        /// <summary>
        /// 램프가 깨졌는지를 매 프레임 본다. 불 하나 읽고 달라졌을 때만 켜고 끄니 할당이 없다.
        /// RunReset을 따로 듣지 않는 이유: 같은 사건을 듣는 Lamp.Restore와 누가 먼저 불릴지 정해져 있지 않아,
        /// 먼저 불리면 아직 깨진 상태를 읽는다. 다음 프레임에 여기서 읽으면 순서와 상관없이 맞는다.
        /// </summary>
        private void SyncLamps()
        {
            for (int i = 0; i < _lamps.Count; i++)
            {
                LampDress dress = _lamps[i];
                if (dress.lamp == null) continue;

                bool broken = dress.lamp.IsBroken;
                if (broken == dress.broken) continue;

                dress.broken = broken;
                ApplyLamp(dress);
            }
        }

        private static void ApplyLamp(LampDress dress)
        {
            if (dress.on != null) dress.on.SetActive(!dress.broken);
            if (dress.off != null) dress.off.SetActive(dress.broken);
        }

        /// <summary>
        /// 열쇠 소품을 판정용 열쇠의 겉모습에 맞춰 켜고 끈다. ObjectiveItem은 주웠다는 사건을
        /// 밖으로 알리지만 "다시 보이게 됐다"는 알리지 않으니, 켜짐 자체를 따라가는 편이 어느 쪽이든 맞는다.
        /// </summary>
        private void SyncKey()
        {
            if (_keyDress == null) return;

            bool show = _keyTracked != null && _keyTracked.activeInHierarchy;
            if (_keyDress.activeSelf != show) _keyDress.SetActive(show);
        }

        /// <summary>
        /// Container가 붙은 자리를 눈에 띄는 상자로 입힌다. Container는 레벨 루트 밖(씬의 Containers)에 있어서
        /// 다시 지어져도 남는다. 그 옆의 높은 엄폐 칸 하나를 차지해 무더기 대신 열린 상자를 세운다.
        /// </summary>
        private void DressHideContainers(GridMap grid, float height)
        {
            if (!HasAny(hideModels)) return;

            // 입히는 순간에만 도는 찾기라 여기 생기는 배열은 매 프레임 할당이 아니다.
            Container[] containers = FindObjectsByType<Container>(FindObjectsSortMode.None);
            float footprint = grid.CellSize * cellFill;

            for (int i = 0; i < containers.Length; i++)
            {
                Vector3 local = _root.InverseTransformPoint(containers[i].transform.position);

                Vector2Int cell;
                Vector3 floor;
                if (TryFindHighCoverCell(grid, local, out cell))
                {
                    // 상자 둘이 한 칸을 가리키면 한 번만 입힌다.
                    if (!_hideCells.Add(CellIndex(grid, cell.x, cell.y))) continue;
                    floor = grid.CellCenter(cell.x, cell.y);
                }
                else
                {
                    // 엄폐물 곁이 아닌 곳에 둔 상자도 보여야 숨을 수 있다는 것을 안다. 그 자리 바닥에 세운다.
                    cell = new Vector2Int(grid.ColOf(local.x), grid.RowOf(local.z));
                    floor = new Vector3(local.x, 0f, local.z);
                }

                DressHide(floor, footprint, height, cell.x, cell.y);
            }
        }

        private void DressHide(Vector3 floor, float footprint, float height, int col, int row)
        {
            GameObject body = Pick(hideModels, Hash(col, row, SaltHidePick));
            if (body == null) return;

            // 몸통과 열린 상자를 합쳐 높은 엄폐 높이에 맞춘다. 판정은 3m 벽인데 옷이 낮으면 넘겨다볼 수 있어 보인다.
            float topHeight = hideTopModel != null ? Mathf.Clamp(hideTopHeight, 0f, height * 0.5f) : 0f;
            float bodyHeight = height - topHeight;

            int turns = (int)(Hash(col, row, SaltHideTurn) & 3u);
            PlaceFitted(body, _staticRoot, floor, turns, footprint, footprint, bodyHeight);

            if (hideTopModel == null || topHeight <= 0f) return;

            float topFootprint = footprint * hideTopFill;
            int topTurns = (int)(Hash(col, row, SaltHideTopTurn) & 3u);
            PlaceFitted(hideTopModel, _staticRoot, floor + Vector3.up * bodyHeight, topTurns, topFootprint, topFootprint, topHeight);
        }

        /// <summary>
        /// 상자가 붙은 높은 엄폐 칸을 찾는다. 씬에 손으로 놓은 상자라 칸 한가운데가 아니라 모서리에 걸쳐 있기도 해서,
        /// 제 칸이 아니면 둘레 여덟 칸에서 가장 가까운 것을 고른다.
        /// </summary>
        private static bool TryFindHighCoverCell(GridMap grid, Vector3 local, out Vector2Int cell)
        {
            int col = grid.ColOf(local.x);
            int row = grid.RowOf(local.z);

            if (grid.Inside(col, row) && grid.At(col, row) == CellType.HighCover)
            {
                cell = new Vector2Int(col, row);
                return true;
            }

            bool found = false;
            float best = float.MaxValue;
            cell = default;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int c = col + dx;
                    int r = row + dz;
                    if (!grid.Inside(c, r) || grid.At(c, r) != CellType.HighCover) continue;

                    Vector3 center = grid.CellCenter(c, r);
                    float distance = (center.x - local.x) * (center.x - local.x) + (center.z - local.z) * (center.z - local.z);
                    if (distance >= best) continue;

                    best = distance;
                    cell = new Vector2Int(c, r);
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// 한 종류의 엄폐 칸을 모두 입힌다. 입힌 것이 있으면 true.
        /// 칸마다 통째로 채울지 무더기로 쌓을지를 칸 좌표의 난수로 고른다.
        /// </summary>
        private bool DressCover(GridMap grid, CellType type, GameObject[] solid, GameObject[] pile,
            float solidChance, int layers, float topJitter, float height, int salt)
        {
            bool hasSolid = HasAny(solid);
            bool hasPile = HasAny(pile);
            if (!hasSolid && !hasPile) return false;

            float footprint = grid.CellSize * cellFill;

            for (int row = 0; row < grid.Rows; row++)
            {
                for (int col = 0; col < grid.Cols; col++)
                {
                    if (grid.At(col, row) != type) continue;
                    if (type == CellType.HighCover && _hideCells.Contains(CellIndex(grid, col, row))) continue;

                    Vector3 center = grid.CellCenter(col, row);
                    bool useSolid = hasSolid && (!hasPile || Random01(Hash(col, row, salt)) < solidChance);

                    if (useSolid)
                    {
                        // 통째로 채우는 것은 윗면을 낮추지 않는다. 한 덩어리가 판정보다 낮으면 그대로 틈으로 보인다.
                        GameObject model = Pick(solid, Hash(col, row, salt + SaltSolidPick));
                        int turns = (int)(Hash(col, row, salt + SaltSolidTurn) & 3u);
                        PlaceFitted(model, _staticRoot, center, turns, footprint, footprint, height);
                    }
                    else
                    {
                        DressPile(pile, col, row, center, footprint, layers, topJitter, height, salt);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 칸을 2x2로 나눠 줄마다 여러 개를 쌓는다. 조각마다 모델과 방향이 달라 무더기처럼 보인다.
        /// 난수는 반 칸 좌표로 뽑아 이웃 칸과 겹치지 않게 한다.
        /// </summary>
        private void DressPile(GameObject[] pile, int col, int row, Vector3 center, float footprint,
            int layers, float topJitter, float height, int salt)
        {
            int count = Mathf.Max(1, layers);
            float quarter = footprint * 0.5f;

            for (int qz = 0; qz < 2; qz++)
            {
                for (int qx = 0; qx < 2; qx++)
                {
                    int subCol = col * 2 + qx;
                    int subRow = row * 2 + qz;
                    var subCenter = new Vector3(center.x + (qx - 0.5f) * quarter, 0f, center.z + (qz - 0.5f) * quarter);

                    // 낮추기만 하고 높이지는 않는다. 지도 높이가 판정의 끝이다.
                    float columnHeight = height * (1f - topJitter * Random01(Hash(subCol, subRow, salt + SaltPileTop)));
                    float pieceHeight = columnHeight / count;

                    for (int layer = 0; layer < count; layer++)
                    {
                        GameObject model = Pick(pile, Hash(subCol, subRow, salt + SaltPilePick + layer));
                        if (model == null) continue;

                        int turns = (int)(Hash(subCol, subRow, salt + SaltPileTurn + layer) & 3u);
                        float side = quarter * pileFill * Mathf.Max(0.1f, 1f - pileTaper * layer);
                        Vector3 floor = subCenter + Vector3.up * (pieceHeight * layer);
                        PlaceFitted(model, _staticRoot, floor, turns, side, side, pieceHeight);
                    }
                }
            }
        }

        /// <summary>
        /// 판정용 엄폐 상자의 렌더러만 끈다. 콜라이더와 레이어는 시야, 총알, 내브메시, 엄폐 판정이 쓰니 그대로 둔다.
        /// 레이어가 없는 프로젝트에서는 LevelBuilder가 기본 레이어로 만들므로 이름으로 가린다.
        /// </summary>
        private static void HideCoverRenderers(Transform levelRoot, bool high, bool low)
        {
            if (!high && !low) return;

            int highLayer = LayerMask.NameToLayer(LevelBuilder.HighCoverLayer);
            int lowLayer = LayerMask.NameToLayer(LevelBuilder.LowCoverLayer);

            for (int i = 0; i < levelRoot.childCount; i++)
            {
                Transform child = levelRoot.GetChild(i);
                bool hide = (high && IsCoverBox(child, highLayer, CellType.HighCover))
                            || (low && IsCoverBox(child, lowLayer, CellType.LowCover));
                if (!hide) continue;

                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = false;
            }
        }

        /// <summary>
        /// 숨는 상자가 선 칸의 높은 엄폐 상자 렌더러를 끈다. 한 칸짜리 상자만 끈다 —
        /// LevelBuilder는 가로로 이어진 칸을 한 상자로 합치므로, 여러 칸짜리를 끄면 옆 칸까지 허공이 된다.
        /// </summary>
        private void HideHideCellBoxes(Transform levelRoot, GridMap grid)
        {
            if (_hideCells.Count == 0) return;

            int highLayer = LayerMask.NameToLayer(LevelBuilder.HighCoverLayer);
            float single = grid.CellSize + 0.1f;

            for (int i = 0; i < levelRoot.childCount; i++)
            {
                Transform child = levelRoot.GetChild(i);
                if (!IsCoverBox(child, highLayer, CellType.HighCover)) continue;

                MeshRenderer renderer = child.GetComponent<MeshRenderer>();
                if (renderer == null) continue;

                Bounds b = renderer.bounds;
                if (b.size.x > single || b.size.z > single) continue;

                for (int row = 0; row < grid.Rows; row++)
                {
                    bool found = false;
                    for (int col = 0; col < grid.Cols; col++)
                    {
                        if (!_hideCells.Contains(CellIndex(grid, col, row))) continue;
                        Vector3 c = grid.CellCenter(col, row);
                        if (Mathf.Abs(c.x - b.center.x) < 0.5f && Mathf.Abs(c.z - b.center.z) < 0.5f)
                        {
                            renderer.enabled = false;
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }
            }
        }

        private static bool IsCoverBox(Transform child, int layer, CellType type)
        {
            if (layer >= 0) return child.gameObject.layer == layer;

            // LevelBuilder.CreateRun이 붙이는 이름: 종류 + "_r" + 행 + "_c" + 열.
            string name = child.name;
            string prefix = type.ToString();
            return name.Length > prefix.Length + 1
                   && name.StartsWith(prefix, System.StringComparison.Ordinal)
                   && name[prefix.Length] == '_';
        }

        /// <summary>
        /// 가로는 제 비율을 지킨 채 발자리 안에 들어가게, 높이는 정확히 맞춘다.
        /// 가로까지 늘리면 긴 상자가 정사각형으로 찌그러져 키트의 생김새가 사라진다.
        /// </summary>
        private GameObject PlaceFitted(GameObject model, Transform parent, Vector3 floor, int turns, float width, float depth, float height)
        {
            if (model == null) return null;

            Bounds bounds = Measure(model);
            bool odd = (turns & 1) != 0;
            float footX = odd ? bounds.size.z : bounds.size.x;
            float footZ = odd ? bounds.size.x : bounds.size.z;

            float horizontal = Mathf.Min(width / footX, depth / footZ);
            float vertical = height / bounds.size.y;
            return Place(model, parent, floor, turns, new Vector3(horizontal, vertical, horizontal), 0f);
        }

        /// <summary>가장 긴 변이 size가 되게 고르게 키운다. 램프처럼 제 비율이 곧 생김새인 것에 쓴다.</summary>
        private GameObject PlaceSized(GameObject model, Transform parent, Vector3 point, int turns, float size, float anchor)
        {
            if (model == null) return null;

            Bounds bounds = Measure(model);
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            return Place(model, parent, point, turns, Vector3.one * (size / longest), anchor);
        }

        /// <summary>
        /// 모델을 point에 놓는다. anchor는 모델 높이에서 point에 맞출 자리(0 바닥, 0.5 가운데)다.
        /// 모델을 빈 부모 밑에 그대로 넣고 부모를 돌리고 키운다. FBX 루트에 붙은 회전과 크기를
        /// 덮어쓰지 않으려는 것이다. 키트마다 피벗이 다르니(table-display는 가운데) 잰 경계로 맞춘다.
        /// </summary>
        private GameObject Place(GameObject model, Transform parent, Vector3 point, int turns, Vector3 scale, float anchor)
        {
            Bounds bounds = Measure(model);
            Quaternion rotation = Quaternion.Euler(0f, 90f * (turns & 3), 0f);
            var pivot = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * anchor, bounds.center.z);

            Transform holder = new GameObject(model.name).transform;
            holder.SetParent(parent, false);
            holder.localRotation = rotation;
            holder.localScale = scale;
            holder.localPosition = point - rotation * Vector3.Scale(scale, pivot);

            GameObject instance = Instantiate(model, holder, false);
            StripColliders(instance);
            return holder.gameObject;
        }

        /// <summary>
        /// 모델의 경계를 부모 공간에서 잰다. 인스턴스를 만들지 않고 에셋의 메시와 변환만 읽는다.
        /// 모델 루트의 로컬 변환까지 넣어야 부모 밑에 넣었을 때의 모습과 맞는다.
        /// </summary>
        private Bounds Measure(GameObject model)
        {
            Bounds cached;
            if (_measured.TryGetValue(model, out cached)) return cached;

            Transform root = model.transform;
            Matrix4x4 toHolder = Matrix4x4.TRS(root.localPosition, root.localRotation, root.localScale) * root.worldToLocalMatrix;

            // 모델마다 한 번 도는 수집이라 매 프레임 할당이 아니다.
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            bool found = false;
            var result = new Bounds();

            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null) continue;

                Matrix4x4 matrix = toHolder * filters[i].transform.localToWorldMatrix;
                Vector3 min = mesh.bounds.min;
                Vector3 max = mesh.bounds.max;

                for (int corner = 0; corner < 8; corner++)
                {
                    var local = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 point = matrix.MultiplyPoint3x4(local);

                    if (!found)
                    {
                        result = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        result.Encapsulate(point);
                    }
                }
            }

            // 메시가 없으면 키트 규격(바닥 가운데 피벗, 1x1x1)으로 본다. 0으로 나누지 않게 크기에 바닥을 둔다.
            if (!found) result = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
            Vector3 size = result.size;
            result.size = new Vector3(Mathf.Max(size.x, 0.001f), Mathf.Max(size.y, 0.001f), Mathf.Max(size.z, 0.001f));

            _measured[model] = result;
            return result;
        }

        /// <summary>임포트에서 콜라이더를 끄고 들여왔지만, 누가 모델을 바꿔 꽂아도 판정에 끼지 않게 한 번 더 뗀다.</summary>
        private void StripColliders(GameObject instance)
        {
            instance.GetComponentsInChildren(true, _colliderBuffer);
            for (int i = 0; i < _colliderBuffer.Count; i++)
            {
                // 지금 프레임에 물리가 보지 않게 바로 뗀다. 막 만든 인스턴스의 컴포넌트라 에셋을 건드리지 않는다.
                DestroyImmediate(_colliderBuffer[i]);
            }

            _colliderBuffer.Clear();
        }

        /// <summary>판정 오브젝트 밑의 MeshRenderer를 끄고, 끈 것 중 첫 번째의 오브젝트를 돌려준다.</summary>
        private GameObject HideRenderers(Transform target)
        {
            target.GetComponentsInChildren(true, _rendererBuffer);

            GameObject first = null;
            for (int i = 0; i < _rendererBuffer.Count; i++)
            {
                MeshRenderer renderer = _rendererBuffer[i];
                if (renderer == null) continue;

                renderer.enabled = false;
                if (first == null) first = renderer.gameObject;
            }

            _rendererBuffer.Clear();
            return first;
        }

        private static void ApplyMaterial(GameObject target, Material material)
        {
            if (target == null) return;

            // 입히는 순간에만 도는 일이라 배열 할당은 괜찮다.
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                for (int j = 0; j < materials.Length; j++) materials[j] = material;
                renderers[i].sharedMaterials = materials;
            }
        }

        private static int CellIndex(GridMap grid, int col, int row)
        {
            return row * grid.Cols + col;
        }

        private static bool HasAny(GameObject[] models)
        {
            if (models == null) return false;

            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] != null) return true;
            }

            return false;
        }

        /// <summary>빈 칸을 건너뛰고 고른다. 배열 중간이 비어 있어도 고르는 결과가 흔들리지 않게 한다.</summary>
        private static GameObject Pick(GameObject[] models, uint hash)
        {
            if (models == null) return null;

            int count = 0;
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] != null) count++;
            }

            if (count == 0) return null;

            int target = (int)(hash % (uint)count);
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] == null) continue;
                if (target == 0) return models[i];
                target--;
            }

            return null;
        }

        /// <summary>
        /// 칸 좌표와 소금으로 정해지는 난수. UnityEngine.Random을 쓰지 않는 이유는 다시 지을 때마다
        /// 모양이 바뀌지 않아야 하고, 다른 코드가 Random의 상태를 흔들어도 영향을 받지 않아야 해서다.
        /// </summary>
        private uint Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B9u;
                h ^= (uint)x * 0x85EBCA6Bu;
                h = (h << 13) | (h >> 19);
                h ^= (uint)y * 0xC2B2AE35u;
                h = (h << 17) | (h >> 15);
                h ^= (uint)salt * 0x27D4EB2Fu;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        private static float Random01(uint hash)
        {
            return (hash & 0xFFFFFFu) / 16777216f;
        }

        private static void DiscardChildren(Transform parent)
        {
            if (parent == null) return;

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);

                // 플레이 중에는 지우는 것이 프레임 끝으로 미뤄진다. 같은 프레임에 새 옷을 합쳐 그릴 때
                // 옛 조각까지 합치지 않게 먼저 떼어 낸다.
                if (Application.isPlaying) child.SetParent(null, false);
                Discard(child.gameObject);
            }
        }

        /// <summary>
        /// 지운다. 플레이 중의 Destroy()는 프레임 끝까지 미뤄지니 먼저 꺼서, 새로 입힌 것과
        /// 한 프레임 겹쳐 그려지지 않게 한다.
        /// </summary>
        private static void Discard(Object target)
        {
            if (target == null) return;

            if (Application.isPlaying)
            {
                GameObject go = target as GameObject;
                if (go != null) go.SetActive(false);
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
