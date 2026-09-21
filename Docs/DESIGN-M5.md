# M5 설계 · 전투

앞 단계는 [DESIGN-M4.md](DESIGN-M4.md)에 있다. 전체 구조는 [ARCHITECTURE.md](ARCHITECTURE.md)를 본다.

## 이 단계에서 만드는 것

들키면 싸울 수는 있다. 다만 한 대 맞으면 끝난다. 그래서 총은 위기를 벗어나는 수단이지 정면 돌파의 수단이 아니다.

- 플레이어는 마우스 방향으로 조준하고 쏜다. 소음기를 달면 소리가 작고, 안 달면 총성이 멀리까지 퍼져 다른 경비를 부른다.
- 경비는 쏘기 전에 겨눈다. 겨누는 동안이 플레이어가 숨거나 먼저 쏠 틈이다.
- 뒤에서 조용히 제압할 수 있다. 쓰러진 몸은 남고 냄새를 풍긴다.
- 맞으면 죽는다. 플레이어도 경비도 같다.

## 병렬 작업 분할

| 갈래 | 담당 파일 |
|---|---|
| A 사격 | `Scripts/Combat/WeaponSettings.cs`, `Scripts/Combat/Weapon.cs`, `Scripts/Combat/AimSolver.cs` |
| B 경비 전투 | `Scripts/AI/GuardGunner.cs`, `Scripts/AI/GuardCombatSettings.cs`, `Scripts/AI/AimTelegraph.cs` |
| C 피격과 제압 | `Scripts/Combat/DamageInfo.cs`, `Scripts/Combat/Damageable.cs`, `Scripts/Combat/TakedownAction.cs` |
| 통합 | 입력 액션 추가, 씬 연결, 두뇌 연결, 검증, 커밋 |

A, B, C는 서로의 파일을 건드리지 않는다. 이미 있는 파일도 건드리지 않는다. Unity 에디터와 git은 통합 담당만 쓴다.

세 갈래가 서로의 타입을 부른다. 아래 계약에 적힌 이름과 서명을 그대로 믿고 쓴다. 상대 파일이 아직 디스크에 없어도 계약대로 코드를 쓰면 된다.

## 계약

### C. 피격과 제압 (다른 갈래가 이걸 부른다)

```csharp
namespace ByAWhisker.Combat
{
    public enum DamageKind { Bullet, Takedown, Fall }

    public struct DamageInfo
    {
        public GameObject attacker;
        public Vector3 point;       // 맞은 자리
        public Vector3 direction;   // 날아온 방향
        public DamageKind kind;
        public bool lethal;         // 이 게임은 거의 항상 true다
    }

    /// 맞을 수 있는 것에 붙는다. 체력 수치는 두지 않는다. 치명상이면 그대로 죽는다.
    public class Damageable : MonoBehaviour
    {
        public bool IsAlive { get; }
        public bool IsDown { get; }               // 제압당해 쓰러진 상태
        public event System.Action<DamageInfo> Damaged;

        /// 맞았다. 이미 죽었거나 쓰러졌으면 false를 돌려준다.
        public bool Apply(DamageInfo info);

        /// 재시작용. 다시 살려 세운다.
        public void Revive();
    }

    /// 뒤에서 조용히 제압한다. 플레이어에 붙는다.
    public class TakedownAction : MonoBehaviour
    {
        public bool HasTarget { get; }            // 지금 제압할 수 있는 상대가 있는가
        public Damageable Target { get; }
        public bool TryTakedown();                // 성공하면 true
    }
}
```

규칙은 이렇다.

- `Damageable`은 죽으면 시체를 남긴다. 콜라이더는 끄고 렌더러는 남긴다. 그 오브젝트에 `ScentSource`가 있으면 세기를 올린다. 시체 냄새가 단서가 되어야 한다.
- 플레이어가 죽으면 `GameEvents.PlayerCaptured`를 올린다. 기존 흐름이 이미 재시작을 맡고 있다.
- `TakedownAction`은 대상의 등 뒤 각도와 거리로 판정한다. 각도와 거리는 직렬화 필드로 둔다. 기본은 뒤쪽 100도 안, 1.4m 안이다.
- 제압은 소리를 낸다. 크지 않은 소리다. `NoiseEmitter.EmitOnce`를 쓴다.
- 대상의 `GuardPerception`이 자기를 보고 있으면(`CanSeePlayer`) 제압에 실패한다. 마주 본 상대를 조용히 잡을 수는 없다.

### A. 사격

```csharp
namespace ByAWhisker.Combat
{
    [CreateAssetMenu(menuName = "By a Whisker/Weapon Settings")]
    public class WeaponSettings : ScriptableObject
    {
        public float range;            // m
        public float spreadDegrees;    // 한 발의 흐트러짐
        public float fireInterval;     // 초
        public int magazine;
        public float reloadSeconds;
        public bool suppressed;
        public float noiseLoudness;    // 0..1. NoiseEmitter로 넘긴다
    }

    /// 총 한 자루. 플레이어와 경비가 같은 것을 쓴다.
    public class Weapon : MonoBehaviour
    {
        public WeaponSettings Settings { get; }
        public int Ammo { get; }
        public bool IsReloading { get; }
        public bool CanFire { get; }

        /// 한 발 쏜다. 쏘지 못하는 상태면 false.
        public bool TryFire(Vector3 origin, Vector3 direction);
        public void Reload();
        public void RefillAmmo();      // 재시작용

        public event System.Action<Vector3> Fired;    // 인자는 맞은 자리(빗나가면 사거리 끝)
    }

    /// 화면의 마우스 위치를 캐릭터가 겨눌 수평 방향으로 바꾼다.
    public static class AimSolver
    {
        /// 바닥 평면(y = groundY)과 마우스 광선의 교점을 구한다.
        public static bool TryAimPoint(Camera camera, Vector2 screenPoint, float groundY, out Vector3 worldPoint);

        /// origin에서 그 점을 향하는 수평 방향. 실패하면 fallback을 돌려준다.
        public static Vector3 AimDirection(Vector3 origin, Vector3 aimPoint, Vector3 fallback);
    }
}
```

규칙은 이렇다.

- `TryFire`는 `Physics.Raycast`로 맞은 것을 찾고, 거기 `Damageable`이 있으면 `Apply`를 부른다. 총알은 치명상이다.
- 쏘면 같은 오브젝트의 `NoiseEmitter`로 소리를 낸다. 종류는 `NoiseKind.Gunshot`이다. 소음기를 달면 크기를 크게 줄인다. 배율은 직렬화 필드로 둔다.
- 시선을 막는 레이어는 직렬화 필드로 받는다. 레이어 번호를 코드에 적지 않는다.
- 탄창이 비면 `Reload`를 부르기 전에는 쏘지 못한다. 자동 재장전은 하지 않는다.

### B. 경비 전투

```csharp
namespace ByAWhisker.AI
{
    [CreateAssetMenu(menuName = "By a Whisker/Guard Combat Settings")]
    public class GuardCombatSettings : ScriptableObject
    {
        public float fireRange;        // 이 거리 안이면 쏜다
        public float aimSeconds;       // 겨누는 시간. 플레이어가 피할 틈이다
        public float recoverSeconds;   // 쏘고 나서 다음 조준까지
        public float loseAimSeconds;   // 대상을 놓치고도 이만큼은 겨눈 채로 버틴다
    }

    /// 경비의 사격. 상태는 여기서만 굴리고 GuardBrain은 켜고 끄기만 한다.
    public class GuardGunner : MonoBehaviour
    {
        public bool IsAiming { get; }
        public float AimProgress { get; }   // 0..1. 1이면 곧 쏜다

        public void Bind(Transform target);
        public void Engage();               // 두뇌가 전투를 시작할 때
        public void Disengage();            // 대상을 잃거나 재시작할 때
    }

    /// 겨누고 있다는 것을 플레이어에게 보여 준다. 경비에 붙는다.
    public class AimTelegraph : MonoBehaviour
    {
        public void Bind(GuardGunner gunner);
    }
}
```

규칙은 이렇다.

- `GuardGunner`는 같은 오브젝트의 `Weapon`과 `GuardPerception`을 쓴다. 보이지 않으면 쏘지 않는다.
- 겨누는 동안 대상이 시야에서 사라지면 `loseAimSeconds`만큼 버티다가 조준을 푼다.
- `AimTelegraph`는 코드로 만든 선이나 고리로 표현한다. 프리팹을 쓰지 않는다. `LineRenderer`는 써도 된다. 색과 굵기는 직렬화 필드로 둔다.
- 겨누는 표현은 경비 발밑에서 대상 쪽으로 뻗는 형태가 좋다. 화면이 어두우니 얇고 밝은 선이 잘 보인다.

## 규칙

- 계약의 공개 멤버 이름과 서명을 바꾸지 않는다.
- 이미 있는 파일을 고치지 않는다. 필요하면 읽기만 한다.
- 매 프레임 할당을 만들지 않는다. 레이캐스트는 `NonAlloc`을 쓴다.
- 레이어 번호를 코드에 적지 않는다. LayerMask는 직렬화 필드로 받는다.
- `.meta` 파일을 만들지 않는다.

## 통합 메모

입력이 늘었다. 좌클릭 사격, R 재장전, F 제압이다. `PlayerInputReader`에 `FireHeld`와 `ReloadRequested`, `TakedownRequested`가 붙었고, `PlayerCombat`이 그것을 총과 제압에 잇는다. 조준 평면의 높이는 `PlayerMotor`가 커서를 볼 때 쓰는 평면과 같게 맞춰야 한다. 다르면 몸은 한쪽을 보는데 총알은 다른 쪽으로 간다.

씬 연결은 이렇게 되어 있다.

- 플레이어: `Weapon`(소음기 권총), `Damageable`(플레이어 표시), `TakedownAction`(대상은 Enemy 레이어), `PlayerCombat`.
- 경비 셋: `Weapon`(소음기 없는 소총), `GuardGunner`, `AimTelegraph`, `Damageable`, 그리고 캡슐 콜라이더.
- 무기의 `hitMask`에는 벽과 엄폐뿐 아니라 Player와 Enemy 레이어도 들어간다. 빠뜨리면 총알이 사람을 그냥 지나간다.
- `GuardBrain`은 Alert에 들어갈 때만 총을 들고, 겨누는 동안에는 걷지 않는다. 걸으면서 쏘면 피할 틈이 없다.
- 총을 든 경비는 근접 포획을 돌리지 않는다. 둘 다 돌면 같은 순간에 두 번 죽는 셈이 된다.
- 부트스트랩이 `GuardPerception.Bind`와 같은 자리에서 `GuardGunner.Bind`도 부른다. 둘이 다른 대상을 보면 엉뚱한 곳을 겨누고 쏜다.

값은 이렇게 잡았다. 플레이어 권총은 사거리 18m, 탄창 6, 간격 0.45초, 소음기. 경비 소총은 20m, 8발, 0.9초, 소음기 없음. 경비 전투는 사거리 12m, 조준 1.2초, 회복 1초, 놓치고 버티기 0.8초다. 조준 시간은 2m 격자 한 칸을 벌 수 있는 길이다.

걸린 곳 둘을 적어 둔다.

- 경비에게 콜라이더가 없었다. 몸이 없으니 총알이 통과하고 제압 판정도 대상을 찾지 못했다. 캡슐을 붙이고, 사람이 길을 막지 않도록 NavMesh를 구울 때 쓰는 레이어에서 Player와 Enemy를 뺐다.
- 트랜스폼을 코드로 옮긴 직후에는 물리 쪽 위치가 아직 옛날이다. 검증 스크립트에서 옮기자마자 레이캐스트를 쏘면 아무것도 맞지 않는다. `Physics.SyncTransforms()`를 먼저 불러야 한다.
