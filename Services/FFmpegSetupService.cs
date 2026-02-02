using Microsoft.Extensions.Hosting;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;
using FFMpegCore;
using VideoToAudio.Controllers;

namespace VideoToAudio.Services
{
    public class FFmpegSetupService : BackgroundService
    {
        private readonly ILogger<FFmpegSetupService> _logger;
        private static bool _isSetupComplete = false;
        private static bool _isSetupInProgress = false;

        public FFmpegSetupService(ILogger<FFmpegSetupService> logger)
        {
            _logger = logger;
        }

        public static bool IsFFmpegReady => _isSetupComplete;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var isAzure = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
            
            if (!isAzure)
            {
                _logger.LogInformation("Local environment detected, marking FFmpeg as ready");
                _isSetupComplete = true;
                return;
            }

            if (_isSetupInProgress)
            {
                _logger.LogInformation("FFmpeg setup already in progress");
                return;
            }

            _isSetupInProgress = true;
            _logger.LogInformation("Starting FFmpeg setup for Azure environment...");

            try
            {
                var ffmpegPath = Path.Combine(Path.GetTempPath(), "ffmpeg");
                Directory.CreateDirectory(ffmpegPath);
                
                _logger.LogInformation($"Downloading FFmpeg to: {ffmpegPath}");
                
                // Download FFmpeg with retry logic
                var maxRetries = 3;
                var retryCount = 0;
                bool downloadSuccess = false;
                
                while (!downloadSuccess && retryCount < maxRetries && !stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegPath);
                        downloadSuccess = true;
                        _logger.LogInformation("FFmpeg download completed successfully");
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.LogWarning($"FFmpeg download attempt {retryCount} failed: {ex.Message}");
                        
                        if (retryCount < maxRetries)
                        {
                            _logger.LogInformation($"Retrying in 5 seconds... (Attempt {retryCount + 1}/{maxRetries})");
                            await Task.Delay(5000, stoppingToken);
                        }
                    }
                }

                if (downloadSuccess)
                {
                    // Configure FFmpeg paths
                    GlobalFFOptions.Configure(options => options.BinaryFolder = ffmpegPath);
                    FFmpeg.SetExecutablesPath(ffmpegPath);
                    VideoConverterController.SetFFmpegPath(ffmpegPath);
                    
                    _isSetupComplete = true;
                    _logger.LogInformation("FFmpeg setup completed successfully for Azure");
                }
                else
                {
                    _logger.LogError($"Failed to download FFmpeg after {maxRetries} attempts");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to setup FFmpeg for Azure environment");
            }
            finally
            {
                _isSetupInProgress = false;
            }
        }
    }
}