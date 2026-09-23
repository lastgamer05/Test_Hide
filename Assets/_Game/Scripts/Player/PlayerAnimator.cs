using UnityEngine;
using ByAWhisker.Combat;

namespace ByAWhisker.Player
{
    /// <summary>
    /// 이동과 자세와 총을 애니메이터 파라미터로 옮긴다.
    /// 판정은 이미 다른 곳에서 끝나 있고 여기서는 보여 주기만 한다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerStance stance;
        [SerializeField] private Weapon weapon;
        [SerializeField] private Damageable damageable;

        [Tooltip("속도가 이 속도를 따라 부드럽게 붙는다. 클수록 빨리 반응한다.")]
        [SerializeField] private float speedLerp = 12f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int CrouchId = Animator.StringToHash("Crouch");
        private static readonly int FireId = Animator.StringToHash("Fire");
        private static readonly int DownId = Animator.StringToHash("Down");

        private float _speed;

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (motor == null) motor = GetComponentInParent<PlayerMotor>();
            if (stance == null) stance = GetComponentInParent<PlayerStance>();
            if (weapon == null) weapon = GetComponentInParent<Weapon>();
            if (damageable == null) damageable = GetComponentInParent<Damageable>();
        }

        private void OnEnable()
        {
            if (weapon != null) weapon.Fired += OnFired;
            if (damageable != null) damageable.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (weapon != null) weapon.Fired -= OnFired;
            if (damageable != null) damageable.Damaged -= OnDamaged;
        }

        private void Update()
        {
            if (animator == null) return;

            // 실제 속도를 그대로 넣으면 블렌드가 덜덜 떨린다. 한 박자 늦게 따라가게 한다.
            float target = motor != null ? motor.CurrentSpeed : 0f;
            _speed = Mathf.Lerp(_speed, target, 1f - Mathf.Exp(-speedLerp * Time.deltaTime));

            animator.SetFloat(SpeedId, _speed);
            animator.SetBool(CrouchId, stance != null && stance.IsCrouching);
        }

        private void OnFired(Vector3 hitPoint)
        {
            animator.SetTrigger(FireId);
        }

        private void OnDamaged(DamageInfo info)
        {
            animator.SetTrigger(DownId);
        }
    }
}
