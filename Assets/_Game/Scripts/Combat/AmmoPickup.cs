using UnityEngine;
using UnityEngine.Rendering;
using ByAWhisker.Core;
using ByAWhisker.Player;

namespace ByAWhisker.Combat
{
    /// <summary>
    /// 바닥에 놓인 탄약 몇 발. 플레이어가 닿으면 예비탄에 더하고 숨는다. 재시작하면 제자리로 돌아온다.
    ///
    /// 권총은 세 발로 시작하고 경비는 열둘이다. 총은 막다른 순간에 쓰는 마지막 수단이어야 하고,
    /// 탄을 더 얻으려면 지도를 돌아다니는 위험을 져야 한다. 그래서 탄약은 이렇게 흩어 둔 것으로만 늘어난다.
    ///
    /// 트리거는 ObjectiveItem과 같은 방식이다. Default 레이어의 트리거 콜라이더이고 Rigidbody는 없다.
    /// CharacterController는 움직일 때 트리거를 스스로 건드려 OnTriggerEnter가 불린다.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    [DisallowMultipleComponent]
    public class AmmoPickup : MonoBehaviour
    {
        [Header("탄약")]
        [Tooltip("주우면 예비탄에 더할 발 수. 탄창에 바로 넣지 않는다. 장전(R)은 플레이어가 고른다.")]
        [Min(1)]
        [SerializeField] private int amount = 2;

        [Header("트리거")]
        [Tooltip("닿았다고 칠 반경(m). 지나가다 스치면 줍게 넉넉히 둔다.")]
        [SerializeField] private float triggerRadius = 0.8f;

        [Tooltip("트리거 중심의 높이(m, 이 오브젝트 기준). 오브젝트는 바닥에 두고, 트리거는 몸통 높이에 띄워야 캡슐과 겹친다.")]
        [SerializeField] private float triggerHeight = 0.9f;

        [Header("겉모습")]
        [Tooltip("주웠을 때 숨길 겉모습. 비우면 visualPrefab을 세우고, 그것도 없으면 작은 상자를 코드로 만든다.")]
        [SerializeField] private GameObject visual;

        [Tooltip("겉모습으로 세울 프리팹(예: SpaceStation 모델). visual이 비었을 때만 쓴다. 콜라이더는 떼어 낸다.")]
        [SerializeField] private GameObject visualPrefab;

        [Tooltip("코드로 만드는 상자의 크기(m).")]
        [SerializeField] private Vector3 boxSize = new Vector3(0.35f, 0.22f, 0.26f);

        [Tooltip("코드로 만드는 상자에 입힐 머티리얼. 비우면 한 번 만든 공용 머티리얼을 쓴다.")]
        [SerializeField] private Material boxMaterial;

        [Tooltip("공용 머티리얼의 색. 목표물·엄폐물과 한눈에 갈리게 탄피 색을 쓴다.")]
        [SerializeField] private Color boxColor = new Color(1f, 0.72f, 0.2f, 1f);

        [Header("움직임")]
        [Tooltip("겉모습이 떠 있는 높이(m, 이 오브젝트 기준).")]
        [SerializeField] private float hoverHeight = 0.45f;

        [Tooltip("위아래로 떠오르는 폭(m). 가만히 있는 소품과 갈리게 조금만 움직인다.")]
        [SerializeField] private float bobAmplitude = 0.06f;

        [Tooltip("위아래 한 번에 걸리는 시간(초).")]
        [SerializeField] private float bobPeriod = 1.6f;

        [Tooltip("초당 도는 각도.")]
        [SerializeField] private float spinDegreesPerSecond = 60f;

        [Header("알림")]
        [Tooltip("주웠을 때 ControlsOverlay에 잠깐 띄울 글. 비우면 띄우지 않는다. {0}에 발 수가 들어간다.")]
        [SerializeField] private string flashFormat = "탄약 +{0}";

        // 코드로 만드는 상자들이 나눠 쓴다. 상자마다 머티리얼을 만들면 개수만큼 사본이 생긴다.
        private static Material _sharedMaterial;

        private SphereCollider _trigger;
        private Transform _visualTransform;
        private float _phase;
        private bool _taken;

        // 주울 때마다 문자열을 새로 붙이지 않게 한 번만 만든다.
        private string _flashText;
        private ByAWhisker.UI.ControlsOverlay _overlay;

        /// <summary>이미 주워서 숨은 상태인가.</summary>
        public bool IsTaken { get { return _taken; } }

        /// <summary>줍는 발 수. 레벨을 만드는 쪽이 바꿀 수 있다.</summary>
        public int Amount
        {
            get { return amount; }
            set { amount = Mathf.Max(1, value); RefreshFlashText(); }
        }

        /// <summary>레벨을 만든 쪽이 겉모습을 연결해 준다. ObjectiveItem.SetVisual과 같은 창구다.</summary>
        public void SetVisual(GameObject target)
        {
            visual = target;
            _visualTransform = visual != null ? visual.transform : null;
            if (visual != null) visual.SetActive(!_taken);
        }

        private void Reset()
        {
            SphereCollider sphere = GetComponent<SphereCollider>();
            if (sphere == null) return;
            sphere.isTrigger = true;
            sphere.radius = triggerRadius;
            sphere.center = new Vector3(0f, triggerHeight, 0f);
        }

        private void Awake()
        {
            _trigger = GetComponent<SphereCollider>();
            // 씬에 어떻게 놓였든 트리거여야 한다. 단단한 콜라이더면 플레이어가 상자에 걸려 멈춘다.
            _trigger.isTrigger = true;
            _trigger.radius = Mathf.Max(0.05f, triggerRadius);
            _trigger.center = new Vector3(0f, triggerHeight, 0f);

            if (visual == null) visual = BuildVisual();
            _visualTransform = visual != null ? visual.transform : null;

            // 여러 개가 같은 박자로 떠오르면 기계처럼 보인다. 자리에서 위상을 뽑아 저마다 다르게 둔다.
            Vector3 p = transform.position;
            _phase = Mathf.Repeat(p.x * 0.37f + p.z * 0.61f, 1f) * Mathf.PI * 2f;

            RefreshFlashText();

            // 구독을 OnEnable이 아니라 Awake에서 한다. 숨을 때 이 오브젝트를 끄는 쪽으로 누가 바꿔도
            // 재시작 신호를 놓치지 않게 하려는 것이다. Lamp와 같은 규칙이다.
            GameEvents.RunReset += Restore;
        }

        private void OnDestroy()
        {
            GameEvents.RunReset -= Restore;
        }

        private void Update()
        {
            if (_taken || _visualTransform == null) return;

            float period = Mathf.Max(0.05f, bobPeriod);
            float y = hoverHeight + Mathf.Sin(Time.time * (Mathf.PI * 2f / period) + _phase) * bobAmplitude;

            Vector3 local = _visualTransform.localPosition;
            local.y = y;
            _visualTransform.localPosition = local;
            _visualTransform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_taken) return;

            // 경비도 이 트리거를 지나간다. 플레이어만 줍는다. 태그 대신 PlayerMotor로 가리는 것은 ObjectiveItem과 같다.
            PlayerMotor player = other.GetComponentInParent<PlayerMotor>();
            if (player == null) return;

            Weapon weapon = FindWeapon(player);
            if (weapon == null) return;

            // 무한 탄창이라 더할 것이 없으면 줍지 않고 남겨 둔다. 사라지기만 하면 헛걸음이 된다.
            if (!weapon.AddReserve(amount)) return;

            Take();
            Flash();
        }

        /// <summary>
        /// 재시작용. 다시 보이고 다시 주울 수 있게 한다. 멀쩡한 것에 불러도 안전하다.
        /// 플레이어의 탄약도 같은 신호에 처음 값으로 돌아간다(PlayerCombat). 그래서 여기가 되살아나도 탄이 쌓이지 않는다.
        /// </summary>
        public void Restore()
        {
            _taken = false;
            if (_trigger != null) _trigger.enabled = true;
            if (visual != null) visual.SetActive(true);
        }

        /// <summary>
        /// 숨긴다. 오브젝트 전체를 끄지 않고 겉모습과 트리거만 끈다. 이 컴포넌트는 살아 있어야
        /// 다음 재시작 신호를 받는다.
        /// </summary>
        private void Take()
        {
            _taken = true;
            if (_trigger != null) _trigger.enabled = false;
            if (visual != null) visual.SetActive(false);
        }

        /// <summary>
        /// 총은 PlayerCombat이 쓰는 것과 같은 것을 찾는다. PlayerCombat은 제 오브젝트에서 Weapon을 찾으니
        /// 먼저 플레이어 몸에서 보고, 총을 자식에 달아 둔 경우를 위해 아래로도 내려가 본다.
        /// 주울 때 한 번만 부르므로 매 프레임 비용이 아니다.
        /// </summary>
        private static Weapon FindWeapon(PlayerMotor player)
        {
            Weapon weapon = player.GetComponent<Weapon>();
            if (weapon != null) return weapon;
            return player.GetComponentInChildren<Weapon>();
        }

        private void Flash()
        {
            if (string.IsNullOrEmpty(_flashText)) return;

            if (_overlay == null) _overlay = FindAnyObjectByType<ByAWhisker.UI.ControlsOverlay>();
            if (_overlay != null) _overlay.Flash(_flashText);
        }

        private void RefreshFlashText()
        {
            _flashText = string.IsNullOrEmpty(flashFormat) ? null : string.Format(flashFormat, amount);
        }

        /// <summary>
        /// 겉모습을 세운다. 프리팹이 있으면 그것을, 없으면 작은 상자를 만든다.
        /// 어느 쪽이든 콜라이더는 뗀다. 남겨 두면 총알과 시선을 막고 플레이어가 걸려 넘어진다.
        /// </summary>
        private GameObject BuildVisual()
        {
            GameObject go;
            if (visualPrefab != null)
            {
                go = Instantiate(visualPrefab, transform, false);
                go.name = "Visual";
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Visual";
                go.transform.SetParent(transform, false);
                go.transform.localScale = boxSize;

                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                Material material = boxMaterial != null ? boxMaterial : SharedMaterial(boxColor);
                if (renderer != null && material != null) renderer.sharedMaterial = material;
            }

            go.transform.localPosition = new Vector3(0f, hoverHeight, 0f);
            StripColliders(go);

            // 작은 소품의 그림자는 읽을 거리가 없고, 빛을 가리는 판정과 헷갈리게만 한다.
            MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }

            return go;
        }

        private static void StripColliders(GameObject go)
        {
            Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                // Destroy는 프레임 끝에 지운다. 그 사이 한 프레임 동안 플레이어를 막지 않게 먼저 끈다.
                colliders[i].enabled = false;
                Destroy(colliders[i]);
            }
        }

        /// <summary>
        /// 공용 머티리얼. Unlit을 먼저 고르는 이유는 어두운 구석에 놓여도 "줍는 것"으로 읽혀야 해서다.
        /// Lit의 발광은 키워드(_EMISSION)가 빌드에서 벗겨질 수 있어 믿지 않는다.
        /// 첫 상자의 색으로 한 번만 만든다. 색을 따로 쓰고 싶으면 boxMaterial을 넣는다.
        /// </summary>
        private static Material SharedMaterial(Color color)
        {
            // 플레이 모드를 나가면 파괴되어 가짜 null이 된다. 그때 다시 만든다.
            if (_sharedMaterial != null) return _sharedMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            Material material = new Material(shader)
            {
                name = "BW_AmmoPickup",
                hideFlags = HideFlags.HideAndDontSave
            };

            // 셰이더마다 색 이름이 다르다. 있는 것만 건드린다.
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);

            _sharedMaterial = material;
            return _sharedMaterial;
        }
    }
}
