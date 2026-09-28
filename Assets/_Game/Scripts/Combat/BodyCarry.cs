using System.Collections.Generic;
using UnityEngine;
using ByAWhisker.Core;
using ByAWhisker.Level;

namespace ByAWhisker.Combat
{
    /// <summary>
    /// 쓰러진 몸을 들고 옮긴다. 플레이어에 붙는다.
    /// 총을 쓴 값을 치르게 하는 장치다. 쏜 자리를 치우지 않으면 지나가던 경비가 시체를 보고 놀란다.
    /// 드는 동안에는 손이 막혀 총을 쏘지 못하고 걸음도 느려진다.
    ///
    /// 상자(Container)를 쓰는 창구도 여기다. 키를 늘리지 않기로 했으니 G 하나가 몸을 드는 일과
    /// 상자에 넣는 일과 상자에 숨는 일을 함께 맡고, 무엇이 될지는 손에 든 것과 옆에 있는 것이 정한다.
    /// </summary>
    public class BodyCarry : MonoBehaviour
    {
        [Header("집기")]
        [Tooltip("들 수 있는 몸이 있는 레이어. Enemy.")]
        [SerializeField] private LayerMask targetLayers;

        [Tooltip("이 거리 안이어야 집을 수 있다(m). 수평 거리로만 잰다. 제압 사거리와 비슷하게 둔다.")]
        [SerializeField] private float range = 2.1f;

        [Tooltip("대상을 다시 찾는 간격(초). 매 프레임 훑을 필요가 없다.")]
        [SerializeField] private float scanInterval = 0.1f;

        [Tooltip("총에 맞아 죽은 몸도 들 수 있게 한다. 끄면 제압해 기절시킨 몸만 옮길 수 있다.")]
        [SerializeField] private bool carryDeadBodies = true;

        [Header("드는 자리")]
        [Tooltip("몸을 붙일 자리. 비우면 아래 오프셋을 플레이어 기준으로 쓴다.")]
        [SerializeField] private Transform carryAnchor;

        [Tooltip("어깨 자리(m). 플레이어 기준이다. 몸의 기준점이 발밑이라 뒤로 당겨야 어깨에 걸린 것처럼 보인다.")]
        [SerializeField] private Vector3 carryOffset = new Vector3(0f, 1.15f, -0.85f);

        [Tooltip("들었을 때의 각도(도). 기본값은 몸을 앞뒤로 눕힌다. 세워서 업고 싶으면 0으로 둔다.")]
        [SerializeField] private Vector3 carryEuler = new Vector3(90f, 0f, 0f);

        [Header("내려놓기")]
        [Tooltip("발밑에서 앞으로 이만큼 떨어진 자리에 놓는다(m).")]
        [SerializeField] private float dropDistance = 0.9f;

        [Tooltip("놓을 때 발밑에서 띄우는 높이(m). DownedBodyVisual의 restHeight와 맞춰야 바닥에 파묻히지 않는다.")]
        [SerializeField] private float dropHeight = 0.34f;

        [Tooltip("놓을 자리를 막는 것이 있는 레이어. 벽과 지형.")]
        [SerializeField] private LayerMask blockingLayers;

        [Tooltip("놓을 자리가 비었는지 볼 때 쓰는 반지름(m). 몸의 두께쯤이면 된다.")]
        [SerializeField] private float dropClearance = 0.35f;

        [Header("이동")]
        [Tooltip("들고 있는 동안 이동 속도에 곱할 값. PlayerMotor가 SpeedMultiplier를 읽어 가야 실제로 느려진다.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float carrySpeedMultiplier = 0.55f;

        [Header("상자")]
        [Tooltip("상자가 붙어 있는 레이어. HighCover. 상자는 높은 엄폐물에 붙는다.")]
        [SerializeField] private LayerMask containerLayers;

        [Tooltip("이 거리 안이어야 상자를 쓸 수 있다(m). 콜라이더의 가장 가까운 면까지로 잰다. 집는 거리와 비슷하게 둔다.")]
        [SerializeField] private float containerRange = 2.1f;

        [Tooltip("한 번에 살펴볼 콜라이더 수. 상자 몇 개가 붙어 있어도 이 정도면 넉넉하다.")]
        [SerializeField] private int maxContainerOverlap = 8;

        // 씬의 몸들. 아래 EnsureBodies 주석 참고. 한 번만 모으므로 매 프레임 할당이 아니다.
        private Damageable[] _bodies;

        // 우리가 직접 끈 콜라이더만 적어 둔다. 재사용하는 목록이라 집을 때마다 새로 만들지 않는다.
        private readonly List<Collider> _disabled = new List<Collider>(8);

        // OverlapSphereNonAlloc이 여기에 채운다. GuardPerception이 시체를 훑는 방식과 같다.
        private Collider[] _containerOverlap;

        private Damageable _self;
        private Damageable _target;
        private Damageable _carried;
        private Transform _carriedParent;
        private Container _container;
        private Container _hiding;
        private float _nextScan;

        private ByAWhisker.UI.ControlsOverlay _overlay;

        /// <summary>지금 몸을 들고 있는가. 총을 막고 걸음을 늦추는 쪽이 읽어 간다.</summary>
        public bool IsCarrying { get { return _carried != null; } }

        /// <summary>지금 집을 수 있는 몸이 있는가. HUD가 이걸 보고 안내를 띄운다.</summary>
        public bool HasTarget { get { return _target != null; } }

        /// <summary>지금 쓸 수 있는 상자가 옆에 있는가. HUD가 이걸 보고 안내를 띄운다.</summary>
        public bool HasContainer { get { return _container != null; } }

        /// <summary>
        /// 지금 상자 안에 숨어 있는가.
        /// 우리가 든 깃발이 아니라 상자에게 물어본다. 재시작처럼 상자가 스스로 플레이어를 내보내는
        /// 길이 있어서, 깃발을 따로 두면 그 길에서 배율이 0에 묶인 채 남아 영영 못 움직인다.
        /// </summary>
        public bool IsHiding { get { return _hiding != null && _hiding.PlayerInside; } }

        /// <summary>
        /// 들고 있는 동안 이동 속도에 곱할 값. 들고 있지 않으면 1이다.
        /// PlayerMotor를 고치지 않고는 속도를 깎을 자리가 없어서 값만 내놓는다.
        /// 상자 안에서 0을 내놓는 것도 같은 이유다. 플레이어를 묶어 둘 자리가 여기뿐이다.
        /// </summary>
        public float SpeedMultiplier
        {
            get
            {
                if (IsHiding) return 0f;
                return IsCarrying ? carrySpeedMultiplier : 1f;
            }
        }

        private void Awake()
        {
            // 자기 몸을 자기가 들지 않도록 어디까지가 자기인지 한 번 정해 둔다. TakedownAction이 쓰는 기준과 같다.
            _self = GetComponentInParent<Damageable>();

            _containerOverlap = new Collider[Mathf.Max(1, maxContainerOverlap)];
        }

        private void OnEnable()
        {
            GameEvents.RunReset += OnRunReset;
        }

        private void OnDisable()
        {
            GameEvents.RunReset -= OnRunReset;

            // 잡히거나 재시작으로 꺼질 때 몸을 플레이어에 매단 채 두면 영영 떨어지지 않는다.
            if (IsCarrying) Drop();

            // 숨은 채로 꺼지면 경비의 눈과 플레이어의 발이 상자에 붙잡힌 채로 남는다.
            ReleaseHiding();

            _target = null;
            _container = null;
            _nextScan = 0f;
        }

        private void Update()
        {
            // 숨어 있는 동안에는 아무것도 찾지 않는다. 나오는 것 말고는 할 수 있는 일이 없다.
            if (IsHiding) return;

            if (Time.time < _nextScan) return;
            _nextScan = Time.time + scanInterval;

            // 넣을 곳은 몸을 들고 있을 때도 알아야 한다. 어디에 넣을 수 있는지 모르면 상자를 찾아 헤맨다.
            _container = FindContainer();

            // 들고 있는 동안에는 다음 대상을 찾지 않는다. 안내가 깜빡이는 것을 막는다.
            _target = IsCarrying ? null : FindTarget();
        }

        /// <summary>
        /// G 하나가 네 가지를 맡는다. 무엇이 될지는 지금 손에 든 것과 옆에 있는 것이 정한다.
        /// 갈리는 순서가 곧 규칙이다. 숨어 있으면 나오기, 들고 있으면 넣기 아니면 내려놓기,
        /// 빈손이면 숨기 아니면 집기.
        /// </summary>
        public void Toggle()
        {
            // 상자 안에서는 다른 것이 되지 않는다. 안에서 몸을 집거나 넣는 그림은 없다.
            if (IsHiding)
            {
                LeaveContainer();
                return;
            }

            // 누른 순간의 상황으로 판정한다. 0.1초 전에 훑어 둔 자리로 집으면 이미 지나쳐 온 몸이 딸려 온다.
            _nextScan = Time.time + scanInterval;
            _container = FindContainer();

            if (IsCarrying)
            {
                // 상자 앞이면 넣는다. 자리가 없으면 지금까지처럼 바닥에 내려놓되 왜 안 들어갔는지 알려 준다.
                if (_container == null) Drop();
                else if (_container.HasRoom) StashInto(_container);
                else
                {
                    Drop();
                    Flash("상자가 가득 찼다");
                }

                return;
            }

            if (_container != null)
            {
                EnterContainer(_container);
                return;
            }

            _target = FindTarget();

            if (_target == null) return;

            PickUp(_target);
            _target = null;
        }

        private void PickUp(Damageable body)
        {
            _carried = body;
            _carriedParent = body.transform.parent;

            DisableColliders(body);

            // 부모를 바꾸면 플레이어를 따라오는 일은 저절로 된다. 대신 놓을 때 원래 부모로 정확히 돌려놔야 한다.
            Transform anchor = carryAnchor != null ? carryAnchor : transform;
            body.transform.SetParent(anchor, false);
            body.transform.localPosition = carryAnchor != null ? Vector3.zero : carryOffset;
            body.transform.localRotation = Quaternion.Euler(carryEuler);
        }

        private void Drop()
        {
            PlaceOnGround(Release());
        }

        /// <summary>
        /// 손을 뗀다. 자리는 부르는 쪽이 정한다. 바닥에 놓는 길과 상자에 넣는 길이 여기서 갈리는데,
        /// 부모와 콜라이더를 되돌리는 일은 어느 길이든 똑같이 해야 한다.
        /// </summary>
        private Damageable Release()
        {
            Damageable body = _carried;

            // 월드 자세를 지킨 채 원래 부모로 돌린다. 부모가 사라졌으면 씬 뿌리에 둔다.
            body.transform.SetParent(_carriedParent != null ? _carriedParent : null, true);

            RestoreColliders();

            _carried = null;
            _carriedParent = null;

            return body;
        }

        private void PlaceOnGround(Damageable body)
        {
            body.transform.position = DropPosition();

            // 자리와 콜라이더를 한꺼번에 바꿨으니 물리 쪽 사본을 지금 맞춘다.
            // 다음 물리 프레임까지 두면 경비가 아직 없는 자리의 몸에 걸린다.
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 들고 온 몸을 상자에 넣는다. 상자가 거절하면 그 자리에 놓는다.
        /// 손도 상자도 비면 몸이 공중에 남는다.
        /// </summary>
        private void StashInto(Container container)
        {
            Damageable body = Release();

            if (container.TryStash(body))
            {
                Flash("상자에 숨겼다");
                return;
            }

            PlaceOnGround(body);
        }

        /// <summary>
        /// 상자에 들어간다. 우리 쪽 상태는 상자가 받아 줬을 때만 바꾼다.
        /// 들어간 뒤에는 SpeedMultiplier가 0이 되어 발이 묶이고, 경비는 플레이어를 놓친다.
        /// </summary>
        private void EnterContainer(Container container)
        {
            if (!container.ToggleHide(transform)) return;

            _hiding = container;
            _target = null;

            Flash("상자에 숨었다. G로 나온다");
        }

        private void LeaveContainer()
        {
            if (ReleaseHiding()) Flash("상자에서 나왔다");
        }

        /// <summary>
        /// 상자에서 나온다. 들어간 자리로 돌려놓는 일은 상자가 한다. 안에 있었으면 true다.
        /// 재시작이나 컴포넌트가 꺼지는 길도 여기를 지난다. 그 길에서는 글자를 띄우지 않는다.
        /// </summary>
        private bool ReleaseHiding()
        {
            if (_hiding == null) return false;

            bool wasInside = _hiding.PlayerInside;
            if (wasInside) _hiding.ToggleHide(transform);
            _hiding = null;

            return wasInside;
        }

        /// <summary>발밑 앞쪽. 그 자리가 막혀 있으면 내 발밑에 놓는다.</summary>
        private Vector3 DropPosition()
        {
            Vector3 feet = transform.position;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            Vector3 wanted = feet + forward * dropDistance;

            // 벽을 마주 보고 놓으면 몸이 벽 속에 박힌다. 한 번만 확인하고 막혀 있으면 포기한다.
            Vector3 probe = wanted + Vector3.up * dropHeight;
            if (Physics.CheckSphere(probe, dropClearance, blockingLayers.value, QueryTriggerInteraction.Ignore))
            {
                wanted = feet;
            }

            wanted.y = feet.y + dropHeight;
            return wanted;
        }

        /// <summary>
        /// 들린 몸이 플레이어를 밀지 않게 콜라이더를 끈다.
        /// 쓰러질 때 Damageable이 이미 다 꺼 두지만, 우리가 끈 것만 적어 뒀다가 그것만 되돌린다.
        /// 전부 되돌리면 재시작 때 Revive가 켜 놓은 콜라이더를 다시 꺼 버린다.
        /// </summary>
        private void DisableColliders(Damageable body)
        {
            _disabled.Clear();

            // 집는 순간에만 도는 수집이라 여기 생기는 배열은 매 프레임 할당이 아니다.
            Collider[] colliders = body.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider part = colliders[i];
                if (part == null || !part.enabled) continue;

                part.enabled = false;
                _disabled.Add(part);
            }
        }

        private void RestoreColliders()
        {
            for (int i = 0; i < _disabled.Count; i++)
            {
                if (_disabled[i] != null) _disabled[i].enabled = true;
            }

            _disabled.Clear();
        }

        /// <summary>범위 안에서 들 수 있는 것 중 가장 가까운 하나를 고른다.</summary>
        private Damageable FindTarget()
        {
            EnsureBodies();

            Vector3 here = transform.position;

            Damageable best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < _bodies.Length; i++)
            {
                Damageable candidate = _bodies[i];
                if (candidate == null || candidate == _self) continue;
                if (!InTargetLayers(candidate.gameObject.layer)) continue;
                if (!IsCarryable(candidate)) continue;

                float distance = HorizontalDistance(here, candidate.transform.position);
                if (distance > range || distance >= bestDistance) continue;

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        /// <summary>
        /// 제압과 달리 물리 질의로 찾을 수 없다. 쓰러진 몸은 Damageable이 콜라이더를 모두 꺼 버려서
        /// OverlapSphere에 한 번도 잡히지 않는다. 그래서 씬의 Damageable을 한 번 모아 두고 거리로만 고른다.
        /// 몸은 레벨이 만들어진 뒤로 늘지 않으므로 모으는 일은 처음 한 번이면 된다.
        /// </summary>
        private void EnsureBodies()
        {
            if (_bodies != null) return;
            _bodies = FindObjectsByType<Damageable>(FindObjectsSortMode.None);
        }

        /// <summary>
        /// 가까운 상자 하나. 몸과 달리 물리 질의로 찾는다. 상자는 높은 엄폐물에 붙어 있어서
        /// 콜라이더가 늘 살아 있고, 그래서 씬을 미리 훑어 둘 필요가 없다.
        /// 거리는 중심이 아니라 콜라이더의 가장 가까운 면까지로 잰다. 높은 엄폐는 여러 칸이 한 덩어리로
        /// 붙어 있어서(6m짜리도 있다) 중심까지 재면 옆에 딱 붙어 서도 닿지 않는다.
        /// 트리거도 센다. 상자의 입을 트리거 볼륨으로 따로 놓아도 되게 열어 두는 것이다.
        /// </summary>
        private Container FindContainer()
        {
            Vector3 here = transform.position;

            int count = Physics.OverlapSphereNonAlloc(
                here, containerRange, _containerOverlap, containerLayers.value, QueryTriggerInteraction.Collide);

            Container best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider hit = _containerOverlap[i];
                if (hit == null) continue;

                // 콜라이더가 자식에 달려 있을 수 있으니 부모까지 올라가서 찾는다.
                Container candidate = hit.GetComponentInParent<Container>();
                if (candidate == null) continue;

                float distance = HorizontalDistance(here, hit.ClosestPoint(here));
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        /// <summary>
        /// 제압해 기절한 몸은 IsDown이고, 총에 맞아 죽은 몸은 IsAlive가 false다.
        /// 쏜 자리를 치우는 것이 이 기능의 목적이라 죽은 몸도 열어 둔다.
        /// </summary>
        private bool IsCarryable(Damageable body)
        {
            // 상자에 넣은 몸은 다시 집히지 않는다. 넣자마자 같은 키로 꺼내면 넣은 뜻이 없다.
            if (Container.IsStashed(body)) return false;

            return body.IsDown || (carryDeadBodies && !body.IsAlive);
        }

        /// <summary>
        /// 한순간에 끝나는 일은 글자가 없으면 키가 먹었는지 알 수 없다. 화면을 찾는 일은 한 번만 한다.
        /// ControlsOverlay는 우리 담당이 아니라 부르기만 한다.
        /// </summary>
        private void Flash(string message)
        {
            if (_overlay == null) _overlay = FindAnyObjectByType<ByAWhisker.UI.ControlsOverlay>();
            if (_overlay != null) _overlay.Flash(message);
        }

        private bool InTargetLayers(int layer)
        {
            return (targetLayers.value & (1 << layer)) != 0;
        }

        /// <summary>재시작이면 들고 있던 몸을 그 자리에 놓는다. 되살아난 경비가 어깨에 매달려 있으면 안 된다.</summary>
        private void OnRunReset()
        {
            if (IsCarrying) Drop();

            // 상자 쪽도 스스로 플레이어를 내보내지만, 어느 쪽이 먼저 불리든 같은 결과가 되게 둘 다 한다.
            ReleaseHiding();

            _target = null;
            _container = null;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 집을 수 있는 거리는 눈에 안 보이니 씬에서 확인할 수 있게 그려 둔다.
            Gizmos.color = new Color(0.45f, 0.75f, 0.9f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, range);

            // 상자에 닿는 거리는 따로 둘 수 있으니 함께 그린다. 두 값이 어긋나면 여기서 바로 보인다.
            Gizmos.color = new Color(0.9f, 0.76f, 0.45f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, containerRange);
        }
#endif
    }
}
