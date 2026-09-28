using System.Collections.Generic;
using UnityEngine;

namespace ByAWhisker.Cameras
{
    /// <summary>
    /// 판정 상자에 붙어 "이 상자가 가리면 대신 이것들을 비춰라"를 알려 준다.
    /// 판정 상자의 그림을 끄고 따로 입힌 옷을 보여 줄 때, OcclusionFader가 부딪힌 상자에서
    /// 보이는 옷을 찾아가게 한다. 옷에는 콜라이더가 없어서 광선이 옷을 직접 맞히지 못한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class OcclusionProxy : MonoBehaviour
    {
        private readonly List<Renderer> _renderers = new List<Renderer>();

        public IReadOnlyList<Renderer> Renderers { get { return _renderers; } }

        public void Add(Renderer renderer)
        {
            if (renderer != null && !_renderers.Contains(renderer)) _renderers.Add(renderer);
        }

        public void Clear()
        {
            _renderers.Clear();
        }
    }
}
