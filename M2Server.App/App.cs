using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Simple;
using M2Server.App.Services;
using M2Server.Lib;
using M2Server.Lib.Infrastructure;
using M2Server.Lib.Services;
using Microsoft.Extensions.DependencyInjection;

namespace M2Server.App;

public sealed class App : Application
{
    private ServiceProvider? _services;

    internal static string? DataDirectory { get; set; }

    public override void Initialize()
    {
        this.RequestedThemeVariant = ThemeVariant.Light;
        this.Styles.Add(new SimpleTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        var services = new ServiceCollection();
        services.AddM2ServerCore(DataDirectory, false);
        services.AddM2ServerDesktop();
        services.AddSingleton(Program.Notifications);
        this._services = services.BuildServiceProvider(true);

        var store = this._services.GetRequiredService<DataStore>();
        var icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri(Constants.AvaloniaIconUri))));
        desktop.MainWindow = new MainWindow(
            store,
            this._services.GetRequiredService<DisplayService>(),
            this._services.GetRequiredService<Security>(),
            this._services.GetRequiredService<CertificateService>(),
            this._services.GetRequiredService<DesktopWorkflow>(),
            this._services.GetRequiredService<INotificationService>()
        ) { Icon = icon };
        desktop.Exit += (_, _) => this._services.Dispose();
        base.OnFrameworkInitializationCompleted();
    }
}