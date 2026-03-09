using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AitEvidenceMatching.App.Services;
using AitEvidenceMatching.App.ViewModels;
using AitEvidenceMatching.App.Views;

namespace AitEvidenceMatching.App;

public partial class App : Application
{
    private OcrServerHost? _ocrServerHost;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            var ocrClient = new OcrClient(new HttpClient
            {
                BaseAddress = new Uri("http://127.0.0.1:3001"),
            });
            var vm = new MainWindowViewModel(ocrClient);
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm,
            };

            _ocrServerHost = new OcrServerHost();
            _ocrServerHost.LogReceived += (line) =>
                Dispatcher.UIThread.Post(() => vm.AppendLog(line));

            desktop.Exit += (_, _) =>
            {
                _ocrServerHost?.StopAsync().GetAwaiter().GetResult();
                _ocrServerHost?.Dispose();
                _ocrServerHost = null;
            };

            _ = InitializeRuntimeAsync(vm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeRuntimeAsync(MainWindowViewModel vm)
    {
        if (_ocrServerHost is null)
        {
            vm.SetStatus("Unavailable", "OCR service host is not initialized.");
            return;
        }

        vm.SetStatus("Starting", "Launching OCR service process...");

        try
        {
            var startResult = await _ocrServerHost.StartAsync(CancellationToken.None);
            vm.SetStatus(startResult.IsHealthy ? "Healthy" : "Error", startResult.Message);
        }
        catch (Exception ex)
        {
            vm.SetStatus("Error", ex.Message);
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}