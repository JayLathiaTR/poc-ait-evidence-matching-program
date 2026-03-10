# Scoring and Decision Metrics

This document defines the current measurement and decision logic used by the backend pipeline.

Scope covered:
- quality score measurement
- invoice/shipping rule scoring
- page class mapping
- grouping confidence and triage flags
- CU analyzer target and dispatch status mapping


## 1. PreExtraction Quality Score
Service: PreExtractionService

Quality score is measured from extracted text content (not filename) and normalized to a 0.05 to 0.99 range.

### 1.1 No-text fallback
If no extractable text is available, return:
- extractionMethod = no-text
- qualityScore = 0.05

### 1.2 Computed quality score
For plain-text extraction paths, compute:

weighted =
- lengthScore * 0.22
- + printableRatio * 0.23
- + tokenDensityScore * 0.20
- + keywordDensityScore * 0.10
- + noiseScore * 0.25

finalQualityScore = clamp(weighted, 0.05, 0.99), rounded to 2 decimals.

### 1.3 Metric definitions
- lengthScore: min(1, normalizedTextLength / 1200)
- printableRatio: printableChars / totalChars
- tokenDensityScore:
  - token count from alphanumeric tokenization
  - optimal window roughly 4 to 24 tokens per 100 chars
  - penalized when too sparse or too dense
- keywordDensityScore:
  - counts document-relevant terms (invoice, bill to, amount due, subtotal, sales tax, ship/shipped/shipping, bill of lading, carrier, freight, po, total, date)
  - score = min(1, hits / 8)
- noiseScore:
  - starts from 1 and is penalized for unusual character ratio, excessive single-character token ratio, and long consonant runs (OCR-noise proxy)

Notes:
- qualityScore is an extraction quality indicator, not a document class score.
- qualityScore and page class scores are intentionally independent.


## 2. Page Routing Scores
Service: PageRoutingService

### 2.1 Scoring mechanics
- invoiceScore and shippingScore are computed independently from page text.
- each matched signal adds +2 points.
- an additional +2 points is added when at least two currency hits are found in text.

### 2.2 Invoice signals (current)
Examples include:
- invoice
- invoice id / invoice # / invoice number
- subtotal
- sales tax
- amount due
- due date

### 2.3 Shipping signals (current)
Examples include:
- bill of lading / bol
- ship to / shipped to
- shipping / shipment
- shipper
- consignee
- delivery / delivered / delivering
- carrier
- freight
- trailer
- customer order no/number/#
- po number/no/#


## 3. Page Class Mapping
Service: PageRoutingService

Given invoiceScore and shippingScore:
- invoice if:
  - invoiceScore >= 6
  - and (invoiceScore - shippingScore) >= 2
- shipping if:
  - shippingScore >= 6
  - and (shippingScore - invoiceScore) >= 2
- other if:
  - invoiceScore >= 2 or shippingScore >= 2
- unknown otherwise

Implication:
- invoiceScore = 4 is not enough for invoice; it maps to other (when shippingScore is low).


## 4. Grouping Rules, Confidence, and Triage
Service: DocumentGroupingService

### 4.1 Group formation rules
- Adjacent grouping is allowed only when page class is the same and source file is the same.
- Unknown + no-text pages are explicitly split across different source files.
- Non-adjacent stitching uses anchor matching and class consistency checks.

### 4.2 Anchor extraction rules
- Anchors are extracted from page text only.
- no-text pages do not generate anchors.
- Typical anchor patterns include invoice/order references such as INV-####, OP-####, CO-####, PO-####.

### 4.3 Group confidence
For each group:
- per-page signal used is:
  - invoiceScore for invoice groups
  - shippingScore for shipping groups
  - max(invoiceScore, shippingScore) for other/unknown groups
- averageSignal = average of per-page signals
- confidence = clamp(averageSignal / 10, 0.1, 1.0), rounded to 2 decimals

### 4.4 Group triage flag
- ready-for-cu when group pageClass is invoice, shipping, or other
- unknown-triage when group pageClass is unknown


## 5. CU Dispatch Decisions
Service: CuOrchestrationService

Per grouped document:
- initialClass = invoice:
  - analyzerTarget = invoice-prebuilt
  - dispatchStatus = queued
- initialClass = shipping or other:
  - analyzerTarget = general-prebuilt
  - dispatchStatus = queued
- initialClass = unknown:
  - if anchors exist and confidence >= 0.4:
    - analyzerTarget = general-prebuilt
    - dispatchStatus = queued
  - otherwise:
    - analyzerTarget = none
    - dispatchStatus = review-needed


## 6. Practical Interpretation
- qualityScore answers: How reliable and clean is extracted text?
- invoiceScore/shippingScore answer: How much route-specific language is present?
- pageClass answers: Which class threshold rule passed?
- confidence answers: How strong is grouped class evidence?
- triageFlag/dispatchStatus answer: Should this proceed automatically to analyzer or to review?

These metrics are expected to evolve as more test files and CU outputs are incorporated.
