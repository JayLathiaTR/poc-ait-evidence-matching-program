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
- `backend/src/services/pre-extraction`: PDF text extraction and OCR pre-pass
- `backend/src/services/page-routing`: pass-1 routing scorecard
- `backend/src/services/document-grouping`: pass-2 page stitching and grouping
- `backend/src/services/cu-orchestration`: CU analyzer routing and execution
- `backend/src/services/extraction-normalization`: normalize invoice/general outputs
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
1. Intake + PreExtraction + Pass-1 Routing
2. Pass-2 Grouping + CU Orchestration
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
- Intake + pre-extraction + page routing:
	- `POST http://localhost:4010/api/phase1/intake-route`
	- `multipart/form-data` field name: `files` (multi-file supported)

## Development Rule
- No commit/push unless explicit approval is given.
