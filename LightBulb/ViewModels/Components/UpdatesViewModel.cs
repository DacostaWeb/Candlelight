using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightBulb.Framework;
using LightBulb.Services;

namespace LightBulb.ViewModels.Components;

public partial class UpdatesViewModel : ViewModelBase
{
    private readonly SettingsService _settings;
    private readonly IUpdateService _updates;
    private readonly Action _shutdown;
    private readonly Action _openDownloads;
    private readonly CancellationTokenSource _lifetime = new();
    private DispatcherTimer? _timer;
    private bool _initialized;
    private bool _disposed;

    public UpdatesViewModel(SettingsService settings, UpdateService updates)
        : this(
            settings,
            updates,
            App.ShutdownForUpdate,
            () =>
                Process.Start(
                    new ProcessStartInfo(Program.ProjectReleasesUrl + "/latest")
                    {
                        UseShellExecute = true,
                    }
                )
        ) { }

    internal UpdatesViewModel(
        SettingsService settings,
        IUpdateService updates,
        Action shutdown,
        Action openDownloads
    )
    {
        _settings = settings;
        _updates = updates;
        _shutdown = shutdown;
        _openDownloads = openDownloads;
        _settings.PropertyChanged += SettingsChanged;
    }

    public string VersionLabel => $"Candlelight {Program.VersionString}";
    public bool IsUpdateAllowed => _updates.IsAllowed;
    public bool IsAutoUpdateEnabled
    {
        get => _settings.IsAutoUpdateEnabled;
        set
        {
            if (value == _settings.IsAutoUpdateEnabled)
                return;
            _settings.IsAutoUpdateEnabled = value;
            _settings.Save();
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyPropertyChangedFor(nameof(UpdateButtonText))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string UpdateStatus { get; set; } = "";

    partial void OnUpdateStatusChanged(string value) =>
        Diagnostics.ColorTrace.Write("Updates: " + value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateButtonText))]
    public partial AppRelease? AvailableRelease { get; set; }

    public string UpdateButtonText =>
        IsBusy ? "Aguarda…"
        : AvailableRelease is { } release
            ? _updates.CanInstall ? $"Instalar {release.Version}"
                : "Descarregar atualização"
        : "Procurar atualizações";

    private bool CanUpdate() => IsUpdateAllowed && !IsBusy && !_disposed;

    private void SettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is "" or nameof(SettingsService.IsAutoUpdateEnabled))
        {
            OnPropertyChanged(nameof(IsAutoUpdateEnabled));
            if (
                _initialized
                && IsAutoUpdateEnabled
                && args.PropertyName == nameof(SettingsService.IsAutoUpdateEnabled)
            )
                _ = CheckAutomaticallyAsync();
        }
    }

    public override async Task InitializeAsync()
    {
        if (_initialized || _disposed)
            return;
        _initialized = true;
        if (!StartOptions.Current.IsPreview)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(3) };
            _timer.Tick += OnCheckTimer;
            _timer.Start();
            var resultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Candlelight",
                "Updates",
                "last-result.txt"
            );
            if (File.Exists(resultPath))
            {
                try
                {
                    UpdateStatus = File.ReadAllText(resultPath)
                        .StartsWith("success", StringComparison.Ordinal)
                        ? "Atualização instalada."
                        : "A instalação não terminou. Podes tentar novamente.";
                    File.Delete(resultPath);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            await CheckAutomaticallyAsync();
        }
    }

    private async void OnCheckTimer(object? sender, EventArgs args) =>
        await CheckAutomaticallyAsync();

    internal Task CheckAutomaticallyAsync() =>
        IsAutoUpdateEnabled && IsUpdateAllowed && !IsBusy ? CheckAsync() : Task.CompletedTask;

    private async Task CheckAsync()
    {
        if (!CanUpdate())
            return;
        IsBusy = true;
        UpdateStatus = "A procurar atualizações…";
        try
        {
            AvailableRelease = await _updates.CheckAsync(_lifetime.Token);
            UpdateStatus = AvailableRelease is { } release
                ? $"Versão {release.Version} disponível."
                : "Tens a versão mais recente.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            UpdateStatus = "Não foi possível verificar. Tenta novamente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        if (!CanUpdate())
            return;
        if (AvailableRelease is not { } release)
        {
            await CheckAsync();
            return;
        }
        if (!_updates.CanInstall)
        {
            try
            {
                _openDownloads();
            }
            catch (Exception)
            {
                UpdateStatus = "Não foi possível abrir os downloads. Tenta novamente.";
            }
            return;
        }
        IsBusy = true;
        try
        {
            UpdateStatus = "A transferir e verificar…";
            var path = await _updates.DownloadAsync(release, _lifetime.Token);
            UpdateStatus = "A instalar. O Candlelight vai reiniciar…";
            await _updates.LaunchInstallerAsync(
                path,
                release,
                _settings.IsAutoStartEnabled,
                _lifetime.Token
            );
            _shutdown();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            UpdateStatus = "Não foi possível atualizar. Podes tentar novamente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _settings.PropertyChanged -= SettingsChanged;
            _timer?.Stop();
            if (_timer is not null)
                _timer.Tick -= OnCheckTimer;
            _lifetime.Cancel();
            _lifetime.Dispose();
        }
        base.Dispose(disposing);
    }
}
