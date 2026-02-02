# Use the official .NET SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Install FFmpeg
RUN apt-get update && apt-get install -y ffmpeg

# Copy project files
COPY *.csproj ./
RUN dotnet restore

# Copy all source code
COPY . ./
RUN dotnet publish -c Release -o /app/publish

# Use the runtime image for the final container
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Install FFmpeg in runtime image
RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*

# Copy the published app
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5000

ENTRYPOINT ["dotnet", "VideoToAudio.dll"]