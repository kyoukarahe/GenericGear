# Finite winding → actual E2b → spur → mechanical modes

역할: **Usage guide — bounded source-only 구현**.
필수 선행: [권취·후단 계약](../../docs/FINITE_WINDING_CONNECTION_CONTRACT.md),
[모드·저장 계약](../../docs/MECHANICAL_CONNECTION_MODES_CONTRACT.md).
Public `0.1.0-rc.public03.1`에 포함된다. 과거 `public02.1`이나 설치형 패키지의 기능으로
소급 해석하지 않는다. 새 입력의 browser-local 실행은 [runtime 예제](../runtime/README.md)를 따른다.

## Ordinary consumer, not a substitute solver

[WindingExample.cs](WindingExample.cs)는 형상과 실제 source를 작성한다.
[ModeExample.cs](ModeExample.cs)는 같은 source에 ordinary mode 요청을 보낸다.
두 파일은 SDK solver가 아니며 검증 fixture를 로드하지 않는다.
[일반 console](dotnet/Program.cs)은 `GearInvestSdk` public API와 여섯 DLL을 참조한다.
[프로젝트](dotnet/Winding.Consumer.csproj)에 production ProjectReference나 내부 test helper가 없다.

| 계층 | 역할 |
|---|---|
| Layout | 명시된 pin seats, material geometry, bounded numerical E1 query/path |
| Engine | E2 exact law 재사용, actual suffix, numeric provenance, same-source mode state |
| Serialization.Json | source·recording·replay의 별도 형식과 current rebuild |
| GearInvest facade | 작성/검증/저장/재열기/추가 진행의 일반 소비 API |
| `/winding`, `/mechanical-modes` | producer의 readonly whole samples·identity·digest 소비 |
| 정적 SVG 예제 | SDK 행렬을 깊이별로 표시, slider·실패 표시·localStorage |

## Build and create

PowerShell 7, .NET SDK + net8 runtime, Node≥22.12와 기존 TypeScript dependencies가 필요하다.
검증 환경의 정확한 버전은 [공개 검증 범위](../../docs/VERIFICATION.md)에 있다.
처음 dependency cache가 없을 때만 해당 .NET restore와 root의 `npm ci --ignore-scripts`를 실행한다.
통상 실행은 incremental build/cache를 재사용하며 clean/전체 복사를 요구하지 않는다.

저장소 root에서:

```powershell
dotnet build src/GearInvest/GearInvest.csproj -c Release
dotnet build examples/winding/dotnet/Winding.Consumer.csproj -c Release
npm run build

$consumer = 'examples/winding/dotnet/bin/Release/net8.0/Winding.Consumer.dll'
$drive = 'artifacts/e123-implementation/example'
$modes = 'artifacts/e123-implementation/example-modes'
dotnet $consumer produce $drive
dotnet $consumer restore $drive
dotnet $consumer modes-produce $modes "$drive/source.json"
dotnet $consumer modes-restore $modes
dotnet $consumer modes-resume $modes
```

`produce`/`modes-produce`/`modes-resume`는 **create-only**다. 기존 파일에 덮어쓰지 않는다.
반복할 때 새 폴더를 선택하거나 읽기 전용 `restore`를 쓴다. 사용자 파일 삭제는 하지 않는다.
각 명령은 별도 OS process다. Reopen은 모든 source/request를 current SDK로 다시 검증한다.
Resume은 restored final StateId/Revision에 새 request를 추가하고 `resumed-*` 파일을 만든다.

`produce <out> 2 20 false`는 pitch/모듈/좌표 2배와 20-link chain,
`produce <out> 1 21 true`는 circular pin-seat guide다. 세 입력 모두 production query를 사용한다.
형상·port·boundary가 다르면 [작성 예제](WindingExample.cs)의 typed definition을 바꾼다.
SDK가 특정 시계나 숫자 100/10,21에만 분기하는 경로는 없다.

이미 빌드된 source-only SDK를 별도 폴더에서 참조할 때는
`-p:GearInvestSdkBin=<six-DLL-directory>`를 지정한다. 라이브러리는 UI/렌더러에 의존하지 않는다.
Unity plugin 재포장/IL2CPP 검증이나 NuGet/npm 배포까지 수행한 예제는 아니다.

## Authoring and values

기본 예제는 21 links/22 pins, pitch2 mm, centers(-30,0,-20)/(0,0,-20),
CW12/CCW12 convex seats, ±1/8 driver turn, 최대45° 관절이다. Output은 ±0.15 turn finite lift.
같은 축선의 independent w/s 사이 coupling을 명시한다.
실제 E2b sun100/planet10, module1, nonzero body mounts, -Z planet, integer K=1이다.
Carrier의 고정축에 20/40 외접 suffix를 z20에 배치하고 output axis를 -Z로 둔다.

```text
C = (10/11)*s - (1/11)*p + 9/110
o = C/2 - 3/16 = (5/11)*s - (1/22)*p - 129/880
```

이는 **이 source에 대해 SDK가 계산한 값**이며 Web 구현에 들어있는 시계 공식이 아니다.
실제 source가 바뀌면 SDK가 law를 다시 만든다. E1 w는 numerical estimate이고 일반적인
connected C/o도 `NumericResidualOnly / error bound=null`이다. 같은 latent가 exact하게
소거되는 순간에만 exact readback이 가능하다. 큰 누적 Rational은 표시 회전과 별개다.

Drive 기록은 감기·정지·풀기·되감기와 p만 바꾸는 두-input 예를 포함한다.
두 방향 경계 밖 요청은 전체가 거부된다. Mode 기록은 release, 독립 s 이동, capture,
world/relative locks, explicit positive direction, 실패, 동시 순서, retry를 포함한다.
예제의 event는 C#에서 작성한다. 브라우저 slider를 움직이는 것은 새로운 mechanical event가 아니다.

## Actual Web entry

정적 server를 저장소 root에 **loopback만** 바인딩한다.

```powershell
python -m http.server 8765 --bind 127.0.0.1
```

열 주소:

- 기본 drive: `http://127.0.0.1:8765/examples/winding/web/`
- Mode: `http://127.0.0.1:8765/examples/winding/web/?replay=/artifacts/e123-implementation/example-modes/replay.json`
- Fresh resumed mode는 같은 query의 파일을 `resumed-replay.json`으로 바꾼다.

[index.html](web/index.html) / [main.js](web/main.js)는 현재 source build의 별도 package entries를
가져온다. Package-only 소비 앱에서는 같은 named exports를 `@gearinvest/replay/winding`,
`@gearinvest/replay/mechanical-modes`에서 import하면 된다. Three.js/DOM은 SDK 내부에 없다.

화면의 `내 replay`로 지원 format 파일을 고를 수 있다. `기록된 sample` slider/이전/다음은
전체 frame을 선택한다. `요청 결과 → 결과 확인`에서 실패를 선택하면 기구를 지우고
applied0/lastValid/remainder를 구분해 보여준다. 성공한 마지막 frame을 실패 결과로 쓰지 않는다.
`재생 위치 저장 → 새 페이지에서 복원`은 same replay bytes와 sample index를 localStorage로 복원한다.
Raw hash PASS도 current C# mechanical rebuild는 아니다. 화면에 두 경계를 그대로 표시한다.

## Reproduce acceptance and limits

`node scripts/prepare-winding.mjs`는 세 입력의 ordinary DLL create/restore 및 mode
restore/resume를 별도 process에서 실행한다. 이어
`node tools/e123-implementation/check-replay.mjs generated/winding`으로 공개 reader의
전체 frame 대조와 rehashed negative 검사를 수행한다. 출력은 create-only다.
실제 Chrome와 미검증 환경은 [공개 검증 범위](../../docs/VERIFICATION.md)에서 구분한다.

이 예제는 제조사 Breguet7047의 내부 구조·GLB를 복원/검증한 결과가 아니다.
Tooth/shaft/arm/swept solids·접촉력·마찰·토크 보상·스프링·조속 성능은 미검증/미지원이다.
Smooth cone/helix, multilayer/slack, 일반 E2a artifact suffix, arbitrary carrier network,
무한 state history, Web authoring solver, interpolation은 여기서 지원하지 않는다.
