using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Entriqa.Application;
using Entriqa.Data;
using Entriqa.Functions;
using Entriqa.Functions.Http;
using Entriqa.Infrastructure;

// Composition root (Solution Standard §9.4): short, named extension calls, nothing else.
var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(app => app.UseMiddleware<ProblemDetailsMiddleware>())
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddEntriqaApplication(context.Configuration);
        services.AddEntriqaData();
        services.AddEntriqaInfrastructure();
        services.AddSingleton<SwaPrincipalReader>();
        services.AddHostedService<LocaleWarningHostedService>();
        services.AddHostedService<DevSeedHostedService>();
    })
    .Build();

host.Run();
