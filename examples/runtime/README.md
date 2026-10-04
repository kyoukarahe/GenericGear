# Static browser runtime and current-state checkpoint

역할: **Usage guide**. 먼저 [지원·신뢰·자원 계약](../../docs/MECHANICAL_RUNTIME_CONTRACT.md)을 읽는다.
새 입력을 계산하는 C# WASM 경로다. 기존 `/winding`, `/mechanical-modes` sample reader는 그대로다.
Public source `0.1.0-rc.public05.1`의 실행 경로다. NuGet/npm binary package 배포는 아니다.

## Build and open

필요 환경: .NET SDK10.0.401(검증 버전), .NET8 host, Node≥22.12, PowerShell7.
기존 NuGet cache를 재사용한다. wasm-tools/AOT 설치는 현재 interpreter 경로의 전제조건이 아니다.

```powershell
./tools/e123-runtime-checkpoint/Build-Web.ps1
node tools/e123-runtime-checkpoint/serve.mjs artifacts/e123-runtime-checkpoint/web/publish/wwwroot 5187
```

`http://127.0.0.1:5187/example/`을 연다. 서버는 정적 GET/HEAD만 제공한다. 모든 runtime/예제 파일은
일반 정적 호스팅으로 옮길 수 있다. `_framework`의 해시가 붙은 파일·dotnet.js·worker/client/store와
example을 함께 배포하며 `.wasm` MIME type을 유지한다. 초기 리소스와 source 로드에는 네트워크가
필요하다. 로드 이후 계산에는 서버가 필요 없다. 서비스워커 기반 offline cold-start는 제공하지 않는다.

1. Driver q와 표시된 independent port 입력을 **turn의 정수 또는 분수**로 입력한다.
2. `입력 실행`은 실제 새 q의 finite path, complete vector와 선택한 도착 후 event를 계산한다.
3. `Release` 다음에는 sun과 planet을 모두 직접 정한다. Lock에서는 q만 제공한다.
4. `체크포인트 저장`은 IndexedDB의 현재 slot을 CAS로 갱신한다. `새 페이지에서 복원` 뒤
   새로운 입력을 다시 실행한다. 두 탭의 stale 저장은 충돌로 거부된다.
5. `정적 로드 후 네트워크 차단`은 페이지/Worker fetch를 막는다. 현재 세션에서 입력/저장은
   계속 가능하다. 새 페이지의 runtime 자원 로드는 별도다.

렌더 loop를 위한 영구 event는 없다. SVG는 SDK가 함께 반환한 owner별 행렬을 소비한다.
각 Z plane은 독립 auto-fit 표시이며 물리적 크기 재설계가 아니다. 계산 실패 시 새 결과 그림을
지우며 마지막 정상 identity를 명시한다. Consumer의 UI 값은 식·ratio·chain 정답이 아니다.

## C# and independent consumption

### Selected real-reel drive boundary

Read the [selected-boundary contract](../../docs/SELECTED_DRIVE_BOUNDARY_CONTRACT.md).
The same249-link source explicitly selects its conical reel as prescribed and
coupling owner. Supply its exact native unwrapped turns and all mode-required
inputs; the shared SDK computes both winding and the connected transmission.

```powershell
./tools/e123-runtime-checkpoint/Build-Web.ps1 -SelectedDrive -OutputDirectory artifacts/selected-drive/web
node tools/e123-runtime-checkpoint/serve.mjs artifacts/selected-drive/web/publish/wwwroot 5189
```

Open `http://127.0.0.1:5189/example/`. The roles must read prescribed/coupling
`spatial/passive`, passive `spatial/driver`; their original ID names do not decide
their roles. Use the same new-input/event/save/new-page-restore controls below.
`create-selected-drive-example <new-source-path>` authors this source.
`inspect-spatial <source-path> -1 23/20` independently checks its2.15-turn
forward/stop/reverse/rewind path and pin/joint geometry. The old spatial example's
default lower bound is not a promise of a solution for the selected opposite branch.
Rebuild checks canonical source identity and refuses mismatched existing inputs.
Use a separate output for each profile. Checkpoint3.0 is explicit; old1.0/2.0
keep their original meaning. Follow the [device pack](../../docs/DRV_DEVICE_ACCEPTANCE.md).

### Spatial winding and composition

For the new profile, read [the spatial contract](../../docs/SPATIAL_WINDING_COMPOSITION_CONTRACT.md)
and build into a separate create-only source directory:

```powershell
./tools/e123-runtime-checkpoint/Build-Web.ps1 -Spatial -OutputDirectory artifacts/spatial/web
node tools/e123-runtime-checkpoint/serve.mjs artifacts/spatial/web/publish/wwwroot 5188
```

Open `/example/` on that port. The same input/event/save/new-page flow evaluates
the new geometry in shared C#, not in JavaScript. Isometric/XY/XZ projections
consume the same current snapshot. For a prefix check, serve
`artifacts/spatial/web/publish` and open `/wwwroot/example/`.
`create-spatial-example <new-source-path>` authors the ordinary249-link source;
`inspect-spatial <source-path>` runs multi-turn forward/stop/reverse/rewind and
independent pin/bend checks. The source remains create-only on rebuild; use a new
explicit output directory for a different authored profile, never overwrite an
old planar source and assume it became spatial. See the [device pack](../../docs/SPATIAL_RUNTIME_DEVICE_ACCEPTANCE.md).

### Native consumer

[console consumer](dotnet/Program.cs)는 여섯 DLL을 참조한다(ProjectReference 없음).
한 줄 JSON command를 stdin으로 받아 한 줄 결과를 stdout으로 반환한다.
`load`, `prepare`, `commit`, `cancel`, `checkpoint`, `restore`, `snapshot`, `dispose`는 SDK host의
직접 경로다. `create-example <new-source-path>`만 예제 기구를 작성하며 기존 파일을 덮어쓰지 않는다.
API 사용자는 `PrepareMechanicalRuntime`/`MechanicalRuntimeSession`을 직접 호출할 수도 있다.

```powershell
dotnet build src/GearInvest/GearInvest.csproj -c Release
dotnet build examples/runtime/dotnet/Runtime.Consumer.csproj -c Release
# prepared source-only candidate에도 동일 명령. 외부 DLL 소비는 다음 속성을 명시한다.
dotnet build examples/runtime/dotnet/Runtime.Consumer.csproj -c Release -p:GearInvestSdkBin=<six-DLL-directory>
```

WASM은 동일 source projects를 직접 빌드한다. 개발 checkout의 Private DLL을 복사해 실행하는
경로가 아니다. 별도 후보 디렉터리의 소스 빌드와 일반 실행 검증은 결과 보고서에 구분한다.
초기 배포는 untrimmed interpreter여서 다운로드/메모리 비용이 작다고 약속하지 않는다.
모바일 지원 가능성과 실제 기기 수용은 다르다. [공개 검증 범위](../../docs/VERIFICATION.md)를 따른다.

## Persistence and limits

Checkpoint는 전체 과거 recording이 아니다. 현재 구속/수치 원본을 source에서 다시 계산한다.
유효 과거로의 rollback 방지나 삭제된 전체 경로의 증명은 하지 않는다. Active provenance64,
최근 retry16, counter128digits, request16segments/events, 문서4MiB의 경계는 숨기지 않는다.
서로 독립인 lock 원인이 계속 늘면 resource refusal이며 임의 numeric 합병으로 우회하지 않는다.
물리 q의 finite domain은 epoch/restore 후에도 유지된다. 기존 planar profile의 작은 범위는 그대로이며,
새 spatial profile만 명시된3D guided winding·수백 링크·다회전 범위를 제공한다.

기존 replay는 계속 readonly recording 소비이며 이 runtime과 검증 범위가 다르다.
