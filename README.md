# BookOrganizer

A CLI tool for organizing audiobook libraries for Audiobookshelf with intelligent metadata extraction, duplicate detection, and Czech language support.

## Features

- **Intelligent Metadata Extraction** - Extracts metadata from MP3 ID3 tags, filenames, and folder structure
- **Czech Language Support** - Full support for Czech diacritics and Windows-1250 encoding fixes
- **MP3 Tag Caching** - Caches extracted ID3 tags in `mp3tags.json` to avoid repeated TagLib reads
- **Audiobookshelf Deduplication** - Checks source audiobooks against an Audiobookshelf server before organizing
- **Duplicate Detection** - Finds potential duplicate audiobooks using normalized metadata
- **Smart Organization** - Organizes into `Author/Series/Title` structure
- **Hierarchical Metadata** - Cascading `bookinfo.json` from author to series to book level
- **Library Verification** - Validates library integrity and metadata consistency
- **Metadata Editing** - Export metadata to JSON/NFO for manual editing

## Installation

### From Source

```bash
git clone https://github.com/yourusername/BookOrganizer.git
cd BookOrganizer

# Use the publish script (recommended)
./publish.sh              # macOS/Linux

# Or manually:
cd BookOrganizer
dotnet pack -c Release -o ../nupkg
dotnet tool install -g BookOrganizer --add-source ../nupkg
```

## Quick Start

```bash
# Preview organization (see what will happen)
bookorganizer preview -s ~/audiobooks -d ~/library

# Preview with Audiobookshelf duplicate check
bookorganizer preview -s ~/audiobooks -d ~/library --check-abs

# Organize audiobooks
bookorganizer organize -s ~/audiobooks -d ~/library --yes

# Verify library integrity
bookorganizer verify -l ~/library
```

## Commands

### `scan` - Quick Discovery

Scan a directory to find audiobook folders without metadata extraction.

```bash
bookorganizer scan -s ~/audiobooks
```

| Option | Short | Description |
|--------|-------|-------------|
| `--source` | `-s` | Source directory to scan (required) |
| `--verbose` | `-v` | Show detailed output |

### `preview` - Full Analysis

Preview how audiobooks will be organized with detailed metadata and issue detection.

```bash
# Basic preview
bookorganizer preview -s ~/audiobooks -d ~/library

# With duplicate detection
bookorganizer preview -s ~/audiobooks -d ~/library --detect-duplicates

# With Audiobookshelf duplicate check
bookorganizer preview -s ~/audiobooks -d ~/library --check-abs

# Export metadata for editing, then prompt to organize
bookorganizer preview -s ~/audiobooks -d ~/library --export-metadata --interactive

# Export preview to file
bookorganizer preview -s ~/audiobooks -d ~/library --export preview.json
```

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--source` | `-s` | Source directory containing audiobooks (required) | |
| `--destination` | `-d`, `-l` | Target library directory (required) | |
| `--operation` | `-o` | Operation type: `copy`, `move`, `hardlink`, `symlink` | `copy` |
| `--export` | `-e` | Export preview to file (.json, .csv, .txt) | |
| `--author` | `-a` | Filter by author name (case-insensitive) | |
| `--series` | | Filter by series name (case-insensitive) | |
| `--max-items` | `-m` | Maximum number of audiobooks to show | |
| `--compact` | `-c` | Use compact display mode | `false` |
| `--no-tree` | | Don't show tree view | `false` |
| `--verbose` | `-v` | Show detailed output with full paths | `false` |
| `--detect-duplicates` | | Detect potential duplicate audiobooks | `false` |
| `--duplicate-threshold` | | Minimum confidence for duplicate detection (0.0-1.0) | `0.7` |
| `--rebuild-cache` | | Force rebuild of library metadata cache | `false` |
| `--export-metadata` | | Export bookinfo.json files to source folders for editing | `false` |
| `--metadata-source` | | Source for metadata: `mp3` (ID3 tags) or `folder` (structure) | `mp3` |
| `--interactive` | `-i` | Prompt to organize immediately after preview | `false` |
| `--preserve-diacritics` | | Preserve Czech diacritics in folder names (UTF-8) | `false` |
| `--check-abs` | | Check for duplicates against Audiobookshelf server | `false` |
| `--abs-url` | | Audiobookshelf server URL (or `AUDIOBOOKSHELF_URL` env var) | |
| `--abs-token` | | Audiobookshelf API token (or `AUDIOBOOKSHELF_TOKEN` env var) | |
| `--abs-library` | | Audiobookshelf library ID (or `AUDIOBOOKSHELF_LIBRARY` env var) | auto-detect |

### `organize` - Execute Organization

Organize audiobooks from source to destination directory.

```bash
# Copy files (default)
bookorganizer organize -s ~/audiobooks -d ~/library

# Move files, auto-confirm
bookorganizer organize -s ~/audiobooks -d ~/library -o move --yes

# With Audiobookshelf dedup — rename source duplicates
bookorganizer organize -s ~/audiobooks -d ~/library --check-abs --duplicate-action rename --yes

# Skip ABS duplicates and delete source folders
bookorganizer organize -s ~/audiobooks -d ~/library --check-abs --duplicate-action delete --yes
```

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--source` | `-s` | Source directory containing audiobooks (required) | |
| `--destination` | `-d`, `-l` | Target library directory (required) | |
| `--operation` | `-o` | Operation type: `copy`, `move`, `hardlink`, `symlink` | `copy` |
| `--no-validate` | | Skip file integrity validation (faster but risky) | `false` |
| `--verbose` | `-v` | Show detailed output | `false` |
| `--yes` | `-y` | Skip confirmation prompt (auto-confirm) | `false` |
| `--detect-duplicates` | | Detect and merge potential duplicate audiobooks | `false` |
| `--duplicate-threshold` | | Minimum confidence for duplicate detection (0.0-1.0) | `0.7` |
| `--preserve-diacritics` | | Preserve Czech diacritics in folder names (UTF-8) | `false` |
| `--check-abs` | | Check for duplicates against Audiobookshelf server | `false` |
| `--abs-url` | | Audiobookshelf server URL (or `AUDIOBOOKSHELF_URL` env var) | |
| `--abs-token` | | Audiobookshelf API token (or `AUDIOBOOKSHELF_TOKEN` env var) | |
| `--abs-library` | | Audiobookshelf library ID (or `AUDIOBOOKSHELF_LIBRARY` env var) | auto-detect |
| `--duplicate-action` | | Action for ABS duplicates: `skip`, `rename`, `move`, `delete` | `skip` |

**Duplicate actions** (when `--check-abs` finds a book already in Audiobookshelf):

| Action | Behavior |
|--------|----------|
| `skip` | Log and ignore — don't organize, source untouched |
| `rename` | Prefix source folder with `_DUP_` so future scans skip it |
| `move` | Move source to a `_duplicates/` subfolder under source root |
| `delete` | Delete source folder (requires `--yes` confirmation) |

### `reorganize` - Reorganize Existing Library

Reorganize an existing library based on updated bookinfo.json metadata files. Moves books within the library to match their metadata.

```bash
# Reorganize with confirmation
bookorganizer reorganize -l ~/library

# Auto-confirm
bookorganizer reorganize -l ~/library --yes
```

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--library` | `-l` | Library directory to reorganize (required) | |
| `--no-validate` | | Skip file integrity validation | `false` |
| `--verbose` | `-v` | Show detailed output | `false` |
| `--yes` | `-y` | Skip confirmation prompt | `false` |
| `--preserve-diacritics` | | Preserve Czech diacritics in folder names | `false` |

### `export-metadata` - Export Metadata Files

Export metadata files to audiobook folders for manual editing or Audiobookshelf compatibility.

```bash
# Export bookinfo.json files
bookorganizer export-metadata -s ~/audiobooks

# Export NFO files (force overwrite)
bookorganizer export-metadata -s ~/audiobooks -f nfo --force

# Export all formats
bookorganizer export-metadata -s ~/audiobooks -f all --force
```

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--source` | `-s` | Source directory to scan for audiobooks (required) | |
| `--format` | `-f` | Output format: `bookorganizer`, `audiobookshelf`, `nfo`, `all` | `bookorganizer` |
| `--force` | | Overwrite existing metadata files | `false` |
| `--verbose` | `-v` | Show detailed output | `false` |

**Metadata formats:**
- `bookorganizer` — `bookinfo.json` (BookOrganizer native format)
- `audiobookshelf` — `metadata.json` (Audiobookshelf format)
- `nfo` — `metadata.nfo` (Kodi/XML format)
- `all` — All three formats

### `verify` - Verify Library

Check library integrity, metadata consistency, and optionally generate missing metadata files.

```bash
# Basic verification
bookorganizer verify -l ~/library

# With duplicate detection
bookorganizer verify -l ~/library --check-duplicates

# Generate missing metadata files
bookorganizer verify -l ~/library --generate-metadata --force

# Generate NFO files
bookorganizer verify -l ~/library --generate-metadata --metadata-format nfo --force
```

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--library` | `-l` | Library directory to verify (required) | |
| `--verbose` | `-v` | Show detailed output | `false` |
| `--check-duplicates` | | Check for potential duplicate audiobooks | `false` |
| `--duplicate-threshold` | | Minimum confidence for duplicate detection (0.0-1.0) | `0.7` |
| `--generate-metadata` | | Generate missing metadata files from folder structure | `false` |
| `--metadata-format` | | Format for `--generate-metadata`: `bookorganizer`, `audiobookshelf`, `nfo`, `all` | `bookorganizer` |
| `--force` | | Overwrite existing metadata files when generating | `false` |

## Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `BOOKORGANIZER_LOG_LEVEL` | Logging verbosity | `Debug`, `Information`, `Warning` (default), `Error` |
| `AUDIOBOOKSHELF_URL` | Audiobookshelf server URL | `http://192.168.1.100:13378` |
| `AUDIOBOOKSHELF_TOKEN` | Audiobookshelf API key | `eyJhbGci...` |
| `AUDIOBOOKSHELF_LIBRARY` | Audiobookshelf library ID (skips auto-detection) | `c7023f78-3d44-...` |

Get your API key from Audiobookshelf: Settings > Users > your user > API Token.

When `AUDIOBOOKSHELF_LIBRARY` is not set, auto-detection prefers a library named "library" and skips libraries with "test" in the name.

## MP3 Tag Caching

BookOrganizer caches extracted ID3 tags in `mp3tags.json` files alongside audio files. On subsequent runs, cached tags are used instead of re-reading MP3 files with TagLib, significantly speeding up repeated operations.

- Cache is **transparent** — no CLI flags needed
- Per-file staleness check uses `lastModifiedUtc` + `fileSizeBytes` (no hashing)
- New or changed files are extracted fresh and the cache is updated
- To force re-extraction, delete the `mp3tags.json` file:

```bash
# Delete all tag cache files
find ~/audiobooks -name "mp3tags.json" -delete
```

## Workflow

### Chunk-Based Import Workflow

For processing audiobooks in batches:

```bash
# 1. Preview a chunk with ABS dedup check
bookorganizer preview -s ~/audiobooks/chunk1 -d ~/library --check-abs

# 2. Organize (skip books already in ABS, rename source duplicates)
bookorganizer organize -s ~/audiobooks/chunk1 -d ~/library \
  --check-abs --duplicate-action rename --yes

# 3. Trigger ABS library scan, then process next chunk
```

### Full Workflow

1. **Preview and Export Metadata**
```bash
bookorganizer preview -s ~/audiobooks -d ~/library --export-metadata
```

2. **Edit Metadata** (if needed) — edit `bookinfo.json` files, set `"source": "manual"` to protect from overwrite

3. **Re-Preview to Verify Changes**
```bash
bookorganizer preview -s ~/audiobooks -d ~/library
```

4. **Organize**
```bash
bookorganizer organize -s ~/audiobooks -d ~/library --yes
```

5. **Verify Library**
```bash
bookorganizer verify -l ~/library --check-duplicates
```

### Quick Interactive Workflow

```bash
bookorganizer preview -s ~/audiobooks -d ~/library --export-metadata --interactive
```

## Metadata Format

The tool creates `bookinfo.json` files:

```json
{
  "title": "Book Title",
  "author": "Author Name",
  "narrator": "Narrator Name",
  "series": "Series Name",
  "seriesNumber": "1",
  "year": 2024,
  "genre": "Fiction",
  "comment": "www.publisher.cz",
  "source": "ID3Tags"
}

```

Set `"source": "manual"` after editing to protect the file from being overwritten by `export-metadata` or `verify`.

## Library Structure

```
library/
├── Author Name/
│   ├── bookinfo.json          # author-level metadata
│   ├── Book Title/
│   │   ├── bookinfo.json      # book-level metadata
│   │   ├── metadata.nfo       # Audiobookshelf NFO
│   │   ├── mp3tags.json       # ID3 tag cache
│   │   ├── 01 - Chapter 1.mp3
│   │   └── ...
│   └── Series Name/
│       ├── bookinfo.json      # series-level metadata
│       ├── 01 - First Book/
│       │   └── *.mp3
│       └── 02 - Second Book/
│           └── *.mp3
```

Hierarchical metadata cascade: author-level < series-level < book-level (book wins).

## Requirements

- .NET 10.0 or later
- Cross-platform: Windows, macOS, Linux
