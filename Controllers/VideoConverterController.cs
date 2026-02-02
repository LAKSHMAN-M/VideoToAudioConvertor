using Microsoft.AspNetCore.Mvc;
using FFMpegCore;
using Whisper.net;
using Whisper.net.Ggml;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Timeouts;

namespace VideoToAudio.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideoConverterController : ControllerBase
{
    private readonly ILogger<VideoConverterController> _logger;
    private static readonly string[] AllowedVideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };

    public VideoConverterController(ILogger<VideoConverterController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            Service = "Video to Audio/Text Converter API",
            Version = "1.0.0",
            Endpoints = new
            {
                VideoToAudio = "/api/videoconverter/audio",
                VideoToText = "/api/videoconverter/text",
                Status = "/api/videoconverter/status"
            }
        });
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var ffmpegAvailable = await CheckFFmpegAvailability();
        return Ok(new
        {
            FFmpegAvailable = ffmpegAvailable,
            SupportedFormats = new
            {
                Input = AllowedVideoExtensions,
                AudioOutput = new[] { "mp3", "wav", "aac", "flac", "ogg" },
                TextOutput = new[] { "txt" }
            }
        });
    }

    [HttpPost("audio")]
    [RequestSizeLimit(500 * 1024 * 1024)] // 500 MB
    [RequestTimeout(600000)] // 10 minutes timeout (600,000 ms) for video processing
    public async Task<IActionResult> ConvertToAudio(IFormFile videoFile, [FromForm] string format = "mp3")
    {
        try
        {
            if (videoFile == null || videoFile.Length == 0)
                return BadRequest("No video file provided.");

            if (!IsValidVideoFile(videoFile.FileName))
                return BadRequest("Invalid video file format.");

            if (!IsValidAudioFormat(format))
                return BadRequest("Invalid audio format. Supported: mp3, wav, aac, flac, ogg");

            // Create temporary files
            var inputPath = Path.GetTempFileName();
            var outputPath = Path.GetTempFileName() + $".{format}";

            try
            {
                // Save uploaded file
                using (var stream = new FileStream(inputPath, FileMode.Create))
                {
                    await videoFile.CopyToAsync(stream);
                }

                _logger.LogInformation($"Converting {videoFile.FileName} to {format}");

                // Convert video to audio
                var success = await ConvertVideoToAudio(inputPath, outputPath, format);

                if (!success)
                    return StatusCode(500, "Conversion failed");

                // Return the audio file
                var audioBytes = await System.IO.File.ReadAllBytesAsync(outputPath);
                var contentType = GetAudioContentType(format);
                var fileName = Path.GetFileNameWithoutExtension(videoFile.FileName) + $".{format}";

                return File(audioBytes, contentType, fileName);
            }
            finally
            {
                // Clean up temporary files
                CleanupFile(inputPath);
                CleanupFile(outputPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting video to audio");
            return StatusCode(500, $"An error occurred: {ex.Message}");
        }
    }

    [HttpPost("text")]
    [RequestSizeLimit(500 * 1024 * 1024)] // 500 MB
    [RequestTimeout(600000)] // 10 minutes timeout (600,000 ms) for video processing
    public async Task<IActionResult> ConvertToText(IFormFile videoFile)
    {
        try
        {
            if (videoFile == null || videoFile.Length == 0)
                return BadRequest("No video file provided.");

            if (!IsValidVideoFile(videoFile.FileName))
                return BadRequest("Invalid video file format.");

            // Create temporary files
            var inputPath = Path.GetTempFileName();
            var tempAudioPath = Path.GetTempFileName() + ".wav";

            try
            {
                // Save uploaded file
                using (var stream = new FileStream(inputPath, FileMode.Create))
                {
                    await videoFile.CopyToAsync(stream);
                }

                _logger.LogInformation($"Converting {videoFile.FileName} to text");

                // Extract audio
                var audioExtractionSuccess = await ExtractAudio(inputPath, tempAudioPath);
                if (!audioExtractionSuccess)
                    return StatusCode(500, "Audio extraction failed");

                // Convert to text using Whisper
                var transcription = await ConvertAudioToText(tempAudioPath);
                if (transcription == null)
                    return StatusCode(500, "Speech-to-text conversion failed");

                if (string.IsNullOrWhiteSpace(transcription))
                    transcription = "No speech detected in the audio file.";

                var result = new
                {
                    FileName = videoFile.FileName,
                    Transcription = transcription,
                    WordCount = transcription.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length,
                    ProcessedAt = DateTime.UtcNow
                };

                return Ok(result);
            }
            finally
            {
                // Clean up temporary files
                CleanupFile(inputPath);
                CleanupFile(tempAudioPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting video to text");
            return StatusCode(500, $"An error occurred: {ex.Message}");
        }
    }

    private static bool IsValidVideoFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLower();
        return AllowedVideoExtensions.Contains(extension);
    }

    private static bool IsValidAudioFormat(string format)
    {
        return new[] { "mp3", "wav", "aac", "flac", "ogg" }.Contains(format.ToLower());
    }

    private static string GetAudioContentType(string format)
    {
        return format.ToLower() switch
        {
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "ogg" => "audio/ogg",
            _ => "application/octet-stream"
        };
    }

    private async Task<bool> ConvertVideoToAudio(string inputPath, string outputPath, string format)
    {
        try
        {
            var codec = GetAudioCodec(format);
            var bitrate = GetOptimalBitrate(format);

            FFMpegArgumentProcessor arguments;

            if (format == "wav" || format == "flac")
            {
                arguments = FFMpegArguments.FromFileInput(inputPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithCustomArgument($"-c:a {codec}")
                        .WithCustomArgument("-vn")
                        .WithCustomArgument("-threads 0"));
            }
            else
            {
                arguments = FFMpegArguments.FromFileInput(inputPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithCustomArgument($"-c:a {codec}")
                        .WithAudioBitrate(bitrate)
                        .WithCustomArgument("-vn")
                        .WithCustomArgument("-threads 0"));
            }

            return await arguments.ProcessAsynchronously();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg conversion failed");
            return false;
        }
    }

    private async Task<bool> ExtractAudio(string inputPath, string outputPath)
    {
        try
        {
            var arguments = FFMpegArguments.FromFileInput(inputPath)
                .OutputToFile(outputPath, true, options => options
                    .WithAudioCodec("pcm_s16le")
                    .WithAudioSamplingRate(16000)
                    .WithCustomArgument("-ac 1")
                    .WithCustomArgument("-vn")
                    .WithCustomArgument("-threads 0"));

            return await arguments.ProcessAsynchronously();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio extraction failed");
            return false;
        }
    }

    private async Task<string?> ConvertAudioToText(string audioPath)
    {
        try
        {
            _logger.LogInformation($"Starting Whisper conversion for: {audioPath}");
            
            var modelPath = await DownloadWhisperModel();
            if (modelPath == null)
            {
                _logger.LogError("Whisper model download failed");
                return null;
            }

            _logger.LogInformation($"Using Whisper model: {modelPath}");

            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage("auto")
                .Build();

            using var fileStream = System.IO.File.OpenRead(audioPath);
            var transcriptionText = new System.Text.StringBuilder();

            _logger.LogInformation("Processing audio with Whisper...");
            
            var segmentCount = 0;
            await foreach (var result in processor.ProcessAsync(fileStream))
            {
                transcriptionText.AppendLine($"[{result.Start:hh\\:mm\\:ss} - {result.End:hh\\:mm\\:ss}] {result.Text}");
                segmentCount++;
            }

            _logger.LogInformation($"Whisper processing completed. Segments found: {segmentCount}");
            
            var finalTranscription = transcriptionText.ToString();
            if (string.IsNullOrWhiteSpace(finalTranscription))
            {
                _logger.LogWarning("Transcription is empty - no speech detected in audio");
                return "No speech detected in the audio file.";
            }

            return finalTranscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Whisper conversion failed");
            return null;
        }
    }

    private async Task<string?> DownloadWhisperModel()
    {
        try
        {
            var modelDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".whisper-models");
            if (!Directory.Exists(modelDir))
            {
                Directory.CreateDirectory(modelDir);
            }

            var modelPath = Path.Combine(modelDir, "ggml-base.bin");

            if (System.IO.File.Exists(modelPath))
                return modelPath;

            _logger.LogInformation("Downloading Whisper model...");

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            var modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";
            using var response = await client.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var responseStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = System.IO.File.Create(modelPath);
            await responseStream.CopyToAsync(fileStream);

            _logger.LogInformation("Whisper model downloaded successfully!");
            return modelPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download Whisper model");
            return null;
        }
    }

    private static string GetAudioCodec(string format)
    {
        return format switch
        {
            "wav" => "pcm_s16le",
            "aac" => "aac",
            "flac" => "flac",
            "ogg" => "libvorbis",
            _ => "libmp3lame"
        };
    }

    private static int GetOptimalBitrate(string format)
    {
        return format switch
        {
            "wav" or "flac" => 0,
            "aac" => 128,
            "ogg" => 192,
            _ => 320
        };
    }

    private static async Task<bool> CheckFFmpegAvailability()
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

    private static void CleanupFile(string filePath)
    {
        try
        {
            if (System.IO.File.Exists(filePath))
                System.IO.File.Delete(filePath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}