# M6 — 늑대 모델과 애니메이션

플레이어를 캡슐에서 늑대 병사로 바꾼다. 서고, 걷고, 뛰고, 앉아 걷고, 쏘고, 쓰러진다.

## 구성

- `Assets/_Game/Models/Wolf/WolfRig.fbx` — 믹사모가 리깅한 몸. Humanoid, 뼈 46개.
- `WolfTPose.glb` — 원본 모델. 이제는 텍스처(`image_0`)를 꺼내 쓰는 용도로만 남아 있다.
- `Wolf_Idle / Wolf_Walk / Wolf_Run / Wolf_CrouchWalk / Wolf_PistolShot / Wolf_HitFall .fbx` — 동작 여섯.
- `Assets/_Game/Data/WolfAnimatorMixamo.controller` — 상태 기계.
- `Assets/_Game/Data/WolfUpperBody.mask` — 상체만 남기는 마스크.
- `Assets/_Game/Materials/WolfBody.mat` — URP Lit, 베이스맵은 `image_0`.

애니메이터 파라미터는 `Speed`(float), `Crouch`(bool), `Fire`(trigger), `Down`(trigger) 넷이다.
`PlayerAnimator`가 이 넷만 다루므로 클립을 갈아 끼워도 코드는 그대로다.

## 레이어

- Base Layer — `Move` 블렌드 트리(Idle 0 / Walk 2.2 / Run 5), `CrouchWalk`, `HitFall`.
  `HitFall`은 AnyState에서 `Down`으로 들어간다.
- UpperBody — 마스크를 쓰고 `NoShot`(빈 상태)과 `PistolShot` 둘. `Fire`로 들어가고 끝나면 돌아온다.
  자기 자신으로 가는 전이가 있어서 연사할 때 동작이 처음부터 다시 나온다.
  사격을 따로 떼어 놓은 이유는 걸으면서도 쏴야 하기 때문이다.

## 걸린 곳

자동 리깅(VARCO)은 이 모델에서 뼈 이름과 위치가 어긋나 머리가 몸통에 박혔다. 두 번 다시 해도 같은
결과였다. 유니티에서 FBX로 내보내(`com.unity.formats.fbx`) 믹사모에 올리는 쪽으로 돌아섰다.

늑대가 흰색으로 나왔다. `WolfTPose.glb` 안에 텍스처가 둘 있는데 `image_1`은 평균색이 (0.49, 0.49, 0.98)
이라 노멀맵이다. 색은 `image_0`(평균 0.12, 0.13, 0.14)에 있다. 텍스처를 고를 때는 평균색을 찍어 본다.

발이 1m 가까이 떠 있었다. 예전 GLB는 피벗이 몸통 가운데라 `WolfVisual`을 0.944 올려 두었는데,
믹사모 FBX는 피벗이 발밑이다. 이제 0이다. 키는 1.67m, 스케일 1.847.

쓰러지는 동작에서 엉덩이가 선 높이 그대로 남았다. 루트 모션을 끄고 쓰기 때문에 세로 움직임이
루트로 빠지면 버려진다. 클립의 `lockRootHeightY`(Bake Into Pose)를 켜야 포즈 안에 남는다.

걷기와 달리기, 앉아 걷기에서 몸만 앞으로 쭉 나갔다가 루프마다 제자리로 튀었다. 앉으면 옆으로
계속 밀렸다. 원인은 XZ의 Bake Into Pose(`lockRootPositionXZ`)를 켜 둔 것이다. 켜면 앞으로 나아가는
움직임이 포즈 안에 남아서 몸이 실제로 이동한다. 꺼야 XZ가 루트 모션으로 빠지고, 루트 모션을 쓰지
않으니(`applyRootMotion = false`) 그대로 버려져 제자리 걸음이 된다. 믹사모에서 In Place로 받지 않은
클립이면 반드시 꺼야 한다. 끈 뒤 몸통이 기준점에서 벗어나는 폭은 4cm 이하로 줄었다.

세로(`lockRootHeightY`)는 반대로 켜 둔다. 꺼서 루트로 빼면 발이 바닥에서 뜬다.

`heightOffset`은 값이 커질수록 몸이 **내려간다**. 올리려고 0.38을 줬다가 더 깊이 박혔다. 지금은 -0.11이다.

사람 동작을 두꺼운 늑대에 옮기면 누운 자세에서 몸이 바닥을 뚫는다. 뼈로는 못 맞춰서 클립 전체를
조금 올려 타협했다. 쓰러지는 도중에는 13cm쯤 뜨지만 그때는 몸이 공중에 있어서 티가 안 난다.

원본 사망 동작은 4.33초인데 앞 1.4초가 서서 비틀거리기만 한다. 재시작까지 1.5초(`GameManager.respawnDelay`)
뿐이라 42~96프레임만 잘라 쓰고 상태 속도를 1.2로 올렸다. 잘린 길이 1.8초 ÷ 1.2 = 1.5초로 딱 맞는다.

`HitFall`은 끝나도 그 자리에 머문다. `PlayerAnimator`가 `GameEvents.RunReset`을 받아 `Move`로 되돌리고
남은 트리거를 지운다. 안 하면 되살아난 늑대가 누운 채로 돌아다닌다.

## 남은 것

- 경비는 아직 캡슐이다. 사람 모델이 필요하다.
- 꼬리는 움직이지 않는다. Humanoid 리그에 꼬리 뼈가 없다.
- `DownedBodyVisual`은 적을 코드로 눕힌다. 경비에 모델이 붙으면 이 사망 클립으로 바꿀 수 있다.
