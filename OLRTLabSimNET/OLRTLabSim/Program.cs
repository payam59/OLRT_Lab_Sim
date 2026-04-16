using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using OLRTLabSim.Data;
using OLRTLabSim.Engine;
using OLRTLabSim.Services;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting web application");
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    Database.InitDb();

    // Add Services
    builder.Services.AddSingleton<ModbusRuntimeManager>();
    builder.Services.AddSingleton<BacnetRuntimeManager>();
    builder.Services.AddSingleton<Dnp3RuntimeManager>();

    // Background Engine
    builder.Services.AddHostedService<SimulationEngine>();

    builder.Services.AddControllersWithViews().AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = new OLRTLabSim.Helpers.SnakeCaseNamingPolicy();
    });

    var app = builder.Build();

    app.UseStaticFiles();

    app.MapControllers();

    // Endpoints fallback
    // Explicitly map / to the index page in Pages
    app.MapGet("/", async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync("Pages/index.html");
    });

    app.MapGet("/status", async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync("Pages/bacnet_status.html");
    });

    app.Run();
}
catch (System.Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
