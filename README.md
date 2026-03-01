# POC: AIT Evidence Matching Program

Static (GitHub Pages) proof-of-concept to demo the Audit Intelligence Test (AIT) evidence matching workflow.

Goal: showcase how auditors can map sample rows to supporting documents and create clickable “Verified” cells that open the document and highlight the selected evidence region.

## Scope (MVP)
- Zero-AI / no Document Intelligence (manual evidence selection)
- Runs fully client-side (static site)

## Dev
- App source lives in `web/`
- Run locally: `cd web` then `npm ci` then `npm start`

## Deploy
GitHub Pages deploys from `master` via `.github/workflows/deploy-pages.yml`.

## Status
Angular scaffold complete; UI implementation next.
