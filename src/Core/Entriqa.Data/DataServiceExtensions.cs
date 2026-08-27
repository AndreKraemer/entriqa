using Microsoft.Extensions.DependencyInjection;

namespace Entriqa.Data;

public sealed class DataMarker;

public static class DataServiceExtensions
{
    public static IServiceCollection AddEntriqaData(this IServiceCollection services)
    {
        services.AddSingleton<TableStorage>();
        services.Scan(scan => scan
            .FromAssemblyOf<DataMarker>()
            .AddClasses(c => c.Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal) || t.Name.EndsWith("Query", StringComparison.Ordinal)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithTransientLifetime());
        return services;
    }
}
