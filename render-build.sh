#!/bin/bash
# render-build.sh - Render.com build script

# Install FFmpeg (if not in Docker)
# apt-get update && apt-get install -y ffmpeg

# Build the .NET application
dotnet publish -c Release -o ./publish

# The app will run from ./publish directory