using UnityEngine;
using ByAWhisker.Perception;

namespace ByAWhisker.Player
{
    /// <summary>
    /// 플레이어가 지금 얼마나 들킬 만한 상태인지 한곳에 모아 둔다.
    /// 경비는 이 수치만 읽는다. 그래야 빛과 소리와 냄새를 나중에 바꿔도 AI를 건드리지 않는다.
    /// </summary>
    public class PlayerExposure : MonoBehaviour
    {
        [Tooltip("눈 위치와 소음을 읽어 올 이동 담당.")]
        [SerializeField] private PlayerMotor motor;

        [Tooltip("빛을 막는 레이어. Wall, HighCover, LowCover를 고른다.")]
        [SerializeField] private LayerMask sightBlockers;

        [Tooltip("밝기가 목표값을 따라가는 속도. 클수록 빨리 붙는다.")]
        [SerializeField] private float lightFollowSpeed = 6f;

        /// <summary>0..1. 램프 아래로 걸어 들어갈 때 값이 튀지 않게 부드럽게 따라간다.</summary>
        public float Light { get; private set; }

        /// <summary>지금 내는 소리. M4에서 소리 시스템으로 교체한다.</summary>
        public float Noise
        {
            get { return motor != null ? motor.NoiseLevel : 0f; }
        }

        /// <summary>M3 이후 ScentField가 채운다. 인간 경비는 후각이 없어서 지금은 쓰이지 않는다.</summary>
        public float Scent
        {
            get { return 0f; }
        }

        private void Awake()
        {
            if (motor == null) motor = GetComponent<PlayerMotor>();
        }

        private void Start()
        {
            // 첫 프레임부터 제대로 된 값을 주려고 보간 없이 한 번 맞춰 둔다.
            Light = SampleLight();
        }

        private void Update()
        {
            float target = SampleLight();

            // 프레임 레이트가 흔들려도 같은 속도로 붙도록 지수 보간을 쓴다.
            float t = 1f - Mathf.Exp(-lightFollowSpeed * Time.deltaTime);
            Light = Mathf.Lerp(Light, target, t);
        }

        private float SampleLight()
        {
            if (motor == null) return 0f;
            return Illumination.Sample(motor.EyePosition, sightBlockers);
        }
    }
}
