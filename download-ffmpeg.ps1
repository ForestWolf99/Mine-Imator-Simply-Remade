#!/usr/bin/env pwsh

# Script to download FFmpeg from BtbN/FFmpeg-Builds and extract ffmpeg.exe to project root

$ErrorActionPreference = "Stop"

Write-Host "Downloading FFmpeg..." -ForegroundColor Green

# Get latest release info
$releaseUrl = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest"
$release = Invoke-RestMethod -Uri $releaseUrl

# Find Windows x64 gpl shared build
$asset = $release.assets | Where-Object { 
    $_.name -match "ffmpeg-master-latest-win64-gpl-shared\.zip$" 
} | Select-Object -First 1

if (-not $asset) {
    Write-Error "Could not find Windows x64 FFmpeg build in latest release"
    exit 1
}

$downloadUrl = $asset.browser_download_url
$zipPath = "$env:TEMP\ffmpeg.zip"
$extractPath = "$env:TEMP\ffmpeg-extract"

try {
    # Download the zip file
    Write-Host "Downloading from: $downloadUrl" -ForegroundColor Yellow
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath
    
    # Extract to temporary directory
    Write-Host "Extracting archive..." -ForegroundColor Yellow
    if (Test-Path $extractPath) {
        Remove-Item $extractPath -Recurse -Force
    }
    Expand-Archive -Path $zipPath -DestinationPath $extractPath
    
    # Find ffmpeg bin directory in the extracted files
    $binDir = Get-ChildItem -Path $extractPath -Name "bin" -Recurse -Directory | Select-Object -First 1
    
    if (-not $binDir) {
        Write-Error "Could not find bin directory in the extracted files"
        exit 1
    }
    
    $ffmpegBinPath = Join-Path $extractPath $binDir
    $targetBinPath = Join-Path (Get-Location) "ffmpeg-bin"
    
    # Copy entire bin directory to project root
    Write-Host "Copying FFmpeg bin directory to project root..." -ForegroundColor Yellow
    if (Test-Path $targetBinPath) {
        Remove-Item $targetBinPath -Recurse -Force
    }
    Copy-Item $ffmpegBinPath $targetBinPath -Recurse -Force
    
    Write-Host "Successfully downloaded FFmpeg with dependencies to ffmpeg-bin folder!" -ForegroundColor Green
    
} finally {
    # Cleanup temporary files
    Write-Host "Cleaning up temporary files..." -ForegroundColor Yellow
    
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    
    if (Test-Path $extractPath) {
        Remove-Item $extractPath -Recurse -Force
    }
}

Write-Host "Done!" -ForegroundColor Green
