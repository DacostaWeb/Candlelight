using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LightBulb.Services;
using LightBulb.ViewModels.Components;
using Xunit;

namespace LightBulb.Core.Tests;

public class UpdateSpecs
{
    private static readonly byte[] Payload = [1, 2, 3, 4];
    private static readonly AppRelease Release = new(
        new Version(0, 6, 4),
        new Uri(
            "https://github.com/DacostaWeb/Candlelight/releases/download/v0.6.4/Candlelight-Setup-win-x64.exe"
        ),
        Convert.ToHexString(SHA256.HashData(Payload)),
        Payload.Length
    );

    private static string Metadata(
        string tag = "v0.6.4",
        bool draft = false,
        bool prerelease = false,
        string? url = null,
        string? digest = null
    ) =>
        JsonSerializer.Serialize(
            new
            {
                tag_name = tag,
                draft,
                prerelease,
                assets = new[]
                {
                    new
                    {
                        name = UpdateService.AssetName,
                        state = "uploaded",
                        browser_download_url = url ?? Release.InstallerUrl.AbsoluteUri,
                        size = Payload.Length,
                        digest = digest ?? "sha256:" + Release.Sha256,
                    },
                },
            }
        );

    [Fact]
    public void A_new_stable_release_uses_the_real_installer_name_and_digest()
    {
        Assert.Equal(Release, UpdateService.ParseRelease(Metadata(), new Version(0, 6, 3, 0)));
    }

    [Theory]
    [InlineData("v0.6.2", false, false)]
    [InlineData("v0.6.3", false, false)]
    [InlineData("v0.6.4", true, false)]
    [InlineData("v0.6.4", false, true)]
    public void Older_equal_draft_and_prerelease_versions_do_not_trigger_updates(
        string tag,
        bool draft,
        bool prerelease
    )
    {
        Assert.Null(
            UpdateService.ParseRelease(Metadata(tag, draft, prerelease), new Version(0, 6, 3, 0))
        );
    }

    [Theory]
    [InlineData(
        "http://github.com/DacostaWeb/Candlelight/releases/download/v0.6.4/Candlelight-Setup-win-x64.exe",
        null
    )]
    [InlineData("https://github.com/someone/another-app/releases/download/v0.6.4/Setup.exe", null)]
    [InlineData(null, "sha256:invalid")]
    public void Untrusted_download_locations_or_invalid_hashes_are_rejected(
        string? url,
        string? digest
    )
    {
        Assert.Throws<InvalidDataException>(() =>
            UpdateService.ParseRelease(Metadata(url: url, digest: digest), new Version(0, 6, 3))
        );
    }

    private sealed class Handler(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes) }
            );
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("corrupt")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    [InlineData("offline")]
    public async Task Only_a_complete_verified_download_is_kept(string scenario)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "Candlelight-update-test-" + Guid.NewGuid().ToString("N")
        );
        var bytes = scenario switch
        {
            "corrupt" => new byte[] { 9, 9, 9, 9 },
            "truncated" => [1, 2],
            "oversized" => [1, 2, 3, 4, 5],
            _ => Payload,
        };
        using var service = new UpdateService(
            new HttpClient(
                new Handler(
                    bytes,
                    scenario == "offline" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK
                )
            ),
            new Version(0, 6, 3),
            directory
        );
        try
        {
            if (scenario == "valid")
            {
                var path = await service.DownloadAsync(Release, default);
                Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
            }
            else
                await Assert.ThrowsAnyAsync<Exception>(() =>
                    service.DownloadAsync(Release, default)
                );
            Assert.False(File.Exists(Path.Combine(directory, UpdateService.AssetName + ".part")));
            Assert.Equal(
                scenario == "valid",
                File.Exists(Path.Combine(directory, UpdateService.AssetName))
            );
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private sealed class Updates : IUpdateService
    {
        public bool IsAllowed { get; set; } = true;
        public bool CanInstall { get; set; } = true;
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public int Launches { get; private set; }
        public string? Failure { get; set; }
        public TaskCompletionSource<AppRelease?>? PendingCheck { get; set; }

        public Task<AppRelease?> CheckAsync(CancellationToken token)
        {
            Checks++;
            if (Failure == "check")
                throw new HttpRequestException("offline");
            return PendingCheck?.Task ?? Task.FromResult<AppRelease?>(Release);
        }

        public Task<string> DownloadAsync(AppRelease release, CancellationToken token)
        {
            Downloads++;
            if (Failure == "download")
                throw new InvalidDataException("bad hash");
            return Task.FromResult("installer.exe");
        }

        public Task LaunchInstallerAsync(
            string path,
            AppRelease release,
            bool autoStart,
            CancellationToken token
        )
        {
            Launches++;
            if (Failure == "launch")
                throw new IOException("helper failed");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Automatic_checks_never_download_or_install()
    {
        var updates = new Updates();
        using var model = new UpdatesViewModel(
            new SettingsService { IsAutoUpdateEnabled = true },
            updates,
            () => throw new Exception("unexpected shutdown"),
            () => throw new Exception("unexpected browser")
        );
        await model.CheckAutomaticallyAsync();
        Assert.Equal(1, updates.Checks);
        Assert.Equal(0, updates.Downloads);
        Assert.Equal(0, updates.Launches);
        Assert.Equal(Release, model.AvailableRelease);
    }

    [Fact]
    public async Task Manual_checks_and_install_work_with_automatic_checks_disabled()
    {
        var updates = new Updates();
        var shutdowns = 0;
        using var model = new UpdatesViewModel(
            new SettingsService { IsAutoUpdateEnabled = false },
            updates,
            () => shutdowns++,
            () => throw new Exception("unexpected browser")
        );
        await model.CheckAutomaticallyAsync();
        Assert.Equal(0, updates.Checks);
        await model.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, updates.Checks);
        Assert.Equal(0, shutdowns);
        await model.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, updates.Downloads);
        Assert.Equal(1, updates.Launches);
        Assert.Equal(1, shutdowns);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("download")]
    [InlineData("launch")]
    public async Task Failures_leave_the_app_running_and_the_button_retryable(string failure)
    {
        var updates = new Updates { Failure = failure };
        var shutdowns = 0;
        using var model = new UpdatesViewModel(
            new SettingsService(),
            updates,
            () => shutdowns++,
            () => { }
        );
        await model.UpdateCommand.ExecuteAsync(null);
        if (failure != "check")
            await model.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(0, shutdowns);
        Assert.False(model.IsBusy);
        Assert.True(model.UpdateCommand.CanExecute(null));
        Assert.Contains("Não foi possível", model.UpdateStatus);
        if (failure == "download")
            Assert.Equal(0, updates.Launches);
    }

    [Fact]
    public async Task A_pending_check_cannot_be_started_twice()
    {
        var updates = new Updates
        {
            PendingCheck = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var model = new UpdatesViewModel(
            new SettingsService(),
            updates,
            () => { },
            () => { }
        );
        var pending = model.CheckAutomaticallyAsync();
        Assert.False(model.UpdateCommand.CanExecute(null));
        await model.CheckAutomaticallyAsync();
        Assert.Equal(1, updates.Checks);
        updates.PendingCheck.SetResult(Release);
        await pending;
        Assert.True(model.UpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Portable_copies_open_downloads_without_running_an_installer()
    {
        var updates = new Updates { CanInstall = false };
        var browsers = 0;
        using var model = new UpdatesViewModel(
            new SettingsService(),
            updates,
            () => throw new Exception("unexpected shutdown"),
            () => browsers++
        );
        await model.UpdateCommand.ExecuteAsync(null);
        await model.UpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, browsers);
        Assert.Equal(0, updates.Downloads);
    }
}
