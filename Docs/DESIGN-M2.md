# M2 설계 · 인간 경비

전체 구조는 [ARCHITECTURE.md](ARCHITECTURE.md), 직전 단계는 [DESIGN-M1.md](DESIGN-M1.md)에 있다.

## 이 단계에서 만드는 것

인간 경비가 창고를 순찰하고, 빛과 소리로 플레이어를 찾고, 잡으면 한 대에 포획한다. 플레이어는 열쇠를 집어 출구로 나가면 이긴다.

인간은 시각이 중심이고 청각은 약하다. 후각은 없다. 경비견은 M2 범위 밖이다.

## 병렬 작업 분할

세 갈래가 서로 다른 파일만 건드린다. 계약은 아래에 고정한다. 계약에 없는 공개 멤버를 임의로 바꾸지 않는다.

| 갈래 | 담당 폴더와 파일 |
|---|---|
| A 감각 기반 | `Scripts/Perception/SenseProfile.cs`, `LightSource.cs`, `Illumination.cs`, `Sight.cs` 와 `Scripts/Player/PlayerExposure.cs` |
| B 경비 AI | `Scripts/AI/PatrolRoute.cs`, `GuardMotor.cs`, `GuardPerception.cs`, `GuardBrain.cs` |
| C 목표와 흐름 | `Scripts/Core/GameEvents.cs`, `GameManager.cs`, `Scripts/Level/Checkpoint.cs`, `ObjectiveItem.cs`, `ExitZone.cs` |

씬 연결, 프리팹, NavMesh 굽기, 컴파일 확인, 커밋은 통합 담당이 한다. 병렬 갈래는 Unity 에디터를 건드리지 않는다.

## 계약

### A. Perception

```csharp
namespace ByAWhisker.Perception
{
    [CreateAssetMenu(menuName = "By a Whisker/Sense Profile", fileName = "SenseProfile")]
    public class SenseProfile : ScriptableObject
    {
        public float fovDegrees = 100f;      // 시야각 전체
        public float rangeDark = 5f;         // 어두운 곳 탐지 거리
        public float rangeLit = 16f;         // 밝은 곳 탐지 거리
        public float hearingMultiplier = 0.6f; // 인간은 귀가 약하다
        public bool canSmell = false;        // 경비견용, M2에서는 false
        public float detectSeconds = 1f;     // 정면에서 완전히 발각되기까지
        public float forgetPerSecond = 0.25f;// 시야 밖일 때 의심도 감소
        public float eyeHeight = 1.6f;
    }

    public class LightSource : MonoBehaviour   // 램프에 붙는다
    {
        public float radius = 5.5f;
        public float intensity = 1f;           // 0..1
        public bool IsOn { get; set; }
        public static IReadOnlyList<LightSource> All { get; }  // OnEnable에서 등록, OnDisable에서 해제
    }

    public static class Illumination
    {
        // 켜진 램프마다 거리 감쇠와 intensity를 곱하고, 시선이 막히면 0으로 본다. 가장 밝은 값을 0..1로 돌려준다.
        public static float Sample(Vector3 worldPosition, LayerMask blockers);
    }

    public static class Sight
    {
        public static bool HasLineOfSight(Vector3 from, Vector3 to, LayerMask blockers);
        public static bool InFieldOfView(Vector3 origin, Vector3 forward, Vector3 target, float fovDegrees);
        public static float HorizontalDistance(Vector3 a, Vector3 b);
    }
}

namespace ByAWhisker.Player
{
    public class PlayerExposure : MonoBehaviour
    {
        public float Light { get; }  // 0..1, 부드럽게 보간
        public float Noise { get; }  // PlayerMotor.NoiseLevel 을 그대로 노출. M4에서 소리 시스템으로 교체
        public float Scent { get; }  // M3 이후 자리. 지금은 0
    }
}
```

### B. AI

```csharp
namespace ByAWhisker.AI
{
    public class PatrolRoute : MonoBehaviour
    {
        public int Count { get; }
        public Vector3 PointAt(int index);   // index는 알아서 순환시킨다
        public float waitSeconds = 1.2f;
    }

    public class GuardMotor : MonoBehaviour   // NavMeshAgent 래퍼
    {
        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public bool ReachedDestination { get; }
        public void MoveTo(Vector3 destination, float speed);
        public void Stop();
        public void FaceTowards(Vector3 worldPosition, float turnSpeed);
    }

    public class GuardPerception : MonoBehaviour
    {
        public float Awareness { get; }          // 0..1
        public bool CanSeePlayer { get; }
        public bool HeardSomething { get; }      // 이번 프레임에 소리를 들었다
        public Vector3 LastKnownPosition { get; }
        public void Bind(Transform playerTransform, ByAWhisker.Player.PlayerExposure exposure);
    }

    public class GuardBrain : MonoBehaviour
    {
        public enum State { Patrol, Suspicious, Search, Alert, Attack, Stunned }
        public State Current { get; }
        public event System.Action<State> StateChanged;
        public void Stun(float seconds);
    }
}
```

감지 규칙은 이렇게 둔다.

- 보이는 조건은 세 가지를 모두 만족할 때다. 시야각 안, 탐지 거리 안, 시선이 막히지 않음.
- 탐지 거리는 플레이어의 빛 노출로 `rangeDark`와 `rangeLit` 사이를 보간한다.
- 보이면 의심도가 `deltaTime / detectSeconds`만큼 오른다. 거리가 멀거나 플레이어가 앉아 있으면 더 느리게 오른다.
- 보이지 않으면 `forgetPerSecond`만큼 내려간다.
- 소리는 플레이어와의 거리가 `PlayerExposure.Noise * hearingMultiplier`보다 가까울 때 들린다. 들으면 마지막 위치를 갱신한다.

상태 전이는 이렇게 둔다.

| 상태 | 들어가는 조건 | 행동 |
|---|---|---|
| Patrol | 기본 | 경로를 돈다. 지점마다 `waitSeconds` 쉰다 |
| Suspicious | 의심도 0.3 이상 | 멈춰서 마지막 위치를 본다 |
| Search | 의심도 0.6 이상 또는 소리를 들음 | 마지막 위치로 가서 5초 둘러본다 |
| Alert | 의심도 1 | 달려서 쫓는다. 4초 동안 못 보면 Search |
| Attack | Alert 중 거리 1.8m 이내 | 멈춰 0.75초 예고 후 포획 |
| Stunned | `Stun()` 호출 | 그동안 아무것도 하지 않는다 |

### C. 목표와 흐름

```csharp
namespace ByAWhisker.Core
{
    public static class GameEvents
    {
        public static event System.Action<GameObject> PlayerCaptured; // 인자는 잡은 적
        public static event System.Action ObjectiveTaken;
        public static event System.Action PlayerEscaped;
        public static event System.Action RunReset;   // 포획 후 재시작. 적도 제자리로 돌아간다

        public static void RaisePlayerCaptured(GameObject by);
        public static void RaiseObjectiveTaken();
        public static void RaisePlayerEscaped();
        public static void RaiseRunReset();
    }

    public class GameManager : MonoBehaviour
    {
        public enum Phase { Playing, Captured, Won }
        public Phase Current { get; }
        public int CaptureCount { get; }
        public bool HasObjective { get; }
        public Vector3 CheckpointPosition { get; }
        public void SetCheckpoint(Vector3 position);
    }
}

namespace ByAWhisker.Level
{
    public class Checkpoint : MonoBehaviour { }    // 트리거. 밟으면 GameManager에 위치를 알린다
    public class ObjectiveItem : MonoBehaviour { } // 열쇠. 닿으면 ObjectiveTaken
    public class ExitZone : MonoBehaviour { }      // 열쇠를 든 상태로 닿으면 PlayerEscaped
}
```

포획되면 1.5초 뒤 마지막 체크포인트에서 다시 시작한다. 경비는 순찰 상태로 되돌리고 시작 지점에 다시 놓는다.

## 규칙

- 계약에 적힌 공개 멤버 이름과 서명은 바꾸지 않는다. 내부 구현과 `[SerializeField]` 비공개 필드는 자유다.
- 참조는 `[SerializeField]`로 받는다. `FindObjectOfType`을 매 프레임 호출하지 않는다.
- 튜닝 값은 ScriptableObject나 직렬화 필드로 둔다.
- 레이어 이름은 Floor, Wall, HighCover, LowCover, Player, Enemy를 쓴다. 번호를 코드에 적지 않는다.
- `.meta` 파일은 만들지 않는다. Unity가 만든다.
