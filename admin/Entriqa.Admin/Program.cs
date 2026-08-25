using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Entriqa.Admin;
using Entriqa.Admin.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API always sits on the origin under /api - no matter whether the app is served from / (dev) or /admin/ (SWA).
// Accept-Language carries the interface language along, so error texts come back in the language the admin is using.
// The client is built lazily, after Ui.Init below, and switching the language reloads the app anyway.
var origin = new Uri(new Uri(builder.HostEnvironment.BaseAddress), "/");
builder.Services.AddScoped(sp =>
{
    var http = new HttpClient { BaseAddress = origin };
    http.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(sp.GetRequiredService<Ui>().Lang));
    return http;
});
builder.Services.AddScoped<AdminApi>();
builder.Services.AddSingleton<Ui>();

var host = builder.Build();

// Take the interface language from localStorage before the first render.
var ui = host.Services.GetRequiredService<Ui>();
try { ui.Init(await host.Services.GetRequiredService<IJSRuntime>().InvokeAsync<string?>("localStorage.getItem", Ui.StorageKey)); }
catch { /* without localStorage it stays German */ }

await host.RunAsync();
