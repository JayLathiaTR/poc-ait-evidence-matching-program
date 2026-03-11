# Scoring and Decision Metrics

This document defines the current measurement and decision logic used by the backend pipeline.

Scope covered:
- intake and grouping metrics
- CU-first classification and analyzer selection
- CU execution and extraction status semantics
- normalization confidence behavior
- CU analyzer target and dispatch status mapping


## 1. Intake and Grouping Metrics
Service: DocumentGroupingService

### 1.1 Group formation rules
- Grouping is source-file scoped from intake page units.
- Group starts as `pageClass = unknown` before CU analysis.
- Group confidence defaults to 0.5 in Phase 1.

### 1.2 Triage flag
- ready-for-cu for all grouped documents, including unknown


## 2. CU Dispatch and Analyzer Policy
Service: CuOrchestrationService

### 2.1 Dispatch policy
- All groups are dispatched with:
  - `initialClass = unknown`
  - `analyzerTarget = document-prebuilt`
  - `dispatchStatus = queued`

### 2.2 First-pass and second-pass policy
- First pass always runs `prebuilt-document`.
- CU first-pass output classifies document intent:
  - `invoice`
  - `shipping`
  - `other`
- Second pass executes only for specialized classes:
  - `invoice` -> `prebuilt-invoice`
  - `shipping` -> `prebuilt-purchaseOrder`
- `other` uses first-pass extraction without second pass.


## 3. CU Execution and Outcome Semantics
Service: CuExecutionService

To avoid ambiguity, CU results use two status dimensions:

- executionStatus: technical call outcome
  - succeeded: analyzer call completed
  - failed: analyzer call failed (transport/auth/server/runtime)
  - skipped: call was not attempted
- outcomeStatus: usefulness of extracted content
  - usable: extraction has enough fields for downstream use
  - low-confidence: extraction exists but weak
  - empty: call succeeded but no usable fields extracted
  - not-run: call not executed

Additional CU-first metadata:
- `classifiedAs`: CU-derived class for downstream normalization
- `analyzerIdUsed`: analyzer ID that produced final payload


## 4. Normalization Confidence
Service: ExtractionNormalizationService

- Normalized fields are mapped only from CU output fields.
- `pageClass` in normalized documents prefers `classifiedAs` from CU results.
- Field confidence is based on CU confidence and normalized fallback computation.


## 5. Practical Interpretation
- Grouping metrics answer: What was submitted for CU processing?
- Dispatch metrics answer: What did CU-first policy schedule?
- CU status metrics answer: Did analyzer calls run and were results usable?
- Normalized confidence answers: How reliable are extracted fields for linking/verification?

These metrics are expected to evolve as more test files and CU outputs are incorporated.
