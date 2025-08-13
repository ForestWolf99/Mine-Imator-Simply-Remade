#!/bin/bash

# Script to download FFmpeg from BtbN/FFmpeg-Builds and extract ffmpeg to project root

set -e

echo "Downloading FFmpeg..."

# Get latest release info
RELEASE_URL="https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/latest"
RELEASE_INFO=$(curl -s "$RELEASE_URL")

# Find Linux x64 gpl shared build
DOWNLOAD_URL=$(echo "$RELEASE_INFO" | grep -o '"browser_download_url": "[^"]*ffmpeg-master-latest-linux64-gpl-shared\.tar\.xz"' | cut -d'"' -f4)

if [ -z "$DOWNLOAD_URL" ]; then
    echo "Error: Could not find Linux x64 FFmpeg build in latest release" >&2
    exit 1
fi

TEMP_DIR=$(mktemp -d)
TAR_PATH="$TEMP_DIR/ffmpeg.tar.xz"

cleanup() {
    echo "Cleaning up temporary files..."
    rm -rf "$TEMP_DIR"
}

trap cleanup EXIT

# Download the tar.xz file
echo "Downloading from: $DOWNLOAD_URL"
curl -L -o "$TAR_PATH" "$DOWNLOAD_URL"

# Extract to temporary directory
echo "Extracting archive..."
cd "$TEMP_DIR"
tar -xJf "$TAR_PATH"

# Find ffmpeg binary in the extracted files
FFMPEG_BIN=$(find . -name "ffmpeg" -type f | head -1)

if [ -z "$FFMPEG_BIN" ]; then
    echo "Error: Could not find ffmpeg binary in the extracted files" >&2
    exit 1
fi

# Copy ffmpeg to project root
echo "Copying ffmpeg to project root..."
cp "$FFMPEG_BIN" "$(pwd)/ffmpeg"
chmod +x "$(pwd)/ffmpeg"

echo "Successfully downloaded ffmpeg to project root!"
echo "Done!"
