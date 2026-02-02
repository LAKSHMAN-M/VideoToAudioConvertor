using FFMpegCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

try
{
    Console.WriteLine("Starting application...");
    
    var builder = WebApplication.CreateBuilder(args);

    // Configure detailed logging
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
    
    Console.WriteLine("Logging configured...");

// Add services to the container.
    Console.WriteLine("Adding services...");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS support
    Console.WriteLine("Configuring CORS...");
{
    options.AddDefaultPolicy(corsBuilder =>
    {
        corsBuilder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// Get port from environment variable (for deployment)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
    Console.WriteLine($"Using port from environment: {port}");
}
else
{
    Console.WriteLine("No PORT environment variable found, using default configuration");
}

// Configure file upload limits and timeouts
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 500 * 1024 * 1024; // 500 MB
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500 * 1024 * 1024; // 500 MB
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(15); // 15 minutes
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(15); // 15 minutes
    options.Limits.MaxConcurrentConnections = 100;
    options.Limits.MaxConcurrentUpgradedConnections = 100;
});

// Configure request timeout
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromMinutes(15) // 15 minutes for video processing
    };
});

// Add additional configuration for Azure App Service
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500 * 1024 * 1024; // 500 MB
    options.ValueLengthLimit = int.MaxValue;
    options.ValueCountLimit = int.MaxValue;
    options.KeyLengthLimit = int.MaxValue;
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Application starting up...");
logger.LogInformation($"Environment: {app.Environment.EnvironmentName}");
logger.LogInformation("CORS configured to allow all origins");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    logger.LogInformation("Swagger UI enabled for development environment");
}

// Use HTTPS redirection in production
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
    logger.LogInformation("HTTPS redirection enabled for production");
}

// Enable static files FIRST - order matters!
app.UseDefaultFiles();
app.UseStaticFiles();
logger.LogInformation("Static files middleware enabled");

// Enable request timeouts
app.UseRequestTimeouts();
logger.LogInformation("Request timeouts configured");

// Enable CORS
app.UseCors();
logger.LogInformation("CORS middleware enabled");

app.UseAuthorization();
app.MapControllers();

// Log mapped endpoints
logger.LogInformation("API Controllers mapped and ready");

// Check FFmpeg availability on startup
logger.LogInformation("Checking FFmpeg availability...");
if (!await CheckFFmpegAvailability())
{
    logger.LogWarning("FFmpeg is not found. Video conversion will not work properly.");
    Console.WriteLine("Warning: FFmpeg is not found. Install FFmpeg for the API to work properly.");
}
else
{
    logger.LogInformation("FFmpeg is available and ready for video processing");
}

logger.LogInformation("Application ready to accept requests");
app.Run();
}
catch (Exception ex)
{
    Console.WriteLine($"Application startup failed: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    
    // Also try to log to a file in case console logging isn't working
    try
    {
        var logPath = Path.Combine(Path.GetTempPath(), "startup_error.log");
        await File.WriteAllTextAsync(logPath, $"Startup Error: {ex.Message}\nStack Trace: {ex.StackTrace}\nTime: {DateTime.UtcNow}");
    }
    catch { /* ignore file write errors */ }
    
    throw; // Re-throw to ensure the process exits with error code
}
{
    try
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}