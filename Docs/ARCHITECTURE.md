# 털끝 하나 · 코드 구조 v0

첫 목표는 "대략 돌아가는 한 판"이다. 늑대 한 마리가 창고에서 인간 경비를 피해 열쇠를 들고 출구로 나간다. 들키면 한 대에 포획되고 마지막 체크포인트에서 다시 시작한다.

기획 기준은 기획 정리 v0.3이다. 적은 인간이고 시점은 90도 회전 쿼터뷰, 레벨은 격자 모듈 구조다.

## 기술 기반

| 항목 | 값 |
|---|---|
| 엔진 | Unity 6000.3.11f1 |
| 렌더링 | URP 17.3, 선형 색공간, PC_RPAsset |
| 입력 | Input System 1.19, 새 입력만 사용 |
| 길찾기 | AI Navigation 2.0, 런타임 NavMesh 굽기 |
| 모델 | GLB는 glTFast 패키지가 필요하다. M6에서 추가한다 |

## 폴더

```
Assets/_Game/
  Scripts/                  ByAWhisker.Game 어셈블리
    Core/        게임 상태, 이벤트, 재시작
    Level/       격자 데이터, 레벨 빌더, 체크포인트, 목표, 출구
    Player/      입력, 이동, 자세, 감각 모드, 노출 수치
    Camera/      쿼터뷰 카메라, 가림 처리
    Perception/  감각 공용 시스템: 빛, 소리, 냄새, 시선
    AI/          경비 두뇌, 지각, 이동, 순찰 경로
    Combat/      무기, 사격, 뒤에서 기절, 피격 처리
    Visibility/  플레이어 시야 계산, 기억 격자, 화면 합성
    UI/          HUD, 감각 오버레이, 안내 메시지
    Data/        ScriptableObject 정의
    Editor/      에디터 도구
  Data/          ScriptableObject 에셋: 감각 프로필, 적 정의, 무기
  Input/         입력 액션 에셋
  Prefabs/  Materials/  Models/  Shaders/  Scenes/
Docs/            이 문서
```

## 설계 원칙

1. **감각은 공용 시스템이다.** 소리, 냄새, 빛은 "발생"과 "질의"로 나눈다. 누가 소리를 내든 NoiseSystem에 이벤트를 올리고, 듣는 쪽은 자기 청각으로 거른다.
2. **플레이어와 적이 같은 감각 데이터를 쓴다.** SenseProfile 하나로 인간, 경비견, 플레이어 캐릭터를 모두 표현한다. 동물 적을 나중에 넣을 때 새 코드 없이 프로필만 바꾼다.
3. **튜닝 값은 데이터로 둔다.** 시야각, 탐지 거리, 속도, 무기 예고 시간은 ScriptableObject에 둔다. 코드에 숫자를 박지 않는다.
4. **격자는 구조에만 쓴다.** 벽과 엄폐물은 2m 격자에 맞추고, 이동은 자유롭게 NavMesh와 CharacterController로 한다.
5. **아는 것과 그리는 것을 나눈다.** 무엇이 보이는지는 게임 로직이 계산하고, 렌더링은 그 결과만 그린다. 적 AI와 화면 표현이 같은 판정을 쓰니까 "보였는데 안 보였다" 같은 불일치가 없다.

## 시스템

| 영역 | 클래스 | 하는 일 |
|---|---|---|
| Core | GameManager | 플레이, 포획, 승리 상태를 관리하고 재시작한다 |
| Core | GameEvents | 포획, 체크포인트, 목표 달성 같은 정적 이벤트 |
| Level | GridMap | 칸 종류 조회: 벽, 낮은 엄폐, 높은 엄폐, 바닥 |
| Level | LevelBuilder | 문자 지도를 읽어 모듈 프리팹을 배치하고 NavMesh를 굽는다 |
| Level | Checkpoint, ObjectiveItem, ExitZone | 재시작 지점, 열쇠, 출구 |
| Player | PlayerInputReader | 입력 액션을 읽어 의도로 바꾼다 |
| Player | PlayerMotor | 화면 기준 이동, 마우스 방향 바라보기 |
| Player | PlayerStance | 서기, 앉기, 달리기와 눈높이 |
| Player | PlayerSenses | 귀 기울이기, 냄새 맡기 모드와 이동 페널티 |
| Player | PlayerExposure | 빛, 소리, 냄새 노출 수치 |
| Camera | QuarterViewCamera | 거리와 각도 고정, 90도 스냅 회전, 시선 앞당김 |
| Camera | OcclusionFader | 카메라와 캐릭터 사이 구조물을 반투명하게 |
| Perception | SenseProfile | 시야각, 어둠과 빛에서의 탐지 거리, 청각 계수, 후각 |
| Perception | Illumination | 램프와 손전등을 등록받아 위치별 밝기를 계산한다 |
| Perception | NoiseSystem | 소리 이벤트 발행과 구독. 크기, 지문, 발생 위치 |
| Perception | ScentField | 격자 냄새 확산과 감쇠, 바람 |
| Perception | LineOfSight | 시선 레이캐스트. 낮은 엄폐는 앉은 눈높이만 막는다 |
| AI | GuardPerception | 시각과 청각으로 의심도를 올리고 내린다 |
| AI | GuardBrain | 상태 기계. 아래 표 참고 |
| AI | GuardMotor, PatrolRoute | NavMeshAgent 래퍼와 순찰 경로 |
| Combat | WeaponDef, WeaponUser | 조준, 예고 레이저, 발사, 발사 소음 |
| Combat | Takedown | 등 뒤에서 조용히 기절시키기 |
| Combat | Capturable | 한 대 맞으면 포획 |
| Visibility | PlayerVision | 원뿔과 몸 주변 원으로 가시 영역 다각형을 계산한다 |
| Visibility | FogMemory | 본 적 있는 칸을 기억한다 |
| Visibility | VisibilityCompositor | 마스크와 기억 텍스처를 만들고 화면에 합성한다 |
| Visibility | EnemyVisibility | 적은 지금 보일 때만 그린다 |
| UI | HUDController | 게이지, 은폐 상태, 목표, 나침반 |
| UI | SenseOverlay | 소리 파문과 냄새 리본 |

## 경비 상태 기계

| 상태 | 들어가는 조건 | 행동 |
|---|---|---|
| Patrol | 기본 | 순찰 경로를 돈다 |
| Suspicious | 의심도 0.3 이상 | 멈춰서 단서 쪽을 본다 |
| Search | 의심도 0.6 이상 또는 소리를 들음 | 마지막 단서 지점으로 가서 둘러본다 |
| Alert | 의심도 1 | 추격한다. 4초 동안 못 보면 Search로 돌아간다 |
| Attack | Alert 중 사거리 안 | 멈춰서 조준 예고 후 발사하거나 근접 공격 |
| Stunned | 기절당함 | 일정 시간 쓰러져 있다 |

의심도는 시야 안에 있으면 오르고 밖에 있으면 천천히 내린다. 거리, 빛, 자세가 오르는 속도를 바꾼다.

## 감각 프로필 기본값

| 프로필 | 시야각 | 어둠 탐지 | 빛 탐지 | 청각 계수 | 후각 |
|---|---|---|---|---|---|
| 인간 경비 | 100° | 5m | 16m | 0.6 | 없음 |
| 손전등 경비 | 100° | 손전등 원뿔 안 16m | 16m | 0.6 | 없음 |
| 경비견 | 120° | 6m | 12m | 1.2 | 흔적 추적 |

모든 값은 프로토타입에서 조정한다.

## 시야 표현

1. PlayerVision이 캐릭터 눈에서 원뿔 방향으로 광선을 부채꼴로 쏜다. 높은 벽과 높은 엄폐는 항상 막고, 낮은 엄폐는 앉았을 때만 막는다. 몸 주변 짧은 원도 더한다.
2. 결과 다각형을 위에서 내려다보는 직교 카메라로 마스크 텍스처에 그린다. 같은 결과를 기억 텍스처에 누적한다.
3. URP 전체화면 패스가 깊이 버퍼로 픽셀의 월드 좌표를 복원한다. 두 텍스처를 읽어서 보이는 곳은 원래 색, 기억한 곳은 어둡고 차갑게, 못 본 곳은 검정으로 칠한다.
4. 적은 PlayerVision이 보인다고 판정할 때만 렌더러를 켠다. 기억 텍스처에는 사람을 남기지 않는다.

HTML 프로토타입은 그림자맵으로 마스크를 만들었다. Unity 버전은 격자 구조를 이용한 2D 다각형 방식으로 바꾼다. 더 정확하고 싸고, 기억 누적도 자연스럽다.

## 입력

| 키 | 동작 |
|---|---|
| WASD | 화면 기준 이동 |
| Shift | 달리기 |
| C | 앉기 전환 |
| 마우스 | 바라보는 방향 |
| 우클릭 | 조준 |
| 좌클릭 | 사격, 조준 중에만 |
| F | 뒤에서 기절 |
| Q 누르고 있기 | 귀 기울이기 |
| E 누르고 있기 | 냄새 맡기 |
| Z, X 또는 휠 클릭 드래그 | 카메라 90도 회전 |
| R | 재시작 |

## 한 프레임의 흐름

```
입력 ─▶ PlayerMotor, PlayerStance ─▶ 발소리 ─▶ NoiseSystem ─▶ GuardPerception
                                  └─▶ 냄새 흔적 ─▶ ScentField ─▶ 경비견만 읽음
Illumination ─▶ PlayerExposure(빛) ─────────────────────────────▶ GuardPerception
GuardPerception ─▶ 의심도 ─▶ GuardBrain ─▶ GuardMotor / WeaponUser ─▶ 포획
PlayerVision ─▶ 가시 다각형 ─▶ FogMemory, VisibilityCompositor, EnemyVisibility
HUDController ◀─ PlayerExposure, GuardBrain 상태, GameManager
```

## 마일스톤

| 단계 | 내용 | 끝났다고 보는 기준 |
|---|---|---|
| M1 뼈대 | 문자 지도 레벨 빌더, 늑대 대신 캡슐, 이동, 앉기, 달리기, 쿼터뷰 카메라, 입력 에셋 | 창고를 걸어 다니고 카메라를 90도씩 돌릴 수 있다 |
| M2 인간 경비 | 순찰, 시각 원뿔, 빛 반영, 약한 청각, 의심도, 상태 기계, 포획, 체크포인트, 열쇠, 출구 | 한 판을 처음부터 끝까지 할 수 있다. 여기부터 "대략 돌아가는" 상태다 |
| M3 시야 표현 | 가시 다각형, 기억 텍스처, 전체화면 합성, 적 숨김, 가림 처리 | 원뿔 밖이 검게 보이고 본 곳은 흐리게 남는다 |
| M4 감각 | 소리 파문, 냄새 리본, HUD 게이지, 바람 | 귀와 코로 벽 너머 적을 읽을 수 있다 |
| M5 전투 | 조준, 소음기 사격, 경비 조준 예고, 뒤에서 기절 | 들켜도 싸워서 빠져나갈 수 있다 |
| M6 모델 | glTFast 추가, 늑대 GLB 배치, 인간 경비 임시 모델 | 캡슐 대신 캐릭터가 보인다 |

그 뒤로 경비견, 애니메이션, 셀 셰이딩, 모듈 아트를 붙인다.

## 규칙

- 네임스페이스는 `ByAWhisker.<영역>`을 쓴다. 한 파일에 한 클래스.
- 레이어는 Floor, Wall, HighCover, LowCover, Player, Enemy를 쓴다. 시선과 가시 영역 레이캐스트는 레이어 마스크로 거른다.
- 튜닝 값은 ScriptableObject나 `[SerializeField]`로 둔다.
- 매 프레임 폴링보다 이벤트를 먼저 쓴다. 소리와 포획은 이벤트로 전달한다.
