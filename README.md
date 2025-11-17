# BookOrganizer

A powerful CLI tool for organizing audiobook libraries for Jellyfin with intelligent metadata extraction, duplicate detection, and Czech language support.

## Features

- 📚 **Intelligent Metadata Extraction** - Extracts metadata from MP3 ID3 tags and filenames
- 🇨🇿 **Czech Language Support** - Full support for Czech diacritics (ě, š, č, ř, ž, ý, á, í, é, ú, ů, ď, ť, ň)
- 🔍 **Duplicate Detection** - Finds potential duplicate audiobooks using normalized metadata
- 🎯 **Smart Organization** - Organizes into `Author/Series/Title` structure for Jellyfin
- ✅ **Library Verification** - Validates library integrity and metadata consistency
- 📝 **Metadata Editing** - Export metadata to JSON for manual editing
- 🎨 **Beautiful CLI** - Rich terminal UI with progress bars, tables, and colors

## Installation

### As .NET Global Tool

```bash
dotnet tool install -g BookOrganizer
```

### From Source

```bash
git clone https://github.com/yourusername/BookOrganizer.git
cd BookOrganizer/BookOrganizer
dotnet pack
dotnet tool install -g --add-source ./bin/Release BookOrganizer
```

## Quick Start

```bash
# Preview organization (see what will happen)
bookorganizer preview --source ~/audiobooks --destination ~/library

# Preview with metadata export and interactive organize
bookorganizer preview -s ~/audiobooks -d ~/library --export-metadata --interactive

# Organize audiobooks
bookorganizer organize --source ~/audiobooks --destination ~/library

# Verify library integrity
bookorganizer verify --library ~/library
```

## Commands

### `scan` - Quick Discovery
Scan a directory to find audiobook folders without metadata extraction.

```bash
bookorganizer scan --source ~/audiobooks
```

### `preview` - Full Analysis
Preview how audiobooks will be organized with detailed metadata and issue detection.

```bash
# Basic preview
bookorganizer preview -s ~/audiobooks -d ~/library

# With duplicate detection
bookorganizer preview -s ~/audiobooks -d ~/library --detect-duplicates

# Export metadata for editing
bookorganizer preview -s ~/audiobooks -d ~/library --export-metadata

# Interactive mode (prompt to organize after preview)
bookorganizer preview -s ~/audiobooks -d ~/library --interactive

# Complete workflow
bookorganizer preview -s ~/audiobooks -d ~/library --export-metadata --interactive
```

**Options:**
- `--export-metadata` - Export metadata.json files to source folders for editing
- `--interactive` - Prompt to organize immediately after clean preview
- `--detect-duplicates` - Check for potential duplicates
- `--max-items <n>` - Limit number of items shown
- `--verbose` - Show detailed output
- `--no-tree` - Don't show tree view

### `organize` - Execute Organization
Organize audiobooks to destination directory.

```bash
# Copy files (default)
bookorganizer organize -s ~/audiobooks -d ~/library

# Move files
bookorganizer organize -s ~/audiobooks -d ~/library --operation move

# With duplicate detection
bookorganizer organize -s ~/audiobooks -d ~/library --detect-duplicates

# Auto-confirm (no prompts)
bookorganizer organize -s ~/audiobooks -d ~/library --yes
```

**Operations:**
- `copy` (default) - Copy files to destination
- `move` - Move files to destination
- `hardlink` - Create hard links
- `symlink` - Create symbolic links

### `export-metadata` - Export Metadata
Export metadata.json files to audiobook folders for manual editing.

```bash
bookorganizer export-metadata --source ~/audiobooks

# Force overwrite existing files
bookorganizer export-metadata --source ~/audiobooks --force
```

### `verify` - Verify Library
Check library integrity and metadata consistency.

```bash
# Basic verification
bookorganizer verify --library ~/library

# With duplicate detection
bookorganizer verify --library ~/library --check-duplicates

# Verbose output
bookorganizer verify --library ~/library --verbose
```

## Workflow

### Recommended Complete Workflow

1. **Preview and Export Metadata**
```bash
bookorganizer preview -s ~/audiobooks -d ~/library --export-metadata
```

2. **Edit Metadata** (if needed)
- Edit the `metadata.json` files created in each audiobook folder
- Fix titles, authors, series information

3. **Re-Preview to Verify Changes**
```bash
bookorganizer preview -s ~/audiobooks -d ~/library
```

4. **Organize**
```bash
bookorganizer organize -s ~/audiobooks -d ~/library --operation copy
```

5. **Verify Library**
```bash
bookorganizer verify --library ~/library --check-duplicates
```

### Quick Interactive Workflow

For a streamlined experience:

```bash
bookorganizer preview -s ~/audiobooks -d ~/library --export-metadata --interactive
```

This will:
1. Show preview
2. Export metadata for editing
3. Prompt you to organize immediately
4. Execute organization if you confirm

## Metadata Format

The tool creates `metadata.json` files with this structure:

```json
{
  "Title": "Book Title",
  "Author": "Author Name",
  "Narrator": "Narrator Name",
  "Series": "Series Name",
  "SeriesNumber": "1",
  "Year": 2024,
  "Genre": "Fiction",
  "Description": "Book description"
}
```

## Library Structure

Books are organized into this structure:

```
library/
├── Author Name/
│   ├── Book Title/
│   │   ├── Chapter 01.mp3
│   │   ├── Chapter 02.mp3
│   │   └── ...
│   └── Series Name/
│       ├── 01 - First Book/
│       │   └── *.mp3
│       └── 02 - Second Book/
│           └── *.mp3
```

## Configuration

### Log Level

By default, BookOrganizer uses `Warning` log level for clean output. To enable verbose logging, set the `BOOKORGANIZER_LOG_LEVEL` environment variable:

```bash
# Enable verbose logging (shows all Info messages)
export BOOKORGANIZER_LOG_LEVEL=Information
bookorganizer preview -s ~/audiobooks -d ~/library

# Enable debug logging (very detailed)
export BOOKORGANIZER_LOG_LEVEL=Debug
bookorganizer preview -s ~/audiobooks -d ~/library

# Reset to default (Warning level)
unset BOOKORGANIZER_LOG_LEVEL
```

**Available log levels:** `Debug`, `Information`, `Warning`, `Error`, `Critical`

## Requirements

- .NET 9.0 or later
- Cross-platform: Windows, macOS, Linux

## License

MIT License - See LICENSE file for details

## Contributing

Contributions welcome! Please feel free to submit a Pull Request.
