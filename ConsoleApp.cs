using FFMpegCore;
using FFMpegCore.Enums;
using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

namespace VideoToAudio;

class ConsoleApp
{
    static async Task MainConsole(string[] args)
    {
        Console.WriteLine("=== Video to Audio Converter ===");
        Console.WriteLine();

        // Check if FFmpeg is available
        if (!await CheckFFmpegAvailability())
        {
            Console.WriteLine("FFmpeg is not found. Please install FFmpeg and ensure it's in your PATH.");
            Console.WriteLine("Download from: https://ffmpeg.org/download.html");
            return;
        }

        if (args.Length >= 2)
        {
            // Command line mode
            if (args[0] == "--text" && args.Length >= 3)
            {
                await ConvertVideoToText(args[1], args[2]);
            }
            else
            {
                await ConvertVideoToAudio(args[0], args[1]);
            }
        }
        else
        {
            // Interactive mode
            await InteractiveMode();
        }
    }

    static async Task<bool> CheckFFmpegAvailability()
    {
        try
        {
            // Simple check by running ffmpeg -version
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

    static async Task InteractiveMode()
    {
        while (true)
        {
            Console.WriteLine("\nOptions:");
            Console.WriteLine("1. Convert single file (Video to Audio)");
            Console.WriteLine("2. Batch convert folder (Video to Audio)");
            Console.WriteLine("3. Convert video to text (Speech-to-Text)");
            Console.WriteLine("4. Exit");
            Console.Write("Choose an option (1-4): ");

            var choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    await SingleFileConversion();
                    break;
                case "2":
                    await BatchConversion();
                    break;
                case "3":
                    await VideoToTextConversion();
                    break;
                case "4":
                    Console.WriteLine("Goodbye!");
                    return;
                default:
                    Console.WriteLine("Invalid option. Please try again.");
                    break;
            }
        }
    }

    static async Task SingleFileConversion()
    {
        Console.Write("\nEnter video file path: ");
        var inputPath = Console.ReadLine()?.Trim('"');

        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.WriteLine("Invalid file path or file does not exist.");
            return;
        }

        Console.Write("Enter output audio file path (or press Enter for auto-generated): ");
        var outputPath = Console.ReadLine()?.Trim('"');

        if (string.IsNullOrEmpty(outputPath))
        {
            outputPath = GenerateOutputPath(inputPath, "mp3");
        }

        // Audio format selection
        var format = SelectAudioFormat();
        if (!string.IsNullOrEmpty(format))
        {
            outputPath = ChangeExtension(outputPath, format);
        }

        await ConvertVideoToAudio(inputPath, outputPath);
    }

    static async Task VideoToTextConversion()
    {
        Console.Write("\nEnter video file path: ");
        var inputPath = Console.ReadLine()?.Trim('"');

        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Console.WriteLine("Invalid file path or file does not exist.");
            return;
        }

        Console.Write("Enter output text file path (or press Enter for auto-generated): ");
        var outputPath = Console.ReadLine()?.Trim('"');

        if (string.IsNullOrEmpty(outputPath))
        {
            outputPath = GenerateOutputPath(inputPath, "txt");
        }

        await ConvertVideoToText(inputPath, outputPath);
    }

    static async Task ConvertVideoToText(string inputPath, string outputPath)
    {
        try
        {
            Console.WriteLine($"\nConverting video to text: {Path.GetFileName(inputPath)}");
            Console.WriteLine($"Output: {Path.GetFileName(outputPath)}");

            var stopwatch = Stopwatch.StartNew();

            // Step 1: Extract audio from video
            Console.WriteLine("Step 1/2: Extracting audio...");
            var tempAudioPath = Path.GetTempFileName() + ".wav";
            
            try
            {
                var audioExtraction = FFMpegArguments.FromFileInput(inputPath)
                    .OutputToFile(tempAudioPath, true, options => options
                        .WithAudioCodec("pcm_s16le")
                        .WithAudioSamplingRate(16000) // Whisper prefers 16kHz
                        .WithCustomArgument("-ac 1") // Mono channel
                        .WithCustomArgument("-vn") // Remove video
                        .WithCustomArgument("-threads 0"));

                var extractionSuccess = await audioExtraction.ProcessAsynchronously();
                if (!extractionSuccess)
                {
                    Console.WriteLine("✗ Failed to extract audio from video!");
                    return;
                }

                Console.WriteLine("✓ Audio extracted successfully!");

                // Step 2: Convert audio to text using Whisper
                Console.WriteLine("Step 2/2: Converting speech to text...");
                
                // Download Whisper model if not exists
                var modelPath = await DownloadWhisperModel();
                if (modelPath == null)
                {
                    Console.WriteLine("✗ Failed to download Whisper model!");
                    return;
                }

                using var whisperFactory = WhisperFactory.FromPath(modelPath);
                using var processor = whisperFactory.CreateBuilder()
                    .WithLanguage("auto")
                    .WithPrintProgress()
                    .WithPrintTimestamps()
                    .Build();

                Console.WriteLine("Processing audio with Whisper...");
                
                using var fileStream = File.OpenRead(tempAudioPath);
                var transcriptionText = new System.Text.StringBuilder();
                
                await foreach (var result in processor.ProcessAsync(fileStream))
                {
                    Console.Write($"\rProgress: {result.Start:hh\\:mm\\:ss} - {result.End:hh\\:mm\\:ss}");
                    transcriptionText.AppendLine($"[{result.Start:hh\\:mm\\:ss} - {result.End:hh\\:mm\\:ss}] {result.Text}");
                }

                // Save transcription to file
                await File.WriteAllTextAsync(outputPath, transcriptionText.ToString());

                stopwatch.Stop();
                Console.WriteLine();
                Console.WriteLine($"✓ Video to text conversion completed successfully!");
                Console.WriteLine($"  Time taken: {stopwatch.Elapsed:hh\\:mm\\:ss}");
                Console.WriteLine($"  Output path: {outputPath}");
                Console.WriteLine($"  Transcription preview:");
                
                // Show first few lines of transcription
                var lines = transcriptionText.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < Math.Min(3, lines.Length); i++)
                {
                    Console.WriteLine($"    {lines[i]}");
                }
                if (lines.Length > 3)
                {
                    Console.WriteLine($"    ... and {lines.Length - 3} more lines");
                }
            }
            finally
            {
                // Clean up temporary audio file
                if (File.Exists(tempAudioPath))
                {
                    try { File.Delete(tempAudioPath); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error during video to text conversion: {ex.Message}");
        }
    }

    static async Task<string?> DownloadWhisperModel()
    {
        try
        {
            var modelDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".whisper-models");
            if (!Directory.Exists(modelDir))
            {
                Directory.CreateDirectory(modelDir);
            }

            var modelPath = Path.Combine(modelDir, "ggml-base.bin");
            
            if (File.Exists(modelPath))
            {
                Console.WriteLine("Using existing Whisper model...");
                return modelPath;
            }

            Console.WriteLine("Downloading Whisper base model (first time setup)...");
            Console.WriteLine("This may take a few minutes depending on your internet connection.");
            
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);
            
            var modelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";
            
            using var response = await client.GetAsync(modelUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;
            
            using var responseStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(modelPath);
            
            var buffer = new byte[8192];
            int bytesRead;
            
            while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;
                
                if (totalBytes > 0)
                {
                    var percentage = (double)downloadedBytes / totalBytes * 100;
                    Console.Write($"\rDownloading: {percentage:F1}% ({downloadedBytes / 1024 / 1024:F1} MB / {totalBytes / 1024 / 1024:F1} MB)");
                }
            }
            
            Console.WriteLine();
            Console.WriteLine("✓ Whisper model downloaded successfully!");
            return modelPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Failed to download Whisper model: {ex.Message}");
            return null;
        }
    }

    static async Task BatchConversion()
    {
        Console.Write("\nEnter folder path containing video files: ");
        var folderPath = Console.ReadLine()?.Trim('"');

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            Console.WriteLine("Invalid folder path or folder does not exist.");
            return;
        }

        var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };
        var videoFiles = Directory.GetFiles(folderPath)
            .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLower()))
            .ToArray();

        if (videoFiles.Length == 0)
        {
            Console.WriteLine("No video files found in the specified folder.");
            return;
        }

        Console.WriteLine($"\nFound {videoFiles.Length} video files.");
        var format = SelectAudioFormat();
        var outputFormat = !string.IsNullOrEmpty(format) ? format : "mp3";

        var successCount = 0;
        var totalFiles = videoFiles.Length;

        foreach (var videoFile in videoFiles)
        {
            var outputPath = GenerateOutputPath(videoFile, outputFormat);
            Console.WriteLine($"\nProcessing: {Path.GetFileName(videoFile)}");
            
            var success = await ConvertVideoToAudio(videoFile, outputPath);
            if (success) successCount++;
        }

        Console.WriteLine($"\n=== Batch Conversion Complete ===");
        Console.WriteLine($"Successfully converted: {successCount}/{totalFiles} files");
    }

    static string SelectAudioFormat()
    {
        Console.WriteLine("\nSelect output format:");
        Console.WriteLine("1. MP3 (default)");
        Console.WriteLine("2. WAV");
        Console.WriteLine("3. AAC");
        Console.WriteLine("4. FLAC");
        Console.WriteLine("5. OGG");
        Console.Write("Choose format (1-5, or press Enter for MP3): ");

        var choice = Console.ReadLine();
        return choice switch
        {
            "2" => "wav",
            "3" => "aac",
            "4" => "flac",
            "5" => "ogg",
            _ => "mp3"
        };
    }

    static async Task<bool> ConvertVideoToAudio(string inputPath, string outputPath)
    {
        try
        {
            Console.WriteLine($"\nConverting: {Path.GetFileName(inputPath)}");
            Console.WriteLine($"Output: {Path.GetFileName(outputPath)}");

            var stopwatch = Stopwatch.StartNew();

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Get input file info
            var mediaInfo = await FFProbe.AnalyseAsync(inputPath);
            Console.WriteLine($"Duration: {mediaInfo.Duration:hh\\:mm\\:ss}");
            Console.WriteLine($"Video: {mediaInfo.PrimaryVideoStream?.Width}x{mediaInfo.PrimaryVideoStream?.Height}");
            Console.WriteLine($"Audio: {mediaInfo.PrimaryAudioStream?.SampleRateHz}Hz, {mediaInfo.PrimaryAudioStream?.Channels} channels");

            // Configure conversion for maximum speed
            var outputExtension = Path.GetExtension(outputPath).ToLower();
            var isLossless = outputExtension == ".wav" || outputExtension == ".flac";
            
            FFMpegArgumentProcessor arguments;
            
            if (isLossless)
            {
                arguments = FFMpegArguments.FromFileInput(inputPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithCustomArgument($"-c:a {GetAudioCodec(outputPath)}")
                        .WithCustomArgument("-vn")
                        .WithCustomArgument("-threads 0"));
            }
            else
            {
                arguments = FFMpegArguments.FromFileInput(inputPath)
                    .OutputToFile(outputPath, true, options => options
                        .WithCustomArgument($"-c:a {GetAudioCodec(outputPath)}")
                        .WithAudioBitrate(GetOptimalBitrate(outputPath))
                        .WithCustomArgument("-vn")
                        .WithCustomArgument("-threads 0"));
            }

            var success = await arguments.ProcessAsynchronously();

            stopwatch.Stop();
            Console.WriteLine();

            if (success)
            {
                var outputFileInfo = new FileInfo(outputPath);
                Console.WriteLine($"✓ Conversion completed successfully!");
                Console.WriteLine($"  Time taken: {stopwatch.Elapsed:hh\\:mm\\:ss}");
                Console.WriteLine($"  Output size: {outputFileInfo.Length / (1024 * 1024):F2} MB");
                Console.WriteLine($"  Output path: {outputPath}");
                return true;
            }
            else
            {
                Console.WriteLine($"✗ Conversion failed!");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error during conversion: {ex.Message}");
            return false;
        }
    }

    static string GetAudioCodec(string outputPath)
    {
        var extension = Path.GetExtension(outputPath).ToLower();
        return extension switch
        {
            ".wav" => "pcm_s16le",
            ".aac" => "aac",
            ".flac" => "flac",
            ".ogg" => "libvorbis",
            _ => "libmp3lame" // Default to MP3
        };
    }

    static int GetOptimalBitrate(string outputPath)
    {
        var extension = Path.GetExtension(outputPath).ToLower();
        return extension switch
        {
            ".wav" or ".flac" => 0, // Lossless formats don't need bitrate
            ".aac" => 128,
            ".ogg" => 192,
            _ => 320 // High quality MP3
        };
    }

    static string GenerateOutputPath(string inputPath, string format)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? "";
        var filename = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(directory, $"{filename}.{format}");
    }

    static string ChangeExtension(string filePath, string newExtension)
    {
        return Path.ChangeExtension(filePath, newExtension);
    }
}