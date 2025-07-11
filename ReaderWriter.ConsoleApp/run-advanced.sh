#!/bin/bash

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
CYAN='\033[0;36m'
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

print_info() {
    echo -e "${CYAN}$1${NC}"
}

# Script configuration
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
SOLUTION_ROOT="$SCRIPT_DIR"
CONSOLE_APP_DIR="$SOLUTION_ROOT/ReaderWriter.ConsoleApp"
PUBLISH_DIR="$CONSOLE_APP_DIR/publish-linux"

# Default values
BUILD_CONFIG="Release"
RUN_TESTS=true
CLEAN_BUILD=true
PUBLISH_ONLY=false
RUN_ONLY=false

# Function to display usage
usage() {
    print_info "Usage: $0 [OPTIONS]"
    echo ""
    echo "Options:"
    echo "  -h, --help           Show this help message"
    echo "  -d, --debug          Build in Debug configuration"
    echo "  -s, --skip-tests     Skip running tests"
    echo "  -n, --no-clean       Don't clean before building"
    echo "  -p, --publish-only   Only publish (skip build and test)"
    echo "  -r, --run-only       Only run the published application"
    echo "  -c, --continuous     Run continuous stress test"
    echo ""
    echo "Examples:"
    echo "  $0                   # Full build, test, publish, and run"
    echo "  $0 --debug           # Build in debug mode"
    echo "  $0 --skip-tests      # Build and publish without tests"
    echo "  $0 --run-only        # Just run the existing published app"
    echo "  $0 --continuous      # Run stress test continuously"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -h|--help)
            usage
            exit 0
            ;;
        -d|--debug)
            BUILD_CONFIG="Debug"
            shift
            ;;
        -s|--skip-tests)
            RUN_TESTS=false
            shift
            ;;
        -n|--no-clean)
            CLEAN_BUILD=false
            shift
            ;;
        -p|--publish-only)
            PUBLISH_ONLY=true
            shift
            ;;
        -r|--run-only)
            RUN_ONLY=true
            shift
            ;;
        -c|--continuous)
            CONTINUOUS=true
            shift
            ;;
        *)
            print_error "Unknown option: $1"
            usage
            exit 1
            ;;
    esac
done

# Function to check if dotnet is installed
check_dotnet() {
    if ! command -v dotnet &> /dev/null; then
        print_error "dotnet CLI is not installed or not in PATH"
        print_info "Please install .NET SDK from: https://dotnet.microsoft.com/download"
        exit 1
    fi
    
    print_status "Detected .NET SDK version:"
    dotnet --version
}

# Function to build the solution
build_solution() {
    cd "$SOLUTION_ROOT"
    
    if [ "$CLEAN_BUILD" = true ]; then
        print_status "Cleaning previous builds..."
        dotnet clean --configuration $BUILD_CONFIG
        print_success "Clean completed"
    fi
    
    print_status "Building solution in $BUILD_CONFIG mode..."
    dotnet build --configuration $BUILD_CONFIG
    print_success "Build completed"
}

# Function to run tests
run_tests() {
    if [ "$RUN_TESTS" = true ]; then
        print_status "Running tests..."
        cd "$SOLUTION_ROOT"
        dotnet test --configuration $BUILD_CONFIG --no-build --verbosity normal
        print_success "All tests passed"
    else
        print_warning "Skipping tests as requested"
    fi
}

# Function to publish the application
publish_app() {
    cd "$CONSOLE_APP_DIR"
    
    print_status "Publishing for Linux x64..."
    dotnet publish -c $BUILD_CONFIG -r linux-x64 --self-contained true \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "$PUBLISH_DIR"
    
    print_success "Publishing completed"
    
    # Ensure appsettings.json is in the publish directory
    if [ ! -f "$PUBLISH_DIR/appsettings.json" ]; then
        print_warning "Copying appsettings.json to publish directory..."
        cp appsettings.json "$PUBLISH_DIR/"
    fi
    
    # Make executable
    chmod +x "$PUBLISH_DIR/ReaderWriter.ConsoleApp"
    
    # Create logs directory
    mkdir -p "$PUBLISH_DIR/logs"
    
    # Show published files info
    print_status "Published executable size:"
    du -h "$PUBLISH_DIR/ReaderWriter.ConsoleApp"
}

# Function to run the application
run_app() {
    local description=$1
    shift
    print_status "$description"
    "$PUBLISH_DIR/ReaderWriter.ConsoleApp" "$@"
    echo ""
}

# Function to run continuous stress test
run_continuous_stress_test() {
    print_info "Starting continuous stress test (Press Ctrl+C to stop)..."
    echo ""
    
    local iteration=1
    while true; do
        print_status "Stress test iteration $iteration"
        
        # Random configuration
        local readers=$((RANDOM % 50 + 10))
        local writers=$((RANDOM % 10 + 1))
        local duration=$((RANDOM % 30 + 10))
        
        print_info "Configuration: $readers readers, $writers writers, $duration seconds"
        
        "$PUBLISH_DIR/ReaderWriter.ConsoleApp" \
            --readers $readers \
            --writers $writers \
            --duration $duration
        
        iteration=$((iteration + 1))
        
        print_info "Waiting 5 seconds before next iteration..."
        sleep 5
        echo ""
    done
}

# Main execution
main() {
    print_info "=== Reader-Writer Application Build and Run Script ==="
    echo ""
    
    # Check prerequisites
    check_dotnet
    echo ""
    
    # Exit on any error from here
    set -e
    
    if [ "$RUN_ONLY" = true ]; then
        # Just run the existing published application
        if [ ! -f "$PUBLISH_DIR/ReaderWriter.ConsoleApp" ]; then
            print_error "Published application not found. Please build first."
            exit 1
        fi
    else
        if [ "$PUBLISH_ONLY" != true ]; then
            # Full build process
            build_solution
            run_tests
        fi
        
        # Publish the application
        publish_app
    fi
    
    echo ""
    print_success "Ready to run the application!"
    echo ""
    
    if [ "$CONTINUOUS" = true ]; then
        run_continuous_stress_test
    else
        # Run with different configurations
        run_app "Running with default settings from appsettings.json..."
        
        run_app "Running with custom settings (15 readers, 4 writers, 20 seconds)..." \
            --readers 15 --writers 4 --duration 20
        
        # Run with environment variables
        print_status "Running with environment variable overrides (25 readers, 10 seconds)..."
        export SimulationSettings__NumberOfReaders=25
        export SimulationSettings__SimulationDurationSeconds=10
        "$PUBLISH_DIR/ReaderWriter.ConsoleApp"
        unset SimulationSettings__NumberOfReaders
        unset SimulationSettings__SimulationDurationSeconds
        
        print_success "All runs completed successfully!"
    fi
}

# Run main function
main