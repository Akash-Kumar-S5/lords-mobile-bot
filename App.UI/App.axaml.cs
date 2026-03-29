using App.UI.ViewModels;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Bot.Core.Interfaces;
using Bot.Core.Services;
using Bot.Emulator.Interfaces;
using Bot.Emulator.Services;
using Bot.Infrastructure.Configuration;
using Bot.Infrastructure.DependencyInjection;
using Bot.Tasks.Interfaces;
using Bot.Tasks.Services;
using Bot.Tasks.Tasks;
using Bot.Vision.Interfaces;
using Bot.Vision.Services;
using Microsoft.Extensions.DependencyInjection;

namespace App.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize()
    {
        Services = ConfigureServices();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure();
        services.AddSingleton<IRuntimeBotSettings, RuntimeBotSettings>();

        services.AddSingleton<IDeviceManager, DeviceManager>();
        services.AddSingleton<IEmulatorController, AdbService>();
        services.AddSingleton<IImageDetector, ImageDetector>();
        services.AddSingleton<IOcrReader, TesseractOcrReader>();
        services.AddSingleton<IMapNavigator, MapNavigator>();
        services.AddSingleton<ITemplateVerifier, TemplateVerifier>();
        services.AddSingleton<IBotTask, ResourceGatherTask>();
        services.AddSingleton<IStateResolver, StateResolver>();
        services.AddSingleton<IBotModeController, BotModeController>();
        services.AddSingleton<IArmyLimitMonitorService, ArmyLimitMonitorService>();
        services.AddSingleton<ITaskSchedulerService, TaskSchedulerService>();
        services.AddSingleton<IBotEngine, BotEngine>();

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
