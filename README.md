# POC: AIT Evidence Matching Program

Static (GitHub Pages) proof-of-concept to demo the Audit Intelligence Test (AIT) evidence matching workflow.

Goal: showcase how auditors can map sample rows to supporting documents and create clickable “Verified” cells that open the document and highlight the selected evidence region.

## Scope (MVP)
- Zero-AI / no Document Intelligence (manual evidence selection)
- Static frontend + OCR API backend

## Dev
- App source lives in `web/`
- Run locally (recommended): `npm start`
- Or: `cd web` then `npm ci` then `npm start`

## Run GitHub Pages + Local API (Developer)
Use this mode when frontend is hosted on GitHub Pages but OCR/API runs on your machine.

1. Start local API server:
	- `cd server`
	- `npm ci`
	- `npm start`
2. Verify API health:
	- `http://localhost:3001/health`
3. Keep runtime config set to localhost:
	- `web/public/runtime-config.js`
	- `apiBaseUrl: 'http://localhost:3001'`
4. Open the GitHub Pages app URL.

Notes:
- Backend must remain running while using the app.
- This mode works for the same machine/browser where the backend is running.

## API Configuration
Frontend API base URL is runtime-configurable via:

- `web/public/runtime-config.js`

Default:

- `apiBaseUrl: 'http://localhost:3001'`

This works for GitHub Pages demos when the backend is running locally.

### Switch to hosted backend later
When a hosted backend URL is available, change only this value:

- from: `http://localhost:3001`
- to: `https://<your-hosted-api-domain>`

Then redeploy GitHub Pages (push to `master` or run workflow manually).

## Deploy
GitHub Pages deploys from `master` via `.github/workflows/deploy-pages.yml`.

### Required secret (corporate registry)
The Angular app dependencies install from the corporate JFrog npm registry.

- Add GitHub repo secret: `JFROG_NPM_TOKEN`
- Value: an npm auth token that has read access to `https://tr1.jfrog.io/tr1/api/npm/npm/`
