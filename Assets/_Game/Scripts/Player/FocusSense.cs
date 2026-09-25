using UnityEngine;
using ByAWhisker.Cameras;

namespace ByAWhisker.Player
{
    /// <summary>
    /// Q를 누르고 있는 동안 감각을 모은다.
    /// 자취가 늘 보이면 화면이 냄새로 덮여 아무것도 안 보이는 것과 같아서, 맡으려 할 때만 드러나게 한다.
    /// 카메라가 다가오는 것도 같은 뜻이다 — 집중은 멀리 보는 대신 가까이를 자세히 보는 것이다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FocusSense : MonoBehaviour
    {
        // 셰이더와 약속한 전역 이름. 바꾸면 합성 셰이더가 자취를 영영 0으로 읽어 냄새가 안 뜬다.
        private static readonly int IdScentReveal = Shader.PropertyToID("_BW_ScentReveal");

        // 지수 보간은 끝에 닿지 않는다. 눈에 안 보일 만큼 가까우면 끊어 준다.
        // 놓았는데 자취가 아주 옅게 남으면 "집중해야만 보인다"는 약속이 깨진다.
        private const float Epsilon = 0.001f;

        [Header("참조")]
        [Tooltip("집중 입력을 읽어 올 곳. 비우면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayerInputReader input;

        [Tooltip("집중할 때 다가올 카메라. 비우면 씬에서 한 번 찾는다.")]
        [SerializeField] private QuarterViewCamera view;

        [Header("보간")]
        [Tooltip("집중에 들어가는 속도. 누른 값이 바로 오게 나오는 쪽보다 빠르게 둔다.")]
        [SerializeField] private float enterSpeed = 10f;

        [Tooltip("집중이 풀리는 속도. 천천히 빠져야 자취와 카메라가 툭 끊기지 않는다.")]
        [SerializeField] private float exitSpeed = 4f;

        /// <summary>지금 집중 키를 누르고 있는가. 보간하기 전의 날 입력이다.</summary>
        public bool IsFocusing { get { return input != null && input.FocusHeld; } }

        /// <summary>0..1. 켜고 끌 때 뚝 끊기지 않게 보간한 값. 셰이더와 카메라가 이것을 읽는다.</summary>
        public float Amount { get; private set; }

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (view == null) view = FindFirstObjectByType<QuarterViewCamera>();
        }

        private void OnEnable()
        {
            // 집중하지 않은 상태에서 시작한다. 지난 판이 남긴 값이 첫 프레임에 튀지 않게 한다.
            Amount = 0f;
            Push();
        }

        private void OnDisable()
        {
            // 전역은 컴포넌트가 꺼져도 남는다. 되돌리지 않으면 플레이를 멈춘 화면에 자취가 그대로 얹혀 있다.
            Amount = 0f;
            Push();
        }

        private void Update()
        {
            bool held = IsFocusing;

            // 들어갈 때와 나올 때의 속도를 따로 둔다. 빨리 켜지고 천천히 꺼지는 쪽이 손에 붙는다.
            float speed = held ? enterSpeed : exitSpeed;

            // 프레임 레이트가 흔들려도 같은 속도로 붙도록 지수 보간을 쓴다. PlayerExposure와 같은 방식이다.
            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
            Amount = Mathf.Lerp(Amount, held ? 1f : 0f, t);

            if (Amount < Epsilon) Amount = 0f;
            else if (Amount > 1f - Epsilon) Amount = 1f;

            Push();
        }

        /// <summary>읽는 쪽 둘에 같이 내보낸다. 한쪽만 갱신하면 화면과 카메라가 어긋난 채로 움직인다.</summary>
        private void Push()
        {
            Shader.SetGlobalFloat(IdScentReveal, Amount);
            if (view != null) view.SetFocus(Amount);
        }
    }
}
