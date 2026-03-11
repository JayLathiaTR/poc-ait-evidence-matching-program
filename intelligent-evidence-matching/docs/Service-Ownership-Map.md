# Service Ownership Map

This map enforces clear service boundaries for parallel developer execution.

## Core Services
- `DocumentIntakeService`
  - Input: uploaded files
  - Output: page units + batch metadata

- `DocumentGroupingService`
  - Input: intake page units
  - Output: grouped logical docs + triage flag (`ready-for-cu`)

- `CuOrchestrationService`
  - Input: grouped docs
  - Output: CU dispatch decisions (first-pass `prebuilt-document`)

- `CuExecutionService`
  - Input: grouped docs + dispatch decisions
  - Output: CU execution results with `classifiedAs`, `analyzerIdUsed`, extraction payload, and status dimensions
  - Execution policy:
    - first pass: `prebuilt-document`
    - second pass (conditional):
      - invoice -> `prebuilt-invoice`
      - shipping (purchase-order-like) -> `prebuilt-purchaseOrder`
      - shipping (bill-of-lading-like) -> remain on `prebuilt-document`

- `ExtractionNormalizationService`
  - Input: grouped docs + CU results
  - Output: unified normalized extraction model

- `DocumentLinkingService`
  - Input: normalized docs
  - Output: invoice-shipping links + confidence scores

- `VerificationDecisionService`
  - Input: links + business checks
  - Output: `auto-verified`/`review-needed`/`failed`

- `ReviewQueueService`
  - Input: review-needed cases
  - Output: review tasks + audit trail

## Integration Services
- `VerificationApiService`
  - Exposes upload, status, results, and review action endpoints.

## Cross-Cutting Rules
- Keep each service stateless where possible.
- No business-rule duplication across services.
- Contracts live in `shared/contracts`.
- Pipeline coordination lives in `backend/src/orchestration`.
