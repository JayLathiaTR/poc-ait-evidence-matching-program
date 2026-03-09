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

        var npmInstallResult = await EnsureServerDependenciesAsync(serverDirectory, nodePath, cancellationToken);
        if (!npmInstallResult.IsHealthy)
        {
            return npmInstallResult;
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

    private async Task<OcrServerStartResult> EnsureServerDependenciesAsync(string serverDirectory, string nodePath, CancellationToken cancellationToken)
    {
        var packageJson = Path.Combine(serverDirectory, "package.json");
        if (!File.Exists(packageJson))
        {
            return new OcrServerStartResult(false,
                $"OCR server package.json not found: {packageJson}");
        }

        var nodeModules = Path.Combine(serverDirectory, "node_modules");
        if (Directory.Exists(nodeModules))
        {
            return new OcrServerStartResult(true, "OCR server dependencies are already present.");
        }

        var npmFileName = ResolveNpmExecutable(nodePath);
        var installInfo = new ProcessStartInfo
        {
            FileName = npmFileName,
            Arguments = "ci --omit=dev",
            WorkingDirectory = serverDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var npmProcess = new Process { StartInfo = installInfo, EnableRaisingEvents = true };
        npmProcess.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) LogReceived?.Invoke($"npm: {e.Data}");
        };
        npmProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) LogReceived?.Invoke($"npm ERR: {e.Data}");
        };

        try
        {
            LogReceived?.Invoke("npm dependencies missing. Running 'npm ci --omit=dev' for OCR server...");
            npmProcess.Start();
            npmProcess.BeginOutputReadLine();
            npmProcess.BeginErrorReadLine();
            await npmProcess.WaitForExitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return new OcrServerStartResult(false,
                $"Could not run npm install for OCR server. Ensure Node.js/npm are installed or bundled. Details: {ex.Message}");
        }

        if (npmProcess.ExitCode != 0)
        {
            return new OcrServerStartResult(false,
                $"npm dependency install failed with exit code {npmProcess.ExitCode}. Ensure internet access or pre-bundle server dependencies.");
        }

        return new OcrServerStartResult(true, "OCR server dependencies installed.");
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

    private static string ResolveNpmExecutable(string nodePath)
    {
        var nodeDirectory = Path.GetDirectoryName(nodePath);
        if (!string.IsNullOrWhiteSpace(nodeDirectory))
        {
            var bundledNpm = Path.Combine(nodeDirectory, OperatingSystem.IsWindows() ? "npm.cmd" : "npm");
            if (File.Exists(bundledNpm))
            {
                return bundledNpm;
            }
        }

        return OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
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
