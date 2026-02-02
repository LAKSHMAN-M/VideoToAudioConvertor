@echo off
echo Starting custom startup script...

REM Create a directory for FFmpeg if it doesn't exist
if not exist "D:\home\site\tools\ffmpeg" mkdir D:\home\site\tools\ffmpeg

REM Download FFmpeg if not already present
if not exist "D:\home\site\tools\ffmpeg\ffmpeg.exe" (
    echo Downloading FFmpeg...
    curl -L -o "D:\home\site\tools\ffmpeg.zip" "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
    
    REM Extract FFmpeg
    echo Extracting FFmpeg...
    powershell -command "Expand-Archive -Path 'D:\home\site\tools\ffmpeg.zip' -DestinationPath 'D:\home\site\tools\temp' -Force"
    
    REM Move the executable to the correct location
    move "D:\home\site\tools\temp\ffmpeg-master-latest-win64-gpl\bin\*" "D:\home\site\tools\ffmpeg\"
    
    REM Cleanup
    rmdir /s /q "D:\home\site\tools\temp"
    del "D:\home\site\tools\ffmpeg.zip"
    
    echo FFmpeg installation completed
)

REM Add FFmpeg to PATH
set PATH=%PATH%;D:\home\site\tools\ffmpeg

echo FFmpeg path added to environment
echo Startup script completed

REM Start the application
dotnet VideoToAudioAPI.dll