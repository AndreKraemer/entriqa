using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Security;

namespace Entriqa.Application;

public sealed class ApplicationMarker;

public static class ApplicationServiceExtensions
{
    /// <summary>Konventions-Registrierung (Solution Standard §9): Suffixe UseCase / Service / Step, transient, AsImplementedInterfaces.</summary>
    public static IServiceCollection AddEntriqaApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EntriqaOptions>(configuration.GetSection(EntriqaOptions.Section));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<FormTokenService>();
        services.AddSingleton<IpHasher>();

        services.Scan(scan => scan
            .FromAssemblyOf<ApplicationMarker>()
            .AddClasses(c => c.Where(t => t.Name.EndsWith("UseCase", StringComparison.Ordinal) || t.Name.EndsWith("Step", StringComparison.Ordinal)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithTransientLifetime()
            .AddClasses(c => c.Where(t => t.Name.EndsWith("Service", StringComparison.Ordinal)
                                          && t != typeof(FormTokenService)), publicOnly: false)   // oben bewusst Singleton – Suffix-Scan würde ihn transient überdecken
                .AsSelf()
                .WithTransientLifetime());

        return services;
    }
}
