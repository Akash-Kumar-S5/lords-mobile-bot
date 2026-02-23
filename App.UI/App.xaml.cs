using App.UI.ViewModels;
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
using Microsoft.UI.Xaml;

namespace App.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = default!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Activate();
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
