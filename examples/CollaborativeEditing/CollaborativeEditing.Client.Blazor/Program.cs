using CollaborativeEditing.Client.Blazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Same-origin by default. When the API runs on a different origin (for
// example a separate Aspire project URL), override it on the editor page or
// via the `ApiBaseUrl` app-setting.
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});
builder.Services.AddScoped<WorkspaceApiClient>();

await builder.Build().RunAsync();
