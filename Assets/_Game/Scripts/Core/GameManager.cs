using System.Collections;
using UnityEngine;
using ByAWhisker.Level;

namespace ByAWhisker.Core
{
    /// <summary>
    /// 한 판의 흐름을 관리한다. 포획되면 마지막 체크포인트에서 다시 시작하고,
    /// 목표물을 들고 출구에 닿으면 승리로 끝낸다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public enum Phase
        {
            Playing,
            Captured,
            Won
        }

        [SerializeField] private Transform player;
        [SerializeField] private LevelBuilder levelBuilder;
        [Tooltip("포획된 뒤 다시 시작하기까지의 시간")]
        [SerializeField] private float respawnDelay = 1.5f;

        public Phase Current { get; private set; }
        public int CaptureCount { get; private set; }
        public bool HasObjective { get; private set; }
        public Vector3 CheckpointPosition { get; private set; }

        private void Awake()
        {
            Current = Phase.Playing;

            if (levelBuilder != null && levelBuilder.Grid == null) levelBuilder.Build();

            CheckpointPosition = levelBuilder != null
                ? levelBuilder.PlayerStartPosition
                : (player != null ? player.position : Vector3.zero);
        }

        private void OnEnable()
        {
            GameEvents.PlayerCaptured += OnPlayerCaptured;
            GameEvents.ObjectiveTaken += OnObjectiveTaken;
            GameEvents.PlayerEscaped += OnPlayerEscaped;
        }

        private void OnDisable()
        {
            GameEvents.PlayerCaptured -= OnPlayerCaptured;
            GameEvents.ObjectiveTaken -= OnObjectiveTaken;
            GameEvents.PlayerEscaped -= OnPlayerEscaped;
        }

        /// <summary>체크포인트를 지날 때 부른다.</summary>
        public void SetCheckpoint(Vector3 position)
        {
            CheckpointPosition = position;
        }

        private void OnPlayerCaptured(GameObject by)
        {
            if (Current != Phase.Playing) return;

            Current = Phase.Captured;
            CaptureCount++;
            Debug.Log("포획됨. 잡은 적: " + (by != null ? by.name : "알 수 없음") + ", 누적 " + CaptureCount + "회");

            StartCoroutine(RespawnAfterDelay());
        }

        private void OnObjectiveTaken()
        {
            HasObjective = true;
            Debug.Log("목표물 획득");
        }

        private void OnPlayerEscaped()
        {
            if (Current != Phase.Playing || !HasObjective) return;

            Current = Phase.Won;
            Debug.Log("탈출 성공. 포획 " + CaptureCount + "회");
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(respawnDelay);

            MovePlayer(CheckpointPosition);

            // 적도 제자리로 돌린다. 누가 듣는지는 여기서 모른다.
            GameEvents.RaiseRunReset();

            Current = Phase.Playing;
        }

        /// <summary>CharacterController는 켜진 채로 위치를 바꾸면 무시한다. 잠깐 끄고 옮긴다.</summary>
        private void MovePlayer(Vector3 position)
        {
            if (player == null) return;

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            player.position = position;

            if (controller != null) controller.enabled = true;
        }
    }
}
