# AIT Evidence Matching Desktop (.NET 10)

Native desktop shell for AIT Evidence Matching using Avalonia.

## Projects
- `AitEvidenceMatching.sln`
- `AitEvidenceMatching.App`

## Current behavior
- App launches native Avalonia UI.
- OCR server is started automatically in background on launch.
- App checks `http://127.0.0.1:3001/health` before marking runtime healthy.
- OCR server is stopped when app exits.

## Local run
1. Ensure Node.js is installed and available in `PATH`.
2. From repository root run:
   - `dotnet run --project desktop-ait-evidence-matching/AitEvidenceMatching.App`

## Build
- `dotnet build desktop-ait-evidence-matching/AitEvidenceMatching.sln`

## Publish (Windows)
- `dotnet publish desktop-ait-evidence-matching/AitEvidenceMatching.App/AitEvidenceMatching.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`

## Notes
- The desktop app currently uses the existing `server/` Node OCR backend.
- For installer packaging, bundle:
  - desktop app publish output
  - `server/` directory
  - optional bundled Node runtime under `node/node.exe`
