# M3 설계 · 시야 표현

앞 단계는 [DESIGN-M2.md](DESIGN-M2.md)에 있다. 전체 구조는 [ARCHITECTURE.md](ARCHITECTURE.md)를 본다.

## 이 단계에서 만드는 것

캐릭터가 지금 보는 곳만 밝게 보이고, 본 적 있는 지형은 어둡게 남고, 한 번도 못 본 곳은 완전히 검게 된다. 적은 지금 보일 때만 그려진다. 카메라와 캐릭터 사이를 가리는 구조물은 투명해진다.

판정 방식은 격자 기반 2D 가시 다각형이다. 캐릭터 눈에서 부채꼴로 광선을 쏴서 보이는 영역을 폴리곤으로 만들고, 그 폴리곤을 위에서 내려다보는 직교 카메라로 텍스처에 그린다. 화면 합성은 그 텍스처를 읽어서 한다.

## 병렬 작업 분할

| 갈래 | 담당 파일 |
|---|---|
| A 가시 영역 | `Scripts/Visibility/PlayerVision.cs`, `Scripts/Visibility/VisionPolygon.cs` |
| B 화면 합성 | `Scripts/Visibility/VisibilityMaskRenderer.cs`, `Shaders/VisionMemoryAccumulate.shader`, `Shaders/VisionComposite.shader` |
| C 통합 | `Scripts/Visibility/EnemyVisibility.cs`, `Scripts/Cameras/OcclusionFader.cs`, 렌더러 피처 등록, 씬 연결 |

A와 B는 서로의 파일을 건드리지 않는다. Unity 에디터와 git은 통합 담당만 쓴다.

## 계약

### A. 가시 영역

```csharp
namespace ByAWhisker.Visibility
{
    /// 부채꼴 광선으로 보이는 영역을 계산한다. 순수 계산과 메시 생성만 한다.
    public static class VisionPolygon
    {
        /// 눈 위치에서 시작해 정면 원뿔과 몸 주변 원을 합친 폴리곤 정점을 구한다.
        /// 결과는 월드 좌표이고, 첫 정점은 중심(눈 위치)이다. 반환값은 채운 정점 수.
        public static int Build(
            Vector3 origin, float yawDegrees, float coneDegrees, float radius,
            float nearRadius, int rayCount, LayerMask blockers,
            Vector3[] buffer);

        /// buffer의 정점으로 삼각 팬 메시를 채운다. mesh는 재사용한다.
        public static void FillMesh(Mesh mesh, Vector3[] buffer, int count, Vector3 origin);
    }

    public class PlayerVision : MonoBehaviour
    {
        public Mesh PolygonMesh { get; }   // XZ 평면의 삼각 팬. 정점은 월드 좌표에서 Origin을 뺀 값
        public Vector3 Origin { get; }     // 눈 위치를 수평면에 내린 점
        public float Yaw { get; }          // 바라보는 각도(도)
        public float Radius { get; }
        public bool IsVisible(Vector3 worldPoint);  // 원뿔 각도, 거리, 시선 세 가지로 판정
        public event System.Action PolygonUpdated;  // 폴리곤을 다시 만든 프레임에 부른다
    }
}
```

PlayerVision의 직렬화 필드는 자유지만 다음은 반드시 둔다. `motor`(PlayerMotor), `stance`(PlayerStance), `blockers`(LayerMask), `lowCover`(LayerMask), `coneDegrees`(기본 100), `radius`(기본 26), `nearRadius`(기본 2.4), `rayCount`(기본 96).

규칙은 이렇다.

- 눈 높이는 `PlayerStance.EyeHeight`를 쓴다.
- 서 있으면 `blockers`만 시선을 막고, 앉아 있으면 `blockers`에 `lowCover`를 더해 막는다.
- 정면 원뿔은 `coneDegrees`를 `rayCount`로 나눠 쏜다. 몸 주변 원은 360도를 성기게 쏴서 `nearRadius`까지만 본다.
- 광선이 막히면 맞은 지점보다 조금 더 바깥을 정점으로 쓴다. 벽면이 보이게 하기 위해서다.
- 매 프레임 할당을 만들지 않는다. 버퍼와 메시를 재사용한다.

### B. 화면 합성

```csharp
namespace ByAWhisker.Visibility
{
    public class VisibilityMaskRenderer : MonoBehaviour
    {
        public RenderTexture MaskTexture { get; }    // 지금 보이는 영역. 흰색이 보이는 곳
        public RenderTexture MemoryTexture { get; }  // 본 적 있는 영역의 누적
        public Vector4 WorldBounds { get; }          // (minX, minZ, sizeX, sizeZ)

        /// 레벨 크기에 맞춰 텍스처와 직교 투영 범위를 잡는다. 통합 담당이 시작할 때 부른다.
        public void Configure(Vector3 worldMin, Vector3 worldSize);

        /// 재시작 등으로 기억을 지울 때 부른다.
        public void ClearMemory();
    }
}
```

동작은 이렇다.

- `PlayerVision.PolygonMesh`를 위에서 내려다보는 직교 행렬로 `MaskTexture`에 흰색으로 그린다. 매 프레임 지우고 다시 그린다.
- `MemoryTexture`는 지우지 않고 `MaskTexture`와 최댓값으로 합쳐 누적한다.
- 전역 셰이더 값으로 다음을 넘긴다. `_BW_VisionMask`, `_BW_VisionMemory`, `_BW_VisionBounds`.
- 텍스처 해상도는 레벨 1m당 4픽셀 정도를 기본으로 하되 직렬화 필드로 조정할 수 있게 한다.

셰이더 두 개를 만든다.

- `Shaders/VisionMemoryAccumulate.shader`: 두 텍스처를 받아 픽셀마다 큰 값을 출력한다. 블릿용이다.
- `Shaders/VisionComposite.shader`: URP 전체화면 패스용이다. 깊이 버퍼로 픽셀의 월드 좌표를 복원하고, 월드 XZ를 `_BW_VisionBounds`로 0..1 UV로 바꿔 두 텍스처를 읽는다. 출력은 이렇게 섞는다.
  - 보이는 곳: 원래 색 그대로
  - 기억한 곳: 원래 색을 어둡고 차갑게 (밝기 0.22배, 푸른 기운)
  - 못 본 곳: 완전한 검정
  - 두 영역 경계는 부드럽게 한다.

URP 17 기준으로 전체화면 패스는 `Blit.hlsl`의 `FullscreenVert`와 `_BlitTexture`를 쓴다. 렌더러 피처 등록은 통합 담당이 한다.

### C. 통합

```csharp
namespace ByAWhisker.Visibility
{
    public class EnemyVisibility : MonoBehaviour   // 적마다 붙는다
    {
        public bool Visible { get; }
    }
}

namespace ByAWhisker.Cameras
{
    public class OcclusionFader : MonoBehaviour    // 카메라에 붙는다
    {
    }
}
```

## 규칙

- 계약의 공개 멤버 이름과 서명을 바꾸지 않는다.
- 매 프레임 할당을 만들지 않는다.
- 레이어 번호를 코드에 적지 않는다. LayerMask는 직렬화 필드로 받는다.
- `.meta` 파일을 만들지 않는다.

## 통합 메모

씬과 렌더러 설정은 다음과 같이 잡혀 있다.

- `PC_Renderer`에 Full Screen Pass Renderer Feature를 `VisionComposite`라는 이름으로 등록했다. 머티리얼은 `Materials/VisionComposite.mat`, 주입 시점은 Before Rendering Post Processing, Requirements는 Depth다. URP 애셋의 Depth Texture도 켰다.
- `VisionMask` 오브젝트가 `VisibilityMaskRenderer`를 들고 있고, 누적 머티리얼은 `Materials/VisionAccumulate.mat`, 해상도는 1m당 16픽셀이다.
- `GameBootstrap`이 시작할 때 레벨 크기에 4m 여유를 더해 `Configure`를 부르고, 적에게 `PlayerVision`을 물려 준다. 재시작 이벤트가 오면 `ClearMemory`로 기억을 지운다.

작업하면서 걸린 곳 세 가지를 적어 둔다.

- URP 17의 전체화면 정점 함수 이름은 `Vert`다. `FullscreenVert`는 이 프로젝트의 패키지에 없다. `Blit.hlsl`은 core 패키지에 있고, 그 앞에 URP `Core.hlsl`을 먼저 포함해야 `TEXTURE2D_X`가 정의된다.
- 직교 투영은 위아래를 뒤집어 잡아야 한다. 그러지 않으면 마스크가 세로로 뒤집혀 그려져서 합성 셰이더가 엉뚱한 곳을 읽는다.
- 셰이더의 `Properties`에 이름을 선언하면 머티리얼 값이 전역 텍스처를 덮어쓴다. 기억 누적용 이전 텍스처를 프로퍼티로 두면 항상 검정이 들어와 누적이 되지 않는다. 전역으로만 받는다.
