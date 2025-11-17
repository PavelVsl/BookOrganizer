#!/bin/bash
set -e

# BookOrganizer - Publish and Install Script
# Builds, packs, and installs the latest version as a .NET global tool

echo "📦 Building and publishing BookOrganizer..."
echo ""

# Navigate to project directory
cd "$(dirname "$0")/BookOrganizer"

# Get current version using nbgv
if command -v nbgv &> /dev/null; then
    VERSION=$(nbgv get-version -v NuGetPackageVersion)
    echo "🔢 Version: $VERSION"
    echo ""
fi

# Clean previous builds
echo "🧹 Cleaning previous builds..."
rm -rf bin obj
cd ..
rm -rf nupkg
mkdir -p nupkg
echo ""

# Build in Release mode
echo "🔨 Building in Release mode..."
dotnet build BookOrganizer/BookOrganizer.csproj -c Release
echo ""

# Pack the tool
echo "📦 Creating NuGet package..."
dotnet pack BookOrganizer/BookOrganizer.csproj -c Release -o ./nupkg
echo ""

# Uninstall existing version (if any)
echo "🗑️  Uninstalling existing version..."
dotnet tool uninstall -g BookOrganizer 2>/dev/null || true
echo ""

# Install new version from local package
echo "⬇️  Installing new version..."
dotnet tool install -g BookOrganizer --add-source ./nupkg --prerelease
echo ""

# Verify installation
echo "✅ Verifying installation..."
if command -v bookorganizer &> /dev/null; then
    echo "✅ BookOrganizer installed successfully!"
    echo ""

    # Show version if nbgv is available
    if [ -n "$VERSION" ]; then
        echo "📌 Installed version: $VERSION"
    fi

    echo ""
    echo "🚀 You can now use: bookorganizer --help"
else
    echo "❌ Installation verification failed!"
    exit 1
fi
