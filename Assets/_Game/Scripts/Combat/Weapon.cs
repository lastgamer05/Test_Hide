using UnityEngine;
using ByAWhisker.Senses;

namespace ByAWhisker.Combat
{
    /// <summary>
    /// 총 한 자루. 플레이어와 경비가 같은 것을 쓴다.
    /// 겨누는 방향은 밖에서 정해서 넘긴다. 플레이어는 마우스로, 경비는 대상 쪽으로 겨누는데
    /// 여기서 그 차이를 알 필요가 없다. 이 클래스는 "쏠 수 있는가"와 "무엇이 맞았는가"만 맡는다.
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        [SerializeField] private WeaponSettings settings;

        [Tooltip("총알이 걸리는 레이어. 벽과 엄폐물에 더해 Player와 Enemy를 같이 넣어야 사람이 맞는다.")]
        [SerializeField] private LayerMask hitMask;

        [Tooltip("소음기를 달았을 때 총성 크기에 곱하는 배율. 크게 줄여야 총이 탈출 수단이 된다.")]
        [Range(0f, 1f)]
        [SerializeField] private float suppressedNoiseScale = 0.15f;

        [Tooltip("소리를 낼 NoiseEmitter. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private NoiseEmitter noise;

        /// <summary>
        /// 한 발에 걸릴 수 있는 콜라이더 수. 총구가 자기 몸 안에 있어서 자기 콜라이더가 먼저
        /// 잡히는 일이 흔하다. 그것들을 건너뛰려면 가장 가까운 하나만 봐서는 안 된다.
        /// </summary>
        private const int MaxHits = 8;

        // 매 발마다 새로 잡지 않으려고 들고 있는다. NonAlloc 레이캐스트가 여기에 채운다.
        private readonly RaycastHit[] _hits = new RaycastHit[MaxHits];

        private Transform _selfOwner;
        private int _ammo;
        private int _reserve;
        private float _reloadLeft;
        private float _nextFireTime;

        public WeaponSettings Settings { get { return settings; } }
        public int Ammo { get { return _ammo; } }

        /// <summary>남은 예비탄. 무한이면 설정에 적힌 음수를 그대로 돌려준다.</summary>
        public int Reserve { get { return _reserve; } }

        /// <summary>장전에 쓸 탄이 남았는가. 무한(음수)은 언제나 남아 있는 것으로 친다.</summary>
        public bool HasReserve { get { return _reserve != 0; } }

        public bool IsReloading { get { return _reloadLeft > 0f; } }

        public bool CanFire
        {
            get
            {
                if (settings == null) return false;
                if (IsReloading) return false;
                if (_ammo <= 0) return false;
                return Time.time >= _nextFireTime;
            }
        }

        /// <summary>한 발 나갔다. 인자는 맞은 자리(빗나가면 사거리 끝). 총구 섬광과 탄흔이 이걸 듣는다.</summary>
        public event System.Action<Vector3> Fired;

        private void Awake()
        {
            // 자기 몸을 쏘지 않으려면 어디까지가 자기인지 한 번 정해 둬야 한다. 매 발 찾지 않는다.
            // 맞을 수 있는 것의 경계가 곧 몸의 경계다. root를 쓰면 경비들이 한 부모 밑에 묶여 있을 때
            // 옆 경비까지 "자기"로 쳐서 총알이 그냥 지나가 버린다.
            Damageable self = GetComponentInParent<Damageable>();
            _selfOwner = self != null ? self.transform : transform.root;

            if (noise == null) noise = GetComponent<NoiseEmitter>();
            RefillAmmo();
        }

        private void OnDisable()
        {
            // 기절하거나 재시작으로 꺼질 때 장전 중이던 상태를 들고 있지 않는다. 다시 켜지면 깔끔하게 시작한다.
            _reloadLeft = 0f;
        }

        private void Update()
        {
            // 장전 중이 아니면 이 컴포넌트는 매 프레임 아무 일도 하지 않는다.
            if (_reloadLeft <= 0f) return;

            _reloadLeft -= Time.deltaTime;
            if (_reloadLeft > 0f) return;

            _reloadLeft = 0f;
            FinishReload();
        }

        /// <summary>한 발 쏜다. 쏘지 못하는 상태면 false.</summary>
        public bool TryFire(Vector3 origin, Vector3 direction)
        {
            if (!CanFire) return false;

            Vector3 aim = direction;
            aim.y = 0f;                                  // 층이 하나라 총알도 수평으로만 난다.
            if (aim.sqrMagnitude < 0.0001f) return false; // 방향이 없으면 쏠 곳도 없다.
            aim.Normalize();
            aim = ApplySpread(aim);

            _ammo--;
            _nextFireTime = Time.time + Mathf.Max(0f, settings.fireInterval);

            float range = Mathf.Max(0.01f, settings.range);
            Vector3 impact;
            Damageable target = TraceShot(origin, aim, range, out impact);

            if (target != null)
            {
                // 총알은 치명상이다. 이 게임에 체력 수치는 없다.
                DamageInfo info;
                info.attacker = gameObject;
                info.point = impact;
                info.direction = aim;
                info.kind = DamageKind.Bullet;
                info.lethal = true;
                target.Apply(info);
            }

            EmitGunshot();

            if (Fired != null) Fired(impact);
            return true;
        }

        /// <summary>장전한다. 탄창이 비어도 자동으로 되지 않는다. 비는 순간이 긴장이라 남겨 둔다.</summary>
        public void Reload()
        {
            if (settings == null) return;
            if (IsReloading) return;
            if (_ammo >= settings.magazine) return;

            // 넣을 것이 없으면 시작조차 하지 않는다. 헛장전으로 몇 초를 묶어 두면 빈 총보다 더 나쁘다.
            if (!HasReserve) return;

            _reloadLeft = Mathf.Max(0f, settings.reloadSeconds);

            // 장전 시간이 0이면 Update를 기다릴 것 없이 바로 채운다. 그래야 0초 설정이 먹통이 안 된다.
            if (_reloadLeft <= 0f) FinishReload();
        }

        /// <summary>
        /// 탄창을 채우고 그만큼 예비탄을 깎는다. 깎는 일을 장전이 끝나는 이 순간까지 미루는 이유는,
        /// 장전 도중에 죽거나 재시작해도 꺼내던 탄을 손해 보지 않게 하려는 것이다.
        /// 예비탄이 모자라면 탄창을 다 못 채워도 있는 만큼만 넣는다.
        /// </summary>
        private void FinishReload()
        {
            if (settings == null) return;

            int magazine = Mathf.Max(0, settings.magazine);
            int need = magazine - _ammo;
            if (need <= 0) return;

            // 무한이면 깎을 것이 없다. 경비 총이 여기로 온다.
            if (_reserve < 0)
            {
                _ammo = magazine;
                return;
            }

            int taken = Mathf.Min(need, _reserve);
            _ammo += taken;
            _reserve -= taken;
        }

        /// <summary>재시작용. 장전 중이던 것도 없던 일로 하고 탄창과 예비탄을 설정값으로 되돌린다.</summary>
        public void RefillAmmo()
        {
            _reloadLeft = 0f;
            _nextFireTime = 0f;
            _ammo = settings != null ? Mathf.Max(0, settings.magazine) : 0;
            // 음수는 무한이라는 뜻이라 0으로 다듬지 않고 그대로 받는다.
            _reserve = settings != null ? settings.reserveAmmo : 0;
        }

        /// <summary>
        /// 총알이 지나간 자리를 찾는다. 맞은 것에 Damageable이 있으면 그걸 돌려준다.
        /// 맞은 자리는 언제나 impact에 담긴다. 빗나갔으면 사거리 끝이다.
        /// </summary>
        private Damageable TraceShot(Vector3 origin, Vector3 direction, float range, out Vector3 impact)
        {
            impact = origin + direction * range;

            // NonAlloc이 아니면 쏠 때마다 배열이 새로 생긴다. 경비가 여럿이면 그게 쌓인다.
            int count = Physics.RaycastNonAlloc(origin, direction, _hits, range, hitMask.value, QueryTriggerInteraction.Ignore);
            if (count <= 0) return null;

            // NonAlloc은 거리순으로 정렬해 주지 않는다. 자기 콜라이더를 뺀 것 중 가장 가까운 하나를 고른다.
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = _hits[i].collider.transform;
                if (hitTransform.IsChildOf(_selfOwner)) continue; // 총구가 제 몸 안에 있다. 자기를 쏘지 않는다.

                if (_hits[i].distance >= bestDistance) continue;
                bestDistance = _hits[i].distance;
                best = i;
            }

            if (best < 0) return null;

            impact = _hits[best].point;
            // 콜라이더가 자식에 달려 있어도 찾도록 부모까지 거슬러 올라간다.
            return _hits[best].collider.GetComponentInParent<Damageable>();
        }

        /// <summary>
        /// 흐트러짐은 수평으로만 준다. 위아래로 벌리면 한 층짜리 레벨에서 허공이나 바닥만 맞는다.
        /// </summary>
        private Vector3 ApplySpread(Vector3 direction)
        {
            float spread = settings.spreadDegrees;
            if (spread <= 0f) return direction;

            float half = spread * 0.5f;
            float angle = Random.Range(-half, half);
            return Quaternion.AngleAxis(angle, Vector3.up) * direction;
        }

        /// <summary>
        /// 총성은 같은 오브젝트의 NoiseEmitter로 낸다. 발소리와 같은 통로를 써야 듣는 쪽이
        /// 자기 청각으로 똑같이 거를 수 있다. 소음기는 크기만 줄이고 소리를 없애지는 않는다.
        /// </summary>
        private void EmitGunshot()
        {
            if (noise == null) return;

            float loudness = settings.noiseLoudness;
            if (settings.suppressed) loudness *= suppressedNoiseScale;

            noise.EmitOnce(loudness, NoiseKind.Gunshot);
        }
    }
}
