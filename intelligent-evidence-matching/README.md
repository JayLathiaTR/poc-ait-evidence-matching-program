# Intelligent Evidence Matching

This project is an isolated implementation track for CU-based evidence verification.

## Purpose
- Build the new architecture without impacting existing WebUI/Desktop behavior.
- Keep service-level separation aligned with the implementation strategy.

## Architecture
- Frontend: static UI (GitHub Pages target)
- Backend: Azure API (local-first during development)
- Intelligence: Azure Content Understanding (CU)

## Folder Structure
- `backend/src/api`: API endpoints and request handlers
- `backend/src/services/document-intake`: file intake and page-unit creation
- `backend/src/services/document-grouping`: intake-file grouping and CU triage staging
- `backend/src/services/cu-orchestration`: CU-first dispatch and analyzer execution
- `backend/src/services/extraction-normalization`: normalize CU outputs into shared schema
- `backend/src/services/document-linking`: invoice-shipping linking and scoring
- `backend/src/services/verification-decision`: decision engine and result statuses
- `backend/src/services/review-queue`: review workflow and audit state
- `backend/src/models`: internal models and domain entities
- `backend/src/orchestration`: pipeline coordinators across services
- `backend/tests`: unit/integration tests
- `frontend/src`: UI application source
- `shared/contracts`: DTOs/contracts shared between backend and frontend
- `docs`: architecture notes, ownership map, and rollout docs

## Delivery Phases (POC)
1. Intake + File-Scoped Grouping
2. CU Dispatch Planning
3. Normalization + Linking
4. Validation + Review Queue + UI progress

## Local Backend Quick Start (Phase 1)
1. `cd intelligent-evidence-matching/backend`
2. `npm install`
3. `npm run dev`

Backend runs at `http://localhost:4010`.

### Smoke Test Endpoints
- Health:
	- `GET http://localhost:4010/api/health`
- Intake + grouping:
	- `POST http://localhost:4010/api/phase1/intake-route`
	- `multipart/form-data` field name: `files` (multi-file supported)
- Intake + grouping + CU dispatch:
	- `POST http://localhost:4010/api/phase2/intake-group-cu`
- Intake + grouping + CU execution + normalization/linking:
	- `POST http://localhost:4010/api/phase3/intake-normalize-link`
- End-to-end verification and review queue:
	- `POST http://localhost:4010/api/phase4/intake-verify`

## CU-First Policy
- Every document group is sent to the configured first-pass analyzer as first pass.
- First pass output classifies group intent (`invoice`, `shipping`, `other`).
- Structured `prebuilt-documentFieldSchema` extraction is the primary source for PO/BOL fields.
- Markdown/text parsing is fallback-only when structured fields are sparse.
- A second pass is applied only when first pass indicates a specialized path and additional extraction is required:
  - `invoice` -> `prebuilt-invoice`
	- `shipping` -> `prebuilt-purchaseOrder` only for purchase-order-like shipping docs that need PO-specialized extraction beyond first-pass payload
	- `shipping` docs that are bill-of-lading-like remain on first-pass analyzer output
- `other` stays on first-pass output.
- `unknown` and low-confidence outcomes are resolved in verification/review flow, not by local extraction/routing heuristics.

### PO/BOL Field Mapping (First-Pass Structured Fields)
- PO and BOL payloads prioritize first-pass structured field objects/arrays (for example `ShipTo`/`Consignee`, `Carrier`, `OrderItems`) over markdown text.
- `CustomerName` is derived from structured recipient name fields (for example `Consignee.Name` / `ShipTo.Name`) when present.
- `ShippingAddress` is composed from structured address fields (for example `Consignee.Address + Consignee.CityStateZip`) when present.

### First-Pass Analyzer Configuration
- First-pass analyzer resolution order:
	- `CU_FIRST_PASS_ANALYZER_ID`
	- `CU_GENERAL_ANALYZER_ID`
	- default: `prebuilt-documentFieldSchema`
- Recommended value: `CU_FIRST_PASS_ANALYZER_ID=prebuilt-documentFieldSchema`

## Development Rule
- No commit/push unless explicit approval is given.
