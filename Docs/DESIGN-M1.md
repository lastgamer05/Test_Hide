# M1 설계 · 뼈대

전체 구조는 [ARCHITECTURE.md](ARCHITECTURE.md)에 있다. 이 문서는 M1 한 단계만 다룬다.

## 이 단계에서 만드는 것

문자 지도로 창고를 세우고, 늑대 대신 캡슐을 그 안에서 움직인다. 카메라는 거리와 각도를 고정한 채 90도씩 돈다.

**만들지 않는 것**: 적, 시야 표현, 소리, 냄새, 전투, 애니메이션. 벽에 가린 캐릭터를 비추는 처리도 M3에서 한다.

## 클래스

### Level

| 타입 | 종류 | 책임 | 주요 멤버 |
|---|---|---|---|
| `CellType` | enum | 칸 종류 | Empty, Wall, HighCover, LowCover, Lamp, PlayerStart, Key, Exit |
| `LevelMapAsset` | ScriptableObject | 문자 지도와 치수, 머티리얼 참조를 담는 데이터 | `rows`(여러 줄 문자열), `cellSize=2`, `wallHeight=3.6`, `highCoverHeight=3`, `lowCoverHeight=1.15`, 머티리얼 네 개 |
| `GridMap` | 순수 C# 클래스 | 칸 조회와 좌표 변환. 씬에 붙지 않는다 | `At(col,row)`, `At(worldPos)`, `CellCenter(col,row)`, `BlocksSight(col,row,crouched)`, `FindFirst(type)` |
| `LevelBuilder` | MonoBehaviour | 지도를 읽어 바닥과 블록을 생성하고 GridMap을 만든다 | `Build()`, `Grid`, `PlayerStartPosition` |

문자 규칙은 HTML 프로토타입과 같다. `#` 벽, `H` 높은 엄폐, `L` 낮은 엄폐, `l` 램프, `P` 시작 위치, `K` 열쇠, `X` 출구, `.` 빈 바닥.

가로로 이어진 같은 종류의 칸은 하나의 상자로 합쳐서 만든다. 오브젝트 수가 줄고 벽이 매끄럽다.

### Player

| 타입 | 종류 | 책임 | 주요 멤버 |
|---|---|---|---|
| `PlayerInputReader` | MonoBehaviour | 입력 액션을 읽어 의도로 바꾼다. 다른 클래스는 입력 시스템을 직접 모른다 | `Move`, `RunHeld`, `PointerPosition`, `CrouchToggled`, `RotateCameraLeft/Right` |
| `PlayerStance` | MonoBehaviour | 서기와 앉기. 캡슐 높이와 눈높이를 바꾼다 | `Current`, `IsCrouching`, `EyeHeight`, `Toggle()` |
| `PlayerMotor` | MonoBehaviour | 화면 기준 이동, 마우스 방향 바라보기 | `CurrentSpeed`, `IsRunning`, `EyePosition`, `NoiseLevel` |
| `MovementSettings` | ScriptableObject | 속도와 높이 값 | 걷기 3.6, 달리기 6.4, 앉기 2.0, 회전 14, 눈높이 1.55와 1.0 |

이동은 CharacterController로 한다. 물리 밀림이 없어서 잠입 게임의 정확한 위치 조작에 맞고, 나중에 NavMesh 위의 적과도 간섭하지 않는다.

`NoiseLevel`은 M1에서 값만 계산해 두고 아무도 쓰지 않는다. M4에서 소리 시스템이 이 값을 그대로 가져간다.

### Cameras

| 타입 | 종류 | 책임 | 주요 멤버 |
|---|---|---|---|
| `QuarterViewCamera` | MonoBehaviour | 거리와 각도를 고정한 채 대상을 따라가고 90도씩 돈다 | `RotateStep(dir)`, `Yaw`, `PlanarForward`, `PlanarRight` |

기본값은 거리 10.5m, 내려보는 각 52도, 시야각 45도, 회전 한 칸 90도, 회전 시간 0.25초, 시선 앞당김 2.5m다. 시작 각도는 45도이고, 이때 격자 벽이 화면에 대각선으로 보인다.

시선 앞당김은 카메라가 캐릭터가 아니라 캐릭터 앞 2.5m 지점을 따라가게 한다. 프로토타입에서 카메라 쪽을 바라보면 앞이 4m밖에 안 보이던 문제를 막는다.

### Core

| 타입 | 종류 | 책임 |
|---|---|---|
| `GameBootstrap` | MonoBehaviour | 레벨을 만들고, 시작 칸에 플레이어를 놓고, 카메라에 대상을 연결한다 |

## 의존 방향

```
LevelMapAsset ─▶ LevelBuilder ─▶ GridMap
                                   │ (칸 조회)
                                   ▼
GameBootstrap ─▶ Player(Motor, Stance) ◀── PlayerInputReader
                      │  위치와 바라보는 방향
                      ▼
                QuarterViewCamera ──▶ 화면 기준 방향을 Motor에 돌려준다
```

- 한 방향으로만 흐른다. 레벨은 플레이어를 모르고, 플레이어는 카메라의 각도만 안다.
- 카메라와 이동은 서로를 참조해야 하지만, 주고받는 값은 각도 하나뿐이다. 카메라가 `Yaw`를 공개하고 이동은 그것만 읽는다.
- `GridMap`은 MonoBehaviour가 아니다. 나중에 시야 계산과 적 AI가 같은 조회 함수를 쓴다.
- 전역 싱글턴은 만들지 않는다. 연결은 GameBootstrap이 한다.

## 씬 구성

새 씬 `Assets/_Game/Scenes/Warehouse.unity`를 만든다. 템플릿의 SampleScene은 건드리지 않는다.

```
Warehouse
  Systems          GameBootstrap, LevelBuilder
  Level            생성된 바닥과 블록이 여기 들어간다
  Player           프리팹 인스턴스, 시작 칸에 배치
  Main Camera      QuarterViewCamera
  Lighting         약한 방향광 하나. 램프는 M2에서
```

## 레이어

| 레이어 | 쓰임 |
|---|---|
| Floor | 마우스 지면 판정 |
| Wall | 시선 차단, 카메라 가림 |
| HighCover | 시선 차단, 카메라 가림 |
| LowCover | 앉았을 때만 시선 차단 |
| Player | 적의 판정 대상 |
| Enemy | M2에서 사용 |

레이어 번호는 코드에 적지 않는다. 마스크는 인스펙터에서 지정한다.

## 입력 액션

`Assets/_Game/Input/ByAWhisker.inputactions`를 새로 만든다. 템플릿 에셋은 쓰지 않는다. M1에서 쓰는 것은 여섯 개다.

| 액션 | 형식 | 바인딩 |
|---|---|---|
| Move | Vector2 | WASD |
| Run | Button | 왼쪽 Shift |
| Crouch | Button | C |
| Point | Vector2 | 마우스 위치 |
| RotateLeft | Button | Z |
| RotateRight | Button | X |

이후 단계에서 Listen(Q), Sniff(E), Aim(우클릭), Fire(좌클릭), Takedown(F), Restart(R)를 같은 에셋에 더한다.

## 작업 순서

한 번에 하나씩 하고, 각 단계가 끝나면 눈으로 확인한다.

| 단계 | 내용 | 확인 방법 |
|---|---|---|
| 1 | CellType, LevelMapAsset, GridMap, LevelBuilder와 씬 | 씬 뷰에 창고가 보인다. 벽과 두 종류의 엄폐물이 구분된다 |
| 2 | 입력 에셋, PlayerInputReader, PlayerStance, PlayerMotor, 플레이어 프리팹 | 캡슐이 걷고 달리고 앉는다. 벽을 통과하지 못한다 |
| 3 | QuarterViewCamera와 GameBootstrap 연결 | 카메라가 90도씩 돌고, 돌린 뒤에도 WASD가 화면 기준으로 움직인다 |

## M1을 끝났다고 보는 기준

- 창고를 자유롭게 걸어 다닌다.
- 걷기, 달리기, 앉기로 속도와 캡슐 높이가 바뀐다.
- 카메라가 네 방향으로 돌고 거리와 각도는 변하지 않는다.
- 캐릭터가 마우스 쪽을 바라본다.
- 콘솔에 오류가 없다.

## 다음 단계에서 이어 붙일 자리

| 나중 시스템 | M1에서 준비해 두는 것 |
|---|---|
| 시야 계산 (M3) | `PlayerMotor.EyePosition`, `GridMap.BlocksSight` |
| 소리 (M4) | `PlayerMotor.NoiseLevel` |
| 적 (M2) | `GridMap`, 레이어 구분, NavMesh를 구울 바닥과 블록 |
| 카메라 가림 (M3) | 카메라와 대상 사이를 검사할 `QuarterViewCamera`의 위치 정보 |
