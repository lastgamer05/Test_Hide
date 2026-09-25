using System.Collections.Generic;
using UnityEngine;
using ByAWhisker.AI;
using ByAWhisker.Core;
using ByAWhisker.Senses;
using ByAWhisker.Visibility;

namespace ByAWhisker.Combat
{
    /// <summary>
    /// 맞을 수 있는 것에 붙는다. 체력 수치는 두지 않는다. 치명상이면 그대로 죽는다.
    /// 수치를 두지 않는 이유는 규칙이 그렇기 때문이다. 한 대 맞으면 끝나니까 총이 무섭고,
    /// 총이 무서워야 숨는 쪽이 본 게임이 된다.
    ///
    /// 죽은 것은 사라지지 않고 시체로 남는다. 콜라이더만 끄고 렌더러는 그대로 둬서,
    /// 지나가던 경비가 보고 냄새 맡을 수 있는 단서가 되게 한다.
    /// 무엇을 껐는지 적어 두었다가 Revive에서 그대로 되돌린다.
    /// </summary>
    [DisallowMultipleComponent]
    public class Damageable : MonoBehaviour
    {
        [Header("정체")]
        [Tooltip("플레이어면 켠다. 쓰러지는 순간 GameEvents.PlayerCaptured를 올린다. 재시작은 GameManager가 맡는다.")]
        [SerializeField] private bool isPlayer;

        [Header("시체")]
        [Tooltip("쓰러진 뒤의 ScentSource 세기. 산 몸보다 진해야 시체가 단서 구실을 한다. 0..1")]
        [Range(0f, 1f)]
        [SerializeField] private float corpseScent = 0.85f;

        [Header("쓰러질 때 끌 것")]
        [Tooltip("비워 두면 Awake에서 자기 밑의 컴포넌트를 모은다. 시체를 그리고 냄새를 내는 쪽은 빼고 모은다.")]
        [SerializeField] private Behaviour[] disableWhenDown;

        [Tooltip("자동으로 모을 때 건드리지 않을 컴포넌트. 시체가 되어도 계속 돌아야 하는 것을 여기 넣는다.")]
        [SerializeField] private Behaviour[] keepEnabled;

        [Header("재시작")]
        [Tooltip("RunReset을 받으면 스스로 Revive한다. 끄면 통합 담당이 직접 불러야 한다.")]
        [SerializeField] private bool reviveOnRunReset = true;

        /// <summary>살아 있는가. 치명상을 입으면 false가 된다.</summary>
        public bool IsAlive { get; private set; }

        /// <summary>제압당해 쓰러진 상태. 죽지는 않았지만 더는 아무것도 하지 못한다.</summary>
        public bool IsDown { get; private set; }

        /// <summary>맞았다. HUD나 효과가 듣는다. 상태를 다 바꾼 뒤에 올리므로 IsAlive를 바로 물어도 된다.</summary>
        public event System.Action<DamageInfo> Damaged;

        // 되돌릴 것들. 껐을 때의 값이 아니라 끄기 직전의 값을 적어 둬야 정확히 돌아온다.
        private Collider[] _colliders;
        private bool[] _colliderWasEnabled;
        private bool[] _colliderWasTrigger;
        private Behaviour[] _behaviours;
        private bool[] _behaviourWasEnabled;

        private ScentSource _scent;
        private float _scentBefore;

        // 경비 두뇌만은 끄지 않는다. 아래 FallDown 주석 참고.
        private GuardBrain _brain;

        private bool _bodyApplied;

        private void Awake()
        {
            IsAlive = true;
            IsDown = false;

            _colliders = GetComponentsInChildren<Collider>(true);
            _colliderWasEnabled = new bool[_colliders.Length];
            _colliderWasTrigger = new bool[_colliders.Length];

            _scent = GetComponentInChildren<ScentSource>(true);
            _brain = GetComponent<GuardBrain>();

            ResolveBehaviours();
        }

        private void OnEnable()
        {
            if (reviveOnRunReset) GameEvents.RunReset += Revive;
        }

        private void OnDisable()
        {
            if (reviveOnRunReset) GameEvents.RunReset -= Revive;
        }

        /// <summary>맞았다. 이미 죽었거나 쓰러졌으면 false를 돌려준다.</summary>
        public bool Apply(DamageInfo info)
        {
            // 시체에 총을 더 쏴도 사건이 두 번 나지 않게 여기서 막는다.
            if (!IsAlive || IsDown) return false;

            if (info.lethal) IsAlive = false;
            else IsDown = true;

            FallDown();

            if (Damaged != null) Damaged(info);

            // 플레이어는 죽든 쓰러지든 한 판이 끝난 것이다. 쓰러진 채로 일어나지 못하면
            // 재시작 신호가 영영 오지 않아 판이 멈춰 버린다.
            if (isPlayer) GameEvents.RaisePlayerCaptured(info.attacker);

            return true;
        }

        /// <summary>재시작용. 다시 살려 세운다. 껐던 것만 되돌리므로 멀쩡한 것에 불러도 안전하다.</summary>
        public void Revive()
        {
            IsAlive = true;
            IsDown = false;

            if (!_bodyApplied) return;
            _bodyApplied = false;

            RestoreColliders();
            RestoreBehaviours();

            if (_scent != null) _scent.SetStrength(_scentBefore);
        }

        /// <summary>
        /// 시체를 만든다. 순서에 뜻이 있다.
        /// 1) 먼저 지금 값을 적는다. 껐다가 적으면 전부 false가 되어 Revive가 아무것도 되살리지 못한다.
        /// 2) 그 다음 두뇌를 재운다. NavMeshAgent가 아직 살아 있어야 GuardMotor.Stop이 제대로 선다.
        /// 3) 마지막에 끈다.
        /// </summary>
        private void FallDown()
        {
            if (_bodyApplied) return;
            _bodyApplied = true;

            RememberState();
            SleepBrain();
            DisableRemembered();
            RaiseScent();
        }

        private void RememberState()
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                _colliderWasEnabled[i] = _colliders[i] != null && _colliders[i].enabled;
                _colliderWasTrigger[i] = _colliders[i] != null && _colliders[i].isTrigger;
            }

            for (int i = 0; i < _behaviours.Length; i++)
            {
                _behaviourWasEnabled[i] = _behaviours[i] != null && _behaviours[i].enabled;
            }

            if (_scent != null) _scentBefore = _scent.Strength;
        }

        /// <summary>
        /// 경비 두뇌는 끄지 않고 영원히 기절시킨다.
        /// GuardBrain은 OnEnable에서 RunReset을 구독하는데, 꺼 두면 재시작 신호를 놓쳐서
        /// 되살아난 경비가 제자리로 돌아가지도, 순찰을 다시 시작하지도 못한다.
        /// 깨어날 시간을 무한으로 주면 두뇌는 매 프레임 남은 시간만 깎고 아무 일도 하지 않는다.
        /// </summary>
        private void SleepBrain()
        {
            if (_brain != null) _brain.Stun(float.PositiveInfinity);
        }

        private void DisableRemembered()
        {
            // 콜라이더는 끄지 않고 트리거로 바꾼다. 꺼 버리면 시체를 물리로 찾을 수 없어서
            // 경비가 시체를 발견하지도, 플레이어가 시체를 들지도 못한다.
            // 총알(Weapon)과 시선(Sight)은 둘 다 트리거를 무시하므로, 뚫고 지나가는 성질은 그대로다.
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null) _colliders[i].isTrigger = true;
            }

            for (int i = 0; i < _behaviours.Length; i++)
            {
                if (_behaviours[i] != null) _behaviours[i].enabled = false;
            }
        }

        private void RestoreColliders()
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] == null) continue;
                _colliders[i].enabled = _colliderWasEnabled[i];
                _colliders[i].isTrigger = _colliderWasTrigger[i];
            }
        }

        private void RestoreBehaviours()
        {
            for (int i = 0; i < _behaviours.Length; i++)
            {
                if (_behaviours[i] != null) _behaviours[i].enabled = _behaviourWasEnabled[i];
            }
        }

        /// <summary>시체 냄새. 산 것보다 진하게 두되 원래 세기보다 낮아지지는 않게 한다.</summary>
        private void RaiseScent()
        {
            if (_scent == null) return;
            _scent.SetStrength(Mathf.Max(_scentBefore, corpseScent));
        }

        /// <summary>
        /// 끌 목록을 한 번만 정한다. 직접 채워 두면 그대로 쓰고, 비어 있으면 자기 밑에서 모은다.
        /// 자동으로 모으는 편이 씬 연결을 빠뜨렸을 때 시체가 계속 순찰하는 사고를 막아 준다.
        /// </summary>
        private void ResolveBehaviours()
        {
            if (disableWhenDown != null && disableWhenDown.Length > 0)
            {
                _behaviours = disableWhenDown;
                _behaviourWasEnabled = new bool[_behaviours.Length];
                return;
            }

            // Awake에서 한 번만 도는 수집이라 여기 생기는 List는 매 프레임 할당이 아니다.
            List<Behaviour> found = new List<Behaviour>(16);
            Behaviour[] all = GetComponentsInChildren<Behaviour>(true);

            for (int i = 0; i < all.Length; i++)
            {
                if (ShouldAutoDisable(all[i])) found.Add(all[i]);
            }

            _behaviours = found.ToArray();
            _behaviourWasEnabled = new bool[_behaviours.Length];
        }

        private bool ShouldAutoDisable(Behaviour candidate)
        {
            if (candidate == null) return false;

            // 자기를 끄면 OnDisable에서 RunReset 구독이 풀려 영영 되살아나지 못한다.
            if (candidate == this) return false;

            // 시체가 계속 풍겨야 할 냄새.
            if (candidate is ScentSource) return false;

            // 시체도 플레이어 시야에 들어올 때만 그려야 한다. 이쪽이 렌더러를 맡는다.
            if (candidate is EnemyVisibility) return false;

            // 재시작 신호를 받아야 해서 끄지 않는다. SleepBrain이 대신 재운다.
            if (candidate is GuardBrain) return false;

            if (keepEnabled != null)
            {
                for (int i = 0; i < keepEnabled.Length; i++)
                {
                    if (keepEnabled[i] == candidate) return false;
                }
            }

            return true;
        }
    }
}
