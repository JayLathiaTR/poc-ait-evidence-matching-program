const express = require('express');
const multer = require('multer');
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const pdfParse = require('pdf-parse');
const { createWorker } = require('tesseract.js');

const app = express();
const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 25 * 1024 * 1024 } });

const port = Number(process.env.PORT || 3001);
const tessdataDir = path.join(__dirname, 'tessdata');
const engTrainedDataPath = path.join(tessdataDir, 'eng.traineddata.gz');
const engTrainedDataRawPath = path.join(tessdataDir, 'eng.traineddata');

app.use(express.json({ limit: '2mb' }));

// Serve local traineddata over HTTP so tesseract.js can fetch it without external internet/TLS.
// If only the uncompressed file is present, gzip it on the fly at the expected URL.
app.get('/tessdata/eng.traineddata.gz', (req, res, next) => {
  if (fs.existsSync(engTrainedDataPath)) return next();
  if (!fs.existsSync(engTrainedDataRawPath)) return res.status(404).end();

  res.setHeader('Content-Type', 'application/gzip');
  res.setHeader('Cache-Control', 'public, max-age=31536000, immutable');
  fs.createReadStream(engTrainedDataRawPath)
    .pipe(zlib.createGzip())
    .pipe(res);
});
app.use('/tessdata', express.static(tessdataDir));

// Very permissive dev CORS for local-only demo.
app.use((req, res, next) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET,POST,OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
  if (req.method === 'OPTIONS') return res.status(204).end();
  next();
});

let workerPromise;

function resolveEngLangPath() {
  if (process.env.TESS_LANG_PATH) return process.env.TESS_LANG_PATH;
  if (process.env.TESS_LANG_URL) return process.env.TESS_LANG_URL;

  // Default to a local HTTP-served tessdata folder.
  if (!fs.existsSync(engTrainedDataPath) && !fs.existsSync(engTrainedDataRawPath)) {
    return undefined;
  }

  return `http://127.0.0.1:${port}/tessdata`;
}

async function getWorker() {
  if (!workerPromise) {
    workerPromise = (async () => {
      const langPath = resolveEngLangPath();
      if (!langPath) {
        throw new Error(
          'Missing Tesseract language data. Place eng.traineddata.gz (or eng.traineddata) in server/tessdata/ or set TESS_LANG_URL/TESS_LANG_PATH.',
        );
      }

      // createWorker signature in tesseract.js v5: createWorker(langs, oem?, options?)
      // where `langPath` can be a local folder or a URL that serves `<lang>.traineddata(.gz)`.
      const worker = await createWorker('eng', 1, {
        langPath,
        cachePath: path.join(__dirname, '.tesseract-cache'),
      });
      return worker;
    })();
  }
  return workerPromise;
}

app.get('/health', (req, res) => {
  res.json({ ok: true });
});

app.post('/api/ocr', upload.single('file'), async (req, res) => {
  try {
    if (!req.file) return res.status(400).json({ error: 'missing_file' });

    const originalName = String(req.file.originalname || '').toLowerCase();
    const mimeType = String(req.file.mimetype || '').toLowerCase();
    const isPdf = mimeType === 'application/pdf' || originalName.endsWith('.pdf');

    if (isPdf) {
      const parsed = await pdfParse(req.file.buffer);
      const text = typeof parsed?.text === 'string' ? parsed.text : '';

      return res.json({
        text,
        words: [],
        source: 'pdf-text',
      });
    }

    const worker = await getWorker();
    const result = await worker.recognize(req.file.buffer);

    const data = result?.data || {};
    const words = Array.isArray(data.words)
      ? data.words
          .filter((w) => w && w.text && w.bbox)
          .map((w) => ({
            text: String(w.text),
            bbox: {
              x0: Number(w.bbox.x0),
              y0: Number(w.bbox.y0),
              x1: Number(w.bbox.x1),
              y1: Number(w.bbox.y1),
            },
            confidence: typeof w.confidence === 'number' ? w.confidence : undefined,
          }))
      : [];

    res.json({
      text: typeof data.text === 'string' ? data.text : '',
      words,
      source: 'tesseract',
    });
  } catch (err) {
    res.status(500).json({
      error: 'ocr_failed',
      message: err && typeof err.message === 'string' ? err.message : String(err),
    });
  }
});

app.listen(port, () => {
  // eslint-disable-next-line no-console
  console.log(`OCR server listening on http://localhost:${port}`);
});
