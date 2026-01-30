# VideoToAudio - Web API & Live Demo

A fast and efficient video to audio converter with AI-powered speech recognition, built with ASP.NET Core 8 and FFMpegCore.

## 🌐 Live Application
- **Demo**: [Your Live App URL](https://your-app.render.com) *(Update after deployment)*
- **GitHub**: [Repository Link](https://github.com/yourusername/VideoToAudio)

## ✨ Features

- **🎵 Video to Audio Conversion**: Convert videos to MP3, WAV, AAC, FLAC, OGG
- **📝 AI Speech Recognition**: Video to text transcription using Whisper.NET
- **⚡ Fast Processing**: Optimized with multi-threading and fast presets
- **📱 Web Interface**: Mobile-friendly HTML/JS frontend
- **🔄 Real-time Progress**: Live progress tracking during conversion
- **📋 Copy & Download**: Copy transcriptions and download audio files
- **🕐 Timestamp Toggle**: View transcripts with/without timestamps
- **☁️ Cloud Ready**: Deployable to Render, Railway, or Azure

## 🛠 Tech Stack
- **Backend**: ASP.NET Core 8.0 Web API
- **Frontend**: HTML5, CSS3, Vanilla JavaScript
- **AI**: Whisper.NET for speech recognition
- **Media Processing**: FFMpegCore + FFmpeg
- **File Handling**: Large file uploads (500MB+)
- **Deployment**: Docker, Render.com, Railway

## Prerequisites

1. **.NET 8 Runtime**: Download from [https://dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)
2. **FFmpeg**: Download from [https://ffmpeg.org/download.html](https://ffmpeg.org/download.html)
   - Make sure FFmpeg is added to your system PATH

## Installation

1. Clone or download the project
2. Navigate to the project directory
3. Restore dependencies:
   ```bash
   dotnet restore
   ```
4. Build the project:
   ```bash
   dotnet build
   ```

## Usage

### Interactive Mode
Run without arguments to enter interactive mode:
```bash
dotnet run
```

### Command Line Mode
Convert a single file directly:
```bash
dotnet run "input_video.mp4" "output_audio.mp3"
```

### Supported Input Formats
- MP4, AVI, MKV, MOV, WMV, FLV, WebM

### Supported Output Formats
- **MP3** (default) - High quality, widely compatible
- **WAV** - Lossless, uncompressed
- **AAC** - Good compression, high quality
- **FLAC** - Lossless, compressed
- **OGG** - Open source, good compression

## Performance Features

- **Multi-threading**: Uses all available CPU cores
- **Fast Presets**: Optimized for speed over file size
- **Memory Efficient**: Streams processing without loading entire files
- **Batch Processing**: Process multiple files efficiently

## Example Usage

### Single File Conversion
```bash
# Convert to MP3 (default quality: 320kbps)
dotnet run "movie.mp4" "audio.mp3"

# Convert to WAV (lossless)
dotnet run "video.avi" "audio.wav"
```

### Batch Conversion
1. Run the application: `dotnet run`
2. Choose option "2" for batch conversion
3. Enter the folder path containing video files
4. Select output format
5. Wait for processing to complete

## Performance Tips

1. **SSD Storage**: Store input/output files on SSD for faster I/O
2. **CPU Cores**: More CPU cores = faster processing
3. **RAM**: Sufficient RAM prevents disk swapping during processing
4. **Format Choice**: MP3 conversion is typically fastest

## Troubleshooting

### "FFmpeg is not found"
- Download FFmpeg from the official website
- Extract to a folder (e.g., `C:\ffmpeg`)
- Add the `bin` folder to your system PATH
- Restart command prompt/terminal

### "File not found" or "Access denied"
- Check file paths are correct
- Ensure you have read permissions for input files
- Ensure you have write permissions for output directory
- Close any applications using the video files

### Slow performance
- Check if your antivirus is scanning the files
- Ensure sufficient disk space
- Use SSD storage if possible
- Close unnecessary applications to free up CPU/RAM

## License

This project uses FFMpegCore, which is licensed under MIT License.