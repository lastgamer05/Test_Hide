using UnityEngine;

namespace ByAWhisker.Data
{
    /// <summary>플레이어 이동과 자세의 튜닝 값. 코드에 숫자를 박지 않는다.</summary>
    [CreateAssetMenu(menuName = "By a Whisker/Movement Settings", fileName = "MovementSettings")]
    public class MovementSettings : ScriptableObject
    {
        [Header("속도 (m/s)")]
        public float walkSpeed = 3.6f;
        public float runSpeed = 6.4f;
        public float crouchSpeed = 2f;
        public float acceleration = 18f;

        [Header("회전")]
        [Tooltip("마우스 쪽으로 도는 속도")]
        public float turnSpeed = 14f;

        [Header("자세 (m)")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.15f;
        public float standEyeHeight = 1.55f;
        public float crouchEyeHeight = 1f;
        [Tooltip("서기와 앉기 사이를 오가는 속도")]
        public float stanceLerp = 10f;

        [Header("소리 크기. M4에서 쓴다")]
        public float crouchNoise = 1.6f;
        public float walkNoise = 4.8f;
        public float runNoise = 11f;
    }
}
