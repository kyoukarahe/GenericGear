# R2 / R4 input preparation

역할: **입력 준비 / 미구현 경계**. [template](future-inputs.json)은 실행 가능한 source가 아니다.
모든 null은 미제공이며 default0 또는 평면 입력으로 해석하지 않는다.

## 현재 자료와 필요한 것

이 배포에는 실제 시계의3D source·artifact·저장 상태나 사용자 CAD 자료가 포함되지 않는다.
현재 평면 profile을 검증했다고 전체 시계 또는 수백 링크3D 권취를 수용한 것으로 주장하지 않는다.
소비 앱은 아래 명시적 입력을 별도로 제공해야 하며 template은 실행 가능한 기구가 아니다.

R2는 두 실제 shaft frame,3D 단층 홈의 분석식/점/단위/방향/unwrapped domain, 재료 link/pin IDs,
관절 축·bend/twist·pitch·끝점 attachment frame, initial branch/wrapped lengths/free span,
independent input과 finite domain, 요구 품질을 채워야 한다.249/0.85는 참고 규모일 뿐이다.

R4는 각 member의 실제 source와 shaft/port ownership, world/carrier-local/relative 기준계,
mount·integer registration·잇수/모듈, 모드별 필요한 입력, 최종 carrier/output 요구를 채워야 한다.
공전 중인 planet 회전 숫자를 고정축에 복사한 adapter는 물리 연결로 인정하지 않는다.
두 independent inputs를 요구하는 differential에 하나만 주면 미결정이지 자동 잠금이 아니다.
현재 source는 winding→coaxial independent sun coupling→E2b→실제 fixed carrier suffix까지다.
임의 전체 전달열, 일반 E2a suffix, escapement/조속 동역학은 자동 확장되지 않는다.

## 정확히 어느 코드가 바뀌어야 하는가

| 현 제약 | 현재 소유 위치 | 향후 새 profile 작업 |
|---|---|---|
| CW/CCW2D seats, Z-plane,4–21 links,1–5contacts, finite lift<1turn | `FiniteWindingGeometry` / `FiniteWindingSolver` in Layout |3D path/joint/contact 및 수백 링크용 새 bounded geometry admission/query/path proof |
| +Z/+X same-plane shafts, stable chain ordering | `FiniteWindingDefinition` | 실제3D frame·attachment·material coordinate 계약 |
| q→w numeric residual, exact E2/suffix transport | `WindingDifferentialEngine` | 새 geometry result를 typed numeric value로 접속; scalar만 복사한 surrogate 금지 |
| live H/L+current-source witnesses | `MechanicalRuntime` | 새 profile에 필요한 winding/branch/witness를 선언; 숨은 history·shape constants 금지 |
| versioned source/current-state payload | `MechanicalRuntimeJson` | 새 profile codec dispatch와 재평가 validator; unknown profile 거부 유지 |
| Worker transport / UI | browser adapter / optional SVG | core equation 복제 없이 새 source profile transport;3D presentation은 별도 adapter |

이 자료 준비는3D 지원 구현 또는 전체 시계 수용이 아니다. 실제 자료가 오면 새 profile의
admission 가능/모순/미결정과 필요한 추가 구현을 판정한다. 유한 체인을 UI에서 wrap/reset하여
무한 동력으로 바꾸지 않는다. 우회 구동은 실제 별도 입력·연결로 작성해야 한다.
