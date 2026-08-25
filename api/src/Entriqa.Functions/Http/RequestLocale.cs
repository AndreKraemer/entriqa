using Microsoft.AspNetCore.Http;
using Entriqa.Application;
using Entriqa.Application.Localization;

namespace Entriqa.Functions.Http;

/// <summary>Reads the language signals off the request and hands them to <see cref="LocaleNegotiation"/>.</summary>
public static class RequestLocale
{
    private const string Key = "entriqa:lang";

    /// <summary>
    /// A language taken from the request body, for functions that read one (submit sends the data-lang of the
    /// embedding page). The middleware runs after the body is gone, so the function has to hand it over here.
    /// </summary>
    public static void Remember(HttpContext? context, string? lang)
    {
        if (context is not null && !string.IsNullOrWhiteSpace(lang)) context.Items[Key] = lang;
    }

    public static string From(HttpRequest? request, EntriqaOptions options) =>
        LocaleNegotiation.Pick(Asked(request),
                               request?.Headers.AcceptLanguage.ToString(),
                               options.SiteLocales);

    private static string? Asked(HttpRequest? request)
    {
        if (request is null) return null;
        var query = request.Query["lang"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(query)) return query;
        return request.HttpContext.Items.TryGetValue(Key, out var remembered) ? remembered as string : null;
    }
}
