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

    builder.Services.AddControllersWithViews();

    var app = builder.Build();

    app.UseStaticFiles();

    app.MapControllers();

    // Endpoints fallback
    app.MapGet("/", context =>
    {
        context.Response.Redirect("/home");
        return System.Threading.Tasks.Task.CompletedTask;
    });

    app.Run("http://0.0.0.0:8001");
}
catch (System.Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
