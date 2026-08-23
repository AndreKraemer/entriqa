using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Entriqa.Application;
using Entriqa.Application.Ports;
using Entriqa.Infrastructure.Brevo;
using Entriqa.Infrastructure.Http;
using Entriqa.Infrastructure.ReportingCloud;
using Entriqa.Infrastructure.Storage;

namespace Entriqa.Infrastructure;

public static class InfrastructureServiceExtensions
{
    /// <summary>HttpClients mit Standard-Resilienz (Retry nur auf transiente Fehler, Timeout, Circuit Breaker – Solution Standard §18.6).</summary>
    public static IServiceCollection AddEntriqaInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient<BrevoAdapter>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<EntriqaOptions>>().Value.Brevo;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.DefaultRequestHeaders.Add("api-key", o.ApiKey);
            c.DefaultRequestHeaders.Add("accept", "application/json");
            c.Timeout = TimeSpan.FromSeconds(20);
        }).AddStandardResilienceHandler();
        services.AddSingleton<Dev.DevMailSinkAdapter>();
        // Ohne Brevo-Key (lokal) landen Mails als klickbare HTML-Dateien in devmails/ statt bei Brevo.
        services.AddTransient<ISendTransactionalMailPort>(sp =>
            string.IsNullOrEmpty(sp.GetRequiredService<IOptions<EntriqaOptions>>().Value.Brevo.ApiKey)
                ? sp.GetRequiredService<Dev.DevMailSinkAdapter>()
                : sp.GetRequiredService<BrevoAdapter>());
        services.AddTransient<IUpsertBrevoContactPort>(sp =>
            string.IsNullOrEmpty(sp.GetRequiredService<IOptions<EntriqaOptions>>().Value.Brevo.ApiKey)
                ? sp.GetRequiredService<Dev.DevMailSinkAdapter>()
                : sp.GetRequiredService<BrevoAdapter>());
        services.AddTransient<IUpsertBrevoCompanyPort>(sp =>
            string.IsNullOrEmpty(sp.GetRequiredService<IOptions<EntriqaOptions>>().Value.Brevo.ApiKey)
                ? sp.GetRequiredService<Dev.DevMailSinkAdapter>()
                : sp.GetRequiredService<BrevoAdapter>());
        services.AddTransient<IBrevoDirectoryPort>(sp => sp.GetRequiredService<BrevoAdapter>());

        services.AddHttpClient<ReportingCloudAdapter>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<EntriqaOptions>>().Value.ReportingCloud;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.DefaultRequestHeaders.Add("Authorization", "ReportingCloud-APIKey " + o.ApiKey);
            c.Timeout = TimeSpan.FromSeconds(60);
        }).AddStandardResilienceHandler();
        services.AddTransient<IMergeDocumentPort>(sp => sp.GetRequiredService<ReportingCloudAdapter>());
        services.AddTransient<IListReportTemplatesPort>(sp => sp.GetRequiredService<ReportingCloudAdapter>());

        services.AddHttpClient<WebhookAdapter>(c => c.Timeout = TimeSpan.FromSeconds(15)).AddStandardResilienceHandler();
        services.AddTransient<IPostWebhookPort>(sp => sp.GetRequiredService<WebhookAdapter>());

        services.AddSingleton<BlobArtifactAdapter>();
        services.AddSingleton<IStoreArtifactPort>(sp => sp.GetRequiredService<BlobArtifactAdapter>());
        services.AddSingleton<ICreateDownloadLinkPort>(sp => sp.GetRequiredService<BlobArtifactAdapter>());
        services.AddSingleton<IListArtifactsPort>(sp => sp.GetRequiredService<BlobArtifactAdapter>());
        return services;
    }
}
