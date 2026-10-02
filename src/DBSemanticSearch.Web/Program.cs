using DBSemanticSearch.Client;
using DBSemanticSearch.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

if (!Uri.TryCreate(builder.Configuration["ApiBaseUrl"], UriKind.Absolute, out var apiBaseUri)
    || apiBaseUri.Scheme is not ("http" or "https"))
    throw new InvalidOperationException("ApiBaseUrl must be an absolute HTTP(S) URL.");

var functionKey = builder.Configuration["FunctionKey"];
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(functionKey))
    throw new InvalidOperationException("FunctionKey must be configured outside Development.");

builder.Services.AddHttpClient<SemanticSearchClient>(client =>
{
    client.BaseAddress = apiBaseUri;

    if (!string.IsNullOrWhiteSpace(functionKey))
        client.DefaultRequestHeaders.Add("x-functions-key", functionKey);
});

var app = builder.Build();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
