# Finite winding and actual differential suffix

역할: **Normative contract — bounded source profile**.
선행: [기존 E2b](BOUNDED_DIFFERENTIAL_CONTRACT.md)의 실제 owner, cardinal frame,
complete vector, contact registration, source 검증과 한도. 이전 E2a/E2b·assembly·chain
형식을 소급 변경하지 않는다. 사용은 [일반 DLL/Web 예제](../examples/winding/README.md),
모드 전환은 [별도 상태 계약](MECHANICAL_CONNECTION_MODES_CONTRACT.md)을 따른다.
Source release `0.1.0-rc.public03.1`에 포함되며 설치형 binary package 지원과 구분한다.

## Supported profiles and ownership

이번 첫 연결은 다음 **하나의 실제 source**다.

```text
prescribed q shaft → finite material chain → passive w shaft
                                             ║ coaxial coupling s = w + H
                                           E2b sun s + prescribed world-signed planet port p
                                             ↓ actual fixed carrier shaft C
                                           one external spur → output shaft o
```

- E1 geometry: `finite-planar-chord-station-winding-v1`.
- Suffix: `one-e2-fixed-axis-external-spur-suffix-v1`.
- Connected source: `finite-winding-differential-spur-v1`.
- E3는 같은 source의 활성 연결만 변경한다. 축·gear·contact K를 새로 만들지 않는다.
- `w`와 `s`는 같은 축선 위 **서로 다른 회전자**다. 동일 Shaft ID로 합치지 않는다.
  coupling ID와 실제 두 owner, sun/planet input port IDs가 source에 남는다.
- 독립 prescribed planet 입력은 carrier-local 축에서의 **world-signed 회전 port**다.
  고정 공간에 있는 모터 축이 planet에 붙어 있다는 물리 배치를 주장하지 않는다.
- 새 연결 API는 `DifferentialRequest`를 받는다. E2a의 기존 API/결과는 그대로이며,
  E2a artifact를 그대로 받는 suffix overload나 일반 assembly 편입은 이번 범위가 아니다.

## Finite material-chain admission

사용자가 두 shaft/center, planar pin-seat polygons, pitch, link count, contact limits,
최대 굽힘, q/output의 **유한 unwrapped 구간**, 초기 q와 양쪽 contact 수를 작성한다.
좌표 단위 mm, 각도 단위 turn. Geometry는 명시적 binary64 입력이다.

두 guide는 평면 등현 길이의 엄격한 convex polygon이다. Driver seats는 CW,
output seats는 CCW이며 각 shaft 원점이 자기 polygon 내부에 있어야 한다.
각 vertex의 두 인접 edge를 포함한 전체 polygon을 검사한다. Star polygon,
축이 바깥에 있는 guide, 닫히지 않는 pitch, 두 guide bounding circle의 중첩을 거부한다.
두 shaft는 +Z/+X zero ray, 같은 plane Z를 사용하며 실제 중심과 일치해야 한다.

연결된 chain의 끝 pin00/last는 각각 seat0에 붙는다. 첫/마지막 link 방향도 첫 seat
chord로 정의되므로 attachment direction을 임의의 화면 각도로 덮어쓸 수 없다.
각 pin joint는 공통 +Z 회전 관절이며 twist가 없다. 자유 구간은 **팽팽한 직선 경계 조건**이다.
중력/장력/접촉력으로 그 경계 조건을 유도하는 동역학 모델은 아니다.

material link/pin ID는 `chainId/link-XX`, `chainId/pin-XX`이고 개수는 각각 N/N+1이다.
양 끝·전체 material ordering은 유지된다. 한 링크가 guide에서 빠져나올 때 free span에
같은 material link가 들어가며, 새 링크를 생성하거나 주기적으로 처음 pin으로 감지 않는다.
Adjacent contact labels가 동일 pose를 나타내는 handoff만 canonical 첫 후보로 정한다.

지원하는 것은 **이산 고정 pitch 체인**이다. smooth cable·평균 반경 고정비·무한 belt가 아니다.
매끄러운 원뿔, helix, 다층 권취, arbitrary 3D groove, twist joint, slack chain은 미지원이다.
원을 등분한 pin-seat guide도 이 chord model이며 analytic continuous-cylinder cable이 아니다.

## Solver and numeric policy

`binary64-residual-upper-support-v1`: 최대 25 contact pairs × 2 circle roots를 계산한다.
각 contact pair의 자유 링크 수 `f=N-ma-mb`는 2 이상이어야 한다.
고정 길이 `f*pitch`와 output seat의 회전 원을 이용하고, 두 polygon에 대한
upper supporting span, joints, finite output lift, pin geometry로 후보를 검사한다.
acos 입력을 clamp하여 없는 해를 만들지 않는다.

| 확인값 | 허용 / 의미 |
|---|---|
| 모든 chord/pin pitch 잔차 | ≤ 1e-9 mm |
| 전체 길이 잔차 | ≤ N × 1e-9 mm |
| 끝점·부착 방향·guide pin·support 잔차 | 각각 ≤ 1e-9 mm |
| Planarity | 같은 Z plane을 구성하므로 0 mm |
| 최대 joint bend | authored limit + 1e-7 degree; guide 자체는 authored limit 이내 |
| 동일 pose handoff tie | 모든 pin 차 ≤ 1e-7 mm, output 차 ≤ 1e-8 turn |
| 서로 다른 후보 | `AmbiguousBranch`; 임의 선택 없음 |

성공 명칭은 `ResidualChecked`, 품질은 `NumericResidualOnly`다.
`SolutionErrorBoundTurns = null`. 작은 길이 잔차는 **각도 해 오차의 인증 경계가 아니다**.
근접 특이점에서 해를 못 정하면 `NumericalUnresolved`, 허용 후보가 없으면
`NoAdmissibleBranch`다. 후자는 이 bounded profile의 결과이며 모든 기계에 대한 불가능 증명이 아니다.
별도의 necessary endpoint-distance bound 실패만 `InfeasibleProvedByEndpointDistanceBound`다.

`ValidateSegment`는 q의 단조 구간에서 analytic guard 후보를 분할한다. 인접 support handoff,
circle root 존재/접점, finite output lift 경계를 최대 200 equations/400 roots로 나눈 뒤
각 경계와 내부를 residual 검사한다. 단순 endpoint 검사로 큰 입력을 승인하지 않는다.
수치상 겹친 root는 coalesce하며, 별도 root 간격이 1e-10 turn보다 작아 순서를 못 정하면
`GuardIndeterminate`다. 이 역시 binary64 검산이지 interval root/전역 유일성 증명이 아니다.
임의 q의 성공이나 source finalization이 구간 전체의 성공을 미리 보장하지 않는다.
Source `RequiredDomains`에 `bounded-path`를 요구하면 `RequiredNotPerformed`다.
실제 요청이 아직 없는 finalization에서 runtime path 검사 기능을 수행 증명으로 승격하지 않는다.

q 입력은 Rational이다. **명시한 binary64 domain 값을 정확하게 해석한 경계**와 먼저
비교하므로 경계 밖 Rational이 double 반올림으로 안쪽에 들어오지 않는다.
그 뒤 수치 E1 query에 double을 사용한다. 이 변환은 E1 해를 exact로 표시하는 기능이 아니다.

## Exact transfer and pose boundary

`ConnectedMotionValue`는 exact constant와 `(latentId, binary64 estimate, Rational coefficient)`
항들의 합이다. E1 latent ID는 winding source/q에 결속한다. 동일 latent는 함께 계산해
상관관계를 보존한다. **비영점 latent 항이 남으면 결과는 근사**이며 error bound는 null이다.
exact 관계로 모든 latent 항이 소거된 경우에만 `ExactRational`이 된다.
E1 출력값을 decimal/분수로 포장하여 exact E2 snapshot에 넣지 않는다.

기존 E2b admission과 exact `Q*u+b`를 재사용한다. 새 connection은 이 law를 품질을 보존하는
affine 값에 적용한다. Ratio/rank solver를 Web이나 별도 시계 공식으로 복제하지 않는다.
기존 `EvaluateDifferential`의 Rational-only 입력 계약은 변하지 않는다.

Suffix는 기존 carrier 또는 sun의 **실제 고정축 owner**를 빌린다. 움직이는 planet owner,
숫자만 복사한 surrogate shaft, body/shaft mismatch, module/중심거리/축 방향 불일치는 거부한다.
한 단계의 외접 평기어, 새 output shaft, 같은 zero X ray와 proper cardinal frames,
평행/반대 축 방향, 명시적 body mounts·정수 registration K·output reference·port를 검사한다.
부모 E2 plane과 suffix plane은 분리한다. Standalone suffix의 E2 prefix가 있으면 그 plane도
분리한다. Connected winding source는 parent prefix/static holds를 허용하지 않는다.

Suffix 식은 `o = transfer*C + offset`; sign, tooth count, 두 mount와 K에서 exact 계산한다.
Standalone `EvaluateDifferentialSuffix`는 exact 두 입력으로 exact 결과를 반환할 수 있다.
연결된 수치 E1 경로의 suffix는 품질을 올려 쓰지 않는다.

Optional pose는 column-major world 4×4, mm이다. Parent carrier와 상대 planet 회전을
각 한 번만 적용하고 mount도 한 번만 적용한다. Pose node ID는 actual body/material ID와 별개다.
근사 display를 만들 수 없으면 빈 matrices와 `DisplayUnavailableReason`을 반환한다.
그것이 이미 성립한 exact law의 실패나 좌표 0을 뜻하지 않는다.

## API, atomic progression and refusal

`GearInvestSdk`의 ordinary entry:

```text
PrepareWindingConnection / WriteWindingConnectionDraft / ReadWindingConnectionDraft
→ FinalizeWindingConnection / ReadWindingConnectionArtifact
→ EvaluateWindingConnection(analysis, WindingDriveInput(q, planetPortTurns))
→ AdvanceWindingConnection(analysis, previousFrame, orderedSegments)
→ RecordWindingConnection / SaveWindingRecording / LoadWindingRecording
→ ExportWindingReplay / RebuildWindingReplay
```

Source/shape/ownership 오류는 analysis diagnostics 또는 finalization exception이다.
현재 drive snapshot이 다른 source이면 `ForeignSnapshot`; E3 capture/lock 상태를 단순 drive
API에 넣어 H를 초기값으로 돌리려 하면 `InvalidDriveSnapshot`이다.
필요한 q와 planet input은 매번 같은 snapshot의 완전한 값으로 제공한다.

Advance는 **whole request atomic reject**다. 하나의 구간이 실패하면 `Frames=[]`,
`Frame=null`, `AppliedSegments=0`, `Remainder=전체 요청`, `LastValidSnapshot=요청 전 상태`다.
경계에서 clamp/부분 진행/automatically wrap하지 않는다. Out-and-back 요청의 최종값이
안쪽이어도 중간 구간이 경계 밖이면 전부 거부한다. 성공 때만 모든 구간 frame을 게시한다.

## Resource limits and persistence

| 범위 | 한도 |
|---|---|
| guide vertices / material links | 각 4–12 vertices / 4–21 links; 각 contact 1–5 |
| geometry | finite absolute ≤ 1e6 mm; pitch 0.001–1000 mm; bend (0,45] degree |
| finite q/output lifts | 각 폭 < 1 turn, 끝점 절댓값 ≤ 1000 turn |
| connected coordinates | E2b 3 + winding 2 + suffix 1 = 실제 owner 6개 |
| drive request / recording | request당 ≤16 segments; ≤16 requests, 총 ≤64 segments |
| exact values / provenance | input components ≤128 decimal characters; derived ≤1024; latent ≤64 |
| document | ≤4 MiB, JSON depth40, nodes131072; 초과 시 실패, 자르기 없음 |

Version `0.1`, 별도 `gear-invest` format:
`winding-connection-draft`, `winding-connection-mechanism`, `differential-spur-suffix`,
`winding-drive-recording`, `winding-connection-replay`.
분수는 `{numerator:"5",denominator:"11"}`의 canonical 문자열 쌍이다.
Binary64 geometry/estimate/pose는 JSON number이며 분수와 혼용하지 않는다.

Source는 explicit geometry, 실제 owner/ports, unchanged E2 draft/artifact, registration,
exact compiled suffix law와 수행/미수행 검증을 보관한다. Required domain 중 수행하지 않는
항목을 요구하면 finalization을 거부한다. Tooth/shaft/arm/swept solids·동역학은 미수행이다.

C# read는 source를 다시 만들고 모든 요청을 다시 실행한다. Source/IDs/상태/순서/분수는 exact
비교, 재계산 수치값은 절대 1e-10, pose matrix 항은 절대 1e-8 허용 차로 비교한다.
이는 플랫폼간 **복원 비교 정책**이며 solution error bound가 아니다.
Raw stored bytes/recording hash는 보존하며, re-export는 그 bytes의 recorded results와 짝을
유지한다. C#의 `Recording` 객체는 current rebuild 결과다. 숫자 허용 차를 넘으면 거부한다.
Bit-identical cross-OS 계산은 보장하지 않는다. 이번 실행 환경은 검증 보고서에 한정한다.

## Web consumer and compatibility

새 entry `@gearinvest/replay/winding`: `readWindingReplay`, `verifyIntegrity`,
`selectSample(integerIndex)`, `selectAttempt`, `originalBytes`.
복사 소유한 byte/readonly sample을 반환한다. Buffer/subarray로 준 입력도 alias하지 않는다.
Source/schema/owner/material IDs/atomic results/수치 품질/한도와 raw digest를 검사한다.
`browserMechanicalValidation` 및 `currentSourceRebuild`는 계속 `notPerformed`다.

Web은 **기록된 whole snapshot**만 고른다. 새로운 q를 풀거나 두 sample 사이를 보간하거나
후속 기어를 별도 시계식으로 움직이지 않는다. UI의 play/stop은 sample 선택이며 E3 event가 아니다.
다른 입력이 필요하면 C# API로 새 기록을 만든다. 기존 replay reader는 새 format을 거부한다.
새 source/codec는 기존 frozen fixture 재생성이나 기존 package 교체를 요구하지 않는다.
