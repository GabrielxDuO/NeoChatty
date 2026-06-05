using ChattyStager.Components;
using ChattyStager.Helpers;
using ChattyStager.Services;

AppSettingsHelper.AppSettingsCheck();
Environment.SetEnvironmentVariable("DOTNET_hostBuilder__reloadConfigOnChange", "false");
Environment.SetEnvironmentVariable("ASPNETCORE_hostBuilder__reloadConfigOnChange", "false");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddHttpClient<DeploymentService>();
builder.Services.AddSingleton<StagerConfigService>();
builder.Services.AddSingleton<SystemInspectionService>();
builder.Services.AddSingleton<DatabaseAdminService>();
builder.Services.AddSingleton<BackendProcessService>();
builder.Services.AddSingleton<DashboardService>();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = { "index.html" }
});


app.UseStaticFiles();

app.MapWhen(ctx => !ctx.Request.Path.StartsWithSegments("/stager"), spaBranch =>
{
    spaBranch.UseRouting();
    spaBranch.UseEndpoints(endpoints =>
    {
        endpoints.MapFallbackToFile("index.html");
    });
});

app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/stager"), stagerBranch =>
{

    stagerBranch.UsePathBase("/stager");
    stagerBranch.UseStaticFiles();
    stagerBranch.UseRouting();
    stagerBranch.UseAntiforgery();

    stagerBranch.UseEndpoints(endpoints =>
    {

        endpoints.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
        // endpoints.MapBlazorHub("/_blazor"); 
    });
});

//
// app.UseStaticFiles();
//
// app.Map("/stager", stager =>
// {
//     stager.UsePathBase("/stager");
//     stager.UseStaticFiles();
//     stager.UseRouting();
//     stager.UseAntiforgery();
//
//     if (!app.Environment.IsDevelopment())
//     {
//         stager.UseExceptionHandler("/Error", createScopeForErrors: true);
//         stager.UseHsts();
//     }
//
//     stager.UseEndpoints(endpoints =>
//     {
//         endpoints.MapBlazorHub();
//         endpoints.MapRazorComponents<App>()
//             .AddInteractiveServerRenderMode();
//     });
// });
//
// app.MapWhen(
//     context => (!context.Request.Path.StartsWithSegments("/stager")),
//     chattyWeb =>
//     {
//         var defaultFilesOptions = new DefaultFilesOptions();
//         defaultFilesOptions.DefaultFileNames.Clear();
//         defaultFilesOptions.DefaultFileNames.Add("index.html");
//         chattyWeb.UseDefaultFiles(defaultFilesOptions);
//
//         chattyWeb.UseStaticFiles();
//
//         chattyWeb.Run(async context =>
//         {
//             context.Response.ContentType = "text/html";
//             await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
//         });
//     });

app.Run();
