# 털끝 하나 / By a Whisker

카툰 3D 잠입 액션. 늑대(플레이어)가 우주정거장 창고에서 인간 경비 12명을 피해 열쇠를 들고 출구로 나간다.
양쪽 모두 한 방에 죽는다. 어둠과 시야가 핵심이고, 차별점은 **후각**(집중 상태에서 경비 뒤에 남는 냄새 자취)이다.

- 엔진: Unity 6000.3.11f1, URP 17.3, Input System, AI Navigation(런타임 NavMesh 굽기)
- 씬: `Assets/_Game/Scenes/Warehouse.unity`, 지도: `Assets/_Game/Data/WarehouseMap.asset`(문자 격자, 칸 2m)
- 코드: `Assets/_Game/Scripts/` (어셈블리 `ByAWhisker.Game`, 네임스페이스 `ByAWhisker.*`)
- 문서: `Docs/ARCHITECTURE.md`(구조), `Docs/DESIGN-M1.md` ~ `DESIGN-M16.md`(마일스톤마다 설계, 측정값, 남은 것). 새 작업 전에 최근 DESIGN 문서의 "남은 것"부터 읽는다.

## 사용자와 일하는 방식

- 대화는 한국어 반말, 짧게. 무엇을 바꿨는지, 왜, 측정값, 남은 것 순서로 보고한다.
- **마일스톤은 병렬 에이전트로 나눠 만든다.** `Docs/DESIGN-M<n>.md`에 공개 시그니처를 먼저 정하고, 에이전트마다 겹치지 않는 파일을 준다. 에이전트는 git, Unity MCP, `.meta` 생성, 씬 편집을 하지 않는다. 메인 세션이 컴파일 확인, 씬 연결, 플레이 검증, 커밋을 맡는다(Unity 편집기는 한 주인만 만진다).
- 끝낼 때는 플레이 모드에서 숫자로 검증하고, 스크린샷을 보여 주고, `feat(m16): ...`/`fix(m14): ...` 형식의 한국어 커밋으로 커밋·푸시한다.
- 일반 적은 **인간**이다. 시각 위주, 청각은 약하고 후각은 없다. 동물 적(경비견 등)은 나중에 어려운 단계로만 나온다.
- "쿼터뷰"는 캐릭터를 가까이 따라가는 높은 각도 카메라다(MGS Delta 레거시 시점, Winter Ember). Commandos식 먼 전술 시점이 아니다.
- 사용자가 "다양하게"라고 하면 개수를 늘리는 게 아니라 생김새를 바꾸라는 뜻일 수 있다. 애매하면 확인한다.

## 설계 원칙 (어기면 버그로 본다)

1. **보이는 것과 판정이 같아야 한다.** 손전등 부채꼴은 `LightSource`의 값을 읽고 벽에서 잘린다. 엄폐물은 판정 상자 모양 그대로 보인다(속이 비친 소품을 씌우면 "숨은 줄 알았는데 들킨다"가 된다).
2. **플레이어가 못 보는 정보는 그리지 않는다.** 시야 밖 경비, 손전등 조명, 부채꼴은 숨긴다. 예외는 집중 상태의 냄새 자취와 소리 파문(감각으로 아는 정보).
3. 튜닝 값은 ScriptableObject(`Assets/_Game/Data/`)에 둔다. 코드에 숫자를 박지 않는다.
4. 감각은 공용 시스템이다(NoiseBus, ScentField, Illumination, Sight). 경비와 플레이어가 같은 판정을 쓴다.

## Unity MCP

- 저장소의 `.mcp.json`이 `unity-mcp`(Unity AI Assistant 릴레이, `%USERPROFILE%\.unity\relay\relay_win.exe --mcp`)를 등록한다. 릴레이는 Unity 편집기에서 `com.unity.ai.assistant` 패키지가 설치·실행하므로, 편집기를 열고 AI Assistant에 로그인한 뒤 Claude Code를 연다.
- `Unity_RunCommand` 코드는 `internal class CommandScript : IRunCommand` 형태. `System.Reflection`은 막혀 있다. `UnityEngine.Motion`/`Mesh`는 전체 이름으로 쓴다. `SerializedObject`는 `using UnityEditor`가 필요하다.
- 플레이 모드 변경은 남지 않는다. 편집 모드에서 다시 적용하고 `EditorSceneManager.SaveScene`.
- 새 스크립트는 `AssetDatabase.ImportAsset(path, ForceUpdate)` 뒤에 쓴다.
- **편집 모드에서 `LevelBuilder.Build()`를 부르고 씬을 저장하지 않는다.** 저장된 레벨 루트가 통째로 바뀌어 씬 diff가 수만 줄이 된다. 플레이 때마다 런타임에 다시 짓는다.
- 레벨 루트(`LevelBuilder.Build()`가 Clear로 지운다) 밑에 남겨야 할 것을 두지 않는다. 컨테이너, 탄약 상자, 옷 입히기는 루트 밖에 있다.
- ScreenSpaceOverlay 캔버스는 RT 캡처에 안 찍힌다. 찍을 때만 잠깐 ScreenSpaceCamera로 바꾼다.
- 스크린샷에서 어둠을 빼려면 `PC_Renderer.asset`의 `VisionComposite` 기능을 잠깐 끄고 반드시 다시 켠다.
- 경비는 걸어서 움직여서, 근접 판정을 시험할 때는 플레이어를 경비 자식으로 붙이거나 편집기를 일시정지하고 `EditorApplication.Step()`으로 진행한다.
- 반투명 큐(3000 이상)는 어둠 합성 뒤에 그려진다. 전역 텍스처(`_BW_VisionMask`, `_BW_ScentMap` 등)는 셰이더 Properties에 선언하지 않는다(전역을 가린다).

## 현재 상태 (M16까지)

- 권총 시작 3발 + 탄약 상자 4개(각 +2), 소음기 없음(경비에게 18m), 경보 단계(평상/경계/비상, `StationAlert`).
- 경비 12명이 순찰. 손전등 부채꼴은 시야 마스크와 벽으로 잘린다. 시야 밖 경비의 조명은 꺼진다.
- 늑대 애니메이션은 Mixamo(`Assets/_Game/Models/Wolf/`, `WolfAnimatorMixamo.controller`). 맵 옷은 Kenney Space Station Kit.
- 남은 것: 경비가 아직 캡슐 모양, 숨는 통이 판정 범위보다 작음, 램프 모델 확인, 소리 파문이 실제 청각 거리보다 크게 그려짐, 경비 5·6·7·8 시작 자리가 순찰로에서 멂, 꼬리 애니메이션 없음, 조용히 램프 끄기 미구현, 점수/평가 없음, UI 팩(Kenney UI Pack Sci-Fi) 미적용.
