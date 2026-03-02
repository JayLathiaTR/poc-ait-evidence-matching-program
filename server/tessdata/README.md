Place Tesseract language data here to enable offline OCR.

Required file:
- eng.traineddata.gz (preferred)
	OR
- eng.traineddata (uncompressed; server will gzip it on the fly)

Why:
- In some corporate networks, Tesseract.js cannot download language data from its default CDN due to TLS interception/CA issues.
- The server is configured to serve this folder locally at `/tessdata` and point Tesseract.js to `http://127.0.0.1:<PORT>/tessdata`.

How to obtain eng.traineddata.gz:
- From an internal approved source, or
- From your local machine if you already have Tesseract language files available.

After adding the file, restart the server.
