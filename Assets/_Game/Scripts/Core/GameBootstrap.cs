using UnityEngine;
using Unity.AI.Navigation;
using ByAWhisker.AI;
using ByAWhisker.Cameras;
using ByAWhisker.Level;
using ByAWhisker.Player;

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
