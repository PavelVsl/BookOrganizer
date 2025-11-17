# BookOrganizer - Publish and Install Script (PowerShell)
# Builds, packs, and installs the latest version as a .NET global tool

$ErrorActionPreference = "Stop"

Write-Host "📦 Building and publishing BookOrganizer..." -ForegroundColor Cyan
Write-Host ""

# Navigate to script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

# Get current version using nbgv
if (Get-Command nbgv -ErrorAction SilentlyContinue) {
    $version = & nbgv get-version -v NuGetPackageVersion
    Write-Host "🔢 Version: $version" -ForegroundColor Green
    Write-Host ""
}

# Clean previous builds
Write-Host "🧹 Cleaning previous builds..." -ForegroundColor Yellow
Remove-Item -Path "BookOrganizer/bin" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "BookOrganizer/obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "nupkg" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -Path "nupkg" -ItemType Directory -Force | Out-Null
Write-Host ""

# Build in Release mode
Write-Host "🔨 Building in Release mode..." -ForegroundColor Yellow
dotnet build BookOrganizer/BookOrganizer.csproj -c Release
Write-Host ""

# Pack the tool
Write-Host "📦 Creating NuGet package..." -ForegroundColor Yellow
dotnet pack BookOrganizer/BookOrganizer.csproj -c Release -o ./nupkg
Write-Host ""

# Uninstall existing version (if any)
Write-Host "🗑️  Uninstalling existing version..." -ForegroundColor Yellow
try {
    dotnet tool uninstall -g BookOrganizer 2>$null
} catch {
    # Ignore errors if tool not installed
}
Write-Host ""

# Install new version from local package
Write-Host "⬇️  Installing new version..." -ForegroundColor Yellow
dotnet tool install -g BookOrganizer --add-source ./nupkg --prerelease
Write-Host ""

# Verify installation
Write-Host "✅ Verifying installation..." -ForegroundColor Yellow
if (Get-Command bookorganizer -ErrorAction SilentlyContinue) {
    Write-Host "✅ BookOrganizer installed successfully!" -ForegroundColor Green
    Write-Host ""

    # Show version if available
    if ($version) {
        Write-Host "📌 Installed version: $version" -ForegroundColor Cyan
    }

    Write-Host ""
    Write-Host "🚀 You can now use: bookorganizer --help" -ForegroundColor Cyan
} else {
    Write-Host "❌ Installation verification failed!" -ForegroundColor Red
    exit 1
}
