using RemoteExec.Api.Configuration;
using RemoteExec.Api.Core.Interfaces;
using RemoteExec.Api.Infrastructure.Executors;
using RemoteExec.Api.Infrastructure.Resilience;
using RemoteExec.Api.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<ResilienceConfig>(builder.Configuration.GetSection("Resilience"));

// Core Services
builder.Services.AddHttpClient("UniversalClient");
builder.Services.AddSingleton<IMetricsCollector, InMemoryMetricsCollector>();
builder.Services.AddSingleton<IResiliencePolicy, CustomResiliencePolicy>();

// Executors (Registered as IExecutor)
builder.Services.AddSingleton<IExecutor, HttpExecutor>();
builder.Services.AddSingleton<IExecutor, PowerShellExecutor>();

// Orchestrator
builder.Services.AddSingleton<IRequestOrchestrator, RequestOrchestrator>();

// Controllers
builder.Services.AddControllers();

// Logging
builder.Services.AddLogging(logging => 
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddDebug();
});

var app = builder.Build();

// Middleware
app.Use(async (context, next) =>
{
    // Correlation ID
    if (!context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
    {
        correlationId = Guid.NewGuid().ToString();
        context.Request.Headers["X-Correlation-ID"] = correlationId;
    }
    
    // Add to Response
    context.Response.OnStarting(() => {
        context.Response.Headers["X-Correlation-ID"] = correlationId;
        return Task.CompletedTask;
    });

    // Logging Scope
    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId.ToString() }))
    {
        await next();
    }
});

app.UseRouting();
app.MapControllers();

app.Run();

public partial class Program { }