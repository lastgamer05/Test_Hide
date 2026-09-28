using System.Collections.Generic;
using UnityEngine;
using ByAWhisker.AI;
using ByAWhisker.Combat;
using ByAWhisker.Core;
using ByAWhisker.Player;
using ByAWhisker.Senses;
using ByAWhisker.Visibility;

namespace ByAWhisker.Level
{
    /// <summary>
    /// 숨을 수 있는 상자. 높은 엄폐물(HighCover)에 붙여 쓴다. 지도 글자를 새로 만들지 않는 이유는
    /// 어느 엄폐물이 열리는지를 씬에서 고르는 편이 지도를 다시 그리는 것보다 싸기 때문이다.
    ///
    /// 두 가지를 맡는다.
    /// 1) 들고 온 몸을 넣는다. 넣은 몸은 보이지 않고, 경비가 시체를 훑는 물리 질의에도 걸리지 않는다.
    /// 2) 플레이어가 들어가 숨는다. 안에 있으면 경비의 눈에서 아예 빠지고 소리도 내지 않는다.
    ///
    /// 경비가 상자를 열어 보는 일은 아직 없다. 숨으면 안전한 자리가 하나는 있어야 총을 쓴 판을
    /// 되돌릴 수 있기 때문이다. 나중에 여는 손을 붙일 수 있게, 무엇이 들었는지 묻고 꺼내는 문만 열어 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public class Container : MonoBehaviour
    {
        [Header("시체 넣기")]
        [Tooltip("몸을 몇 구까지 넣을 수 있는가. 상자 하나로 방 하나를 치울 수 있으면 총을 쓴 값이 너무 싸진다.")]
        [SerializeField] private int bodyCapacity = 2;

        [Tooltip("넣은 몸을 둘 자리. 상자 위치에서의 오프셋이다(m, 월드). 보이지 않으니 눈에 보이는 뜻은 없고, 냄새가 상자 자리에서 나게 하려고 옮긴다.")]
        [SerializeField] private Vector3 stashOffset = Vector3.zero;

        [Header("플레이어 숨기")]
        [Tooltip("안에 있는 동안 발소리 담당을 재워 소리를 아예 끊는다. 끄면 멈춰 선 만큼만 조용해진다.")]
        [SerializeField] private bool silenceWhileHidden = true;

        /// <summary>
        /// 어느 상자에 들어 있는지와 상관없이 "숨겨졌다"는 사실만 알아야 하는 쪽이 있다.
        /// 몸을 집는 쪽은 옆에 어느 상자가 있는지 모르는 채로 물어봐야 해서, 여기 한곳에 모아 둔다.
        /// 들고 있는 것은 인스턴스 ID뿐이라 씬이 바뀌어도 남는 참조가 없다.
        /// GuardPerception이 놀란 시체를 적어 두는 방식과 같다.
        /// </summary>
        private static readonly HashSet<int> StashedBodies = new HashSet<int>();

        /// <summary>
        /// 넣은 몸 하나. 되돌릴 것을 함께 들고 있어야 재시작에서 정확히 원래대로 돌아온다.
        /// 우리가 끈 것만 적어 둔다. 전부 되돌리면 쓰러질 때 Damageable이 꺼 둔 것까지 켜 버린다.
        /// </summary>
        private class Slot
        {
            public Damageable body;
            public Vector3 position;
            public EnemyVisibility visibility;
            public readonly List<Collider> colliders = new List<Collider>(8);
            public readonly List<Renderer> renderers = new List<Renderer>(8);
        }

        // 칸은 Awake에서 한 번만 만들고 비웠다 채워 쓴다. 넣고 꺼낼 때마다 새로 만들면 쓰레기가 쌓인다.
        private Slot[] _slots;

        private Transform _hidePlayer;
        private PlayerExposure _hideExposure;
        private NoiseEmitter _hideEmitter;
        private Vector3 _enterPosition;

        // 씬의 경비를 한 번 모아 두고 재사용한다. 레벨이 만들어진 뒤로 경비는 늘지 않는다.
        // GuardPerception이 동료를 모으는 방식과 같다.
        private GuardPerception[] _guards;

        /// <summary>안에 몸이 들어 있는가. 나중에 상자를 열어 보는 손이 이걸 읽는다.</summary>
        public bool HasBody { get { return BodyCount > 0; } }

        /// <summary>지금 플레이어가 안에 들어가 있는가.</summary>
        public bool PlayerInside { get; private set; }

        /// <summary>넣어 둔 몸의 수.</summary>
        public int BodyCount
        {
            get
            {
                if (_slots == null) return 0;

                int count = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].body != null) count++;
                }

                return count;
            }
        }

        /// <summary>한 구 더 들어갈 자리가 있는가. 넣기 전에 물어봐야 손에 든 몸을 헛되이 놓지 않는다.</summary>
        public bool HasRoom { get { return FreeSlot() != null; } }

        /// <summary>몸이든 사람이든 안에 무언가 있는가. 상자를 열어 보는 손이 생기면 이것부터 볼 것이다.</summary>
        public bool Occupied { get { return PlayerInside || HasBody; } }

        /// <summary>이 몸이 어느 상자에 숨겨져 있는가. 들 수 있는 몸을 고르는 쪽이 물어본다.</summary>
        public static bool IsStashed(Damageable body)
        {
            return body != null && StashedBodies.Contains(body.GetInstanceID());
        }

        private void Awake()
        {
            _slots = new Slot[Mathf.Max(1, bodyCapacity)];
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new Slot();
        }

        private void OnEnable()
        {
            GameEvents.RunReset += OnRunReset;
        }

        private void OnDisable()
        {
            GameEvents.RunReset -= OnRunReset;

            // 꺼진 상자가 경비의 눈과 플레이어의 발소리를 계속 붙잡고 있으면 영영 풀리지 않는다.
            if (PlayerInside) Exit(false);
            ReleaseBodies();
        }

        /// <summary>
        /// 몸을 넣는다. 들어가면 보이지 않고 경비의 물리 질의에도 걸리지 않는다.
        /// 자리가 없거나 산 몸이면 false다. 부르는 쪽은 실패했을 때 그 몸을 어딘가에 놓아 줘야 한다.
        /// </summary>
        public bool TryStash(Damageable body)
        {
            if (body == null) return false;

            // 넣을 수 있는 것의 기준은 경비가 시체로 보는 기준과 같게 둔다. 산 사람은 밀어 넣지 못한다.
            if (body.IsAlive && !body.IsDown) return false;

            // 안에 사람이 들어가 있으면 그 위로 몸을 얹지 않는다.
            if (PlayerInside) return false;

            Slot slot = FreeSlot();
            if (slot == null) return false;

            slot.body = body;
            slot.position = body.transform.position;

            // 부모를 바꾸지 않고 자리만 옮긴다. 높은 엄폐물은 크기를 늘려 만든 상자라, 그 밑에 매달면
            // 몸이 함께 늘어나고 꺼낼 때 원래 크기로 정확히 돌아오지 않는다.
            body.transform.position = transform.position + stashOffset;

            HideColliders(slot);
            HideRenderers(slot);

            StashedBodies.Add(body.GetInstanceID());

            // 자리와 콜라이더를 한꺼번에 바꿨으니 물리 쪽 사본을 지금 맞춘다. 다음 물리 프레임까지 두면
            // 경비가 아직 사라지지 않은 콜라이더를 한 번 더 잡는다. BodyCarry가 몸을 놓을 때와 같은 이유다.
            Physics.SyncTransforms();
            return true;
        }

        /// <summary>
        /// 들어가고 나온다. 돌려주는 값은 "부른 뒤에 안에 들어가 있는가"다.
        /// 못 들어간 것과 나온 것이 같은 false인 이유는, 부르는 쪽이 알아야 하는 것이
        /// "지금 상자 안인가" 하나뿐이기 때문이다.
        /// player는 들어갈 때만 쓴다. 나올 때는 들어온 사람을 이미 들고 있다.
        /// </summary>
        public bool ToggleHide(Transform player)
        {
            if (PlayerInside)
            {
                Exit(true);
                return false;
            }

            return Enter(player);
        }

        /// <summary>
        /// 넣어 둔 몸을 모두 꺼내 원래대로 돌린다. 재시작이 부르고, 나중에 상자를 열어 보는 손도 이걸 쓴다.
        /// 몸을 어디에 세울지는 여기서 정하지 않는다. 경비는 GuardBrain이 제자리로 warp시킨다.
        /// </summary>
        public void ReleaseBodies()
        {
            if (_slots == null) return;

            bool released = false;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].body != null) released = true;
                Release(_slots[i]);
            }

            // 여러 구를 한꺼번에 되돌려도 물리 쪽 사본은 한 번만 맞추면 된다.
            if (released) Physics.SyncTransforms();
        }

        private bool Enter(Transform player)
        {
            if (player == null) return false;

            _hidePlayer = player;
            _hideExposure = player.GetComponent<PlayerExposure>();

            // 나오는 자리는 들어간 자리다. 안에 있는 동안 무엇이 플레이어를 밀어도 여기 적어 둔 자리로 돌려놓는다.
            _enterPosition = player.position;

            BlindGuards(null, null);
            Silence(player);

            PlayerInside = true;
            return true;
        }

        /// <summary>
        /// 나온다. 재시작으로 꺼내는 길에서는 자리를 되돌리지 않는다. 그때 플레이어가 설 자리는
        /// 체크포인트가 이미 정해 놓았고(GameManager가 RunReset보다 먼저 옮긴다),
        /// 여기서 덮으면 되살아난 플레이어가 상자 앞으로 끌려온다.
        /// </summary>
        private void Exit(bool restorePosition)
        {
            PlayerInside = false;

            BlindGuards(_hidePlayer, _hideExposure);
            Unsilence();

            if (restorePosition && _hidePlayer != null) MovePlayer(_hidePlayer, _enterPosition);

            _hidePlayer = null;
            _hideExposure = null;
        }

        /// <summary>
        /// 경비의 눈에서 플레이어를 뗀다. GuardPerception은 Bind로 받아 둔 대상만 보고 느끼므로,
        /// 그 대상을 빼는 것이 판정을 끊는 가장 얕은 자리다. 시야와 인기척이 한 군데서 같이 끊긴다.
        /// 컴포넌트를 아예 끄면 귀와 시체 발견까지 멈춰서, 숨은 사이에 총성이 나도 아무도 듣지 않는다.
        /// 나올 때 같은 것을 다시 물려 준다.
        /// </summary>
        private void BlindGuards(Transform player, PlayerExposure exposure)
        {
            EnsureGuards();

            for (int i = 0; i < _guards.Length; i++)
            {
                if (_guards[i] != null) _guards[i].Bind(player, exposure);
            }
        }

        private void EnsureGuards()
        {
            if (_guards != null) return;
            _guards = FindObjectsByType<GuardPerception>(FindObjectsSortMode.None);
        }

        /// <summary>
        /// 안에서는 소리를 내지 않는다. 멈춰 서 있으면 발소리야 안 나지만, 0인 것과 "거의 0"인 것은
        /// 다르다. 인기척 판정이 소음을 배율로 쓰기 때문이다.
        /// 발소리 담당을 끄면 그쪽 OnDisable이 걸어 온 거리와 마지막 소리 크기까지 함께 지운다.
        /// </summary>
        private void Silence(Transform player)
        {
            if (!silenceWhileHidden) return;

            NoiseEmitter emitter = player.GetComponent<NoiseEmitter>();
            if (emitter == null || !emitter.enabled) return;

            emitter.enabled = false;
            _hideEmitter = emitter;
        }

        private void Unsilence()
        {
            if (_hideEmitter == null) return;

            // 다시 켜지는 순간 그쪽이 마지막 자리를 지금 자리로 맞추므로, 상자에서 나오는 걸음이
            // 한 발짝 소리로 터지지 않는다.
            _hideEmitter.enabled = true;
            _hideEmitter = null;
        }

        /// <summary>
        /// 들린 몸이 경비의 시체 질의에 잡히지 않게 콜라이더를 끈다.
        /// 쓰러질 때 Damageable은 콜라이더를 끄지 않고 트리거로만 바꿔 둔다. 경비가 물리로 찾을 수
        /// 있어야 했기 때문이다. 상자는 그 반대를 원하니 여기서 끈다.
        /// </summary>
        private static void HideColliders(Slot slot)
        {
            slot.colliders.Clear();

            // 넣는 순간에만 도는 수집이라 여기 생기는 배열은 매 프레임 할당이 아니다.
            Collider[] colliders = slot.body.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider part = colliders[i];
                if (part == null || !part.enabled) continue;

                part.enabled = false;
                slot.colliders.Add(part);
            }
        }

        /// <summary>
        /// 몸을 안 보이게 한다. 렌더러만 끄고 EnemyVisibility를 켜 둔 채로 두면 안 된다.
        /// 그쪽은 플레이어 시야에 들어올 때마다 자기가 아는 값으로 렌더러를 죄다 켜 버려서,
        /// 상자를 다시 바라보는 순간 숨긴 몸이 되살아난다.
        /// </summary>
        private static void HideRenderers(Slot slot)
        {
            slot.renderers.Clear();

            EnemyVisibility visibility = slot.body.GetComponentInChildren<EnemyVisibility>(true);
            if (visibility != null && visibility.enabled)
            {
                visibility.enabled = false;
                slot.visibility = visibility;
            }

            Renderer[] renderers = slot.body.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer part = renderers[i];
                if (part == null || !part.enabled) continue;

                part.enabled = false;
                slot.renderers.Add(part);
            }
        }

        private void Release(Slot slot)
        {
            Damageable body = slot.body;
            if (body != null)
            {
                StashedBodies.Remove(body.GetInstanceID());
                body.transform.position = slot.position;
            }

            for (int i = 0; i < slot.colliders.Count; i++)
            {
                if (slot.colliders[i] != null) slot.colliders[i].enabled = true;
            }

            for (int i = 0; i < slot.renderers.Count; i++)
            {
                if (slot.renderers[i] != null) slot.renderers[i].enabled = true;
            }

            if (slot.visibility != null) slot.visibility.enabled = true;

            slot.colliders.Clear();
            slot.renderers.Clear();
            slot.visibility = null;
            slot.body = null;
        }

        private Slot FreeSlot()
        {
            if (_slots == null) return null;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].body == null) return _slots[i];
            }

            return null;
        }

        /// <summary>재시작이면 숨긴 것을 모두 내놓는다. 시체도 함께 되살아나기 때문이다.</summary>
        private void OnRunReset()
        {
            if (PlayerInside) Exit(false);
            ReleaseBodies();
        }

        /// <summary>
        /// CharacterController는 켜진 채로 위치를 바꾸면 무시한다. 잠깐 끄고 옮긴다.
        /// GameManager가 체크포인트로 돌려놓을 때 쓰는 방법과 같다.
        /// </summary>
        private static void MovePlayer(Transform player, Vector3 position)
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            player.position = position;

            if (controller != null) controller.enabled = true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 넣은 몸이 놓이는 자리는 눈에 안 보이니 씬에서 확인할 수 있게 그려 둔다.
            Gizmos.color = new Color(0.9f, 0.76f, 0.45f, 0.6f);
            Gizmos.DrawWireSphere(transform.position + stashOffset, 0.35f);
        }
#endif
    }
}
