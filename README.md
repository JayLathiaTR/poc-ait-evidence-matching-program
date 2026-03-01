# POC: AIT Evidence Matching Program

Static (GitHub Pages) proof-of-concept to demo the Audit Intelligence Test (AIT) evidence matching workflow.

Goal: showcase how auditors can map sample rows to supporting documents and create clickable “Verified” cells that open the document and highlight the selected evidence region.

## Scope (MVP)
- Zero-AI / no Document Intelligence (manual evidence selection)
- Runs fully client-side (static site)

## Dev
- App source lives in `web/`
- Run locally (recommended): `npm start`
- Or: `cd web` then `npm ci` then `npm start`

## Deploy
GitHub Pages deploys from `master` via `.github/workflows/deploy-pages.yml`.

### Required secret (corporate registry)
The Angular app dependencies install from the corporate JFrog npm registry.

- Add GitHub repo secret: `JFROG_NPM_TOKEN`
- Value: an npm auth token that has read access to `https://tr1.jfrog.io/tr1/api/npm/npm/`

## Status
Angular scaffold complete; UI implementation next.
