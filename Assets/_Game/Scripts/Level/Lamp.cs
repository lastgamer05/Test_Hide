using UnityEngine;
using ByAWhisker.Combat;
using ByAWhisker.Core;
using ByAWhisker.Perception;
using ByAWhisker.Senses;

namespace ByAWhisker.Level
{
    /// <summary>
    /// 램프 하나. 빛을 끄고 켜는 창구를 여기 하나로 모은다.
    /// 어둠을 만들 수 있어야 지도가 퍼즐이 되는데, 끄는 길이 여러 군데로 흩어지면
    /// "왜 여기가 어두운가"를 나중에 쫓을 수 없다.
    ///
    /// 총으로 깨는 길은 Damageable이 열어 준다. 같은 오브젝트에 Damageable이 있으면
    /// Damaged를 받아 스스로 깨진다. 없어도 컴포넌트는 돌아가고, 그때는 Break()를
    /// 밖에서 부르는 것으로만 꺼진다.
    /// </summary>
    [RequireComponent(typeof(LightSource))]
    [DisallowMultipleComponent]
    public class Lamp : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("껐다 켤 빛. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private LightSource source;

        [Tooltip("맞는 쪽. 비우면 같은 오브젝트에서 찾는다. 없으면 총으로 깰 수 없고 Break()만 남는다.")]
        [SerializeField] private Damageable damageable;

        [Header("깨지는 소리")]
        [Tooltip("유리 깨지는 소리 크기. 0..1. 총성보다는 작아도 발소리와 비교가 안 되게 커야 경비가 온다.")]
        [Range(0f, 1f)]
        [SerializeField] private float breakLoudness = 0.8f;

        [Tooltip("이 거리 안에서 들린다(m). 사람은 SenseProfile.hearingMultiplier만큼 줄여 들으니, 줄어든 뒤에도 옆 통로까지 닿게 넉넉히 둔다.")]
        [SerializeField] private float breakNoiseRadius = 20f;

        [Tooltip("소리를 낼 높이(m, 월드). 램프는 천장에 달려 있지만 소리 고리는 바닥에 그려야 자리가 맞는다.")]
        [SerializeField] private float noiseHeight = 0f;

        /// <summary>깨져서 꺼졌는가. 화면 표시와 디버그가 읽어 간다.</summary>
        public bool IsBroken { get; private set; }

        private void Awake()
        {
            if (source == null) source = GetComponent<LightSource>();
            if (damageable == null) damageable = GetComponent<Damageable>();

            // 구독을 OnEnable이 아니라 Awake에서 한다. Damageable은 쓰러질 때 자기 밑의 Behaviour를
            // 끄는데 이 컴포넌트도 거기 들 수 있고, 그러면 OnDisable에서 구독이 풀려 깨지는 순간의
            // Damaged를 놓치거나 RunReset을 못 받아 램프가 영영 꺼진 채로 남는다.
            // Damageable 자신과 GuardBrain이 같은 이유로 자기를 끄지 않는다.
            if (damageable != null) damageable.Damaged += OnDamaged;
            GameEvents.RunReset += Restore;
        }

        private void OnDestroy()
        {
            if (damageable != null) damageable.Damaged -= OnDamaged;
            GameEvents.RunReset -= Restore;
        }

        /// <summary>
        /// 깬다. 총알 말고 다른 손이 생겨도 이 하나만 부르면 된다.
        /// 두 번 불러도 소리는 한 번만 난다. 시체에 총을 더 쏘는 것과 같은 이유다.
        /// </summary>
        public void Break()
        {
            if (IsBroken) return;
            IsBroken = true;

            // 보이는 조명은 LightSource가 IsOn에 맞춰 같이 끈다. Light를 직접 만지면
            // 판정과 그림이 두 곳에서 갈려 "밝은데 안 보인다"를 쫓게 된다.
            if (source != null) source.IsOn = false;

            EmitBreakNoise();
        }

        /// <summary>
        /// 다시 켠다. 재시작에 불린다. 멀쩡한 램프에 불러도 안전하다.
        /// </summary>
        public void Restore()
        {
            IsBroken = false;

            // 빛을 먼저 올린다. 아래 Revive가 꺼 뒀던 LightSource를 다시 켤 때
            // OnEnable이 지금 값을 그대로 실제 조명에 옮기기 때문이다.
            if (source != null) source.IsOn = true;

            // 되살리지 않으면 깨진 램프가 다음 판에도 총알을 받지 않는다.
            // Revive는 껐던 것만 되돌리므로 두 번 불려도 해가 없다.
            if (damageable != null) damageable.Revive();
        }

        /// <summary>
        /// 맞았다. 종류를 가리지 않는다. 총알이든 뒤에서 내리친 것이든 유리는 깨지고,
        /// 가려 두면 손을 하나 더 붙일 때마다 여기를 다시 고쳐야 한다.
        /// </summary>
        private void OnDamaged(DamageInfo info)
        {
            Break();
        }

        /// <summary>
        /// 깨지는 소리. NoiseEmitter를 거치지 않고 직접 싣는다. 그쪽은 반경을 크기에서
        /// 계산하는데, 유리 소리는 발소리 통로의 그 비율에 묶이면 안 될 만큼 멀리 가야 한다.
        /// </summary>
        private void EmitBreakNoise()
        {
            float loudness = Mathf.Clamp01(breakLoudness);
            if (loudness <= 0f) return;

            NoiseEvent evt;
            evt.position = new Vector3(transform.position.x, noiseHeight, transform.position.z);
            evt.radius = Mathf.Max(0f, breakNoiseRadius);
            evt.loudness = loudness;

            // 있는 것 중에서 Object를 고른다. 램프가 깨지면 경비는 그 자리를 뒤지러 와야 하는데,
            // Gunshot으로 내면 GuardBrain이 "내가 사격받는 중"으로 읽어 쏜 사람도 없이 엄폐로 흩어지고
            // GuardPerception은 의심도를 단번에 끝까지 올려 찾아보기도 전에 발각된 것이 된다.
            // Footstep은 SenseHud와 NoiseRipple이 발소리 색으로 칠해 플레이어에게 거짓말이 된다.
            // Bump는 반응이 같지만 이름이 몸이 부딪히는 소리다. 깨진 유리는 물건이 낸 소리 쪽이 맞다.
            evt.kind = NoiseKind.Object;

            // 주인을 적어 둬야 듣는 쪽이 제 소리를 걸러 낼 수 있다. 램프는 듣지 않지만 규칙을 맞춘다.
            evt.source = gameObject;
            evt.time = Time.time;

            NoiseBus.Emit(evt);
        }
    }
}
