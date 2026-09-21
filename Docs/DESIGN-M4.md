# M4 설계 · 소리와 냄새

앞 단계는 [DESIGN-M3.md](DESIGN-M3.md)에 있다. 전체 구조는 [ARCHITECTURE.md](ARCHITECTURE.md)를 본다.

## 이 단계에서 만드는 것

지금까지는 시각만 있었다. M4에서 소리와 냄새를 더한다.

- 발소리, 부딪히는 소리 같은 사건이 한 곳으로 모이고, 경비는 그걸 듣고 반응한다.
- 사람과 물건은 냄새를 남긴다. 냄새는 바람을 타고 퍼지고 시간이 지나면 옅어진다. 플레이어가 후각이 발달한 동물이면 벽 너머의 적도 어렴풋이 안다.
- 화면에는 내가 내는 소음, 내가 받는 빛, 최근에 난 소리의 방향, 근처 냄새 흔적이 보인다.

인간 경비는 시각과 약간의 청각만 가진다. 후각은 플레이어 쪽 능력이다.

## 병렬 작업 분할

| 갈래 | 담당 파일 |
|---|---|
| A 소리 | `Scripts/Senses/NoiseEvent.cs`, `Scripts/Senses/NoiseBus.cs`, `Scripts/Senses/NoiseEmitter.cs` |
| B 냄새 | `Scripts/Senses/ScentField.cs`, `Scripts/Senses/ScentSource.cs`, `Scripts/Senses/WindSettings.cs` |
| C 화면 표시 | `Scripts/UI/SenseHud.cs`, `Scripts/UI/SenseHudStyle.cs`, `Scripts/UI/SenseHudBuilder.cs` |
| 통합 | 경비 연결, 앉기 판정, 씬 연결, 커밋 |

A, B, C는 서로의 파일을 건드리지 않는다. 이미 있는 파일도 건드리지 않는다. Unity 에디터와 git은 통합 담당만 쓴다.

## 계약

### A. 소리

```csharp
namespace ByAWhisker.Senses
{
    public enum NoiseKind { Footstep, Bump, Object, Gunshot }

    public struct NoiseEvent
    {
        public Vector3 position;
        public float radius;     // 이 거리 안에서 들린다 (m)
        public float loudness;   // 0..1
        public NoiseKind kind;
        public GameObject source;
        public float time;       // Time.time
    }

    /// 소리 사건이 모이는 곳. 듣는 쪽은 이벤트를 받거나 최근 목록을 훑는다.
    public static class NoiseBus
    {
        public static event System.Action<NoiseEvent> Emitted;

        public static void Emit(NoiseEvent evt);

        /// listener에게 들릴 만한 최근 소리를 buffer에 채우고 개수를 돌려준다.
        /// maxAge초보다 오래된 소리와 radius 밖의 소리는 뺀다.
        public static int Collect(Vector3 listener, float maxAge, List<NoiseEvent> buffer);

        /// 재시작할 때 부른다.
        public static void Clear();
    }

    /// 움직이는 것에 붙어 발소리를 낸다.
    public class NoiseEmitter : MonoBehaviour
    {
        public float LastLoudness { get; }
        public void EmitOnce(float loudness, NoiseKind kind);
    }
}
```

규칙은 이렇다.

- `NoiseEmitter`는 같은 오브젝트의 `PlayerMotor` 또는 `GuardMotor`를 찾아 쓰되, 없으면 `CharacterController`나 `NavMeshAgent`의 속도로 대신한다. 어느 것도 없으면 조용히 아무 일도 하지 않는다. 기존 파일은 고치지 않는다.
- 걸은 거리 기준으로 발소리를 낸다. 시간 기준으로 하면 느리게 걸을 때 소리가 과하게 난다. 보폭은 직렬화 필드로 둔다.
- 앉으면 조용하고 달리면 시끄럽다. 배율은 직렬화 필드로 둔다. 기본값은 앉기 0.35, 걷기 1, 달리기 1.9다.
- `NoiseBus`는 정적이고 고정 크기 링 버퍼로 최근 사건을 들고 있다. 매 프레임 할당을 만들지 않는다. 플레이 모드를 다시 시작해도 남지 않도록 `RuntimeInitializeOnLoadMethod`로 비운다.

### B. 냄새

```csharp
namespace ByAWhisker.Senses
{
    [CreateAssetMenu(menuName = "ByAWhisker/Wind Settings")]
    public class WindSettings : ScriptableObject
    {
        public Vector3 direction;     // 수평 방향. 정규화해서 쓴다
        public float speed;           // m/s
        public float gustStrength;    // 0이면 일정하게 분다
        public float gustPeriod;      // 초
        public Vector3 SampleAt(float time);   // 돌풍을 섞은 지금의 바람
    }

    /// 냄새를 남기는 것에 붙는다. 사람, 시체, 기름통 같은 것들.
    public class ScentSource : MonoBehaviour
    {
        public int OwnerId { get; }        // 개체 구분용. 같은 오브젝트는 늘 같은 값
        public float Strength { get; }     // 0..1. 지금 내뿜는 세기
        public void SetStrength(float value);
    }

    public struct ScentReading
    {
        public float strength;   // 0..1
        public Vector3 gradient; // 짙어지는 쪽 수평 방향
        public int ownerId;      // 가장 짙은 냄새의 주인
        public float age;        // 이 자리에 남은 지 몇 초 됐는지
    }

    public class ScentField : MonoBehaviour
    {
        public bool IsConfigured { get; }
        public Vector4 WorldBounds { get; }   // (minX, minZ, sizeX, sizeZ)

        /// 레벨 크기에 맞춰 격자를 잡는다. 통합 담당이 시작할 때 부른다.
        public void Configure(Vector3 worldMin, Vector3 worldSize, float cellSize);

        /// 재시작할 때 부른다.
        public void Clear();

        public float Sample(Vector3 worldPoint);
        public bool TrySample(Vector3 worldPoint, out ScentReading reading);
    }
}
```

규칙은 이렇다.

- 격자 한 칸은 기본 1m다. 칸마다 세기와 주인과 남은 시간을 들고 있다.
- 매 프레임이 아니라 정해진 간격으로 갱신한다. 간격은 직렬화 필드로 두고 기본 0.2초다.
- 갱신은 세 가지를 한다. `ScentSource`가 자기 칸에 세기를 더하고, 바람 방향으로 조금 흘리고, 전체가 시간에 따라 옅어진다. 벽을 넘어가는 것은 막지 않는다. 냄새가 벽 너머로 도는 것이 이 게임에서 후각의 쓸모다.
- 격자와 버퍼는 한 번만 잡고 재사용한다. 매 프레임 할당을 만들지 않는다.
- `ScentSource`는 `ScentField`를 찾지 않는다. 필드 쪽이 `ScentSource`의 정적 목록을 훑는다. M2의 `LightSource`와 같은 방식이다.

### C. 화면 표시

```csharp
namespace ByAWhisker.UI
{
    /// 감각 표시를 담당한다. 캔버스와 위젯은 코드로 만든다. 프리팹을 쓰지 않는다.
    public class SenseHud : MonoBehaviour
    {
        public void Bind(Transform player, Camera viewCamera);
        public void SetScentField(ByAWhisker.Senses.ScentField field);
        public void SetExposure(ByAWhisker.Player.PlayerExposure exposure);
    }
}
```

화면에 넣을 것은 넷이다.

- 왼쪽 아래 소음 막대. 지금 내가 내는 소리 크기다. `PlayerExposure.Noise`를 쓴다.
- 그 옆 빛 막대. 지금 내가 받는 빛이다. `PlayerExposure.Light`를 쓴다.
- 최근에 난 소리의 방향 표시. 화면 가운데를 중심으로 그 방향 가장자리에 짧은 호를 띄우고 시간이 지나면 사라진다. `NoiseBus.Collect`로 받는다. 내가 낸 소리는 빼고 표시한다.
- 근처 냄새 점. `ScentField.TrySample`로 플레이어 주변 몇 군데를 찍어 짙은 곳에 흐린 점을 띄운다. 주인이 다르면 색을 달리한다.

`SenseHudStyle`은 색과 크기 값을 담은 `ScriptableObject`다. `SenseHudBuilder`는 캔버스와 위젯을 코드로 만드는 정적 도우미다. UGUI(`UnityEngine.UI`)를 쓴다.

표시는 어디까지나 힌트다. 정확한 좌표를 찍어 주지 않는다. 방향과 세기만 보여 준다.

## 규칙

- 계약의 공개 멤버 이름과 서명을 바꾸지 않는다.
- 이미 있는 파일을 고치지 않는다. 필요하면 읽기만 한다.
- 매 프레임 할당을 만들지 않는다.
- 레이어 번호를 코드에 적지 않는다. LayerMask는 직렬화 필드로 받는다.
- `.meta` 파일을 만들지 않는다.

## 통합 메모

씬 연결은 이렇게 되어 있다.

- 플레이어와 경비 셋에 `NoiseEmitter`와 `ScentSource`가 붙어 있다.
- `ScentField`는 `GameBootstrap` 아래 오브젝트에 있고, 바람은 `Data/WarehouseWind.asset`이다. 레벨 크기에 4m 여유를 더한 범위를 1m 칸으로 잡는다.
- `SenseHud`도 같은 자리에 있고, 부트스트랩이 카메라·노출·냄새 격자를 물려 준다. 캔버스는 코드가 만든다.
- 재시작하면 시야 기억, 소리 버스, 냄새 격자를 모두 지운다.

판정 쪽에서 바뀐 것은 둘이다.

- 경비의 청각이 `PlayerExposure.Noise`를 보던 것에서 `NoiseBus.Collect`로 바뀌었다. 자기 소리는 거르고, 사건 반경에 `hearingMultiplier`를 곱해 듣는다. 이제 경비는 플레이어뿐 아니라 다른 경비의 소리도 듣는다.
- `PlayerExposure.Noise`는 0..1이 되었다. 예전에는 미터 단위(1.6~11)였다. 화면 막대가 이 값을 그대로 쓴다.

미뤄 뒀던 앉기 판정도 여기서 처리했다. `PlayerExposure.BodyPoint`가 자세에 따라 1.0m와 0.55m를 오가고, 경비는 플레이어가 앉아 있을 때만 `lowCover`를 시선 차단에 더한다. 경비의 `blockers` 마스크에 LowCover가 들어 있어서 서 있어도 낮은 엄폐 너머가 안 보이던 것을 Wall|HighCover로 고쳤다.

걸린 곳 둘을 적어 둔다.

- 발소리를 속도×시간으로 세면 벽에 밀착해 제자리걸음을 할 때도 소리가 난다. 실제로 옮겨 간 거리로 세야 한다.
- 화면 표시는 `ScreenSpaceOverlay` 캔버스라 시야 합성 패스 뒤에 그려진다. 카메라 공간으로 바꿔 렌더 텍스처에 찍으면 합성이 HUD까지 검게 덮으니, 화면 확인은 게임 뷰로 해야 한다.
