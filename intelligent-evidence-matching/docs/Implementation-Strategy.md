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
2. Store raw files.
3. Split multi-page documents (for example PDFs/TIFFs) into page units and treat single-image files as one-page units.
4. Create a tracking ID per upload batch.

**Output**
- Batch with file/page metadata.

---

### 2. Text Extraction Layer (Cost-Aware First Pass)
**Service name:** `PreExtractionService`

**Responsibilities**
1. Try direct text extraction from supported digital documents.
2. If image/scanned page, run Tesseract OCR.
3. Return normalized page text and basic quality score.

**Output**
- Page text and OCR confidence/quality.

---

### 3. Pass-1 Routing (Page-Level)
**Service name:** `PageRoutingService`

**Responsibilities**
1. Apply rule-based scorecard on extracted text.
2. Classify each page as `invoice`, `shipping`, `other`, or `unknown`.
3. Save routing confidence.

**Output**
- Page class + confidence.

---

### 4. Pass-2 Grouping (Document Stitching)
**Service name:** `DocumentGroupingService`

**Responsibilities**
1. Merge adjacent pages with the same class.
2. Stitch non-adjacent continuation pages using anchors.
3. Mark uncertain joins as provisional and send them to unknown triage.

**Output**
- Logical document groups (not just individual pages).
- Group-level confidence and provisional triage flags (`ready-for-cu` / `unknown-triage`).

---

### 5. CU Orchestration Layer
**Service name:** `CuOrchestrationService`

**Responsibilities**
1. Route grouped docs to the correct CU analyzer.
2. Handle retries/timeouts.
3. Collect raw CU responses.
4. Re-score unknown-triage groups and either auto-route or send to review queue.

**Routing policy**
- `invoice` -> prebuilt invoice analyzer.
- `shipping/other` -> prebuilt general document analyzer.
- `unknown` -> prebuilt general analyzer fallback, then re-classify:
   - if confidence improves, auto-route to invoice/shipping/other path.
   - if still low confidence, mark `review-needed` and send to manual review queue.

**Output**
- Raw CU results per grouped document.

---

### 6. Normalization Layer
**Service name:** `ExtractionNormalizationService`

**Responsibilities**
1. Convert CU invoice fields to common schema.
2. Parse CU general markdown/tables/lines into the same schema.
3. Add field-level confidence and source traces.

**Output**
- Unified extraction model for all document types.

---

### 7. Matching + Linking Layer
**Service name:** `DocumentLinkingService`

**Responsibilities**
1. Link shipping docs to invoice docs (PO/reference/date/address rules).
2. Score each link.
3. Produce matched pairs and unmatched lists.

**Output**
- Match graph with confidence.

---

### 8. Validation + Decision Layer
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

### 9. Review + Audit Layer
**Service name:** `ReviewQueueService`

**Responsibilities**
1. Keep uncertain cases for human review.
2. Track manual overrides.
3. Store audit trail for every decision.

**Output**
- Review tasks + audit history.

---

### 10. API + UI Integration Layer
**Service name:** `VerificationApiService`

**Responsibilities**
1. Expose endpoints for upload, status, results, and review actions.
2. Stream progress to UI.
3. Return consistent response contracts.

**Output**
- Frontend-ready APIs.

---

## Suggested Delivery Phases
1. **Phase 1:** Intake + PreExtraction + Pass-1 Routing.
2. **Phase 2:** Pass-2 Grouping + CU Orchestration.
3. **Phase 3:** Normalization + Linking.
4. **Phase 4:** Validation + Review Queue + UI progress.

## Operational Metrics Reference
The detailed measurement and decision logic for scoring and routing is maintained in:

- [Scoring-and-Decision-Metrics.md](./Scoring-and-Decision-Metrics.md)
