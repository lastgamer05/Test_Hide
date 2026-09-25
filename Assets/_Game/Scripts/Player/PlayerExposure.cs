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

        [Tooltip("자세를 읽어 올 담당. 앉으면 낮은 엄폐 뒤로 몸이 숨는다.")]
        [SerializeField] private PlayerStance stance;

        [Tooltip("소리를 내는 쪽. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private ByAWhisker.Senses.NoiseEmitter emitter;

        [Tooltip("경비가 겨누는 몸통 높이. 서 있을 때와 앉았을 때.")]
        [SerializeField] private float standingBodyHeight = 1f;
        [SerializeField] private float crouchingBodyHeight = 0.55f;

        [Tooltip("빛을 막는 레이어. Wall, HighCover, LowCover를 고른다.")]
        [SerializeField] private LayerMask sightBlockers;

        [Tooltip("밝기가 목표값을 따라가는 속도. 클수록 빨리 붙는다.")]
        [SerializeField] private float lightFollowSpeed = 6f;

        [Header("총구 섬광")]
        [Tooltip("총을 쏜 순간 몸이 얼마나 환해지는가. 0..1. 어둠이 총을 공짜로 만들지 않게 하는 값이다.")]
        [Range(0f, 1f)]
        [SerializeField] private float muzzleFlashLight = 1f;
        [Tooltip("섬광이 사그라드는 시간(초). 이 사이에 경비 눈에 들면 들킨다.")]
        [SerializeField] private float muzzleFlashSeconds = 0.6f;

        [Header("냄새")]
        [Tooltip("내 냄새를 남기는 쪽. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private ByAWhisker.Senses.ScentSource scentSource;
        [Tooltip("냄새 격자. 통합 담당이 넣어 준다. 없으면 Scent가 0으로 남는다.")]
        [SerializeField] private ByAWhisker.Senses.ScentField scentField;
        [Tooltip("서서 걸을 때와 앉았을 때 남기는 냄새의 세기.")]
        [SerializeField] private float standingScent = 1f;
        [SerializeField] private float crouchingScent = 0.6f;

        /// <summary>0..1. 램프 아래로 걸어 들어갈 때 값이 튀지 않게 부드럽게 따라간다.</summary>
        public float Light { get; private set; }

        // 총구 섬광이 남긴 밝기. 램프가 만드는 밝기와 따로 두고, 읽을 때 둘 중 큰 값을 준다.
        private float _flashLeft;

        /// <summary>
        /// 지금 내는 소리. 0..1이다. 경비는 이 값이 아니라 NoiseBus의 사건을 듣고,
        /// 이 값은 화면 막대처럼 "내가 지금 얼마나 시끄러운가"를 보여 주는 쪽에서 쓴다.
        /// </summary>
        public float Noise
        {
            get { return emitter != null ? emitter.LastLoudness : 0f; }
        }

        /// <summary>
        /// 내가 선 자리에 남은 내 냄새의 짙기. 인간 경비는 후각이 없어서 아직 쓰지 않지만,
        /// 뒤에 나올 개 같은 적이 이 값을 읽는다.
        /// </summary>
        public float Scent { get; private set; }

        /// <summary>앉아 있는가. 경비의 시선 판정이 낮은 엄폐를 셈에 넣을지 가른다.</summary>
        public bool Crouching
        {
            get { return stance != null && stance.IsCrouching; }
        }

        /// <summary>
        /// 경비가 겨누는 몸통의 높이. 발밑도 머리끝도 아닌 가운데를 본다.
        /// 앉으면 낮아져서 낮은 엄폐 너머로 보이지 않는다.
        /// </summary>
        public float BodyHeight
        {
            get { return Crouching ? crouchingBodyHeight : standingBodyHeight; }
        }

        /// <summary>경비가 겨눌 점. 자세에 따라 높이가 달라지는 것을 한곳에서 정한다.</summary>
        public Vector3 BodyPoint
        {
            get { return transform.position + Vector3.up * BodyHeight; }
        }

        private void Awake()
        {
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (stance == null) stance = GetComponent<PlayerStance>();
            if (emitter == null) emitter = GetComponent<ByAWhisker.Senses.NoiseEmitter>();
            if (scentSource == null) scentSource = GetComponent<ByAWhisker.Senses.ScentSource>();
        }

        /// <summary>통합 담당이 시작할 때 냄새 격자를 물려 준다.</summary>
        public void SetScentField(ByAWhisker.Senses.ScentField field)
        {
            scentField = field;
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

            // 섬광은 보간하지 않는다. 터지는 순간 바로 환해져야 "쏜 자리가 드러난다"가 된다.
            if (_flashLeft > 0f)
            {
                _flashLeft -= Time.deltaTime;
                float fade = Mathf.Clamp01(_flashLeft / Mathf.Max(0.01f, muzzleFlashSeconds));
                Light = Mathf.Max(Light, muzzleFlashLight * fade);
            }

            UpdateScent();
        }

        /// <summary>
        /// 총을 쏜 순간 몸이 드러난다. 어둠 속 탐지 거리가 5m뿐이라 소음기 권총이
        /// 사거리 안에서 공짜가 되어 버렸다. 섬광이 그 공짜를 없앤다.
        /// </summary>
        public void FlashFromMuzzle()
        {
            _flashLeft = muzzleFlashSeconds;
            Light = Mathf.Max(Light, muzzleFlashLight);
        }

        /// <summary>앉으면 몸을 낮춰 냄새도 덜 퍼뜨린다. 남은 냄새는 격자에서 읽어 온다.</summary>
        private void UpdateScent()
        {
            if (scentSource != null)
            {
                scentSource.SetStrength(Crouching ? crouchingScent : standingScent);
            }

            Scent = scentField != null ? scentField.Sample(transform.position) : 0f;
        }

        private float SampleLight()
        {
            if (motor == null) return 0f;
            return Illumination.Sample(motor.EyePosition, sightBlockers);
        }
    }
}
