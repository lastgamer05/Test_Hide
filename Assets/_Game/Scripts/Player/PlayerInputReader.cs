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
        private InputAction _fire;
        private InputAction _reload;
        private InputAction _takedown;
        private InputAction _carry;
        private InputAction _focus;

        /// <summary>앉기 전환을 눌렀다.</summary>
        public event Action CrouchToggled;

        /// <summary>카메라 회전 요청. -1은 왼쪽, +1은 오른쪽.</summary>
        public event Action<int> CameraRotateRequested;

        /// <summary>재장전을 눌렀다.</summary>
        public event Action ReloadRequested;

        /// <summary>제압을 눌렀다.</summary>
        public event Action TakedownRequested;

        /// <summary>시체 들기와 내려놓기를 눌렀다. 같은 키가 두 가지를 다 맡아서 어느 쪽인지는 받는 쪽이 정한다.</summary>
        public event Action CarryRequested;

        /// <summary>사격은 누르고 있는 동안 계속 쏠 수 있어야 해서 이벤트가 아니라 상태로 준다.</summary>
        public bool FireHeld { get { return _fire != null && _fire.IsPressed(); } }

        /// <summary>집중은 누르고 있는 동안만 이어진다. 사격과 같은 이유로 이벤트가 아니라 상태다.</summary>
        public bool FocusHeld { get { return _focus != null && _focus.IsPressed(); } }

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
            _fire = _map.FindAction("Fire", true);
            _reload = _map.FindAction("Reload", true);
            _takedown = _map.FindAction("Takedown", true);
            _carry = _map.FindAction("Carry", true);
            _focus = _map.FindAction("Focus", true);
        }

        private void OnEnable()
        {
            if (_map == null) return;

            _crouch.performed += OnCrouch;
            _rotateLeft.performed += OnRotateLeft;
            _rotateRight.performed += OnRotateRight;
            _reload.performed += OnReload;
            _takedown.performed += OnTakedown;
            _carry.performed += OnCarry;
            _map.Enable();
        }

        private void OnDisable()
        {
            if (_map == null) return;

            _crouch.performed -= OnCrouch;
            _rotateLeft.performed -= OnRotateLeft;
            _rotateRight.performed -= OnRotateRight;
            _reload.performed -= OnReload;
            _takedown.performed -= OnTakedown;
            _carry.performed -= OnCarry;
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

        private void OnReload(InputAction.CallbackContext context)
        {
            if (ReloadRequested != null) ReloadRequested();
        }

        private void OnTakedown(InputAction.CallbackContext context)
        {
            if (TakedownRequested != null) TakedownRequested();
        }

        private void OnCarry(InputAction.CallbackContext context)
        {
            if (CarryRequested != null) CarryRequested();
        }
    }
}
