using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LightBulb.Services;

public sealed record AppRelease(Version Version, Uri InstallerUrl, string Sha256, long Size);

public interface IUpdateService
{
    bool IsAllowed { get; }
    bool CanInstall { get; }
    Task<AppRelease?> CheckAsync(CancellationToken cancellationToken);
    Task<string> DownloadAsync(AppRelease release, CancellationToken cancellationToken);
    Task LaunchInstallerAsync(
        string installerPath,
        AppRelease release,
        bool autoStart,
        CancellationToken cancellationToken
    );
}

public sealed class UpdateService : IUpdateService, IDisposable
{
    internal const string AssetName = "Candlelight-Setup-win-x64.exe";
    private const long MaximumInstallerSize = 200 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly Version _current;
    private readonly string _cacheDirectory;

    public UpdateService()
        : this(
            new HttpClient(),
            Program.Version,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Candlelight",
                "Updates"
            )
        ) { }

    internal UpdateService(HttpClient http, Version current, string cacheDirectory)
    {
        _http = http;
        _current = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        _cacheDirectory = cacheDirectory;
        _http.Timeout = TimeSpan.FromMinutes(5);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Candlelight/{Program.VersionString}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public bool IsAllowed => StartOptions.Current.IsAutoUpdateAllowed;
    public bool CanInstall => File.Exists(Path.Combine(Program.ExecutableDirPath, ".installed"));

    public async Task<AppRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!IsAllowed)
            throw new InvalidOperationException(
                "As atualizações estão desativadas neste ambiente."
            );
        if (StartOptions.Current.IsPreview)
            return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await _http.GetAsync(
            "https://api.github.com/repos/DacostaWeb/Candlelight/releases/latest",
            timeout.Token
        );
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token), _current);
    }

    internal static AppRelease? ParseRelease(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            return null;
        var tag = root.GetProperty("tag_name").GetString();
        if (
            tag is null
            || !tag.StartsWith('v')
            || !Version.TryParse(tag[1..], out var version)
            || version.Build < 0
            || version.Revision >= 0
        )
            throw new InvalidDataException("A versão publicada não é válida.");
        if (version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build)))
            return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AssetName)
                continue;
            var expectedUrl =
                $"https://github.com/DacostaWeb/Candlelight/releases/download/{tag}/{AssetName}";
            var digest = asset.GetProperty("digest").GetString();
            var size = asset.GetProperty("size").GetInt64();
            if (
                asset.GetProperty("browser_download_url").GetString() != expectedUrl
                || asset.GetProperty("state").GetString() != "uploaded"
                || digest is null
                || !digest.StartsWith("sha256:", StringComparison.Ordinal)
                || digest.Length != 71
                || !digest[7..].All(Uri.IsHexDigit)
                || size <= 0
                || size > MaximumInstallerSize
            )
                throw new InvalidDataException("O instalador publicado não passou a verificação.");
            return new AppRelease(version, new Uri(expectedUrl), digest[7..], size);
        }
        throw new InvalidDataException(
            "O instalador ainda não está disponível. Tenta novamente mais tarde."
        );
    }

    public async Task<string> DownloadAsync(AppRelease release, CancellationToken cancellationToken)
    {
        if (!IsAllowed || StartOptions.Current.IsPreview)
            throw new InvalidOperationException("Transferência de atualizações indisponível.");
        Directory.CreateDirectory(_cacheDirectory);
        var path = Path.Combine(_cacheDirectory, AssetName);
        var partial = path + ".part";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var response = await _http.GetAsync(
                release.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token
            );
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = File.Create(partial))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > release.Size || total > MaximumInstallerSize)
                        throw new InvalidDataException(
                            "O tamanho do download não corresponde ao publicado."
                        );
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
                if (total != release.Size)
                    throw new InvalidDataException("O download ficou incompleto.");
            }
            await using (var file = File.OpenRead(partial))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, timeout.Token));
                if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "A verificação do download falhou. Tenta novamente."
                    );
            }
            File.Move(partial, path, true);
            return path;
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    public async Task LaunchInstallerAsync(
        string installerPath,
        AppRelease release,
        bool autoStart,
        CancellationToken cancellationToken
    )
    {
        if (!IsAllowed || !CanInstall || StartOptions.Current.IsPreview)
            throw new InvalidOperationException(
                "Usa o instalador para atualizar esta cópia portátil."
            );
        if (
            Path.GetFullPath(installerPath)
            != Path.GetFullPath(Path.Combine(_cacheDirectory, AssetName))
        )
            throw new InvalidOperationException("Caminho de atualização inesperado.");
        var helper = Path.Combine(_cacheDirectory, "install-update.ps1");
        var ready = Path.Combine(_cacheDirectory, "ready");
        File.Delete(ready);
        await using (
            var resource =
                typeof(UpdateService).Assembly.GetManifestResourceStream(
                    "Candlelight.InstallUpdate.ps1"
                )
                ?? throw new InvalidOperationException(
                    "O assistente de atualização não está disponível."
                )
        )
        await using (var output = File.Create(helper))
            await resource.CopyToAsync(output, cancellationToken);
        var start = new ProcessStartInfo(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"
            )
        )
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (
            var argument in new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                helper,
                "-ParentProcessId",
                Environment.ProcessId.ToString(),
                "-ApplicationPath",
                Program.ExecutableFilePath,
                "-ExpectedSha256",
                release.Sha256,
                "-AutoStart",
                autoStart ? "yes" : "no",
            }
        )
            start.ArgumentList.Add(argument);
        using var process =
            Process.Start(start) ?? throw new IOException("Não foi possível abrir o instalador.");
        var deadline = Stopwatch.StartNew();
        try
        {
            while (!File.Exists(ready))
            {
                if (process.HasExited || deadline.Elapsed > TimeSpan.FromSeconds(15))
                    throw new IOException(
                        "Não foi possível iniciar a atualização. A aplicação continua aberta."
                    );
                await Task.Delay(100, cancellationToken);
            }
        }
        catch
        {
            // The helper cannot install while its parent is still running.
            if (!process.HasExited)
                process.Kill();
            throw;
        }
    }

    public void Dispose() => _http.Dispose();
}
