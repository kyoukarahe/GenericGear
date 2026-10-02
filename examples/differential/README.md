# E2b source-only differential consumer

역할: Public source release의 일반 DLL / static Web 사용 안내. 먼저 [bounded contract](../../docs/BOUNDED_DIFFERENTIAL_CONTRACT.md)를 읽는다. 이 소스에서 직접 빌드하며 설치형 NuGet/UPM/npm 패키지 배포를 의미하지 않는다. `DifferentialExample.cs`는 소비자 측 작성 예제이며 SDK 의존성이 아니다.

## 일반 작성과 입력 선택

실제 profile은 free-sun + carrier + external planet이다. 세 actual coordinate를 만들고, 같은 좌표를 참조하는 port와 선택할 독립 input basis를 별도로 지정한다. `CarrierOutputPort`와 `CarrierLocalShaft`는 E2a와 공유하는 immutable geometry DTO다. E2a definition의 hold를 몰래 해제하는 옵션이 아니다.

```csharp
using GearInvest;
using GearInvest.Core;
using GearInvest.Engine;

var sdk = GearInvestSdk.CreateDefault();
var plane = OrientedFrame.Identity;
var local = plane.At(new ExactVector3(55, 0, 0));
var ports = new[] { "sun", "carrier", "planet" }.Select(id =>
    new CarrierOutputPort(id + "-port", id, plane, ExactQuantity.Turns(0)));
var definition = new DifferentialDefinition(
    new OrientedShaft("carrier", plane), new OrientedShaft("sun", plane),
    new CarrierLocalShaft("planet", "carrier", local), plane, 100, 10,
    ExactQuantity.Millimeters(1), ExactQuantity.Turns(0), ExactQuantity.Turns(0),
    ExactQuantity.Turns(0), ExactQuantity.Turns(0), ExactQuantity.Turns(0), 0, ports);
var request = new DifferentialRequest(definition, new[] { "sun-port", "carrier-port" });
var prepared = sdk.PrepareDifferential(request);
var result = sdk.TryFinalizeDifferential(request);
if (!result.IsFinalized) throw new InvalidOperationException(result.Analysis.Status.ToString());
var input = new DifferentialInputSnapshot(new Dictionary<string, ExactQuantity> {
    ["sun-port"] = ExactQuantity.Turns(new Rational(1, 10)),
    ["carrier-port"] = ExactQuantity.Turns(new Rational(1, 4))
});
var value = sdk.EvaluateDifferential(prepared, input); // planet=7/4; relative=3/2
var display = sdk.DisplayDifferential(prepared, input); // optional, independently unavailable
```

`PrepareDifferential`의 `RequestId`는 input basis까지 묶는다. 같은 definition의 다른 basis는 새 request이며 clutch 전환이 아니다. `DifferentialInputSnapshot.Values`와 결과의 `Input`은 complete vector다. Missing/extra/duplicate key와 잘못된 단위는 거부하며 이전 입력이나 zero로 메우지 않는다. 입력값은 topology identity와 구분한다.

### Rank·partial·redundancy

`AnalyzeDifferentialBoundary(definition, IEnumerable<DifferentialBoundary>)`는 port ID, condition ID, exact turn 값을 받아 실제 행을 줄인다. 세 좌표에 값을 모두 주어 consistent redundancy와 inconsistency를 검사할 수 있다. `Reduction.Rank`, `Reduction.FreeColumns`, `Coordinates`, `Diagnostics.Related`를 읽는다. `WitnessRows`는 elimination lineage이며 minimal unsat core라는 보장은 없다.

`Coordinate.Law + FreeTerms`가 전체 표현이다. `IsKnown=false`인 law의 상수/입력 부분만 실제 위치로 쓰면 안 된다. `EvaluateDifferential`은 consistent partial analysis의 알려진 좌표만 반환한다. `DisplayDifferential`은 complete pose를 주지 않는다. Inconsistent analysis는 평가 자체를 거부한다. `ExactLinearResourceException`과 입력 오류를 수학적 inconsistency로 바꾸지 않는다.

`DifferentialHold`는 authored coordinate의 정적 위치다. Sun hold + carrier input은 E2a의 같은 reference/geometry 관측과 일치한다. 일반 two-input request에서 held sun을 다시 독립 input으로 선택하면 arbitrary vector preparation은 거부한다. 같은 sun coordinate의 두 port alias도 독립 입력 두 개가 아니다.

## 저장과 fresh source rebuild

- `Write/ReadDifferentialDraft`: 미결정 draft도 저장 가능. 읽었다고 실행 가능해지는 것은 아니다.
- `TryFinalizeDifferential`: geometry/reference/contact/rank/required scope와 prefix source admission 확인.
- `Save/LoadDifferentialArtifact`, `Read/RebuildDifferentialArtifact`: canonical source와 모든 compiled rows/Q/b/frames/checks를 재계산해 비교.
- `ExportDifferentialReplay`, `SaveDifferentialReplay`, `RebuildDifferentialReplay`: 같은 source 포함, C#에서는 actual source rebuild.
- `WriteDifferentialAnalysis`: readonly 관측 영수증. 저장된 분석을 authoritative result로 읽어오는 API가 아니다.

Storage는 create-only다. 기존 파일을 덮어쓰지 않는다. Exact fraction은 decimal-string numerator/denominator이며 float 근사값이 아니다. Raw SHA-256, request/definition/artifact/replay identity는 서로 다른 binding이다. 해시가 정상이어도 compiled cache가 source rebuild와 다르면 거절한다.

## 실제 DLL 소비 실행

Repo root에서 기존 캐시를 재사용한다. .NET SDK는 `global.json`/project 설정을 따른다. 소비자는 ProjectReference 없이 `src/GearInvest/bin/Release/net8.0`의 6 DLL을 참조한다. 다른 DLL 경로는 `-p:GearInvestSdkBin=<directory>`로 명시한다.

```powershell
dotnet build src/GearInvest/GearInvest.csproj -c Release -p:RestoreLockedMode=true
dotnet build examples/differential/dotnet/Differential.Consumer.csproj -c Release -p:RestoreLockedMode=true
dotnet examples/differential/dotnet/bin/Release/net8.0/Differential.Consumer.dll create generated/my-differential 37 23 signed my-
dotnet examples/differential/dotnet/bin/Release/net8.0/Differential.Consumer.dll reopen generated/my-differential
```

`create`는 존재하지 않는 디렉터리가 필요하다. Modes: `sun-carrier`, `sun-planet`, `carrier-planet`, `prefix`, `hold`, `signed`, `display-unavailable`. `prefix 42/18`은 실제 generated/imported 20:40 spur source를 carrier로 연결한다. `reopen`은 새 process에서 draft를 finalize하고 artifact/replay를 rebuild한 뒤 exact/matrix 관측을 출력한다. 생성 예제는 일반 자료이며 frozen baseline이 아니다.

## Headless Web와 실제 화면

`packages/replay` workspace의 새 import는 `@gearinvest/replay/differential`이다. 정적 예제는 같은 빌드의 `dist/differential.js`를 상대경로로 읽는다. 새 npm 발행은 하지 않는다.

```javascript
import { readDifferentialReplay } from "@gearinvest/replay/differential";
const replay = readDifferentialReplay(ownedUtf8Bytes);
await replay.verifyIntegrity(hostSha256);
const instance = replay.createInstance("viewport");
const value = instance.evaluate({
  "carrier-port": { numerator: "1", denominator: "4" },
  "sun-port": { numerator: "1", denominator: "10" }
});
// value.coordinates / planetCommon / planetRelative are exact.
// value.display.nodes are optional column-major mm world matrices.
instance.dispose(); replay.dispose();
```

Parse는 bounded schema/reference/source-compiled correspondence를 검사하고 bytes를 소유한다. `verifyIntegrity`는 host hash provider만 이용한다. Browser의 mechanical validation/current source rebuild/rank solving은 **notPerformed**다. 행렬이 같이 바뀌고 정상 재해시된 수학적 거짓을 browser가 재해석해서 검증한다고 주장하지 않는다. C# source rebuild가 그 책임을 갖는다.

```powershell
npm run build
npm run labs
```

Vite가 출력하는 loopback 주소의 `/examples/differential/web/index.html`을 연다. `npm run labs -- --base /tools/`로 실행하면 `/tools/examples/differential/web/index.html`에서 같은 페이지를 볼 수 있다. 의존성 설치는 저장소 root의 `npm ci --ignore-scripts`를 사용한다. Node 버전은 root README를 따른다.

각 입력란을 독립적으로 바꾼 뒤 **두 입력 함께 적용**한다. Playback은 선택한 한 입력만 바꾸고 나머지는 호스트가 명시적으로 유지한다. 다른 basis 버튼은 C#에서 준비한 다른 문서를 로드한다. `세션 저장`은 original replay bytes + complete vector + host view 설정을 localStorage에 보관하고 `새 페이지로 열기`에서 digest/parse 후 다시 평가한다. Download 버튼은 원본 replay만 저장한다. OS file chooser는 별도 경로다.

오류·source 교체·미결정 관측은 이전 pose를 현재 결과로 유지하지 않는다. Display conversion 실패는 exact readback과 분리한다. `session.js`의 monotonically increasing epoch와 page의 request generation이 A→B→A 및 늦은 play/load/digest callback을 차단한다. 외부 `marker.json`은 host-owned geometry이며 `inverse(parentWorld) * bodyWorld * assetCorrection`으로 연결한다. 렌더 parent나 pivot 보정이 exact 기구학을 바꾸지 않는다. SVG는 pitch geometry의 평면 투영이며 치형 mesh/GLB importer가 아니다.

## Support / composition boundary

| 항목 | E2a | E2b |
|---|---|---|
| Sun | world-held | 독립 회전 coordinate; optional explicit static hold |
| 입력 | 단일 source root | 최대 2 independent port inputs; numeric 조건은 최대 6 |
| 해석 | single affine transfer | bounded exact rank + vector-affine + partial/free relations |
| 독립 basis | carrier만 | standalone의 세 조합; actual prefix root + sun 검증 |
| 실제 prefix | admitted standalone/one spur | actual one-spur output carrier + independent sun |
| downstream worm/belt/nonlinear | 별도 assembly bridge 없음 | 미지원; 없는 bridge를 숫자 adapter로 위장하지 않음 |
| 공통 geometry | proper cardinal parallel frames, tangency, separated pitch planes | 동일 이상화; 임의 경사/중첩 carrier 미지원 |
| E1/E3 연결 | 후속 계약 필요 | 알려진 좌표와 signed world/local/relative frames, exact turns/mm 경계가 전제 |

E1의 유한 chain state·E3 lock/release/event/mode·일반 유성 topology synthesis·동역학은 이번 구현에 없다. 범용 `BoundedLinearBlock`은 제한된 산술 primitive이며 arbitrary authored matrix에 mechanical approval을 부여하는 API가 아니다.

공개 수용 범위와 미검증 환경은 [verification scope](../../docs/VERIFICATION.md)를 본다. `node scripts/prepare-moving.mjs`는 공개 소스에서 일곱 예제를 다시 생성하고 별도 DLL process로 재열기한다. Private evidence나 이전 DLL은 필요하지 않다.
