using System.Runtime.Versioning;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Lenticulous;
using SpawnDev.SpawnJS;

[assembly: SupportedOSPlatform("browser")]

var builder = WebAssemblyHostBuilder.CreateDefault(args);
// SpawnDev.SpawnJS runtime
builder.Services.AddSpawnJSRuntime(out var JS);

builder.Services.AddScoped<Lenticulous.Services.RobotService>();

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().SpawnJSRunAsync();
