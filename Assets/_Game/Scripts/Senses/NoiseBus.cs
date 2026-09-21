using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.Senses
{
    /// <summary>
    /// 소리 사건이 모이는 곳. 듣는 쪽은 이벤트를 받거나 최근 목록을 훑는다.
    /// 정적으로 둔 이유는 소리를 내는 쪽이 듣는 쪽을 하나도 몰라도 되게 하려는 것이다.
    /// 경비는 이벤트를 받아 즉시 반응하고, HUD처럼 "최근에 뭐가 있었나"를 묻는 쪽은 Collect를 쓴다.
    /// </summary>
    public static class NoiseBus
    {
        // 최근 사건만 있으면 되니 크기를 고정하고 오래된 것부터 덮어쓴다.
        // 길이를 늘리는 대신 Collect가 maxAge로 걸러 주므로 이 정도면 한 프레임에 쏟아지는 소리를 다 담는다.
        private const int Capacity = 64;

        private static readonly NoiseEvent[] _ring = new NoiseEvent[Capacity];
        private static int _next;   // 다음에 쓸 자리
        private static int _count;  // 채워진 개수. Capacity에서 멈춘다

        /// <summary>
        /// 소리가 날 때마다 불린다. 듣자마자 반응해야 하는 쪽이 구독한다.
        /// 구독은 OnEnable에서 넣고 OnDisable에서 빼라. 죽은 오브젝트가 계속 불리면 곤란하다.
        /// </summary>
        public static event System.Action<NoiseEvent> Emitted;

        /// <summary>
        /// 플레이 모드를 다시 시작해도 남지 않게 정적 상태를 되돌린다.
        /// 도메인 리로드를 끄면 이전 판의 링 버퍼와 구독자가 그대로 살아 있어서 첫 프레임부터 유령 소리가 들린다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Emitted = null;
            Clear();
        }

        public static void Emit(NoiseEvent evt)
        {
            // 시간을 안 채우고 보내는 호출자가 있어도 나이 계산이 무너지지 않게 여기서 한 번 더 찍는다.
            if (evt.time <= 0f) evt.time = Time.time;

            _ring[_next] = evt;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;

            if (Emitted != null) Emitted(evt);
        }

        /// <summary>
        /// listener에게 들릴 만한 최근 소리를 buffer에 채우고 개수를 돌려준다.
        /// maxAge초보다 오래된 소리와 radius 밖의 소리는 뺀다. 새로 난 것부터 담는다.
        /// buffer는 부르는 쪽이 들고 있다가 재사용하라. 매번 새로 만들면 할당이 생긴다.
        /// </summary>
        public static int Collect(Vector3 listener, float maxAge, List<NoiseEvent> buffer)
        {
            if (buffer == null) return 0;
            buffer.Clear();

            float cutoff = Time.time - Mathf.Max(0f, maxAge);

            // 넣은 순서가 곧 시간 순서라, 새것부터 거꾸로 훑다가 오래된 것이 나오면 거기서 멈출 수 있다.
            for (int i = 1; i <= _count; i++)
            {
                int index = _next - i;
                if (index < 0) index += Capacity;

                NoiseEvent evt = _ring[index];
                if (evt.time < cutoff) break;

                float dx = evt.position.x - listener.x;
                float dz = evt.position.z - listener.z;
                // 층이 하나뿐이라 높이는 거리로 치지 않는다. 제곱으로 비교해서 루트를 피한다.
                if (dx * dx + dz * dz > evt.radius * evt.radius) continue;

                buffer.Add(evt);
            }

            return buffer.Count;
        }

        /// <summary>재시작할 때 부른다. 구독자는 건드리지 않는다. 판이 바뀌어도 듣는 쪽은 그대로 살아 있어야 한다.</summary>
        public static void Clear()
        {
            _next = 0;
            _count = 0;

            // 낡은 GameObject 참조를 들고 있지 않도록 배열도 비운다. 재시작 때 한 번이라 비용은 상관없다.
            System.Array.Clear(_ring, 0, Capacity);
        }
    }
}
