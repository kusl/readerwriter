#!/bin/bash

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1"
}

print_error() {
    echo -e "${RED}[$(date '+%Y-%m-%d %H:%M:%S')]${NC} $1"
}

# Exit on any error
set -e

# Get the directory where the script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

# Set the solution root directory (assuming script is in the solution root)
SOLUTION_ROOT="$SCRIPT_DIR"
CONSOLE_APP_DIR="$SOLUTION_ROOT/ReaderWriter.ConsoleApp"
PUBLISH_DIR="$CONSOLE_APP_DIR/publish-linux"

print_status "Starting Reader-Writer application build process..."

# Navigate to solution root
cd "$SOLUTION_ROOT"

# Clean previous builds
print_status "Cleaning previous builds..."
dotnet clean --configuration Release
print_success "Clean completed"

# Build the solution
print_status "Building solution..."
dotnet build --configuration Release
print_success "Build completed"

# Run tests
print_status "Running tests..."
dotnet test --configuration Release --no-build --verbosity normal
print_success "All tests passed"

# Navigate to console app directory
cd "$CONSOLE_APP_DIR"

# Publish for Linux x64
print_status "Publishing for Linux x64..."
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$PUBLISH_DIR"
print_success "Publishing completed"

# Ensure appsettings.json is in the publish directory
if [ ! -f "$PUBLISH_DIR/appsettings.json" ]; then
    print_warning "Copying appsettings.json to publish directory..."
    cp appsettings.json "$PUBLISH_DIR/"
fi

# Make the executable file executable (just in case)
chmod +x "$PUBLISH_DIR/ReaderWriter.ConsoleApp"

# Create logs directory if it doesn't exist
mkdir -p "$PUBLISH_DIR/logs"

print_success "Build and publish process completed successfully!"
echo ""

# Function to run the application
run_app() {
    local description=$1
    shift
    print_status "$description"
    "$PUBLISH_DIR/ReaderWriter.ConsoleApp" "$@"
    echo ""
}

# Run with default settings
run_app "Running with default settings from appsettings.json..."

# Run with custom command-line settings
run_app "Running with custom settings (15 readers, 4 writers, 20 seconds)..." \
    --readers 15 --writers 4 --duration 20

# Run with environment variables
print_status "Running with environment variable overrides (25 readers, 10 seconds)..."
export SimulationSettings__NumberOfReaders=25
export SimulationSettings__SimulationDurationSeconds=10
"$PUBLISH_DIR/ReaderWriter.ConsoleApp"
unset SimulationSettings__NumberOfReaders
unset SimulationSettings__SimulationDurationSeconds
echo ""

print_success "All runs completed successfully!"

# Optional: Display the published files
print_status "Published files:"
ls -la "$PUBLISH_DIR"

# Optional: Show file size of the executable
print_status "Executable size:"
du -h "$PUBLISH_DIR/ReaderWriter.ConsoleApp"