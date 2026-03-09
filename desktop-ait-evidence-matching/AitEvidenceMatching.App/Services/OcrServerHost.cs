using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AitEvidenceMatching.App.Services;

public sealed class OcrServerHost : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private Process? _process;

    public event Action<string>? LogReceived;

    public async Task<OcrServerStartResult> StartAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            var alreadyHealthy = await WaitForHealthAsync(cancellationToken);
            return new OcrServerStartResult(alreadyHealthy, alreadyHealthy
                ? "OCR server already running."
                : "OCR server process exists but health check failed.");
        }

        var serverDirectory = ResolveServerDirectory();
        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            return new OcrServerStartResult(false,
                "Could not locate server folder. Expected a 'server' directory beside app or repository root.");
        }

        var nodePath = ResolveNodeExecutable();
        if (string.IsNullOrWhiteSpace(nodePath))
        {
            return new OcrServerStartResult(false,
                "Could not locate Node.js executable. Install Node.js or bundle node runtime with the app.");
        }

        var entryFile = Path.Combine(serverDirectory, "index.js");
        if (!File.Exists(entryFile))
        {
            return new OcrServerStartResult(false,
                $"OCR server entry file not found: {entryFile}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = nodePath,
            Arguments = "index.js",
            WorkingDirectory = serverDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) LogReceived?.Invoke(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) LogReceived?.Invoke($"ERR: {e.Data}");
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var healthy = await WaitForHealthAsync(cancellationToken);
        return new OcrServerStartResult(healthy,
            healthy
                ? "OCR server started and healthy at http://127.0.0.1:3001/health"
                : "OCR server started but did not pass health check in time.");
    }

    public async Task StopAsync()
    {
        if (_process is null) return;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // Best-effort shutdown for app exit.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    private static string ResolveNodeExecutable()
    {
        // Prefer bundled node runtime when packaging desktop app.
        var bundledNode = Path.Combine(AppContext.BaseDirectory, "node", "node.exe");
        if (File.Exists(bundledNode)) return bundledNode;

        // Fall back to PATH-installed node.
        return "node";
    }

    private static string? ResolveServerDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "server"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "server")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "server")),
        };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private async Task<bool> WaitForHealthAsync(CancellationToken cancellationToken)
    {
        const string healthUrl = "http://127.0.0.1:3001/health";
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await _httpClient.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode) return true;
            }
            catch
            {
                // Server may still be booting.
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _process?.Dispose();
    }
}

public sealed record OcrServerStartResult(bool IsHealthy, string Message);
