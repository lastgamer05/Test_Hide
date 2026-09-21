using UnityEngine;
using ByAWhisker.Data;

namespace ByAWhisker.Player
{
    /// <summary>
    /// 서기와 앉기. 캡슐 높이와 눈높이를 바꾼다.
    /// 눈높이는 M3의 시야 계산이 그대로 가져간다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerStance : MonoBehaviour
    {
        public enum Stance
        {
            Standing,
            Crouching
        }

        [SerializeField] private MovementSettings settings;
        [Tooltip("보이는 캡슐. 자세에 따라 세로로 눌린다.")]
        [SerializeField] private Transform model;

        private CharacterController _controller;

        public Stance Current { get; private set; }
        public bool IsCrouching { get { return Current == Stance.Crouching; } }
        public float EyeHeight { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            Current = Stance.Standing;
            EyeHeight = settings != null ? settings.standEyeHeight : 1.55f;
            ApplyImmediate();
        }

        public void Toggle()
        {
            Set(IsCrouching ? Stance.Standing : Stance.Crouching);
        }

        public void Set(Stance stance)
        {
            Current = stance;
        }

        private void Update()
        {
            if (settings == null) return;

            float targetHeight = IsCrouching ? settings.crouchHeight : settings.standHeight;
            float targetEye = IsCrouching ? settings.crouchEyeHeight : settings.standEyeHeight;
            float t = 1f - Mathf.Exp(-settings.stanceLerp * Time.deltaTime);

            float height = Mathf.Lerp(_controller.height, targetHeight, t);
            SetControllerHeight(height);
            EyeHeight = Mathf.Lerp(EyeHeight, targetEye, t);
        }

        private void ApplyImmediate()
        {
            if (settings == null) return;
            SetControllerHeight(settings.standHeight);
        }

        private void SetControllerHeight(float height)
        {
            _controller.height = height;
            _controller.center = new Vector3(0f, height * 0.5f, 0f);

            if (model == null) return;
            Vector3 scale = model.localScale;
            // 기본 캡슐 메시는 높이가 2m라서 절반으로 나눈다.
            scale.y = height * 0.5f;
            model.localScale = scale;
            model.localPosition = new Vector3(0f, height * 0.5f, 0f);
        }
    }
}
