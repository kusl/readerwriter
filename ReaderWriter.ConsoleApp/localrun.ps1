# Clean, build, and test
Set-Location "C:\code\ReaderWriter\"
dotnet clean
dotnet build --configuration Release
dotnet test --configuration Release

# Publish the console app
Set-Location "C:\code\ReaderWriter\ReaderWriter.ConsoleApp\"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish

# Ensure appsettings.json is in the publish directory
if (-not (Test-Path "./publish/appsettings.json")) {
    Copy-Item "appsettings.json" "./publish/"
}

# Run the published executable
Write-Host "Running with default settings from appsettings.json..." -ForegroundColor Green
.\publish\ReaderWriter.ConsoleApp.exe

# Or run with custom settings via command line
Write-Host "`nRunning with custom settings..." -ForegroundColor Green
.\publish\ReaderWriter.ConsoleApp.exe --readers 15 --writers 4 --duration 20

# Or run with environment variables
Write-Host "`nRunning with environment variable overrides..." -ForegroundColor Green
$env:SimulationSettings__NumberOfReaders = 25
$env:SimulationSettings__SimulationDurationSeconds = 10
.\publish\ReaderWriter.ConsoleApp.exe