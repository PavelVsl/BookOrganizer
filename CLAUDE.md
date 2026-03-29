# BookOrganizer - Claude Code Instructions

## Project Overview
AudioBook Organizer is a .NET 10 solution for organizing and managing a personal audiobook library. It consists of a CLI tool, a shared core library, and a cross-platform desktop GUI (Avalonia UI). The tool analyzes unorganized audiobook folders, extracts metadata, and reorganizes them into a clean, consistent structure with Audiobookshelf integration.

**Primary Use Case**: Managing a Czech audiobook library with hundreds of books in various states of organization.

## Testing Folders

**IMPORTANT**: Always use these dedicated testing folders - NEVER use production folders for testing:
- **Test Source**: `/Users/pavel/Documents/dev/source`
- **Test Library**: `/Users/pavel/Documents/dev/library`

**Production Folders** (use only for actual organization, never for testing):
- **Production Source**: `/Users/pavel/Documents/audiobooks`
- **Production Library**: `/Users/pavel/Documents/library`

## Task Master AI Instructions
**Import Task Master's development workflow commands and guidelines, treat as if import is in the main CLAUDE.md file.**
@./.taskmaster/CLAUDE.md

## Technology Stack

### Core Technologies
- **.NET 10** with C# 14 (implicit usings, nullable reference types enabled)
- **System.CommandLine** for CLI argument parsing
- **Spectre.Console 0.54** for enhanced CLI UI (progress bars, tables, prompts)
- **Avalonia UI 11.3** for cross-platform desktop GUI (macOS, Windows, Linux)
- **CommunityToolkit.MVVM 8.4** for MVVM pattern in desktop app
- **TagLib-Sharp 2.3** for MP3 metadata reading/writing
- **Microsoft.Data.Sqlite 10.0** for metadata caching
- **Microsoft.Extensions.DependencyInjection** for IoC container
- **Microsoft.Extensions.Logging** for structured logging
- **System.Text.Json** for configuration files
- **Nerdbank.GitVersioning** for version management (CLI project)

### Modern C# Features
- File-scoped namespaces
- Records for immutable data structures
- Pattern matching
- Nullable reference types (enabled)
- Required members
- Primary constructors where appropriate

## Solution Structure

Three projects in `BookOrganizer.sln`:

```
BookOrganizer/                  # CLI Console Application (.NET Global Tool)
├── Commands/                   # 6 CLI command implementations
├── Infrastructure/Configuration/
├── Rendering/                  # PreviewRenderer, DeduplicationResolver
└── Program.cs

BookOrganizer.Core/             # Shared Class Library (all business logic)
├── Infrastructure/
│   ├── Configuration/          # DI extensions (CoreServiceCollectionExtensions)
│   ├── Database/               # SQLite metadata caching
│   └── Exceptions/             # Custom exception hierarchy
├── Models/                     # 19 domain models (records)
└── Services/
    ├── Audiobookshelf/         # ABS API client, publishing, dedup
    ├── Deduplication/          # Duplicate detection & content analysis
    ├── Library/                # Library tree structure
    ├── Metadata/               # Extraction, consolidation, formatting
    ├── Operations/             # File operations (Strategy Pattern)
    ├── Preview/                # Preview generation
    ├── Scanning/               # Directory scanning & file detection
    └── Text/                   # Czech text normalization

BookOrganizer.Desktop/          # Avalonia UI Desktop Application
├── Views/                      # XAML views (MainWindow, LibraryView, etc.)
├── ViewModels/                 # MVVM view models (10 total)
├── Services/                   # PublishQueueService
└── Assets/
```

## Architecture Guidelines

### Design Patterns
- **Strategy Pattern**: File operations (copy, move, hardlink, symlink)
- **Repository Pattern**: Metadata caching (SQLite)
- **Plugin Architecture**: Metadata formatters (BookOrganizer, Audiobookshelf, NFO)
- **Dependency Injection**: All services and dependencies
- **MVVM**: Desktop application with CommunityToolkit.MVVM
- **CQRS**: Separation of read operations (scanning, metadata) vs write operations (file organization)

### Separation of Concerns
- Keep CLI commands thin - delegate to services
- All business logic lives in **BookOrganizer.Core**, shared by CLI and Desktop
- Services should have single, clear responsibilities
- Use composition over inheritance
- Keep classes simple - no over-engineering

## CLI Commands

```bash
bookorganizer scan <source-path> [options]              # Quick folder discovery
bookorganizer preview <source-path> <dest-path> [options]  # Full analysis with metadata
bookorganizer organize <source-path> <dest-path> [options] # Execute file operations
bookorganizer reorganize <library-path> [options]       # Reorganize existing library
bookorganizer export-metadata <path> [options]          # Export metadata (bookinfo/abs/nfo)
bookorganizer verify <library-path> [options]           # Validate library integrity
```

### Key CLI Options
- Operations: `copy`, `move`, `hardlink`, `symlink`
- Duplicate handling: `skip`, `rename`, `move`, `delete`
- Metadata sources: MP3 ID3 tags or folder structure
- Export formats: `bookorganizer` (bookinfo.json), `audiobookshelf` (metadata.json), `nfo` (XML), `all`
- Filtering: `--author`, `--series`, `--max-items`
- Output: `--quiet`, `--verbose`, `--json`, `--tree`

## Desktop Application (Avalonia UI)

### Key Features
- Native macOS menu bar (in-window menu on Windows/Linux)
- Library tree navigation: Author -> Series -> Book -> Volume
- Inline metadata editing with keyboard shortcuts (Cmd+S to save)
- Audiobookshelf integration: publish, duplicate checking, library browsing
- Background publishing queue with progress tracking
- Auto-load last opened library on startup
- Settings dialog for library paths and ABS credentials

### MVVM Architecture
- **ViewModels**: MainWindowViewModel, LibraryViewModel, BookDetailViewModel, AbsLibraryViewModel, SettingsViewModel, AuthorDetailViewModel, SeriesDetailViewModel, VolumeDetailViewModel
- **Views**: MainWindow, LibraryView, LibraryGridView, AbsLibraryView, SettingsWindow

## Coding Conventions

### General Principles
- **Simplicity first**: Always prefer the simplest solution that works
- **No over-engineering**: Don't add complexity for hypothetical future needs
- **Clear naming**: Use descriptive names that explain intent
- **Small methods**: Keep methods focused and concise
- **Immutability**: Prefer immutable data structures (records) where possible

### Specific Conventions
- Use **file-scoped namespaces** for all files
- XML comments for all **public APIs**
- Use **required** keyword for mandatory properties
- Leverage **pattern matching** for readability
- Use **async/await** properly (don't block, use ConfigureAwait(false) in libraries)
- Handle **nullable reference types** correctly - no null-forgiving operators without justification
- All services support **CancellationToken** for graceful interruption

### Czech Language Handling
- Always use **UTF-8** encoding (Windows-1250 for legacy support via System.Text.Encoding.CodePages)
- Test with Czech diacritics: ě, š, č, ř, ž, ý, á, í, é, ú, ů, ď, ť, ň
- Normalize strings using proper culture-aware comparisons
- Use `StringComparison.CurrentCultureIgnoreCase` for Czech text matching

## Testing Strategy

### No Mocking - Real Services
- Build **real service stack** in tests (no mocking frameworks)
- Use **integration tests** with actual file operations
- Create **test fixtures** with sample audiobook folder structures
- Test with **real MP3 files** and metadata

### Key Test Scenarios
- Czech character handling in filenames and metadata
- Edge cases: very long paths, special characters, Unicode
- Various filename patterns and metadata sources
- File operation reliability and integrity
- Performance with hundreds of books
- Interrupted operation recovery

## Error Handling

### Approach
- Use **exceptions** for exceptional conditions only (custom hierarchy: BookOrganizerException, DirectoryScanningException, FileOrganizationException, MetadataExtractionException)
- Return **Result<T>** type for expected failures
- Log errors with appropriate context
- Provide **helpful error messages** with suggested fixes
- Never swallow exceptions silently

## Key Implementation Details

- **MP3 Tag Caching**: Smart staleness checking using lastModifiedUtc + fileSizeBytes (no expensive hashing)
- **Metadata Consolidation**: Multi-source extraction with confidence scoring
- **Hierarchical Metadata**: Cascading bookinfo.json from author -> series -> book level
- **Atomic Moves**: Never merges into existing folders; creates conflict-free paths
- **File Integrity**: SHA256 checksums validate copy operations
- **ABS Deduplication**: Checks source audiobooks against Audiobookshelf server before organizing
- **Background Publishing**: Queue-based publishing to Audiobookshelf with progress tracking and cancellation

## Data Safety
- **Never lose data** - verify operations before execution
- Implement **checksum validation** after file operations
- Support **dry-run/preview** mode for all destructive operations
- Log all changes for audit trail

## Environment Variables
- `BOOKORGANIZER_SOURCE` / `BOOKORGANIZER_LIBRARY` - Default paths
- `BOOKORGANIZER_OPERATION` - Default file operation type
- `BOOKORGANIZER_PRESERVE_DIACRITICS` - Diacritics handling
- `BOOKORGANIZER_METADATA_SOURCE` / `BOOKORGANIZER_EXPORT_FORMAT`
- `BOOKORGANIZER_LOG_LEVEL`
- `AUDIOBOOKSHELF_URL` / `AUDIOBOOKSHELF_TOKEN` / `AUDIOBOOKSHELF_LIBRARY` - ABS connection

## Development Workflow

### Before Starting Work
1. `task-master next` - Get next task
2. `task-master show <id>` - Review task details
3. Read relevant code files
4. Plan implementation approach

### During Implementation
1. Write failing test first (when appropriate)
2. Implement feature
3. Verify tests pass
4. `task-master update-subtask --id=<id> --prompt="implementation notes"`
5. Manual testing with sample data

### After Completion
1. Review code for simplicity
2. Ensure error handling is robust
3. Update documentation if needed
4. `task-master set-status --id=<id> --status=done`

## Questions and Clarifications

When uncertain about implementation details:
- **Ask Pavel** before adding complex solutions
- Consider if there's a simpler approach
- Check if it's truly necessary for initial version
- Review if it aligns with "avoid over-engineering" principle

## Publishing

### CLI Tool
```bash
./publish.sh    # Builds NuGet package and installs as dotnet global tool
```

### Desktop App (macOS)
```bash
./publish-desktop.sh    # Builds BookOrganizer.app in publish/
```
Produces a self-contained `.app` bundle for `osx-arm64` with generated `.icns` icon. No code signing (personal use). Output: `publish/BookOrganizer.app`.

## Resources

- [System.CommandLine Docs](https://learn.microsoft.com/dotnet/standard/commandline/)
- [TagLib-Sharp GitHub](https://github.com/mono/taglib-sharp)
- [Spectre.Console Docs](https://spectreconsole.net/)
- [Avalonia UI Docs](https://docs.avaloniaui.net/)
- [.NET 10 What's New](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10)
- source @/Users/pavel/Documents/audiobooks destination @/Users/pavel/Documents/library
