using UnityEngine;
using ByAWhisker.Cameras;
using ByAWhisker.Level;

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
        [SerializeField] private bool movePlayerToStart = true;
        [Tooltip("에디터 창이 뒤에 있어도 플레이 모드가 계속 돌게 한다. 자동 확인에 필요하다.")]
        [SerializeField] private bool runInBackground = true;

        public GridMap Grid { get { return levelBuilder != null ? levelBuilder.Grid : null; } }

        private void Awake()
        {
            Application.runInBackground = runInBackground;

            if (levelBuilder != null && levelBuilder.Grid == null) levelBuilder.Build();

            if (movePlayerToStart && player != null && levelBuilder != null)
            {
                MovePlayer(levelBuilder.PlayerStartPosition);
            }

            if (viewCamera != null && player != null)
            {
                viewCamera.SetTarget(player);
            }
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
