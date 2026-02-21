using Bot.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace Bot.Infrastructure.DependencyInjection;

public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        var uiStream = new InMemoryLogStream();
        services.AddSingleton(uiStream);
        services.AddSingleton<ILogEventStream>(uiStream);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File("logs/lordsbot-.log", rollingInterval: RollingInterval.Day)
            .WriteTo.Sink(new UiLogSink(uiStream))
            .CreateLogger();

        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: true));
        return services;
    }
}
