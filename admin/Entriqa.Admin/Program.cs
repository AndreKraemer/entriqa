using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Entriqa.Admin;
using Entriqa.Admin.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Die API liegt immer auf der Origin unter /api – egal ob die App unter / (Dev) oder /admin/ (SWA) ausgeliefert wird.
var origin = new Uri(new Uri(builder.HostEnvironment.BaseAddress), "/");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = origin });
builder.Services.AddScoped<AdminApi>();
builder.Services.AddSingleton<Ui>();

var host = builder.Build();

// Oberflächensprache vor dem ersten Render aus dem localStorage übernehmen.
var ui = host.Services.GetRequiredService<Ui>();
try { ui.Init(await host.Services.GetRequiredService<IJSRuntime>().InvokeAsync<string?>("localStorage.getItem", Ui.StorageKey)); }
catch { /* ohne localStorage bleibt Deutsch */ }

await host.RunAsync();
