# CU-Based Verification: Implementation Strategies

## Architecture Snapshot
- Frontend: GitHub Pages (static UI)
- Backend: Azure API
- Intelligence: Azure Content Understanding (CU)

## Service-by-Service Plan

### 1. Upload + Intake Layer
**Service name:** `DocumentIntakeService`

**Responsibilities**
1. Accept uploaded files.
2. Split multi-page documents (for example PDFs/TIFFs) into page units and treat single-image files as one-page units.
3. Preserve source-file mapping for CU-first grouping.
4. Create a tracking ID per upload batch.

**Output**
- Batch with file/page metadata.

---

### 2. Grouping Layer
**Service name:** `DocumentGroupingService`

**Responsibilities**
1. Create logical groups from intake pages by source file boundary.
2. Initialize group triage state for CU execution.
3. Keep grouping deterministic and independent from local extraction/routing heuristics.

**Output**
- Group-level metadata and triage flag (`ready-for-cu`).

---

### 3. CU Orchestration Layer
**Service name:** `CuOrchestrationService`

**Responsibilities**
1. Dispatch every group to first-pass analyzer target (`document-prebuilt`).
2. Keep dispatch policy centralized and explicit.
3. Mark groups for downstream CU execution.

**Output**
- CU dispatch decisions.

---

### 4. CU Execution Layer
**Service name:** `CuExecutionService` + executors

**Responsibilities**
1. Execute first-pass analysis using configured analyzer priority:
   - `CU_FIRST_PASS_ANALYZER_ID`
   - fallback `CU_GENERAL_ANALYZER_ID`
   - default `prebuilt-documentFields`
2. Classify by CU-first-pass result into `invoice`, `shipping`, or `other`.
3. Run second pass only when needed:
   - `invoice` -> `prebuilt-invoice`
   - `shipping` -> `prebuilt-purchaseOrder` only when shipping intent is purchase-order-like
   - `shipping` that is bill-of-lading-like stays on first-pass analyzer output
4. Reuse first-pass output for `other`.
5. Apply markdown/text fallback extraction for sparse first-pass field output.
6. Return technical and extraction statuses separately.

**Output**
- CU extraction payloads with `classifiedAs` and `analyzerIdUsed`.

---

### 5. Normalization Layer
**Service name:** `ExtractionNormalizationService`

**Responsibilities**
1. Convert CU-native fields into the normalized internal schema.
2. Use CU-derived class (`classifiedAs`) for normalized `pageClass`.
3. Preserve CU confidence while providing normalized fallback confidence.

**Output**
- Unified extraction model for all document types.

---

### 6. Matching + Linking Layer
**Service name:** `DocumentLinkingService`

**Responsibilities**
1. Link shipping docs to invoice docs (PO/reference/date/address rules).
2. Score each link.
3. Produce matched pairs and unmatched lists.

**Output**
- Match graph with confidence.

---

### 7. Validation + Decision Layer
**Service name:** `VerificationDecisionService`

**Responsibilities**
1. Run business checks (amount tolerance, required fields, etc.).
2. Mark result status:
   - `auto-verified`
   - `review-needed`
   - `failed`
3. Generate explainable reasons.

**Output**
- Final decision package.

---

### 8. Review + Audit Layer
**Service name:** `ReviewQueueService`

**Responsibilities**
1. Keep uncertain cases for human review.
2. Track manual overrides.
3. Store audit trail for every decision.

**Output**
- Review tasks + audit history.

---

### 9. API + UI Integration Layer
**Service name:** `VerificationApiService`

**Responsibilities**
1. Expose endpoints for upload, status, results, and review actions.
2. Stream progress to UI.
3. Return consistent response contracts.

**Output**
- Frontend-ready APIs.

---

## Suggested Delivery Phases
1. **Phase 1:** Intake + Grouping.
2. **Phase 2:** CU Dispatch.
3. **Phase 3:** Normalization + Linking.
4. **Phase 4:** Validation + Review Queue + UI progress.

## Operational Metrics Reference
The detailed measurement and decision logic for scoring and routing is maintained in:

- [Scoring-and-Decision-Metrics.md](./Scoring-and-Decision-Metrics.md)
