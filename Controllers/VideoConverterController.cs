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

    [HttpGet("diagnostics")]
    public async Task<IActionResult> GetDiagnostics()
    {
        var diagnostics = new
        {
            Environment = Environment.MachineName,
            Platform = Environment.OSVersion.ToString(),
            WorkingDirectory = Environment.CurrentDirectory,
            TempPath = Path.GetTempPath(),
            FFmpegAvailable = await CheckFFmpegAvailability(),
            EnvironmentVariables = new
            {
                PATH = Environment.GetEnvironmentVariable("PATH"),
                TEMP = Environment.GetEnvironmentVariable("TEMP"),
                TMP = Environment.GetEnvironmentVariable("TMP")
            }
        };

        _logger.LogInformation($"Diagnostics: {System.Text.Json.JsonSerializer.Serialize(diagnostics)}");
        return Ok(diagnostics);
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

    [HttpPost("test-ffmpeg")]
    public async Task<IActionResult> TestFFmpeg([FromForm] string filePath)
    {
        try
        {
            _logger.LogInformation($"Testing FFmpeg with file: {filePath}");
            
            if (!System.IO.File.Exists(filePath))
            {
                return BadRequest($"File not found: {filePath}");
            }

            var fileInfo = new FileInfo(filePath);
            _logger.LogInformation($"File size: {fileInfo.Length} bytes");

            // Test basic FFmpeg probe
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{filePath}\" -t 1 -f null -",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    output.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    error.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return Ok(new
            {
                FilePath = filePath,
                FileSize = fileInfo.Length,
                ExitCode = process.ExitCode,
                Output = output.ToString(),
                Error = error.ToString(),
                FFmpegSuccess = process.ExitCode == 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg test failed");
            return StatusCode(500, $"FFmpeg test error: {ex.Message}");
        }
    }

    [HttpPost("audio")]
    [RequestSizeLimit(500 * 1024 * 1024)] // 500 MB
    [RequestTimeout(900000)] // 15 minutes timeout (900,000 ms) for video processing
    public async Task<IActionResult> ConvertToAudio(IFormFile videoFile, [FromForm] string format = "mp3")
    {
        _logger.LogInformation("ConvertToAudio endpoint called");
        
        try
        {
            if (videoFile == null || videoFile.Length == 0)
            {
                _logger.LogWarning("No video file provided in request");
                return BadRequest("No video file provided.");
            }

            _logger.LogInformation($"Received video file: {videoFile.FileName}, Size: {videoFile.Length} bytes ({videoFile.Length / (1024 * 1024)} MB)");

            // Check file size limit (500 MB = 524,288,000 bytes)
            if (videoFile.Length > 500 * 1024 * 1024)
            {
                _logger.LogWarning($"File too large: {videoFile.Length} bytes");
                return BadRequest("File size exceeds 500 MB limit.");
            }

            if (!IsValidVideoFile(videoFile.FileName))
            {
                _logger.LogWarning($"Invalid video file format: {videoFile.FileName}");
                return BadRequest("Invalid video file format.");
            }

            if (!IsValidAudioFormat(format))
            {
                _logger.LogWarning($"Invalid audio format requested: {format}");
                return BadRequest("Invalid audio format. Supported: mp3, wav, aac, flac, ogg");
            }

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
    [RequestTimeout(900000)] // 15 minutes timeout (900,000 ms) for video processing
    public async Task<IActionResult> ConvertToText(IFormFile videoFile)
    {
        _logger.LogInformation("ConvertToText endpoint called");
        
        try
        {
            if (videoFile == null || videoFile.Length == 0)
            {
                _logger.LogWarning("No video file provided in request");
                return BadRequest("No video file provided.");
            }

            _logger.LogInformation($"Received video file: {videoFile.FileName}, Size: {videoFile.Length} bytes ({videoFile.Length / (1024 * 1024)} MB)");

            // Check file size limit (500 MB = 524,288,000 bytes)
            if (videoFile.Length > 500 * 1024 * 1024)
            {
                _logger.LogWarning($"File too large: {videoFile.Length} bytes");
                return BadRequest("File size exceeds 500 MB limit.");
            }

            if (!IsValidVideoFile(videoFile.FileName))
            {
                _logger.LogWarning($"Invalid video file format: {videoFile.FileName}");
                return BadRequest("Invalid video file format.");
            }

            // Create temporary files
            var inputPath = Path.GetTempFileName();
            var tempAudioPath = Path.GetTempFileName() + ".wav";

            _logger.LogInformation($"Created temp files - Input: {inputPath}, Audio: {tempAudioPath}");

            try
            {
                // Save uploaded file
                _logger.LogInformation("Saving uploaded file to temporary location");
                using (var stream = new FileStream(inputPath, FileMode.Create))
                {
                    await videoFile.CopyToAsync(stream);
                }
                _logger.LogInformation($"File saved successfully, size: {new FileInfo(inputPath).Length} bytes");

                _logger.LogInformation($"Starting conversion of {videoFile.FileName} to text");

                // Extract audio
                _logger.LogInformation("Extracting audio from video file");
                var audioExtractionSuccess = await ExtractAudio(inputPath, tempAudioPath);
                if (!audioExtractionSuccess)
                {
                    _logger.LogError("Audio extraction failed");
                    return StatusCode(500, "Audio extraction failed");
                }
                _logger.LogInformation("Audio extraction completed successfully");

                // Convert to text using Whisper
                _logger.LogInformation("Starting speech-to-text conversion using Whisper");
                var transcription = await ConvertAudioToText(tempAudioPath);
                if (transcription == null)
                {
                    _logger.LogError("Speech-to-text conversion returned null");
                    return StatusCode(500, "Speech-to-text conversion failed");
                }
                
                _logger.LogInformation($"Transcription completed, result length: {transcription?.Length ?? 0} characters");

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
            _logger.LogInformation($"Starting FFmpeg audio extraction from {inputPath} to {outputPath}");
            
            // Check if input file exists
            if (!System.IO.File.Exists(inputPath))
            {
                _logger.LogError($"Input file does not exist: {inputPath}");
                return false;
            }

            var inputFileInfo = new FileInfo(inputPath);
            _logger.LogInformation($"Input file size: {inputFileInfo.Length} bytes");

            // Try with more robust FFmpeg arguments
            var arguments = FFMpegArguments.FromFileInput(inputPath)
                .OutputToFile(outputPath, true, options => options
                    .WithAudioCodec("pcm_s16le")
                    .WithAudioSamplingRate(16000)
                    .WithCustomArgument("-ac 1")
                    .WithCustomArgument("-vn")
                    .WithCustomArgument("-threads 0")
                    .WithCustomArgument("-loglevel verbose"));

            _logger.LogInformation("Starting FFmpeg process...");
            var result = await arguments.ProcessAsynchronously();
            
            if (result)
            {
                _logger.LogInformation("FFmpeg audio extraction completed successfully");
                var fileInfo = new FileInfo(outputPath);
                if (fileInfo.Exists)
                {
                    _logger.LogInformation($"Output audio file created, size: {fileInfo.Length} bytes");
                    if (fileInfo.Length == 0)
                    {
                        _logger.LogError("Output audio file is empty");
                        return false;
                    }
                }
                else
                {
                    _logger.LogError("Output audio file was not created despite success status");
                    return false;
                }
            }
            else
            {
                _logger.LogError("FFmpeg audio extraction failed - process returned false");
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio extraction failed with exception");
            
            // Try alternative approach with direct Process execution
            try
            {
                _logger.LogInformation("Attempting alternative FFmpeg execution...");
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = $"-i \"{inputPath}\" -acodec pcm_s16le -ar 16000 -ac 1 -vn \"{outputPath}\" -y -loglevel error",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                var output = new System.Text.StringBuilder();
                var error = new System.Text.StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.AppendLine(e.Data);
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        error.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                _logger.LogInformation($"FFmpeg exit code: {process.ExitCode}");
                if (output.Length > 0)
                    _logger.LogInformation($"FFmpeg output: {output}");
                if (error.Length > 0)
                    _logger.LogError($"FFmpeg error: {error}");

                if (process.ExitCode == 0 && System.IO.File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    if (fileInfo.Length > 0)
                    {
                        _logger.LogInformation($"Alternative method succeeded, file size: {fileInfo.Length} bytes");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception altEx)
            {
                _logger.LogError(altEx, "Alternative FFmpeg execution also failed");
                return false;
            }
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

    private async Task<bool> CheckFFmpegAvailability()
    {
        try
        {
            _logger.LogInformation("Checking FFmpeg availability...");
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    output.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    error.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;
            _logger.LogInformation($"FFmpeg check - Exit code: {process.ExitCode}, Success: {success}");
            
            if (!success)
            {
                _logger.LogError($"FFmpeg error output: {error}");
            }
            else
            {
                _logger.LogInformation($"FFmpeg version info: {output.ToString().Split('\n')[0]}");
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while checking FFmpeg availability");
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