using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DBSemanticSearch.Client;
using DBSemanticSearch.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = string.IsNullOrWhiteSpace(apiBaseUrl)
        ? new Uri(builder.HostEnvironment.BaseAddress)
        : new Uri(apiBaseUrl, UriKind.Absolute)
});
builder.Services.AddScoped<SemanticSearchClient>();

await builder.Build().RunAsync();
