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

# Find ffmpeg bin directory in the extracted files
BIN_DIR=$(find . -name "bin" -type d | head -1)

if [ -z "$BIN_DIR" ]; then
    echo "Error: Could not find bin directory in the extracted files" >&2
    exit 1
fi

# Copy entire bin directory to project root
echo "Copying FFmpeg bin directory to project root..."
CURRENT_DIR=$(pwd)
cd "$(dirname "$0")"
if [ -d "ffmpeg-bin" ]; then
    rm -rf "ffmpeg-bin"
fi
cp -r "$TEMP_DIR/$BIN_DIR" "ffmpeg-bin"

echo "Successfully downloaded FFmpeg with dependencies to ffmpeg-bin folder!"
echo "Done!"
