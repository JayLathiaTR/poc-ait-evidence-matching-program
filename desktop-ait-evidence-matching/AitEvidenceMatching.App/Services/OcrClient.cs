using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AitEvidenceMatching.App.Services;

public sealed class OcrClient
{
    private readonly HttpClient _httpClient;

    public OcrClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<OcrResponse> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GetMimeType(filePath));
        form.Add(content, "file", Path.GetFileName(filePath));

        using var response = await _httpClient.PostAsync("/api/ocr", form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"OCR request failed ({(int)response.StatusCode}): {body}");
        }

        var json = await response.Content.ReadFromJsonAsync<OcrResponse>(cancellationToken: cancellationToken);
        return json ?? new OcrResponse();
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".tif" => "image/tiff",
            ".tiff" => "image/tiff",
            _ => "application/octet-stream",
        };
    }
}

public sealed class OcrResponse
{
    public string Text { get; set; } = string.Empty;
}
