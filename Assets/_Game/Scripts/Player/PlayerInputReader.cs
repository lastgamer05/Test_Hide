using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ByAWhisker.Player
{
    /// <summary>
    /// 입력 액션을 읽어 의도로 바꾼다.
    /// 이 클래스만 입력 시스템을 알고, 나머지는 여기서 값을 받아 쓴다.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private string mapName = "Player";

        private InputActionMap _map;
        private InputAction _move;
        private InputAction _run;
        private InputAction _crouch;
        private InputAction _point;
        private InputAction _rotateLeft;
        private InputAction _rotateRight;

        /// <summary>앉기 전환을 눌렀다.</summary>
        public event Action CrouchToggled;

        /// <summary>카메라 회전 요청. -1은 왼쪽, +1은 오른쪽.</summary>
        public event Action<int> CameraRotateRequested;

        public Vector2 Move { get { return _move != null ? _move.ReadValue<Vector2>() : Vector2.zero; } }
        public bool RunHeld { get { return _run != null && _run.IsPressed(); } }
        public Vector2 PointerPosition { get { return _point != null ? _point.ReadValue<Vector2>() : Vector2.zero; } }

        private void Awake()
        {
            if (actions == null)
            {
                Debug.LogError("PlayerInputReader: 입력 액션 에셋이 비어 있다.", this);
                return;
            }

            _map = actions.FindActionMap(mapName, true);
            _move = _map.FindAction("Move", true);
            _run = _map.FindAction("Run", true);
            _crouch = _map.FindAction("Crouch", true);
            _point = _map.FindAction("Point", true);
            _rotateLeft = _map.FindAction("RotateLeft", true);
            _rotateRight = _map.FindAction("RotateRight", true);
        }

        private void OnEnable()
        {
            if (_map == null) return;

            _crouch.performed += OnCrouch;
            _rotateLeft.performed += OnRotateLeft;
            _rotateRight.performed += OnRotateRight;
            _map.Enable();
        }

        private void OnDisable()
        {
            if (_map == null) return;

            _crouch.performed -= OnCrouch;
            _rotateLeft.performed -= OnRotateLeft;
            _rotateRight.performed -= OnRotateRight;
            _map.Disable();
        }

        private void OnCrouch(InputAction.CallbackContext context)
        {
            if (CrouchToggled != null) CrouchToggled();
        }

        private void OnRotateLeft(InputAction.CallbackContext context)
        {
            if (CameraRotateRequested != null) CameraRotateRequested(-1);
        }

        private void OnRotateRight(InputAction.CallbackContext context)
        {
            if (CameraRotateRequested != null) CameraRotateRequested(1);
        }
    }
}
