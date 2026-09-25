# M7 — 총을 게임의 답이 아니게

지금은 총이 모든 문제의 답이다. 사거리 18m로 경비 시야(밝은 곳 16m) 밖에서 쏘고,
총성은 0.97m까지만 들리고, 탄약은 무한이고, 시체는 아무도 신경 쓰지 않는다.
네 갈래로 나눠 고친다. 서로 다른 파일을 건드리므로 병렬로 만든다.

## A. 유한 탄약

`WeaponSettings`에 예비탄을 넣는다. 장전은 예비탄에서 꺼내 채우고, 예비탄이 없으면
장전이 실패한다. 음수는 무한으로 친다. 경비는 무한을 쓰고 플레이어만 유한하다.

- `WeaponSettings.reserveAmmo` (int) — 예비탄. 음수면 무한.
- `Weapon.Reserve` (int) — 남은 예비탄. 무한이면 음수를 그대로 돌려준다.
- `Weapon.HasReserve` (bool)
- `Weapon.Reload()`는 예비탄이 0이면 아무 일도 하지 않는다.
- `Weapon.RefillAmmo()`는 탄창과 예비탄을 모두 처음으로 되돌린다. 재시작에 쓰인다.
- 장전은 탄창을 꽉 채우지 않는다. 예비탄이 모자라면 있는 만큼만 넣는다.

건드리는 파일: `Combat/WeaponSettings.cs`, `Combat/Weapon.cs`, `UI/ControlsOverlay.cs`.

## B. 시체가 단서

경비가 쓰러진 몸을 보면 놀란다. 지금은 시체가 남아도 아무 일이 없다.

- `GuardPerception`이 매 프레임 주변의 `Damageable`을 훑어 `IsDown`인 것을 찾는다.
  시야각과 거리와 시선 막힘은 플레이어를 볼 때와 같은 규칙을 쓴다.
- `GuardPerception.SeesBody` (bool), `GuardPerception.LastBodyPosition` (Vector3).
- `GuardBrain`은 `SeesBody`면 `Search`로 올리고 그 자리를 목적지로 삼는다.
  이미 `Alert`나 `Attack`이면 내리지 않는다.
- 자기 자신이 쓰러진 경우는 세지 않는다.

건드리는 파일: `AI/GuardPerception.cs`, `AI/GuardBrain.cs`.

## C. 시체 옮기기

쏜 자리를 치울 수 있어야 총을 쓴 값을 치른다.

- 새 컴포넌트 `Combat/BodyCarry.cs`. 플레이어에 붙는다.
- G(액션 이름 `Carry`, 이미 `ByAWhisker.inputactions`에 넣어 두었다)로 집고 내려놓는다.
- 들 수 있는 것은 `Damageable.IsDown`이고 사거리 안에 있는 몸이다.
- 드는 동안 느려지고 총을 쏠 수 없다.
- 내려놓으면 그 자리에 그대로 남는다. 겹쳐 놓지 않는다.
- 재시작(`GameEvents.RunReset`)이면 들고 있던 것을 놓는다.

건드리는 파일: `Combat/BodyCarry.cs`(새 파일), `Player/PlayerInputReader.cs`, `Player/PlayerCombat.cs`.

## D. 사거리 (통합 담당이 직접)

`PlayerPistol.asset`의 `range`를 18에서 11로 내린다. 경비 시야 16m 안으로 들어가야
쏠 수 있게 된다. 예비탄도 여기서 정한다.

## 규칙

- 공개 멤버 이름과 서명을 위에 적은 대로 맞춘다. 통합 담당이 이 이름으로 잇는다.
- 맡은 파일 밖은 읽기만 한다.
- 숫자는 코드에 박지 않는다. 직렬화 필드나 ScriptableObject로 뺀다.
- 매 프레임 할당을 만들지 않는다. 물리 질의는 `NonAlloc`을 쓴다.
- 레이어 번호를 코드에 적지 않는다. `LayerMask`를 직렬화 필드로 받는다.
- `.meta` 파일을 만들지 않는다.
- 주석은 한국어로, 무엇이 아니라 왜를 적는다. 주위 코드의 밀도에 맞춘다.

## 통합 메모

에이전트 셋이 각자 맡은 파일만 고쳤고, 파일이 겹치지 않아 충돌은 없었다. 다만 계약이
미처 보지 못한 것 셋이 통합에서 드러났다.

**시체에는 켜진 콜라이더가 없었다.** `Damageable.DisableRemembered()`가 쓰러진 몸의 콜라이더를
전부 껐다. 총알과 시선이 시체를 뚫고 지나가게 하려던 것인데, 그 바람에 물리 질의로 시체를
찾을 수 없어 B(경비가 시체를 봄)와 C(시체를 듦)가 둘 다 막혔다. 끄는 대신 **트리거로 바꿨다**.
`Weapon`의 레이캐스트와 `Sight.HasLineOfSight`는 둘 다 `QueryTriggerInteraction.Ignore`를 쓰므로
뚫고 지나가는 성질은 그대로고, 찾는 쪽만 `Collide`로 훑으면 된다.

**총에 맞은 시체는 `IsDown`이 아니다.** `Damageable.Apply`는 치명상이면 `IsAlive`를 내리고
`IsDown`은 false로 둔다. 계약에 `IsDown`이라고만 적은 탓에 총으로 죽인 시체를 아무도 보지 못하고
들지도 못할 뻔했다. 양쪽 다 `!IsAlive || IsDown`으로 본다.

**시체는 낮은 엄폐물에 늘 가려진다.** 플레이어 쪽 `SightBlockers()`는 플레이어가 앉았을 때만
LowCover를 세는데, 시체는 언제나 앉은 것보다 낮다. `BodyBlockers()`가 LowCover를 늘 더한다.

속도 제한은 `PlayerMotor`가 `BodyCarry.SpeedMultiplier`를 곱하는 한 줄로 이었다. 값은
`BodyCarry`가 들고 있어서 `MovementSettings`를 건드리지 않는다.

씬 연결: `BodyCarry`를 플레이어에 붙이고 `targetLayers`=Enemy, `blockingLayers`=Wall|HighCover|LowCover.
경비 여덟의 `GuardPerception.bodyMask`=Enemy|Player.

## 확인한 값 (플레이)

- 권총 사거리 11m, 탄창 6, 예비 6. 장전이 끝나는 순간 예비가 6에서 5로 줄었다.
- 쓰러진 경비의 콜라이더가 물리 질의에 1개 잡힌다.
- 경비 앞 5m에 시체를 두면 그 경비의 단서 자리가 시체에서 0.27m 안으로 들어온다.
  뒤질 자리를 시체로 잡았다는 뜻이다.
- 시체를 들면 속도 배율 0.55, 부모가 Guards에서 Player로 바뀌고 5m를 걸어도 1.43m 거리를 지킨다.
  내려놓으면 부모가 Guards로 돌아가고 플레이어 발밑 + 0.34m에 놓인다.
