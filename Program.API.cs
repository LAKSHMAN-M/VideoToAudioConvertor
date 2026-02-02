using FFMpegCore;
using Microsoft.AspNetCore.Builder;
using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS support
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// Get port from environment variable (for deployment)
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Configure file upload limits and timeouts
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 500 * 1024 * 1024; // 500 MB
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500 * 1024 * 1024; // 500 MB
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10); // 10 minutes
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(10); // 10 minutes  
});

// Configure request timeout
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromMinutes(10) // 10 minutes for video processing
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Use HTTPS redirection in production
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

// Enable static files FIRST - order matters!
app.UseDefaultFiles();
app.UseStaticFiles();

// Enable request timeouts
app.UseRequestTimeouts();

// Enable CORS
app.UseCors();

app.UseAuthorization();
app.MapControllers();

// Check FFmpeg availability on startup
if (!await CheckFFmpegAvailability())
{
    Console.WriteLine("Warning: FFmpeg is not found. Install FFmpeg for the API to work properly.");
}

app.Run();

static async Task<bool> CheckFFmpegAvailability()
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