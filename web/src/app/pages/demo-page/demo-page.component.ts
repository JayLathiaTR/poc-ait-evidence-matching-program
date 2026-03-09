import { CommonModule } from '@angular/common';
import { Component, OnDestroy } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import * as XLSX from 'xlsx';

type RuntimeAppConfig = {
  apiBaseUrl?: string;
};

declare global {
  interface Window {
    __APP_CONFIG__?: RuntimeAppConfig;
  }
}

type FieldKey =
  | 'verifiedDocumentId'
  | 'verifiedCustomerName'
  | 'verifiedDocumentDate'
  | 'verifiedAmount'
  | 'verifiedShippingDate'
  | 'verifiedShippingAddress';

type NormalizedRect = {
  x: number;
  y: number;
  w: number;
  h: number;
};

type FieldAnnotation = {
  docId: string;
  rect: NormalizedRect;
  value: string;
};

type SampleRow = {
  transactionId: string;
  customerName: string;
  documentId: string;
  transactionDate: string;
  dueDate: string;
  originalTransactionAmount: number;

  linkedEvidenceDocId?: string;
  verifiedDocumentId?: string;
  verifiedCustomerName?: string;
  verifiedDocumentDate?: string;
  verifiedAmount?: number;
  verifiedShippingDate?: string;
  verifiedShippingAddress?: string;
  shippingSourceType?: 'BOL' | 'PO' | 'JE' | 'TB' | 'OTHER';
  shippingSourceDocId?: string;

  annotations?: Partial<Record<FieldKey, FieldAnnotation>>;
};

type UploadedDoc = {
  id: string;
  name: string;
  mimeType: string;
  kind: 'image' | 'pdf';
  objectUrl: string;
  file: File;
  documentId?: string;
  isSelected: boolean;
};

type OcrWord = {
  text: string;
  bbox: { x0: number; y0: number; x1: number; y1: number };
  confidence?: number;
};

type OcrResult = {
  text: string;
  words: OcrWord[];
};

type ScoredCandidate<T> = {
  value: T;
  score: number;
  lineIndex?: number;
  raw?: string;
};

type SampleFieldKey =
  | 'documentId'
  | 'customerName'
  | 'transactionDate'
  | 'originalTransactionAmount';

@Component({
  selector: 'app-demo-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './demo-page.component.html',
  styleUrl: './demo-page.component.css',
})
export class DemoPageComponent implements OnDestroy {
  constructor(private readonly sanitizer: DomSanitizer) {}

  private readonly missingDocumentIdMessage = 'Document ID is not available';

  sampleRows: SampleRow[] = [
    {
      transactionId: 'TRX-0001',
      customerName: 'Russell Proctor',
      documentId: 'INV-1099',
      transactionDate: '2021-03-25',
      dueDate: '2021-02-14',
      originalTransactionAmount: 1250.0,
      linkedEvidenceDocId: undefined,
      verifiedDocumentId: '',
      verifiedCustomerName: '',
      verifiedDocumentDate: '',
      verifiedAmount: undefined,
      verifiedShippingDate: '',
      verifiedShippingAddress: '',
      annotations: {},
    },
    {
      transactionId: 'TRX-0002',
      customerName: 'Contoso Ltd',
      documentId: 'INV-1100',
      transactionDate: '2024-01-18',
      dueDate: '2024-02-17',
      originalTransactionAmount: 980.5,
      linkedEvidenceDocId: undefined,
      verifiedDocumentId: '',
      verifiedCustomerName: '',
      verifiedDocumentDate: '',
      verifiedAmount: undefined,
      verifiedShippingDate: '',
      verifiedShippingAddress: '',
      annotations: {},
    },
    {
      transactionId: 'TRX-0003',
      customerName: 'Fabrikam Inc',
      documentId: 'INV-1101',
      transactionDate: '2024-01-22',
      dueDate: '2024-02-21',
      originalTransactionAmount: 4200.0,
      linkedEvidenceDocId: undefined,
      verifiedDocumentId: '',
      verifiedCustomerName: '',
      verifiedDocumentDate: '',
      verifiedAmount: undefined,
      verifiedShippingDate: '',
      verifiedShippingAddress: '',
      annotations: {},
    },
  ];

  docs: UploadedDoc[] = [];
  selectedDocId: string | null = null;
  selectedTransactionId: string | null = null;

  isGenerating = false;
  generationProgressPercent = 0;
  generationProgressText = 'Ready to generate.';
  clearEvidenceConfirmVisible = false;
  generateError: string | null = null;
  private ocrByDocId = new Map<string, OcrResult>();

  annotationField: FieldKey = 'verifiedCustomerName';
  annotationValue = '';
  drawMode = false;
  private isDrawing = false;
  private drawStart: { x: number; y: number } | null = null;
  pendingRect: NormalizedRect | null = null;

  highlightDocId: string | null = null;
  highlightRect: NormalizedRect | null = null;

  readonly fieldOptions: ReadonlyArray<{ key: FieldKey; label: string }> = [
    { key: 'verifiedCustomerName', label: 'Verified Customer name' },
    { key: 'verifiedDocumentDate', label: 'Verified Document date' },
    { key: 'verifiedAmount', label: 'Verified Amount' },
    { key: 'verifiedShippingDate', label: 'Verified Shipping date' },
    { key: 'verifiedShippingAddress', label: 'Verified Shipping address' },
  ];

  readonly outputFieldOptions: ReadonlyArray<{ key: FieldKey; label: string }> = [
    { key: 'verifiedDocumentId', label: 'Verified Document ID' },
    { key: 'verifiedCustomerName', label: 'Verified Customer name' },
    { key: 'verifiedAmount', label: 'Verified Amount' },
    { key: 'verifiedDocumentDate', label: 'Verified Document date' },
    { key: 'verifiedShippingDate', label: 'Verified Shipping date' },
    { key: 'verifiedShippingAddress', label: 'Verified Shipping address' },
  ];

  selectedOutputFields: Record<FieldKey, boolean> = {
    verifiedDocumentId: false,
    verifiedCustomerName: false,
    verifiedDocumentDate: false,
    verifiedAmount: false,
    verifiedShippingDate: false,
    verifiedShippingAddress: false,
  };

  readonly sampleFieldOptions: ReadonlyArray<{ key: SampleFieldKey; label: string }> = [
    { key: 'documentId', label: 'Document ID' },
    { key: 'customerName', label: 'Customer name' },
    { key: 'transactionDate', label: 'Transaction date' },
    { key: 'originalTransactionAmount', label: 'Original transaction amount' },
  ];

  pendingSampleHeaders: string[] = [];
  pendingSampleRows: string[][] = [];
  pendingSampleFilename = '';
  sampleColumnMapping: Record<SampleFieldKey, string> = {
    documentId: '-1',
    customerName: '-1',
    transactionDate: '-1',
    originalTransactionAmount: '-1',
  };
  activeSampleFields: Record<SampleFieldKey, boolean> = {
    documentId: true,
    customerName: true,
    transactionDate: true,
    originalTransactionAmount: true,
  };

  private cachedPdfObjectUrl: string | null = null;
  private cachedPdfSafeUrl: SafeResourceUrl | null = null;

  ngOnDestroy(): void {
    for (const doc of this.docs) {
      URL.revokeObjectURL(doc.objectUrl);
    }
  }

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement | null;
    const files = input?.files;
    if (!files || files.length === 0) return;

    const nextDocs: UploadedDoc[] = [];
    for (const file of Array.from(files)) {
      const isImage = file.type.startsWith('image/');
      const isPdf = file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf');
      if (!isImage && !isPdf) continue;

      const id = this.createId();
      const objectUrl = URL.createObjectURL(file);

      nextDocs.push({
        id,
        name: file.name,
        mimeType: file.type,
        kind: isPdf ? 'pdf' : 'image',
        objectUrl,
        file,
        documentId: this.tryExtractDocumentIdFromFilename(file.name),
        isSelected: false,
      });
    }

    this.docs = [...this.docs, ...nextDocs];
    for (const doc of nextDocs) {
      this.ocrByDocId.delete(doc.id);
    }
    if (!this.selectedDocId && this.docs.length > 0) {
      this.selectedDocId = this.docs[0].id;
    }

    if (input) input.value = '';
  }

  async onSampleFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement | null;
    const file = input?.files?.[0];
    if (!file) return;

    try {
      const rows = await this.parseTabularFile(file);
      if (rows.length < 2) {
        throw new Error('Sample file must include a header row and at least one data row.');
      }

      const headerRowIndex = this.findBestHeaderRowIndex(rows);
      const headerRow = rows[headerRowIndex] ?? [];
      const dataRows = rows.slice(headerRowIndex + 1);

      this.pendingSampleFilename = file.name;
      this.pendingSampleHeaders = headerRow.map((value, index) => {
        const trimmed = (value ?? '').trim();
        return trimmed || `Column ${index + 1}`;
      });
      this.pendingSampleRows = dataRows.filter((row) => row.some((cell) => cell.trim().length > 0));

      if (this.pendingSampleRows.length === 0) {
        throw new Error('Sample file has no valid data rows.');
      }

      this.sampleColumnMapping = this.getAutoSampleMapping(this.pendingSampleHeaders);
      this.generateError = null;
    } catch (err) {
      this.generateError =
        err && typeof (err as any).message === 'string'
          ? (err as any).message
            : 'Unable to parse sample file';
      this.clearPendingSampleImport();
    } finally {
      if (input) input.value = '';
    }
  }

  downloadSampleTemplate(): void {
    const headers = [
      'Document ID',
      'Customer name',
      'Transaction date',
      'Original transaction amount',
    ];
    const exampleRow = ['INV-1099', 'Russell Proctor', '2021-03-25', '3000.20'];
    const csv = `${headers.join(',')}\n${exampleRow.join(',')}\n`;

    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = 'ait-sample-template.csv';
    anchor.click();
    URL.revokeObjectURL(url);
  }

  setSampleColumnMapping(field: SampleFieldKey, indexValue: string): void {
    this.sampleColumnMapping = {
      ...this.sampleColumnMapping,
      [field]: indexValue,
    };
  }

  getSampleHeaderIndexValue(index: number): string {
    return `${index}`;
  }

  isSampleHeaderSelected(field: SampleFieldKey, index: number): boolean {
    return this.sampleColumnMapping[field] === this.getSampleHeaderIndexValue(index);
  }

  get hasPendingSampleMapping(): boolean {
    return this.pendingSampleHeaders.length > 0 && this.pendingSampleRows.length > 0;
  }

  get hasAnySampleMappingSelected(): boolean {
    return Object.values(this.sampleColumnMapping).some((v) => v !== '-1');
  }

  get canApplySampleImport(): boolean {
    return this.hasPendingSampleMapping && this.sampleColumnMapping.documentId !== '-1';
  }

  applySampleImport(): void {
    if (!this.hasPendingSampleMapping) return;
    if (this.sampleColumnMapping.documentId === '-1') {
      this.generateError = 'Document ID is required. Please map a column to Document ID before import.';
      return;
    }

    const imported: SampleRow[] = [];
    for (let i = 0; i < this.pendingSampleRows.length; i += 1) {
      const row = this.pendingSampleRows[i];
      const documentId = this.readMappedCell(row, this.sampleColumnMapping.documentId);
      const customerName = this.readMappedCell(row, this.sampleColumnMapping.customerName);
      const transactionDate = this.readMappedCell(row, this.sampleColumnMapping.transactionDate);
      const amountRaw = this.readMappedCell(row, this.sampleColumnMapping.originalTransactionAmount);
      const amount = Number((amountRaw || '').replace(/[^0-9.-]/g, ''));

      imported.push({
        transactionId: `TRX-${String(i + 1).padStart(4, '0')}`,
        customerName,
        documentId: documentId.trim(),
        transactionDate,
        dueDate: '',
        originalTransactionAmount: Number.isFinite(amount) ? amount : 0,
        linkedEvidenceDocId: undefined,
        verifiedDocumentId: '',
        verifiedCustomerName: '',
        verifiedDocumentDate: '',
        verifiedAmount: undefined,
        verifiedShippingDate: '',
        verifiedShippingAddress: '',
        annotations: {},
      });
    }

    this.sampleRows = imported;
    this.activeSampleFields = {
      documentId: this.sampleColumnMapping.documentId !== '-1',
      customerName: this.sampleColumnMapping.customerName !== '-1',
      transactionDate: this.sampleColumnMapping.transactionDate !== '-1',
      originalTransactionAmount:
        this.sampleColumnMapping.originalTransactionAmount !== '-1',
    };
    this.selectedTransactionId = imported[0]?.transactionId ?? null;
    this.clearPendingSampleImport();
    this.generateError = null;
  }

  clearPendingSampleImport(): void {
    this.pendingSampleFilename = '';
    this.pendingSampleHeaders = [];
    this.pendingSampleRows = [];
    this.sampleColumnMapping = {
      documentId: '-1',
      customerName: '-1',
      transactionDate: '-1',
      originalTransactionAmount: '-1',
    };
  }

  toggleOutputField(field: FieldKey, checked: boolean): void {
    this.selectedOutputFields = {
      ...this.selectedOutputFields,
      [field]: checked,
    };
  }

  isOutputFieldSelected(field: FieldKey): boolean {
    return Boolean(this.selectedOutputFields[field]);
  }

  isSampleFieldVisible(field: SampleFieldKey): boolean {
    return Boolean(this.activeSampleFields[field]);
  }

  get hasSelectedOutputFields(): boolean {
    return Object.values(this.selectedOutputFields).some(Boolean);
  }

  get canGenerateResult(): boolean {
    return this.docs.length > 0 && this.hasSelectedOutputFields && !this.isGenerating;
  }

  get canEditInputs(): boolean {
    return !this.isGenerating;
  }

  get hasSelectedEvidenceForBulkDelete(): boolean {
    return this.docs.some((d) => d.isSelected);
  }

  get canRemoveSelectedEvidence(): boolean {
    return this.canEditInputs && this.hasSelectedEvidenceForBulkDelete;
  }

  get canClearEvidenceFiles(): boolean {
    return this.canEditInputs && this.docs.length > 0;
  }

  isRowMissingDocumentId(row: SampleRow): boolean {
    return !this.hasRequiredDocumentId(row);
  }

  isMissingDocumentIdStatus(row: SampleRow): boolean {
    return this.normalizeText(row.verifiedDocumentId) === this.normalizeText(this.missingDocumentIdMessage);
  }

  get showShippingSourceColumn(): boolean {
    return (
      this.isOutputFieldSelected('verifiedShippingDate') ||
      this.isOutputFieldSelected('verifiedShippingAddress')
    );
  }

  selectDoc(docId: string): void {
    this.selectedDocId = docId;
    if (this.highlightDocId !== docId) {
      this.highlightDocId = null;
      this.highlightRect = null;
    }
  }

  selectSampleRow(transactionId: string): void {
    this.selectedTransactionId = transactionId;
  }

  get selectedSampleRow(): SampleRow | null {
    if (!this.selectedTransactionId) return null;
    return (
      this.sampleRows.find((r) => r.transactionId === this.selectedTransactionId) ??
      null
    );
  }

  linkSelectedDocToSelectedRow(): void {
    const row = this.selectedSampleRow;
    const doc = this.selectedDoc;
    if (!row || !doc) return;

    this.sampleRows = this.sampleRows.map((r) => {
      if (r.transactionId !== row.transactionId) return r;
      return {
        ...r,
        linkedEvidenceDocId: doc.id,
        verifiedDocumentId: doc.documentId?.trim() || doc.name,
      };
    });
  }

  setEvidenceDocumentId(evidenceId: string, value: string): void {
    this.docs = this.docs.map((d) =>
      d.id === evidenceId ? { ...d, documentId: value } : d,
    );
  }

  toggleEvidenceSelected(evidenceId: string, checked: boolean): void {
    if (!this.canEditInputs) return;
    this.docs = this.docs.map((doc) =>
      doc.id === evidenceId ? { ...doc, isSelected: checked } : doc,
    );
  }

  removeEvidence(evidenceId: string): void {
    if (!this.canEditInputs) return;
    this.removeEvidenceByIds(new Set([evidenceId]));
  }

  removeSelectedEvidence(): void {
    if (!this.canRemoveSelectedEvidence) return;
    const ids = new Set(this.docs.filter((d) => d.isSelected).map((d) => d.id));
    this.removeEvidenceByIds(ids);
  }

  requestClearEvidenceFiles(): void {
    if (!this.canClearEvidenceFiles) return;
    this.clearEvidenceConfirmVisible = true;
  }

  cancelClearEvidenceFiles(): void {
    this.clearEvidenceConfirmVisible = false;
  }

  confirmClearEvidenceFiles(): void {
    if (!this.canClearEvidenceFiles) {
      this.clearEvidenceConfirmVisible = false;
      return;
    }
    this.removeEvidenceByIds(new Set(this.docs.map((d) => d.id)));
    this.clearEvidenceConfirmVisible = false;
  }

  async generateResult(): Promise<void> {
    if (!this.canGenerateResult) return;

    this.isGenerating = true;
    this.clearEvidenceConfirmVisible = false;
    this.generationProgressPercent = 0;
    this.generationProgressText = 'Starting generation...';
    this.generateError = null;

    try {
      const totalSteps = Math.max(1, this.docs.length + this.sampleRows.length);
      let completedSteps = 0;

      // 1) OCR each doc (cache results per evidence id).
      for (const doc of this.docs) {
        if (!this.ocrByDocId.has(doc.id)) {
          const ocr = await this.ocrDocument(doc);
          this.ocrByDocId.set(doc.id, ocr);
        }
        completedSteps += 1;
        this.updateGenerationProgress('OCR processing', completedSteps, totalSteps);
      }

      // 2) Auto-match rows.
      const docImages = await this.getDocImageSizes();

      this.sampleRows = this.sampleRows.map((row) => {
        completedSteps += 1;
        this.updateGenerationProgress('Matching transactions', completedSteps, totalSteps);

        if (!this.hasRequiredDocumentId(row)) {
          return {
            ...row,
            linkedEvidenceDocId: undefined,
            verifiedDocumentId: this.missingDocumentIdMessage,
            verifiedCustomerName: '',
            verifiedDocumentDate: '',
            verifiedAmount: undefined,
            verifiedShippingDate: '',
            verifiedShippingAddress: '',
            shippingSourceType: undefined,
            shippingSourceDocId: undefined,
            annotations: {},
          };
        }

        const linkedDoc = row.linkedEvidenceDocId
          ? (this.docs.find((d) => d.id === row.linkedEvidenceDocId) ?? null)
          : null;

        const linkedOcr = linkedDoc ? this.ocrByDocId.get(linkedDoc.id) : undefined;
        const linkedDocType = linkedDoc
          ? this.detectDocumentTypeForDoc(linkedDoc, linkedOcr?.text ?? '')
          : 'other';

        let bestDoc = linkedDoc;
        if (!bestDoc || linkedDocType !== 'invoice') {
          const invoiceCandidate = this.findBestDocForRow(row);
          if (invoiceCandidate) {
            bestDoc = invoiceCandidate;
          }
        }
        if (!bestDoc) return row;

        const ocr = this.ocrByDocId.get(bestDoc.id);
        if (!ocr) return row;

        const imgSize = docImages.get(bestDoc.id);
        const annotations = { ...(row.annotations ?? {}) };

        const next: SampleRow = {
          ...row,
          linkedEvidenceDocId: bestDoc.id,
          annotations,
        };

        if (this.isOutputFieldSelected('verifiedDocumentId')) {
          next.verifiedDocumentId = row.documentId;
        }

        // (a) Document ID highlight
        if (this.isOutputFieldSelected('verifiedDocumentId')) {
          const docIdRect = imgSize
            ? this.findRectForSearch(ocr.words, imgSize, row.documentId)
            : null;
          if (docIdRect) {
            annotations.verifiedDocumentId = {
              docId: bestDoc.id,
              rect: docIdRect,
              value: row.documentId,
            };
          }
        }

        if (this.isOutputFieldSelected('verifiedCustomerName')) {
          const customerCandidate = this.extractCustomerCandidate(ocr.text, row.customerName);
          const customerFromDoc = customerCandidate?.value ?? null;
          const customerRect = imgSize && customerFromDoc
            ? this.findRectForSearch(ocr.words, imgSize, customerFromDoc)
            : null;
          if (customerFromDoc) {
            next.verifiedCustomerName = customerFromDoc;
          }
          if (customerRect && customerFromDoc) {
            annotations.verifiedCustomerName = {
              docId: bestDoc.id,
              rect: customerRect,
              value: customerFromDoc,
            };
          }
        }

        if (this.isOutputFieldSelected('verifiedDocumentDate')) {
          const dateCandidate = this.extractDocumentDateCandidate(ocr.text, row.transactionDate);
          const dateFromDoc = dateCandidate?.value ?? null;
          const dateHit = imgSize && dateFromDoc
            ? this.findRectForSearch(ocr.words, imgSize, dateFromDoc)
            : null;
          if (dateFromDoc) {
            next.verifiedDocumentDate = dateFromDoc;
          }
          if (dateHit && dateFromDoc) {
            annotations.verifiedDocumentDate = {
              docId: bestDoc.id,
              rect: dateHit,
              value: dateFromDoc,
            };
          }
        }

        if (this.isOutputFieldSelected('verifiedAmount')) {
          const amountCandidate = this.extractAmountCandidate(ocr.text, row.originalTransactionAmount);
          const amountFromDoc = amountCandidate ? { value: amountCandidate.value } : null;
          const amountHit = imgSize && amountFromDoc
            ? this.findFirstRectForAnySearch(
                ocr.words,
                imgSize,
                this.getAmountCandidates(amountFromDoc.value),
              )
            : null;
          if (amountFromDoc) {
            next.verifiedAmount = amountFromDoc.value;
          }
          if (amountHit && amountFromDoc) {
            annotations.verifiedAmount = {
              docId: bestDoc.id,
              rect: amountHit.rect,
              value: amountFromDoc.value.toFixed(2),
            };
          }
        }

        // (e) Shipping fields from related PO/BOL/JE/TB document referenced by invoice
        const includeAnyShipping =
          this.isOutputFieldSelected('verifiedShippingDate') ||
          this.isOutputFieldSelected('verifiedShippingAddress');

        if (includeAnyShipping) {
          const relatedShippingDoc = this.findRelatedShippingDoc(
            bestDoc.id,
            ocr.text,
            row.documentId,
          );
          if (relatedShippingDoc) {
            const relatedOcr = this.ocrByDocId.get(relatedShippingDoc.id);
            if (relatedOcr) {
              const shipping = this.extractShippingFieldsFromOcr(relatedOcr.text);
              const shippingSourceType = this.toShippingSourceType(
                this.detectDocumentTypeForDoc(relatedShippingDoc, relatedOcr.text),
              );
              const relatedImgSize = docImages.get(relatedShippingDoc.id);
              let hasShippingValue = false;

              if (this.isOutputFieldSelected('verifiedShippingDate') && shipping.date) {
                next.verifiedShippingDate = shipping.date;
                hasShippingValue = true;
                const dateRect = relatedImgSize
                  ? this.findRectForSearch(relatedOcr.words, relatedImgSize, shipping.date)
                  : null;
                if (dateRect) {
                  annotations.verifiedShippingDate = {
                    docId: relatedShippingDoc.id,
                    rect: dateRect,
                    value: shipping.date,
                  };
                }
              }

              if (this.isOutputFieldSelected('verifiedShippingAddress') && shipping.address) {
                next.verifiedShippingAddress = shipping.address;
                hasShippingValue = true;
                const addressRect = relatedImgSize
                  ? this.findRectForSearch(relatedOcr.words, relatedImgSize, shipping.address)
                  : null;
                if (addressRect) {
                  annotations.verifiedShippingAddress = {
                    docId: relatedShippingDoc.id,
                    rect: addressRect,
                    value: shipping.address,
                  };
                }
              }

              if (hasShippingValue) {
                next.shippingSourceType = shippingSourceType;
                next.shippingSourceDocId = relatedShippingDoc.id;
              }
            }
          }
        }

        return next;
      });

      this.generationProgressPercent = 100;
      this.generationProgressText = 'Generation completed (100%).';
    } catch (err) {
      this.generateError =
        err && typeof (err as any).message === 'string'
          ? (err as any).message
          : String(err);
      this.generationProgressText = 'Generation failed.';
    } finally {
      this.isGenerating = false;
      if (this.generationProgressPercent < 100 && !this.generateError) {
        this.generationProgressText = 'Ready to generate.';
      }
    }
  }

  private updateGenerationProgress(phase: string, completed: number, total: number): void {
    const safeTotal = Math.max(1, total);
    const percent = Math.max(0, Math.min(100, Math.round((completed / safeTotal) * 100)));
    this.generationProgressPercent = percent;
    this.generationProgressText = `${phase}: ${completed}/${safeTotal} (${percent}%)`;
  }

  private removeEvidenceByIds(idsToRemove: Set<string>): void {
    if (idsToRemove.size === 0) return;

    const removedDocs = this.docs.filter((doc) => idsToRemove.has(doc.id));
    for (const doc of removedDocs) {
      URL.revokeObjectURL(doc.objectUrl);
      this.ocrByDocId.delete(doc.id);
    }

    this.docs = this.docs.filter((doc) => !idsToRemove.has(doc.id));

    if (this.selectedDocId && idsToRemove.has(this.selectedDocId)) {
      this.selectedDocId = this.docs[0]?.id ?? null;
    }

    if (this.highlightDocId && idsToRemove.has(this.highlightDocId)) {
      this.highlightDocId = null;
      this.highlightRect = null;
    }

    this.sampleRows = this.sampleRows.map((row) => {
      const removedLinkedDoc = row.linkedEvidenceDocId && idsToRemove.has(row.linkedEvidenceDocId);
      const removedShippingDoc = row.shippingSourceDocId && idsToRemove.has(row.shippingSourceDocId);

      const currentAnnotations = row.annotations ?? {};
      const filteredEntries = Object.entries(currentAnnotations).filter(([, annotation]) => {
        if (!annotation) return false;
        return !idsToRemove.has(annotation.docId);
      });
      const filteredAnnotations = Object.fromEntries(filteredEntries) as Partial<Record<FieldKey, FieldAnnotation>>;

      if (!removedLinkedDoc && !removedShippingDoc && filteredEntries.length === Object.keys(currentAnnotations).length) {
        return row;
      }

      return {
        ...row,
        linkedEvidenceDocId: removedLinkedDoc ? undefined : row.linkedEvidenceDocId,
        verifiedDocumentId: removedLinkedDoc ? '' : row.verifiedDocumentId,
        verifiedCustomerName: removedLinkedDoc ? '' : row.verifiedCustomerName,
        verifiedDocumentDate: removedLinkedDoc ? '' : row.verifiedDocumentDate,
        verifiedAmount: removedLinkedDoc ? undefined : row.verifiedAmount,
        verifiedShippingDate: removedShippingDoc || removedLinkedDoc ? '' : row.verifiedShippingDate,
        verifiedShippingAddress: removedShippingDoc || removedLinkedDoc ? '' : row.verifiedShippingAddress,
        shippingSourceType: removedShippingDoc || removedLinkedDoc ? undefined : row.shippingSourceType,
        shippingSourceDocId: removedShippingDoc || removedLinkedDoc ? undefined : row.shippingSourceDocId,
        annotations: filteredAnnotations,
      };
    });

    if (this.docs.length === 0) {
      this.clearEvidenceConfirmVisible = false;
    }
  }

  autoMatchByDocumentId(): void {
    if (this.docs.length === 0) return;

    const docById = new Map<string, UploadedDoc>();
    for (const doc of this.docs) {
      const normalized = this.normalizeId(doc.documentId);
      if (!normalized) continue;
      if (!docById.has(normalized)) {
        docById.set(normalized, doc);
      }
    }

    this.sampleRows = this.sampleRows.map((row) => {
      // Don't overwrite a manual decision.
      if (row.linkedEvidenceDocId || row.verifiedDocumentId) return row;

      const normalizedRowId = this.normalizeId(row.documentId);
      if (!normalizedRowId) return row;

      const doc = docById.get(normalizedRowId);
      if (!doc) return row;

      return {
        ...row,
        linkedEvidenceDocId: doc.id,
        verifiedDocumentId: doc.documentId?.trim() || doc.name,
      };
    });
  }

  openLinkedEvidence(row: SampleRow): void {
    if (!row.linkedEvidenceDocId) return;
    this.selectDoc(row.linkedEvidenceDocId);
    const viewer = document.getElementById('evidenceViewer');
    viewer?.scrollIntoView({ block: 'nearest' });
  }

  openFieldEvidence(row: SampleRow, field: FieldKey): void {
    const annotation = row.annotations?.[field];
    const fallbackDocId = this.getFieldSourceDocId(row, field);
    if (!annotation && !fallbackDocId) return;

    const docId = annotation?.docId ?? fallbackDocId!;
    this.selectSampleRow(row.transactionId);
    this.selectDoc(docId);
    this.highlightDocId = annotation?.docId ?? null;
    this.highlightRect = annotation?.rect ?? null;

    const viewer = document.getElementById('evidenceViewer');
    viewer?.scrollIntoView({ block: 'nearest' });
  }

  canOpenFieldEvidence(row: SampleRow, field: FieldKey): boolean {
    if (row.annotations?.[field]) return true;
    return Boolean(this.getFieldSourceDocId(row, field));
  }

  private getFieldSourceDocId(row: SampleRow, field: FieldKey): string | null {
    const annotationDocId = row.annotations?.[field]?.docId;
    if (annotationDocId) return annotationDocId;

    if (
      field === 'verifiedShippingDate' ||
      field === 'verifiedShippingAddress'
    ) {
      return row.shippingSourceDocId ?? null;
    }

    return row.linkedEvidenceDocId ?? null;
  }

  startDrawMode(): void {
    if (!this.selectedSampleRow || !this.selectedDoc || !this.selectedDocIsImage) return;
    this.drawMode = true;
    this.pendingRect = null;
    this.isDrawing = false;
    this.drawStart = null;
  }

  cancelDrawMode(): void {
    this.drawMode = false;
    this.pendingRect = null;
    this.isDrawing = false;
    this.drawStart = null;
  }

  onViewerMouseDown(event: MouseEvent): void {
    if (!this.drawMode || !this.selectedDocIsImage) return;
    const start = this.getNormalizedPointFromMouseEvent(event);
    if (!start) return;

    this.isDrawing = true;
    this.drawStart = start;
    this.pendingRect = { x: start.x, y: start.y, w: 0, h: 0 };
  }

  onViewerMouseMove(event: MouseEvent): void {
    if (!this.drawMode || !this.selectedDocIsImage || !this.isDrawing || !this.drawStart) return;
    const current = this.getNormalizedPointFromMouseEvent(event);
    if (!current) return;

    const x1 = this.drawStart.x;
    const y1 = this.drawStart.y;
    const x2 = current.x;
    const y2 = current.y;

    const x = Math.min(x1, x2);
    const y = Math.min(y1, y2);
    const w = Math.abs(x2 - x1);
    const h = Math.abs(y2 - y1);

    this.pendingRect = { x, y, w, h };
  }

  onViewerMouseUp(): void {
    if (!this.drawMode) return;
    this.isDrawing = false;
    this.drawStart = null;
  }

  saveAnnotation(): void {
    const row = this.selectedSampleRow;
    const doc = this.selectedDoc;
    const rect = this.pendingRect;
    if (!this.selectedDocIsImage) return;
    if (!row || !doc || !rect) return;
    if (rect.w < 0.005 || rect.h < 0.005) return;

    const field = this.annotationField;
    const value = this.annotationValue.trim();

    this.sampleRows = this.sampleRows.map((r) => {
      if (r.transactionId !== row.transactionId) return r;

      const next: SampleRow = {
        ...r,
        linkedEvidenceDocId: r.linkedEvidenceDocId ?? doc.id,
        verifiedDocumentId: r.verifiedDocumentId || doc.documentId?.trim() || doc.name,
        annotations: {
          ...(r.annotations ?? {}),
          [field]: { docId: doc.id, rect, value },
        },
      };

      switch (field) {
        case 'verifiedCustomerName':
          next.verifiedCustomerName = value;
          break;
        case 'verifiedDocumentDate':
          next.verifiedDocumentDate = value;
          break;
        case 'verifiedAmount': {
          const parsed = Number(value.replace(/,/g, ''));
          next.verifiedAmount = Number.isFinite(parsed) ? parsed : undefined;
          break;
        }
        case 'verifiedShippingDate':
          next.verifiedShippingDate = value;
          break;
        case 'verifiedShippingAddress':
          next.verifiedShippingAddress = value;
          break;
      }

      return next;
    });

    this.highlightDocId = doc.id;
    this.highlightRect = rect;
    this.pendingRect = null;
  }

  hasAnnotation(row: SampleRow, field: FieldKey): boolean {
    return Boolean(row.annotations?.[field]);
  }

  isCustomerNameMatch(row: SampleRow): boolean {
    const left = this.normalizeText(row.customerName);
    const right = this.normalizeText(row.verifiedCustomerName);
    return Boolean(left && right && left === right);
  }

  isDocumentIdMatch(row: SampleRow): boolean {
    const left = this.normalizeForSearch(row.documentId);
    const right = this.normalizeForSearch(row.verifiedDocumentId);
    return Boolean(left && right && left === right);
  }

  isDocumentDateMatch(row: SampleRow): boolean {
    const leftIso = this.toIsoDate(row.transactionDate);
    const rightIso = this.toIsoDate(row.verifiedDocumentDate);
    if (leftIso && rightIso) return leftIso === rightIso;

    const left = this.normalizeText(row.transactionDate);
    const right = this.normalizeText(row.verifiedDocumentDate);
    return Boolean(left && right && left === right);
  }

  isAmountMatch(row: SampleRow): boolean {
    if (row.verifiedAmount === undefined || row.verifiedAmount === null) return false;
    // Avoid float noise in a demo; compare at 2dp and allow sign differences.
    const left = Number(Math.abs(row.originalTransactionAmount).toFixed(2));
    const right = Number(Math.abs(Number(row.verifiedAmount)).toFixed(2));
    return Number.isFinite(left) && Number.isFinite(right) && left === right;
  }

  private getNormalizedPointFromMouseEvent(
    event: MouseEvent,
  ): { x: number; y: number } | null {
    const img = document.getElementById('viewerImg') as HTMLImageElement | null;
    if (!img) return null;

    const rect = img.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return null;

    const x = (event.clientX - rect.left) / rect.width;
    const y = (event.clientY - rect.top) / rect.height;

    return {
      x: this.clamp01(x),
      y: this.clamp01(y),
    };
  }

  private clamp01(value: number): number {
    if (value < 0) return 0;
    if (value > 1) return 1;
    return value;
  }

  private normalizeText(value: string | undefined): string {
    return (value ?? '').trim().replace(/\s+/g, ' ').toLowerCase();
  }

  get selectedDoc(): UploadedDoc | null {
    if (!this.selectedDocId) return null;
    return this.docs.find((d) => d.id === this.selectedDocId) ?? null;
  }

  get selectedDocIsPdf(): boolean {
    return this.selectedDoc?.kind === 'pdf';
  }

  get selectedDocIsImage(): boolean {
    return this.selectedDoc?.kind === 'image';
  }

  get selectedPdfSafeUrl(): SafeResourceUrl | null {
    if (!this.selectedDoc || this.selectedDoc.kind !== 'pdf') return null;

    if (this.cachedPdfObjectUrl !== this.selectedDoc.objectUrl) {
      this.cachedPdfObjectUrl = this.selectedDoc.objectUrl;
      this.cachedPdfSafeUrl = this.sanitizer.bypassSecurityTrustResourceUrl(
        this.selectedDoc.objectUrl,
      );
    }

    return this.cachedPdfSafeUrl;
  }

  private createId(): string {
    // Good enough for a demo; avoids adding dependencies.
    return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
  }

  private normalizeId(value: string | undefined): string {
    const normalized = (value ?? '').trim().toUpperCase();
    return normalized.length > 0 ? normalized : '';
  }

  private tryExtractDocumentIdFromFilename(filename: string): string {
    // Heuristic only: looks for patterns like INV-1099, inv_1099, inv1099.
    const match = filename.match(/\b(inv)[\s_-]?(\d{3,})\b/i);
    if (!match) return '';
    return `INV-${match[2]}`;
  }

  private async ocrDocument(doc: UploadedDoc): Promise<OcrResult> {
    const form = new FormData();
    form.append('file', doc.file, doc.name);

    const response = await fetch(this.buildApiUrl('/api/ocr'), {
      method: 'POST',
      body: form,
    });

    if (!response.ok) {
      const text = await response.text().catch(() => '');
      throw new Error(`OCR failed (${response.status}): ${text || response.statusText}`);
    }

    const json = (await response.json()) as OcrResult;
    return {
      text: typeof json.text === 'string' ? json.text : '',
      words: Array.isArray(json.words) ? json.words : [],
    };
  }

  private findBestDocForRow(row: SampleRow): UploadedDoc | null {
    const normalizedRowId = this.normalizeForSearch(row.documentId);
    const rowDigits = row.documentId.replace(/\D+/g, '');

    let best: { doc: UploadedDoc; score: number } | null = null;

    for (const doc of this.docs) {
      const ocr = this.ocrByDocId.get(doc.id);
      if (!ocr) continue;

      const text = this.normalizeForSearch(ocr.text);
      let score = 0;

      const evidenceId = this.normalizeForSearch(doc.documentId);
      const idExact = Boolean(evidenceId && evidenceId === normalizedRowId);
      const idInText = Boolean(normalizedRowId && text.includes(normalizedRowId));
      const digitsInText = Boolean(rowDigits && text.includes(rowDigits));

      // Guardrail: do not link rows using weak signals only (e.g., customer-name-only).
      if (!idExact && !idInText && !digitsInText) continue;

      if (idExact) score += 12;
      if (idInText) score += 10;
      if (digitsInText) score += 4;

      const docType = this.detectDocumentTypeForDoc(doc, ocr.text);
      if (docType === 'bol' || docType === 'po' || docType === 'je' || docType === 'tb') score -= 6;

      const customer = this.normalizeForSearch(row.customerName);
      if (customer && text.includes(customer)) score += 2;

      if (score > 0 && (!best || score > best.score)) {
        best = { doc, score };
      }
    }

    return best?.doc ?? null;
  }

  private async getDocImageSizes(): Promise<Map<string, { w: number; h: number }>> {
    const sizes = new Map<string, { w: number; h: number }>();
    await Promise.all(
      this.docs.map(async (doc) => {
        if (doc.kind !== 'image') return;
        const img = new Image();
        img.src = doc.objectUrl;
        await new Promise<void>((resolve) => {
          img.onload = () => resolve();
          img.onerror = () => resolve();
        });
        if (img.naturalWidth > 0 && img.naturalHeight > 0) {
          sizes.set(doc.id, { w: img.naturalWidth, h: img.naturalHeight });
        }
      }),
    );
    return sizes;
  }

  private getDateCandidates(isoDate: string): string[] {
    // isoDate expected as YYYY-MM-DD
    const trimmed = isoDate.trim();
    const match = trimmed.match(/^(\d{4})-(\d{2})-(\d{2})$/);
    if (!match) return [trimmed];

    const yyyy = match[1];
    const mm = match[2];
    const dd = match[3];

    const m = String(Number(mm));
    const d = String(Number(dd));

    return [
      `${yyyy}-${mm}-${dd}`,
      `${mm}/${dd}/${yyyy}`,
      `${m}/${d}/${yyyy}`,
      `${dd}/${mm}/${yyyy}`,
      `${d}/${m}/${yyyy}`,
    ];
  }

  private extractCustomerCandidate(
    text: string,
    expectedName?: string,
  ): ScoredCandidate<string> | null {
    const lines = this.getNormalizedLines(text);
    const candidates: Array<{ value: string; score: number }> = [];

    const labelRegex = /(bill\s*to|customer(?:\s*name)?|sold\s*to)/i;

    for (let i = 0; i < lines.length; i += 1) {
      const line = lines[i];
      if (!labelRegex.test(line)) continue;

      const sameLine = line
        .replace(/.*?(bill\s*to|customer(?:\s*name)?|sold\s*to)\s*[:\-]?\s*/i, '')
        .trim();
      const cleanedSame = this.cleanExtractedCustomerName(sameLine);
      if (cleanedSame) {
        candidates.push({
          value: cleanedSame,
          score: this.scoreCustomerName(cleanedSame, true, expectedName),
        });
      }

      const nextLine = lines[i + 1] ?? '';
      const cleanedNext = this.cleanExtractedCustomerName(nextLine);
      if (cleanedNext) {
        candidates.push({
          value: cleanedNext,
          score: this.scoreCustomerName(cleanedNext, true, expectedName),
        });
      }

      // Handle merged lines like "03/28/2021458-555-0148Gamma" where the name is at the tail.
      const tailName = this.extractTailNameFromMixedLine(nextLine);
      if (tailName) {
        candidates.push({
          value: tailName,
          score: this.scoreCustomerName(tailName, true, expectedName) + 1,
        });
      }
    }

    // Regex fallback from full text, but with lower score.
    const regexFallbacks = [
      /bill\s*to\s*[:\-]?\s*([^\n\r]+)/i,
      /customer(?:\s*name)?\s*[:\-]?\s*([^\n\r]+)/i,
      /sold\s*to\s*[:\-]?\s*([^\n\r]+)/i,
    ];
    for (const re of regexFallbacks) {
      const m = text.match(re);
      const cleaned = this.cleanExtractedCustomerName(m?.[1] ?? '');
      if (cleaned) {
        candidates.push({
          value: cleaned,
          score: this.scoreCustomerName(cleaned, false, expectedName),
        });
      }
    }

    if (candidates.length === 0) return null;
    candidates.sort((a, b) => b.score - a.score);
    if (candidates[0].score < 3) return null;
    return { value: candidates[0].value, score: candidates[0].score };
  }

  private cleanExtractedCustomerName(value: string): string | null {
    let candidate = value.trim();
    candidate = candidate
      .split(/\b(phone|fax|e-?mail|address|invoice|bill\s*to|ship\s*to|contact)\b/i)[0]
      ?.trim() ?? candidate;

    // Remove trailing inline date if OCR merged them on one line.
    candidate = candidate.replace(/\s+\d{1,2}[\/-]\d{1,2}[\/-]\d{2,4}\s*$/, '').trim();

    candidate = candidate.replace(/^[^a-z0-9]+|[^a-z0-9]+$/gi, '').trim();
    if (!candidate) return null;

    const letters = (candidate.match(/[a-z]/gi) ?? []).length;
    if (letters < 3) return null;

    return candidate;
  }

  private scoreCustomerName(
    value: string,
    fromLabelLine: boolean,
    expectedName?: string,
  ): number {
    const v = value.trim();
    if (!v) return -10;

    let score = 0;
    if (fromLabelLine) score += 2;

    const tokens = v.split(/\s+/g).filter(Boolean);
    const alphaTokens = tokens.filter((t) => /^[a-z][a-z'.-]*$/i.test(t));

    if (tokens.length >= 2 && tokens.length <= 4) score += 2;
    if (alphaTokens.length >= 2) score += 2;
    if (tokens.length === 1 && alphaTokens.length === 1 && v.length >= 4) score += 1;

    if (/\b(invoice|total|amount|description|qty|quantity|price|date|tax|email|phone|fax|street|city|state|zip)\b/i.test(v)) score -= 3;
    if (/\d/.test(v)) score -= 2;
    if (tokens.some((t) => t.length <= 1)) score -= 1;

    if (expectedName) {
      const sim = this.tokenSimilarity(v, expectedName);
      if (sim >= 0.75) score += 2;
      else if (sim >= 0.5) score += 1;
    }

    return score;
  }

  private extractTailNameFromMixedLine(line: string): string | null {
    const cleaned = line
      .replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, ' ')
      .replace(/\+?\d[\d\s().-]{6,}/g, ' ')
      .replace(/\d{1,4}[\/-]\d{1,2}[\/-]\d{2,4}/g, ' ')
      .replace(/[^A-Za-z\s'-]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();

    if (!cleaned) return null;

    const tail = cleaned.match(/([A-Za-z][A-Za-z'-]{2,}(?:\s+[A-Za-z][A-Za-z'-]{2,}){0,2})\s*$/);
    return tail?.[1]?.trim() ?? null;
  }

  private extractDocumentDateCandidate(
    text: string,
    expectedDate?: string,
  ): ScoredCandidate<string> | null {
    const lines = this.getNormalizedLines(text);
    const expectedIso = this.toIsoDate(expectedDate);
    const candidates: ScoredCandidate<string>[] = [];

    for (let i = 0; i < lines.length; i += 1) {
      const line = lines[i];
      const lineDates = [...line.matchAll(/(\d{1,4}[\/-]\d{1,2}[\/-]\d{2,4})/g)]
        .map((m) => this.normalizeDateToken(m[1]))
        .filter(Boolean);

      for (const dateValue of lineDates) {
        let score = 1;
        const lower = line.toLowerCase();
        const prev = (lines[i - 1] ?? '').toLowerCase();
        const next = (lines[i + 1] ?? '').toLowerCase();

        if (/invoice\s*date|date\s*issued|document\s*date/.test(lower)) score += 4;
        else if (/invoice|issued/.test(lower)) score += 2;
        if (/invoice/.test(prev)) score += 2;

        if (/due\s*date/.test(lower) || /due\s*date/.test(prev) || /due\s*date/.test(next)) score -= 2;

        if (i < Math.ceil(lines.length / 3)) score += 1;

        if (expectedIso) {
          const candidateIso = this.toIsoDate(dateValue);
          if (candidateIso && candidateIso === expectedIso) score += 2;
        }

        candidates.push({ value: dateValue, score, lineIndex: i, raw: line });
      }
    }

    if (candidates.length === 0) return null;
    candidates.sort((a, b) => b.score - a.score);
    if (candidates[0].score < 3) return null;
    return candidates[0];
  }

  private extractAmountCandidate(
    text: string,
    expectedAmount?: number,
  ): ScoredCandidate<number> | null {
    const lines = this.getNormalizedLines(text);
    const candidates: ScoredCandidate<number>[] = [];

    for (let i = 0; i < lines.length; i += 1) {
      const line = lines[i];
      const lower = line.toLowerCase();
      const prev = (lines[i - 1] ?? '').toLowerCase();
      const next = (lines[i + 1] ?? '').toLowerCase();
      const prev2 = (lines[i - 2] ?? '').toLowerCase();
      const next2 = (lines[i + 2] ?? '').toLowerCase();

      const isGrandTotalContext =
        /(^|\b)(grand\s*)?total\b|total\s*due|amount\s*due|invoice\s*total/.test(lower) &&
        !/line\s*total/.test(lower);
      const isLineItemContext = /line\s*total|unit\s*price|quantity|description|rate\s*per\s*hour|hours|discount/.test(lower);
      const isSubtotalLine = /\bsubtotal\b/.test(lower);
      const taxAndTotalBlockNearby =
        /sales\s*tax\s*total/.test(lower) ||
        /sales\s*tax\s*total/.test(prev) ||
        /sales\s*tax\s*total/.test(next) ||
        /sales\s*tax\s*total/.test(prev2) ||
        /sales\s*tax\s*total/.test(next2) ||
        /sales\s*tax/.test(prev) ||
        /sales\s*tax/.test(next) ||
        /sales\s*tax/.test(prev2) ||
        /sales\s*tax/.test(next2);

      const lineAmounts = [...line.matchAll(/\$?\s*([0-9]{1,3}(?:,[0-9]{3})*(?:\.[0-9]{2})|[0-9]+\.[0-9]{2})/g)]
        .map((m) => ({ raw: m[1], value: this.parseAmount(m[1]) }))
        .filter((m): m is { raw: string; value: number } => m.value !== null);

      const amountCount = lineAmounts.length;

      for (let j = 0; j < lineAmounts.length; j += 1) {
        const amount = lineAmounts[j];
        let score = 1;

        if (isGrandTotalContext) score += 7;
        else if (/\btotal\b/.test(lower)) score += 2;

        if (isLineItemContext) score -= 4;
        if (isSubtotalLine) score -= 1;

        if (taxAndTotalBlockNearby) score += 2;

        if (line.includes('$')) score += 1;
        if (amount.value >= 100) score += 1;

        if (amountCount >= 3 && line.includes('$')) score += 2;

        // In TOTAL/TAX/SUBTOTAL rows with multiple amounts, right-most amount is often the invoice grand total.
        if (amountCount > 1 && (isGrandTotalContext || taxAndTotalBlockNearby)) {
          if (j === amountCount - 1) score += 2;
          else score -= 1;
        }

        // Multiple amounts in a non-total line usually indicates line-item math; penalize heavily.
        if (amountCount > 1 && !isGrandTotalContext && !taxAndTotalBlockNearby) {
          score -= 2;
        }

        if (expectedAmount !== undefined) {
          const delta = Math.abs(amount.value - expectedAmount);
          if (delta <= 0.01) score += 3;
          else if (delta <= Math.max(1, expectedAmount * 0.05)) score += 2;
        }

        candidates.push({ value: amount.value, score, lineIndex: i, raw: amount.raw });
      }
    }

    if (candidates.length === 0) return null;
    candidates.sort((a, b) => {
      if (b.score !== a.score) return b.score - a.score;
      return b.value - a.value;
    });
    if (candidates[0].score < 4) return null;
    return candidates[0];
  }

  private findRelatedShippingDoc(
    invoiceDocId: string,
    invoiceText: string,
    rowDocumentId?: string,
  ): UploadedDoc | null {
    const refs = this.extractReferenceIds(invoiceText);
    const normalizedRowDocId = this.normalizeForSearch(rowDocumentId);
    if (refs.length === 0 && !normalizedRowDocId) return null;

    const invoiceNormalized = this.normalizeForSearch(invoiceText);
    let best: { doc: UploadedDoc; score: number } | null = null;

    for (const doc of this.docs) {
      if (doc.id === invoiceDocId) continue;

      const ocr = this.ocrByDocId.get(doc.id);
      if (!ocr) continue;

      const candidateDocType = this.detectDocumentTypeForDoc(doc, ocr.text);
      if (candidateDocType !== 'bol' && candidateDocType !== 'po' && candidateDocType !== 'je' && candidateDocType !== 'tb') {
        continue;
      }

      const textNorm = this.normalizeForSearch(ocr.text);
      const nameNorm = this.normalizeForSearch(doc.name);
      const docIdNorm = this.normalizeForSearch(doc.documentId);

      let score = 0;

      if (normalizedRowDocId) {
        if (nameNorm.includes(normalizedRowDocId)) score += 9;
        if (docIdNorm.includes(normalizedRowDocId)) score += 9;
        if (textNorm.includes(normalizedRowDocId)) score += 4;
      }

      for (const ref of refs) {
        if (textNorm.includes(ref) || docIdNorm.includes(ref)) score += 8;
      }

      if (/(purchaseorder|\bpo\b|billoflading|\bbol\b|shipping|delivery)/.test(nameNorm)) score += 2;
      if (/(purchase\s*order|\bpo\b|bill\s*of\s*lading|\bb\/?l\b|shipping|ship\s*to|delivery)/i.test(ocr.text)) score += 2;

      // Slight boost if document appears related by common reference neighborhood in invoice.
      if (refs.some((r) => invoiceNormalized.includes(r))) score += 1;

      if (score > 0 && (!best || score > best.score)) {
        best = { doc, score };
      }
    }

    return best && best.score >= 8 ? best.doc : null;
  }

  private detectDocumentTypeForDoc(
    doc: UploadedDoc,
    text: string,
  ): 'bol' | 'po' | 'je' | 'tb' | 'invoice' | 'other' {
    const fromMeta = `${doc.name} ${doc.documentId ?? ''}`.toLowerCase();

    if (/bill\s*of\s*lading|\bbol\b|\bb\/?l\b/.test(fromMeta)) return 'bol';
    if (/purchase\s*order|\bpo\b/.test(fromMeta)) return 'po';
    if (/journal\s*entry|journal\s*id|journal\s*no|\bje\b/.test(fromMeta)) return 'je';
    if (/trial\s*balance|\btb\b/.test(fromMeta)) return 'tb';

    return this.detectDocumentType(text);
  }

  private extractReferenceIds(text: string): string[] {
    const refs = new Set<string>();

    const patterns = [
      /purchase\s*order\s*#?\s*[:\-]?\s*([A-Z]{0,6}[-\s]?\d{3,})/gi,
      /\bpo\s*#?\s*[:\-]?\s*([A-Z]{0,6}[-\s]?\d{3,})/gi,
      /bill\s*of\s*lading\s*#?\s*[:\-]?\s*([A-Z]{0,6}[-\s]?\d{3,})/gi,
      /\bb\/?l\s*(?:no|number)?\s*[:\-#]?\s*([A-Z]{0,6}[-\s]?\d{3,})/gi,
      /\bbol\s*(?:no|number)?\s*[:\-#]?\s*([A-Z]{0,6}[-\s]?\d{3,})/gi,
      /\bje\s*(?:no|number)?\s*[:\-#]?\s*([A-Z]{0,6}[-\s]?\d{3,})/gi,
      /\btb\s*(?:no|number)?\s*[:\-#]?\s*([A-Z]{0,6}[-\s]?\d{3,})/gi,
      /\b([A-Z]{1,4}-\d{4,})\b/g,
    ];

    for (const pattern of patterns) {
      const matches = text.matchAll(pattern);
      for (const match of matches) {
        const value = this.normalizeForSearch(match[1]);
        if (value.length >= 5) refs.add(value);
      }
    }

    return Array.from(refs);
  }

  private extractShippingFieldsFromOcr(text: string): {
    date?: string;
    address?: string;
  } {
    const lines = this.getNormalizedLines(text);
    const docType = this.detectDocumentType(text);
    const generic = this.extractShippingGeneric(lines);

    let docSpecific: { date?: string; address?: string } = {};
    if (docType === 'bol') {
      docSpecific = this.extractShippingFromBol(lines);
    } else if (docType === 'po') {
      docSpecific = this.extractShippingFromPo(lines);
    } else if (docType === 'je') {
      docSpecific = this.extractShippingFromJe(lines);
    } else if (docType === 'tb') {
      docSpecific = this.extractShippingFromTb(lines);
    }

    return this.mergeShippingExtractions(generic, docSpecific);
  }

  private detectDocumentType(text: string): 'bol' | 'po' | 'je' | 'tb' | 'invoice' | 'other' {
    const t = text.toLowerCase();
    if (/bill\s*of\s*lading|\bbol\b|\bb\/?l\b|shipper|ship\s*to/.test(t)) return 'bol';
    if (/purchase\s*order|\bpo\b/.test(t)) return 'po';
    if (/journal\s*entry|journal\s*id|journal\s*no|journal\s*number|\bje\b/.test(t)) return 'je';
    if (/trial\s*balance|\btb\b/.test(t)) return 'tb';
    if (/invoice/.test(t)) return 'invoice';
    return 'other';
  }

  private toShippingSourceType(
    docType: 'bol' | 'po' | 'je' | 'tb' | 'invoice' | 'other',
  ): 'BOL' | 'PO' | 'JE' | 'TB' | 'OTHER' {
    switch (docType) {
      case 'bol':
        return 'BOL';
      case 'po':
        return 'PO';
      case 'je':
        return 'JE';
      case 'tb':
        return 'TB';
      default:
        return 'OTHER';
    }
  }

  private extractShippingFromBol(lines: string[]): { date?: string; address?: string } {
    const result: { date?: string; address?: string } = {};

    const shipToStart = lines.findIndex((line) => /\bship\s*to\b/i.test(line));
    const shipToEnd = shipToStart >= 0
      ? lines.findIndex(
          (line, idx) =>
            idx > shipToStart &&
            /(third\s*party\s*freight|special\s*instructions|customer\s*order\s*no|handling\s*unit)/i.test(line),
        )
      : -1;

    const shipBlock = shipToStart >= 0
      ? lines.slice(shipToStart, shipToEnd > shipToStart ? shipToEnd : Math.min(lines.length, shipToStart + 14))
      : [];

    const shipToContacts = this.extractBolShipToContacts(shipBlock);
    const shipToAddress = this.extractLabeledFieldFromLines(shipBlock, ['address', 'street'], ['serial', 'cid no', 'carrier name']);
    const shipToCityStateZip = this.extractLabeledFieldFromLines(
      shipBlock,
      ['city / state / zip', 'city/state/zip', 'city state zip'],
      ['cid no', 'carrier name'],
    );

    const combinedShipTo = [...shipToContacts, shipToAddress ?? '', shipToCityStateZip ?? '']
      .map((part) => part.trim())
      .filter((part) => this.isLikelyAddressFragment(part));
    if (combinedShipTo.length > 0) {
      result.address = Array.from(new Set(combinedShipTo)).join(', ');
    }

    const dateCandidates = this.collectDateCandidates(lines, [
      /ship\s*date/i,
      /shipping\s*date/i,
      /pick-?up\s*date/i,
      /shipper\s*signature\s*&\s*date/i,
    ]);
    if (dateCandidates.length > 0) {
      // For BOL, usually shipper date is earlier than pickup date.
      dateCandidates.sort((a, b) => (this.toComparableDate(a) ?? Number.MAX_SAFE_INTEGER) - (this.toComparableDate(b) ?? Number.MAX_SAFE_INTEGER));
      result.date = dateCandidates[0];
    }

    return result;
  }

  private extractShippingFromPo(lines: string[]): { date?: string; address?: string } {
    const result: { date?: string; address?: string } = {};

    const shipToStart = lines.findIndex((line) => /\bship\s*to\b/i.test(line));
    const shipToEnd = shipToStart >= 0
      ? lines.findIndex(
          (line, idx) =>
            idx > shipToStart &&
            /(bill\s*to|vendor|supplier|payment\s*terms|terms|item\s*description|line\s*items?|description\s*qty)/i.test(line),
        )
      : -1;

    const shipBlock = shipToStart >= 0
      ? lines.slice(shipToStart, shipToEnd > shipToStart ? shipToEnd : Math.min(lines.length, shipToStart + 16))
      : [];

    const shipToContacts = this.extractShipToContacts(shipBlock);
    if (shipToContacts.length > 0) {
      result.address = shipToContacts.join(', ');
    }

    const shipName = this.extractLabeledFieldFromLines(shipBlock, ['name', 'ship to', 'consignee'], ['phone', 'email', 'fax', 'city']);
    const shipAddress = this.extractLabeledFieldFromLines(shipBlock, ['address', 'street'], ['phone', 'email', 'fax']);
    const shipCity = this.extractLabeledFieldFromLines(shipBlock, ['city / state / zip', 'city/state/zip', 'city, state, zip', 'city state zip'], ['phone', 'email', 'fax']);

    const addressParts = [shipName, shipAddress, shipCity]
      .filter((part): part is string => Boolean(part && this.isLikelyAddressFragment(part)));
    if (!result.address && addressParts.length > 0) {
      result.address = addressParts.join(', ');
    }

    const poDateCandidates = this.collectDateCandidates(lines, [
      /ship\s*date/i,
      /delivery\s*date/i,
      /requested\s*date/i,
      /promised\s*date/i,
      /order\s*date/i,
    ]);
    if (poDateCandidates.length > 0) {
      // Prefer ship/delivery/requested dates over order date by favoring earlier-in-list anchors from collectDateCandidates pass.
      result.date = poDateCandidates[0];
    }

    return result;
  }

  private extractShippingFromJe(lines: string[]): { date?: string; address?: string } {
    const result: { date?: string; address?: string } = {};

    // JE typically provides transactional narrative + date; shipping address may be absent.
    const jeDateCandidates = this.collectDateCandidates(lines, [
      /journal\s*date/i,
      /posting\s*date/i,
      /document\s*date/i,
      /entry\s*date/i,
      /date/i,
    ]);
    if (jeDateCandidates.length > 0) {
      result.date = jeDateCandidates[0];
    }

    // Optional party/address extraction if JE includes customer/vendor/ship-to details.
    const partyAddress = this.extractLabeledFieldFromLines(
      lines,
      ['ship to', 'customer', 'vendor', 'party', 'address'],
      ['debit', 'credit', 'account', 'amount', 'balance'],
    );
    if (partyAddress && this.isLikelyAddressFragment(partyAddress)) {
      result.address = partyAddress;
    }

    return result;
  }

  private extractShippingFromTb(lines: string[]): { date?: string; address?: string } {
    // Trial Balance is typically not a shipping-source document.
    // Keep this conservative: only extract when explicit shipping cues are present.
    const text = lines.join('\n').toLowerCase();
    const hasShippingCue = /(ship\s*to|shipping\s*address|shipping\s*date|delivery\s*date|bill\s*of\s*lading|\bbol\b)/.test(text);
    if (!hasShippingCue) {
      return {};
    }

    const result: { date?: string; address?: string } = {};

    const dateCandidates = this.collectDateCandidates(lines, [
      /shipping\s*date/i,
      /delivery\s*date/i,
      /ship\s*date/i,
    ]);
    if (dateCandidates.length > 0) {
      result.date = dateCandidates[0];
    }

    const address = this.extractLabeledFieldFromLines(
      lines,
      ['ship to', 'shipping address', 'delivery address', 'deliver to', 'address'],
      ['debit', 'credit', 'balance', 'account', 'amount', 'total'],
    );
    if (address && this.isLikelyAddressFragment(address)) {
      result.address = address;
    }

    return result;
  }

  private extractShippingGeneric(lines: string[]): { date?: string; address?: string } {
    const lowerLines = lines.map((line) => line.toLowerCase());
    const result: { date?: string; address?: string } = {};

    for (let i = 0; i < lines.length; i += 1) {
      const lower = lowerLines[i];
      if (!/(ship\s*date|shipping\s*date|delivery\s*date|dispatch\s*date)/.test(lower)) continue;

      const same = lines[i].match(/(\d{1,4}[\/-]\d{1,2}[\/-]\d{2,4})/);
      if (same?.[1]) {
        result.date = same[1];
        break;
      }

      const next = lines[i + 1]?.match(/(\d{1,4}[\/-]\d{1,2}[\/-]\d{2,4})/);
      if (next?.[1]) {
        result.date = next[1];
        break;
      }
    }

    for (let i = 0; i < lines.length; i += 1) {
      const lower = lowerLines[i];
      if (!/(ship\s*to|shipping\s*address|delivery\s*address|deliver\s*to)/.test(lower)) continue;

      const afterLabel = lines[i].replace(/.*?(ship\s*to|shipping\s*address|delivery\s*address|deliver\s*to)\s*[:\-]?\s*/i, '').trim();
      if (afterLabel.length > 5 && this.isLikelyAddressFragment(afterLabel)) {
        result.address = afterLabel;
        break;
      }

      const collected: string[] = [];
      for (let j = i + 1; j < Math.min(lines.length, i + 4); j += 1) {
        const candidate = lines[j].trim();
        if (!candidate) continue;
        if (/(invoice|date|total|amount|description|qty|quantity|price|phone|fax|email|contact)/i.test(candidate)) break;
        if (!this.isLikelyAddressFragment(candidate)) continue;
        collected.push(candidate);
      }
      if (collected.length > 0) {
        result.address = collected.join(', ');
        break;
      }
    }

    return result;
  }

  private mergeShippingExtractions(
    generic: { date?: string; address?: string },
    docSpecific: { date?: string; address?: string },
  ): { date?: string; address?: string } {
    const merged: { date?: string; address?: string } = {
      date: docSpecific.date ?? generic.date,
      address: docSpecific.address ?? generic.address,
    };

    if (merged.address) {
      const cleanedAddress = this.cleanShippingAddress(merged.address);
      if (cleanedAddress && this.isLikelyAddressFragment(cleanedAddress)) {
        merged.address = cleanedAddress;
      } else {
        delete merged.address;
      }
    }

    return merged;
  }

  private cleanShippingAddress(value: string): string | undefined {
    const parts = value
      .split(',')
      .map((part) => part.trim())
      .filter(Boolean)
      .filter((part) => !/(^hwy\s*carrier$|carrier\s*name|trailer\s*no|serial\s*nos?|cid\s*no|sid\s*no)/i.test(part));

    const cleaned = parts.join(', ').replace(/\s+/g, ' ').trim();
    return cleaned || undefined;
  }

  private extractLabeledFieldFromLines(
    lines: string[],
    labels: string[],
    stopMarkers: string[] = [],
  ): string | undefined {
    for (let i = 0; i < lines.length; i += 1) {
      const line = lines[i];
      const lower = line.toLowerCase();
      if (!labels.some((label) => lower.includes(label.toLowerCase()))) continue;

      let extracted = line;
      for (const label of labels) {
        extracted = extracted.replace(new RegExp(`.*?${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*[:\\-]?\\s*`, 'i'), '');
      }
      extracted = extracted.trim();

      for (const marker of stopMarkers) {
        const idx = extracted.toLowerCase().indexOf(marker.toLowerCase());
        if (idx >= 0) extracted = extracted.slice(0, idx).trim();
      }

      if (extracted && !this.isHeaderLikeText(extracted)) return extracted;

      const next = lines[i + 1] ?? '';
      if (next && !this.isHeaderLikeText(next)) return next.trim();
    }

    return undefined;
  }

  private collectDateCandidates(lines: string[], anchorPatterns: RegExp[]): string[] {
    const found = new Set<string>();

    for (let i = 0; i < lines.length; i += 1) {
      const line = lines[i];
      const lower = line.toLowerCase();
      const anchored = anchorPatterns.some((pattern) => pattern.test(lower));
      if (!anchored) continue;

      const window = [lines[i - 1] ?? '', line, lines[i + 1] ?? '', lines[i + 2] ?? ''];
      for (const part of window) {
        const matches = part.matchAll(/(\d{1,4}[\/-]\d{1,2}[\/-]\d{2,4})/g);
        for (const match of matches) {
          const normalized = this.normalizeDateToken(match[1]);
          if (normalized) found.add(normalized);
        }
      }
    }

    // Fallback for glued-text docs: capture standalone date-only lines if no anchored dates were found.
    if (found.size === 0) {
      for (const line of lines) {
        const m = line.match(/^(\d{1,4}[\/-]\d{1,2}[\/-]\d{2,4})$/);
        if (m?.[1]) {
          const normalized = this.normalizeDateToken(m[1]);
          if (normalized) found.add(normalized);
        }
      }
    }

    return Array.from(found);
  }

  private extractBolShipToContacts(lines: string[]): string[] {
    return this.extractShipToContacts(lines);
  }

  private extractShipToContacts(lines: string[]): string[] {
    const contacts: string[] = [];

    for (let i = 0; i < lines.length; i += 1) {
      const line = lines[i].trim();
      const lower = line.toLowerCase();

      if (!/\bname\b/.test(lower)) continue;
      if (/(carrier\s*name|third\s*party|shipper|vendor|supplier)/i.test(lower)) continue;

      let value = line.replace(/.*?\bname\b\s*[:\-]?\s*/i, '').trim();
      if (!value) {
        value = (lines[i + 1] ?? '').trim();
      }

      if (!value) continue;
      if (this.isHeaderLikeText(value)) continue;
      if (/(carrier\s*name|trailer|serial|cid\s*no|sid\s*no|city\s*\/\s*state\s*\/\s*zip|vendor|supplier)/i.test(value)) continue;

      const letters = (value.match(/[a-z]/gi) ?? []).length;
      if (letters < 4) continue;

      contacts.push(value);
    }

    return Array.from(new Set(contacts));
  }

  private isHeaderLikeText(value: string): boolean {
    const v = value.trim();
    if (!v) return true;
    if (/^[A-Z\s/&.-]+$/.test(v) && v.length > 8) return true;
    if (/(carrier\s*name|description\s*of\s*articles|special\s*marks|exceptions|nmfc|class|qty|type|line\s*total)/i.test(v)) return true;
    return false;
  }

  private isLikelyAddressFragment(value: string): boolean {
    const v = value.trim();
    if (!v || this.isHeaderLikeText(v)) return false;
    if (/(carrier\s*name|trailer\s*no|serial\s*nos?|cid\s*no|sid\s*no)/i.test(v)) return false;

    const letters = (v.match(/[a-z]/gi) ?? []).length;
    return letters >= 4;
  }

  private toComparableDate(value: string): number | null {
    const iso = this.toIsoDate(value);
    if (!iso) return null;
    const ts = Date.parse(iso);
    return Number.isFinite(ts) ? ts : null;
  }

  private getNormalizedLines(text: string): string[] {
    return text
      .split(/\r?\n/g)
      .map((line) => line.replace(/\s+/g, ' ').trim())
      .filter((line) => line.length > 0);
  }

  private normalizeDateToken(value: string | undefined): string {
    const v = (value ?? '').trim();
    if (!v) return '';

    const anchored = v.match(/^(\d{1,4}[\/-]\d{1,2}[\/-]\d{2,4})/);
    return anchored?.[1] ?? '';
  }

  private parseAmount(value: string | undefined): number | null {
    if (!value) return null;
    const parsed = Number(value.replace(/[^0-9.]/g, ''));
    return Number.isFinite(parsed) ? Number(parsed.toFixed(2)) : null;
  }

  private toIsoDate(value: string | undefined): string | null {
    const v = (value ?? '').trim();
    if (!v) return null;

    let m = v.match(/^(\d{4})-(\d{2})-(\d{2})$/);
    if (m) return `${m[1]}-${m[2]}-${m[3]}`;

    m = v.match(/^(\d{1,2})\/(\d{1,2})\/(\d{2,4})$/);
    if (m) {
      const mm = m[1].padStart(2, '0');
      const dd = m[2].padStart(2, '0');
      const yyyy = m[3].length === 2 ? `20${m[3]}` : m[3];
      return `${yyyy}-${mm}-${dd}`;
    }

    m = v.match(/^(\d{1,2})-(\d{1,2})-(\d{2,4})$/);
    if (m) {
      const mm = m[1].padStart(2, '0');
      const dd = m[2].padStart(2, '0');
      const yyyy = m[3].length === 2 ? `20${m[3]}` : m[3];
      return `${yyyy}-${mm}-${dd}`;
    }

    return null;
  }

  private tokenSimilarity(left: string, right: string): number {
    const leftTokens = new Set(
      left
        .toLowerCase()
        .replace(/[^a-z0-9\s]+/g, ' ')
        .split(/\s+/g)
        .filter((t) => t.length > 1),
    );
    const rightTokens = new Set(
      right
        .toLowerCase()
        .replace(/[^a-z0-9\s]+/g, ' ')
        .split(/\s+/g)
        .filter((t) => t.length > 1),
    );

    if (leftTokens.size === 0 || rightTokens.size === 0) return 0;

    let intersection = 0;
    for (const token of leftTokens) {
      if (rightTokens.has(token)) intersection += 1;
    }

    const union = leftTokens.size + rightTokens.size - intersection;
    return union > 0 ? intersection / union : 0;
  }

  private getAmountCandidates(amount: number): string[] {
    const fixed = amount.toFixed(2);
    const withComma = new Intl.NumberFormat('en-US', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount);
    const noDecimals = String(Number(fixed));
    return [fixed, withComma, `$${withComma}`, `$${fixed}`, noDecimals];
  }

  private findFirstRectForAnySearch(
    words: OcrWord[],
    img: { w: number; h: number },
    searches: string[],
  ): { rect: NormalizedRect; value: string } | null {
    for (const s of searches) {
      const rect = this.findRectForSearch(words, img, s);
      if (rect) return { rect, value: s };
    }
    return null;
  }

  private findRectForSearch(
    words: OcrWord[],
    img: { w: number; h: number },
    search: string,
  ): NormalizedRect | null {
    const needle = this.normalizeForSearch(search);
    if (!needle) return null;

    // Find any word containing the needle or a strong substring match.
    for (const w of words) {
      const hay = this.normalizeForSearch(w.text);
      if (!hay) continue;
      if (hay.includes(needle) || needle.includes(hay)) {
        const x0 = this.clamp01(w.bbox.x0 / img.w);
        const y0 = this.clamp01(w.bbox.y0 / img.h);
        const x1 = this.clamp01(w.bbox.x1 / img.w);
        const y1 = this.clamp01(w.bbox.y1 / img.h);
        return { x: x0, y: y0, w: Math.max(0, x1 - x0), h: Math.max(0, y1 - y0) };
      }
    }

    // Fallback: if searching a multi-word value, try token-by-token.
    const tokens = search
      .split(/\s+/g)
      .map((t) => this.normalizeForSearch(t))
      .filter((t) => t.length >= 3);
    for (const token of tokens) {
      for (const w of words) {
        const hay = this.normalizeForSearch(w.text);
        if (!hay) continue;
        if (hay.includes(token) || token.includes(hay)) {
          const x0 = this.clamp01(w.bbox.x0 / img.w);
          const y0 = this.clamp01(w.bbox.y0 / img.h);
          const x1 = this.clamp01(w.bbox.x1 / img.w);
          const y1 = this.clamp01(w.bbox.y1 / img.h);
          return { x: x0, y: y0, w: Math.max(0, x1 - x0), h: Math.max(0, y1 - y0) };
        }
      }
    }

    return null;
  }

  private normalizeForSearch(value: string | undefined): string {
    return (value ?? '').trim().toLowerCase().replace(/[^a-z0-9]+/g, '');
  }

  private hasRequiredDocumentId(row: SampleRow): boolean {
    return this.normalizeId(row.documentId).length > 0;
  }

  private findBestHeaderRowIndex(rows: string[][]): number {
    const maxRowsToScan = Math.min(rows.length, 25);
    const fieldAliases: Record<SampleFieldKey, string[]> = {
      documentId: ['documentid', 'documentnumber', 'invoiceid', 'invoicenumber', 'docid'],
      customerName: ['customername', 'customer', 'client', 'party'],
      transactionDate: ['transactiondate', 'documentdate', 'date'],
      originalTransactionAmount: ['originaltransactionamount', 'transactionamount', 'amount', 'total', 'value'],
    };

    let bestIndex = 0;
    let bestScore = -1;

    for (let rowIndex = 0; rowIndex < maxRowsToScan; rowIndex += 1) {
      const row = rows[rowIndex] ?? [];
      const normalized = row.map((cell) => this.normalizeCsvHeader(cell));
      const nonEmptyCount = normalized.filter(Boolean).length;
      if (nonEmptyCount < 2) continue;

      let score = 0;
      const matchedFields = new Set<SampleFieldKey>();
      for (const field of Object.keys(fieldAliases) as SampleFieldKey[]) {
        const aliases = fieldAliases[field];
        const hasExact = normalized.some((h) => aliases.some((alias) => h === this.normalizeCsvHeader(alias)));
        const hasRelaxed = normalized.some((h) => aliases.some((alias) => h.includes(this.normalizeCsvHeader(alias)) || this.normalizeCsvHeader(alias).includes(h)));

        if (hasExact) {
          score += 4;
          matchedFields.add(field);
        } else if (hasRelaxed) {
          score += 2;
          matchedFields.add(field);
        }
      }

      score += matchedFields.size * 2;
      if (score > bestScore) {
        bestScore = score;
        bestIndex = rowIndex;
      }
    }

    return bestIndex;
  }

  private getAutoSampleMapping(headers: string[]): Record<SampleFieldKey, string> {
    const normalizedHeaders = headers.map((h) => this.normalizeCsvHeader(h));
    const mapping: Record<SampleFieldKey, string> = {
      documentId: '-1',
      customerName: '-1',
      transactionDate: '-1',
      originalTransactionAmount: '-1',
    };

    const preferredExactLabels: Record<SampleFieldKey, string[]> = {
      documentId: ['documentid'],
      customerName: ['customername'],
      transactionDate: ['transactiondate'],
      originalTransactionAmount: ['originaltransactionamount'],
    };

    const fieldAliases: Record<SampleFieldKey, string[]> = {
      documentId: ['documentid', 'documentnumber', 'invoiceid', 'invoicenumber', 'docid'],
      customerName: ['customername', 'customer', 'client', 'party'],
      transactionDate: ['transactiondate', 'documentdate', 'date'],
      originalTransactionAmount: ['originaltransactionamount', 'transactionamount', 'amount', 'total', 'value'],
    };

    const orderedFields: SampleFieldKey[] = [
      'documentId',
      'customerName',
      'transactionDate',
      'originalTransactionAmount',
    ];

    const usedIndexes = new Set<number>();

    // 1) Hard-coded exact labels (order-independent).
    for (const field of orderedFields) {
      const exactPreferredIndex = this.findHeaderIndex(normalizedHeaders, preferredExactLabels[field], false, usedIndexes);
      if (exactPreferredIndex !== -1) {
        mapping[field] = String(exactPreferredIndex);
        usedIndexes.add(exactPreferredIndex);
      }
    }

    // 2) Exact alias matching.
    for (const field of orderedFields) {
      if (mapping[field] !== '-1') continue;
      const exactIndex = this.findHeaderIndex(normalizedHeaders, fieldAliases[field], false, usedIndexes);
      if (exactIndex !== -1) {
        mapping[field] = String(exactIndex);
        usedIndexes.add(exactIndex);
      }
    }

    // 3) Alias/contains fallback.
    for (const field of orderedFields) {
      if (mapping[field] !== '-1') continue;
      const relaxedIndex = this.findHeaderIndex(normalizedHeaders, fieldAliases[field], true, usedIndexes);
      if (relaxedIndex !== -1) {
        mapping[field] = String(relaxedIndex);
        usedIndexes.add(relaxedIndex);
      }
    }

    return mapping;
  }

  private findHeaderIndex(
    headers: string[],
    aliases: string[],
    allowContains = false,
    usedIndexes: Set<number> = new Set<number>(),
  ): number {
    const aliasSet = aliases.map((alias) => this.normalizeCsvHeader(alias));
    for (let i = 0; i < headers.length; i += 1) {
      if (usedIndexes.has(i)) continue;
      const h = headers[i];
      if (!h) continue;
      if (aliasSet.includes(h)) return i;
      if (allowContains && aliasSet.some((alias) => h.includes(alias) || alias.includes(h))) return i;
    }
    return -1;
  }

  private readMappedCell(row: string[], indexValue: string): string {
    const index = Number(indexValue);
    if (!Number.isInteger(index) || index < 0) return '';
    return (row[index] ?? '').trim();
  }

  private parseCsvRows(text: string): string[][] {
    const rows: string[][] = [];
    let current: string[] = [];
    let cell = '';
    let inQuotes = false;

    for (let i = 0; i < text.length; i += 1) {
      const ch = text[i];
      const next = text[i + 1];

      if (ch === '"') {
        if (inQuotes && next === '"') {
          cell += '"';
          i += 1;
        } else {
          inQuotes = !inQuotes;
        }
        continue;
      }

      if (!inQuotes && ch === ',') {
        current.push(cell);
        cell = '';
        continue;
      }

      if (!inQuotes && (ch === '\n' || ch === '\r')) {
        if (ch === '\r' && next === '\n') i += 1;
        current.push(cell);
        rows.push(current);
        current = [];
        cell = '';
        continue;
      }

      cell += ch;
    }

    current.push(cell);
    rows.push(current);

    return rows.filter((row) => row.length > 1 || row[0]?.trim().length > 0);
  }

  private async parseTabularFile(file: File): Promise<string[][]> {
    const lower = file.name.toLowerCase();
    const isExcel =
      lower.endsWith('.xlsx') ||
      lower.endsWith('.xls') ||
      file.type.includes('sheet') ||
      file.type.includes('excel');

    if (!isExcel) {
      const text = await file.text();
      return this.parseCsvRows(text);
    }

    const buffer = await file.arrayBuffer();
    const workbook = XLSX.read(buffer, { type: 'array' });
    const firstSheetName = workbook.SheetNames[0];
    if (!firstSheetName) return [];

    const sheet = workbook.Sheets[firstSheetName];
    const matrix = XLSX.utils.sheet_to_json<(string | number | boolean | null)[]>(sheet, {
      header: 1,
      raw: false,
      blankrows: false,
      defval: '',
    });

    return matrix.map((row) =>
      row.map((cell) => (cell === null || cell === undefined ? '' : String(cell).trim())),
    );
  }

  private normalizeCsvHeader(value: string): string {
    return (value ?? '')
      .replace(/\uFEFF/g, '')
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '');
  }

  private buildApiUrl(path: string): string {
    const safePath = path.startsWith('/') ? path : `/${path}`;
    const configuredBase = (window.__APP_CONFIG__?.apiBaseUrl ?? '').trim();
    const defaultBase = 'http://localhost:3001';
    const base = configuredBase || defaultBase;

    if (base === '/') return safePath;
    return `${base.replace(/\/$/, '')}${safePath}`;
  }
}
