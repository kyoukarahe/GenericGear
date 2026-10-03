# Bounded mechanical connection modes

역할: **Normative contract — bounded source profile**.
필수 선행: [finite winding connection](FINITE_WINDING_CONNECTION_CONTRACT.md)의 source,
수치 품질, atomic path, resource 및 persistence 전부. 여기서는 **같은 기구**에 적용하는
위상/활성 구속/입력 소유권/순서/저장을 정의한다. 사용은 [일반 예제](../examples/winding/README.md).
기존 guarded calendar/indexed `HybridStateSnapshot`, E2 static hold/basis와 다른 계약이다.

## Definition and active constraints

Profile `bounded-winding-mechanical-modes-v1`, format version `0.1`.
`MechanicalModeDefinition`은 immutable connection source, 허용 mode 집합,
alignment offset을 소유한다. `DriveCapture`는 초기 모드로 필수다.
Source와 policy에서 DefinitionId를 만든다. 전환은 source를 변경하지 않는다.

q = E1 driver, w = E1 passive shaft, s = E2 sun native shaft coordinate,
C = fixed carrier native coordinate, p = E2 planet world-signed coordinate.
항상 기존 E2 contact와 실제 suffix contact가 활성이다. 그 위에 다음 조건만 추가/제거한다.
아래 `p-port`, `s-port`는 source가 선언한 실제 port ID/축 방향/offset을 적용한 값이다.

| Mode | 추가 활성 조건 | 다음 구간 independent inputs | 자유도 처리 |
|---|---|---|---|
| DriveCapture | `s=w+H` | q, p-port | q에서 E1을 풀고 p와 함께 기존 E2 law 평가 |
| Released | w/s coupling 없음 | q, s-port, p-port | 분리된 s의 다음 값을 명시적으로 요구 |
| WorldCarrierLock | `s=w+H`, `C=Lworld` | q만 | 기존 E2 law에서 p-port를 유도 |
| PlanetRelativeLock | `s=w+H`, `ep*p-C_common=Lrelative` | q만 | 기존 E2 relative law에서 p-port를 유도 |
| DirectionRestrictedDrive | `s=w+H`, 선언한 q 진행 부호 | q, p-port | 전체 segment가 허용 부호인지 검사 |

`ep`는 carrier-local planet frame의 Z 부호이고 `C_common`은 기존 E2의 zero-ray/sign을
적용한 carrier 공통좌표다. Relative lock은 world planet을 고정하는 것이 아니다.
Body mounting phase나 display matrix 값을 상대 좌표 대신 쓰지 않는다.
World lock은 움직이지 않는 carrier 축의 native coordinate를 포착한다.

예제의 +Z carrier / -Z planet에서는 `Lrelative=-p-C`, `Lworld=C`다.
초기 E2 rank가 이미 검증된 두-input profile이며 새 모드는 위 조건으로 실제 unknown을
정확히 제거한다. general constraint switcher나 임의 비선형 rank network가 아니다.
잠금 law의 제거 계수가 0이어서 결정되지 않으면 임의 값을 만들지 않는다.

## Events and phase policy

| Event | 동작 |
|---|---|
| Release | w/s coupling과 mode lock/direction을 해제; 현재 frame 유지 |
| Capture | 현재 실제 `H=s-w`를 이상적으로 포착하여 DriveCapture |
| AlignCapture | `s-w == authored alignmentOffset`가 증명될 때만 결합 |
| LockWorldCarrier | 현재 C를 저장하고 world lock; coupling은 유지 |
| LockPlanetRelative | 현재 native-relative 공통 좌표를 저장하고 relative lock |
| CapturePositive / CaptureNegative | 현재 H를 포착하고 각각 양/음 진행만 허용 |

Capture는 치형 충돌·동기화 충격을 계산하지 않는 **ideal phase capture**다.
H가 numerical이면 그 provenance/상관관계를 유지한다. K나 기존 body mount를 수정하지 않는다.
AlignCapture는 별도 정책이다. Exact affine cancellation으로 차가 exact 0일 때만 성공하고,
exact nonzero이면 `AlignmentConflict`, numerical term이 남으면 `GuardIndeterminate`다.
작은 epsilon으로 phase mismatch를 감추거나 residue를 0으로 clamp하지 않는다.

Released의 현재 snapshot에 s가 알려져 있어도 다음 구간의 s가 정해진 것은 아니다.
입력이 없으면 `Underdetermined`다. 관성 지속/즉시 정지/직전값 재사용을 추론하지 않는다.
Released에서 lock만 요구하면 `MissingCouplingBoundary`; 먼저 명시적 Capture가 필요하다.
같은 instant에서 release/capture를 연속 event로 요청할 수 있다.

DirectionRestrictedDrive는 명시적 운동 방향 제한이지 토크로 engagement가 정해지는
physical freewheel/ratchet이 아니다. 이 profile은 두 축이 convex guide 내부인 같은-side
support branch이므로 w는 q와 같은 방향으로 진행한다. q 경로의 부호를 제한한다.
역방향은 `DirectionConflict`이며 자동 Release하지 않는다. 다시 결합/방향 변경하려면
명시적 event를 보낸다. 힘에 따른 overrunning, 마찰/충격/접촉력 조건은 미지원이다.

## Ordered atomic request

`StartMechanicalModes(definition, initialPlanetPortTurns)`
→ `AdvanceMechanicalModes(definition, state, request)`.

Request는 `Id`, `SourceId`, `ExpectedStateId`, `ExpectedRevision`, ordered segments를 포함한다.
각 segment는 q + complete independent port vector + optional exact observations + events다.
Mode별 input port set은 `MechanicalModeEngine.RequiredInputPorts`로 얻는다.
관측값은 구속을 덮어쓰는 입력이 아니다. 추가 unknown port는 거부하며 known-but-numeric
값을 exact 관측과 일치한다고 판정할 근거가 없으면 `GuardIndeterminate`다.

1. 요청 source/state/revision와 최근 idempotency ledger를 검사한다.
2. 현재 mode의 완전한 independent vector를 검사한다. 누락은 `Underdetermined`,
   mode에서 이미 유도하는 p를 별도 input으로 주면 `ModeInputOwnershipConflict`다.
3. **이전 mode로** 구간 도착점을 평가한다. E1 전체 q segment도 검산한다.
4. 그 도착점에서 events를 ordinal 0,1,… 순서로 적용한다. 각 global sequence는
   이전 EventCursor+1이어야 한다. 동일 ID/잘못된 ordinal/sequence는 거부한다.
5. 그 다음 segment는 변경된 mode의 input ownership을 적용한다.
6. 전부 성공한 경우에만 revision을 1 증가시키고 모든 samples/최종 state/ledger를 게시한다.

Active contact handoff와 event 위치의 차가 1e-10 turn보다 작아 순서를 수치로 확정하지
못하면 `GuardIndeterminate`로 돌려보낸다. 요청자가 event를 별도 명확한 경계에 놓아야 한다.
많은 시간을 한 번에 건너뛴다는 이유로 중간 geometry failure를 생략하지 않는다.

실패는 새 `State=null`, `Samples=[]`, `AppliedSegments=0`, `Remainder=전체 segments`,
`LastValidSnapshot=요청 전 state`다. 성공한 prefix event도 commit하지 않는다.
오류/거부 상태에서 마지막 유효 화면을 새 성공 frame으로 표시하지 않는다.

Idempotency는 최근 **16 request IDs**의 bounded ledger다. 같은 ID/같은 payload는
`AlreadyApplied`, 적용 0, 새 State 없음, 최초 ResultStateId를 별도 `ReplayedStateId`로 돌려준다.
다른 payload는 `IdempotencyConflict`; ledger 밖 오래된 재시도는 원래 state/revision이
맞지 않으므로 `StaleSnapshot`다. 무한 기간 exactly-once 저장소를 주장하지 않는다.

## State, numerical provenance and canonical storage

State에는 DefinitionId, semantic StateId/HistoryId, actual frame, mode, H, lock reference,
direction, revision, EventCursor, recent ledger가 있다. Frame은 unwrapped actual coordinates,
quality, stable material IDs와 optional matrices를 가진다. Source + mode + H/L가 위 표의
활성 제약을 완전히 결정한다. Unknown/free future motion을 임의 pose로 채우지 않는다.

Semantic ID는 exact coefficients, latent IDs, q, contact branch labels, history에 결속하며
binary64 estimate의 플랫폼 차이는 제외한다. **Raw payload SHA-256와 semantic ID는 다르다**.
Numeric H/L의 source latent가 이후 state에서 다시 쓰이므로 correlation을 잃지 않는다.

새 형식은 `gear-invest.mechanical-mode-recording`과 `gear-invest.mechanical-mode-replay`다.
Recording은 source artifact + policy + initial input + ordered requests + checked results다.
`ReadMechanicalModeRecording`은 처음부터 그 요청들을 current source에 재적용하고 상태·
이력·ledger·결과를 비교한다. 저장된 캐시를 보고 mode를 맞췄다고 선언하지 않는다.
`SaveMechanicalModeRecording` / `LoadMechanicalModeRecording` / `RebuildMechanicalModeReplay`
로 fresh process에서 최종 상태를 복원하고 새 request를 추가할 수 있다.

이 단계의 영속 checkpoint는 **bounded history recording**이다. 임의 JSON State 객체를
독립적인 trusted snapshot으로 import하거나 history compaction으로 제한을 우회하지 않는다.
새 source/profile/policy이면 ID 불일치로 거부한다. 묵시적 migration은 없다.
Backward playback은 저장된 과거 whole sample 선택이지 Release/Capture event의 역실행이 아니다.

| Resource | 한도 |
|---|---|
| 한 request | ≤16 segments, 전체 ≤16 events |
| 한 recording | ≤32 requests, segments+events 총 ≤64 |
| Live state | revision ≤4096; recent ledger16; inherited latent64 |
| Event order | request 내 ordinal0–15; global sequence는 현재 cursor 다음 |
| Wire | ≤4 MiB, depth40, nodes131072; JSON 실패 시 잘라 저장하지 않음 |

Inherited exact numeric bounds, source resource bounds, numerical tolerance와 `NotPerformed`
도 모두 적용한다. Source와 request는 immutable이고 같은 유효 입력 순서에서 같은 semantic
결과를 지향한다. Cross-OS bit-identical numerical estimates 또는 event-root certification은 없다.

## Web and version boundary

`@gearinvest/replay/mechanical-modes`의 `readMechanicalModeReplay`, `verifyIntegrity`,
`selectSample`, `selectAttempt`, `originalBytes`가 별도 consumer entry다.
계약상 mode/input declaration, source/owner/frame, ordinal data, revision chain, atomic result,
readonly byte·sample·raw digest를 검사한다. **C# mode engine을 브라우저에서 실행하지 않는다**.
Raw digest PASS와 stored Finalized 표시는 current source mechanical rebuild가 아니다.

SDK가 내보낸 같은 whole frame의 모든 부품을 동시에 표시한다. Web의 slider/재생/정지,
localStorage 저장·새 페이지 복원은 consumer 기능이다. Mechanical event를 UI에서 새로
발생시켰다는 증거로 세지 않는다. 새 q/events는 C# ordinary consumer로 작성한다.
기존 E2/assembly/indexed-calendar 형식·API·고정 fixture는 변경하지 않는다.
이번 지원은 source-only 추가 경로이며 Unity 신규 consumer/registry/binary package와 별개다.
브라우저에서 새 입력을 계산하는 별도 [runtime 계약](MECHANICAL_RUNTIME_CONTRACT.md)은
이 readonly recording/replay 계약을 변경하지 않는다.
