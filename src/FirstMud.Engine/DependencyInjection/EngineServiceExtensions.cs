using FirstMud.Engine.Commands;
using FirstMud.Engine.Events;
using FirstMud.Engine.Tick;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FirstMud.Engine.DependencyInjection;

/// <summary>
/// High-level composition helpers for wiring Engine-layer services into a host's
/// IServiceCollection. Each method is intentionally small and focused so
/// Program.cs can read as a composition, not a pile of registrations.
/// </summary>
public static class EngineServiceExtensions
{
    /// <summary>
    /// Reserved for future Engine-owned messaging registrations. Today a no-op
    /// because <see cref="Messaging.IGameNotifier"/> is provided by the host
    /// (it needs SignalR, which Engine does not reference).
    /// </summary>
    public static IServiceCollection AddEngineMessaging(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>
    /// Registers the in-process event bus (<see cref="IGameEventPublisher"/>).
    /// Concrete <see cref="IGameEventSubscriber{TEvent}"/> implementations stay
    /// in the host because they are game-specific.
    /// </summary>
    public static IServiceCollection AddEngineEvents(this IServiceCollection services)
    {
        services.AddScoped<IGameEventPublisher, GameEventPublisher>();
        return services;
    }

    /// <summary>
    /// Registers the generic command pipeline. Concrete
    /// <c>ICommandHandler&lt;T&gt;</c> registrations remain in the host because
    /// they are game-specific.
    /// </summary>
    public static IServiceCollection AddEngineCommandPipeline(this IServiceCollection services)
    {
        services.AddScoped<CommandDispatcher>();
        return services;
    }

    /// <summary>
    /// Registers the generic tick loop as a singleton hosted service. Concrete
    /// <see cref="ITickHandler"/> registrations remain in the host.
    /// </summary>
    public static IServiceCollection AddEngineTickLoop(this IServiceCollection services)
    {
        services.AddSingleton<GameLoopService>();
        services.AddHostedService(sp => sp.GetRequiredService<GameLoopService>());
        return services;
    }
}
