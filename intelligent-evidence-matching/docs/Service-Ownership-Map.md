# Service Ownership Map

This map enforces clear service boundaries for parallel developer execution.

## Core Services
- `DocumentIntakeService`
  - Input: uploaded files
  - Output: page units + batch metadata

- `PreExtractionService`
  - Input: page units
  - Output: extracted text + extraction quality

- `PageRoutingService`
  - Input: extracted text
  - Output: page class (`invoice`/`shipping`/`other`/`unknown`) + confidence

- `DocumentGroupingService`
  - Input: page classes + anchors
  - Output: grouped logical docs + triage flags (`ready-for-cu`/`unknown-triage`)

- `CuOrchestrationService`
  - Input: grouped docs
  - Output: raw CU responses
  - Routing:
    - invoice -> prebuilt invoice analyzer
    - shipping/other -> prebuilt general analyzer
    - unknown -> general fallback, then reclassify or mark `review-needed`

- `ExtractionNormalizationService`
  - Input: CU raw responses
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
