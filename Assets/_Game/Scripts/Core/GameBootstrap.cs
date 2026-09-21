using UnityEngine;
using Unity.AI.Navigation;
using ByAWhisker.AI;
using ByAWhisker.Cameras;
using ByAWhisker.Level;
using ByAWhisker.Player;
using ByAWhisker.Visibility;

namespace ByAWhisker.Core
{
    /// <summary>
    /// 씬의 조각들을 연결한다. 전역 싱글턴을 두지 않고 여기서만 참조를 나눈다.
    /// 나중에 적 배치와 체크포인트도 이 자리에 붙는다.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private LevelBuilder levelBuilder;
        [SerializeField] private Transform player;
        [SerializeField] private QuarterViewCamera viewCamera;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private PlayerExposure playerExposure;
        [Tooltip("레벨이 런타임에 만들어지므로 NavMesh도 시작할 때 굽는다.")]
        [SerializeField] private NavMeshSurface navMeshSurface;
        [SerializeField] private PlayerVision playerVision;
        [Tooltip("시야 마스크를 그리는 쪽. 레벨 크기를 시작할 때 알려준다.")]
        [SerializeField] private VisibilityMaskRenderer maskRenderer;
        [Tooltip("냄새 격자. 이쪽도 레벨 크기를 받아야 한다.")]
        [SerializeField] private ByAWhisker.Senses.ScentField scentField;
        [Tooltip("냄새 격자 한 칸의 크기(m). 레벨 격자와 무관하게 정한다.")]
        [SerializeField] private float scentCellSize = 1f;
        [Tooltip("감각 표시. 소음, 빛, 소리 방향, 냄새 점을 띄운다.")]
        [SerializeField] private ByAWhisker.UI.SenseHud senseHud;
        [SerializeField] private bool movePlayerToStart = true;
        [Tooltip("에디터 창이 뒤에 있어도 플레이 모드가 계속 돌게 한다. 자동 확인에 필요하다.")]
        [SerializeField] private bool runInBackground = true;

        public GridMap Grid { get { return levelBuilder != null ? levelBuilder.Grid : null; } }

        private void Awake()
        {
            Application.runInBackground = runInBackground;

            if (levelBuilder != null && levelBuilder.Grid == null) levelBuilder.Build();

            if (navMeshSurface != null) navMeshSurface.BuildNavMesh();

            if (movePlayerToStart && player != null && levelBuilder != null)
            {
                MovePlayer(levelBuilder.PlayerStartPosition);
            }

            if (viewCamera != null && player != null)
            {
                viewCamera.SetTarget(player);
            }

            BindLevelObjects();
            BindGuards();
            BindVision();
        }

        private void OnEnable()
        {
            GameEvents.RunReset += OnRunReset;
        }

        private void OnDisable()
        {
            GameEvents.RunReset -= OnRunReset;
        }

        /// <summary>다시 시작하면 지형을 본 기억과 남은 소리, 남은 냄새도 지운다.</summary>
        private void OnRunReset()
        {
            if (maskRenderer != null) maskRenderer.ClearMemory();
            if (scentField != null) scentField.Clear();
            ByAWhisker.Senses.NoiseBus.Clear();
        }

        /// <summary>
        /// 마스크를 그릴 범위를 레벨 크기로 맞추고, 적에게 플레이어 시야를 알려준다.
        /// 레벨은 원점에서 +X, +Z 방향으로 자라므로 최소점이 원점이다.
        /// </summary>
        private void BindVision()
        {
            if (playerVision == null && player != null) playerVision = player.GetComponent<PlayerVision>();
            if (playerVision == null) return;

            EnemyVisibility[] enemies = FindObjectsByType<EnemyVisibility>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++) enemies[i].Bind(playerVision);

            if (playerExposure != null && scentField != null) playerExposure.SetScentField(scentField);

            GridMap grid = Grid;
            if (grid == null) return;

            Vector3 size = grid.WorldSize;
            // 레벨 밖으로 조금 넉넉히 잡는다. 가장자리 벽이 마스크에서 잘리지 않게.
            const float margin = 4f;
            Vector3 worldMin = new Vector3(-margin, 0f, -margin);
            Vector3 worldSize = new Vector3(size.x + margin * 2f, 6f, size.z + margin * 2f);

            if (maskRenderer != null) maskRenderer.Configure(worldMin, worldSize);
            if (scentField != null) scentField.Configure(worldMin, worldSize, scentCellSize);

            BindHud();
        }

        /// <summary>감각 표시는 격자가 잡힌 뒤에 연결한다. 그래야 첫 프레임부터 냄새 점이 제자리에 뜬다.</summary>
        private void BindHud()
        {
            if (senseHud == null || player == null) return;

            Camera cam = viewCamera != null ? viewCamera.GetComponent<Camera>() : Camera.main;
            senseHud.Bind(player, cam);
            senseHud.SetExposure(playerExposure);
            senseHud.SetScentField(scentField);
        }

        /// <summary>
        /// 경비에게 추적 대상을 알려준다. NavMesh는 방금 구웠으므로 에이전트를 제자리에 다시 올린다.
        /// </summary>
        private void BindGuards()
        {
            if (player == null) return;

            if (playerExposure == null) playerExposure = player.GetComponent<PlayerExposure>();

            GuardPerception[] guards = FindObjectsByType<GuardPerception>(FindObjectsSortMode.None);
            for (int i = 0; i < guards.Length; i++)
            {
                guards[i].Bind(player, playerExposure);

                GuardMotor motor = guards[i].GetComponent<GuardMotor>();
                if (motor != null) motor.Warp(guards[i].transform.position, guards[i].transform.rotation);

                // 감각과 사격이 같은 대상을 봐야 한다. 다르면 엉뚱한 곳을 겨누고 쏜다.
                GuardGunner gunner = guards[i].GetComponent<GuardGunner>();
                if (gunner != null) gunner.Bind(player);
            }

            if (guards.Length > 0) Debug.Log("경비 " + guards.Length + "명 연결됨");
        }

        /// <summary>레벨이 만들어 낸 체크포인트와 출구에 게임 매니저를 연결한다.</summary>
        private void BindLevelObjects()
        {
            if (levelBuilder == null || gameManager == null) return;

            Transform root = levelBuilder.LevelRoot;

            Checkpoint[] checkpoints = root.GetComponentsInChildren<Checkpoint>(true);
            for (int i = 0; i < checkpoints.Length; i++) checkpoints[i].Bind(gameManager);

            ExitZone[] exits = root.GetComponentsInChildren<ExitZone>(true);
            for (int i = 0; i < exits.Length; i++) exits[i].Bind(gameManager);
        }

        /// <summary>CharacterController는 켜진 상태로 위치를 바꾸면 무시한다. 잠깐 끄고 옮긴다.</summary>
        private void MovePlayer(Vector3 position)
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            player.position = position;

            if (controller != null) controller.enabled = true;
        }
    }
}
