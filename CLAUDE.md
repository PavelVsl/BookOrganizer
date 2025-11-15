# BookOrganizer - Claude Code Instructions

## Project Overview
AudioBook Organizer is a .NET 9 console application for organizing and structuring a personal audiobook library. The tool analyzes unorganized audiobook folders, extracts metadata, and reorganizes them into a clean, consistent structure.

**Primary Use Case**: Managing a Czech audiobook library with hundreds of books in various states of organization.

## Task Master AI Instructions
**Import Task Master's development workflow commands and guidelines, treat as if import is in the main CLAUDE.md file.**
@./.taskmaster/CLAUDE.md

## Technology Stack

### Core Technologies
- **.NET 9** with C# 13
- **Console Application** (cross-platform: Windows, macOS, Linux)
- **System.CommandLine** for CLI argument parsing
- **TagLib-Sharp** for MP3 metadata reading/writing
- **Spectre.Console** for enhanced CLI UI (progress bars, tables, prompts)
- **Microsoft.Extensions.DependencyInjection** for IoC container
- **Microsoft.Extensions.Logging** for structured logging
- **SQLite** for metadata caching
- **System.Text.Json** for configuration files

### Modern C# Features
- File-scoped namespaces
- Records for immutable data structures
- Pattern matching
- Nullable reference types (enabled)
- Required members
- Primary constructors where appropriate

## Architecture Guidelines

### Design Patterns
- **Strategy Pattern**: File operations (copy vs move)
- **Repository Pattern**: Metadata caching
- **Plugin Architecture**: Metadata providers
- **Dependency Injection**: All services and dependencies
- **CQRS**: Separation of read/write operations where it improves clarity

### Project Structure
```
BookOrganizer/
├── Commands/           # CLI command implementations
├── Services/           # Core business logic
│   ├── Scanning/      # Directory scanning and file detection
│   ├── Metadata/      # Metadata extraction and consolidation
│   ├── Operations/    # File copy/move operations
│   └── Providers/     # Metadata provider plugins
├── Models/            # Domain models and DTOs
├── Infrastructure/    # Cross-cutting concerns
│   ├── Logging/
│   ├── Configuration/
│   └── Caching/
└── Tests/             # Integration and unit tests
```

### Separation of Concerns
- Keep CLI commands thin - delegate to services
- Services should have single, clear responsibilities
- Avoid service classes with too many dependencies
- Use composition over inheritance
- Keep classes simple - no over-engineering

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

### Czech Language Handling
- Always use **UTF-8** encoding
- Test with Czech diacritics: ě, š, č, ř, ž, ý, á, í, é, ú, ů, ď, ť, ň
- Normalize strings using proper culture-aware comparisons
- Use `StringComparison.CurrentCultureIgnoreCase` for Czech text matching

## Testing Strategy

### No Mocking - Real Services
- Build **real service stack** in tests (no mocking frameworks)
- Use **integration tests** with actual file operations
- Create **test fixtures** with sample audiobook folder structures
- Test with **real MP3 files** and metadata

### Test Organization
```
Tests/
├── Fixtures/          # Sample audiobook folders, MP3 files
├── Integration/       # End-to-end workflow tests
├── Services/          # Service-level tests with real dependencies
└── TestHelpers/       # Shared test utilities
```

### Key Test Scenarios
- Czech character handling in filenames and metadata
- Edge cases: very long paths, special characters, Unicode
- Various filename patterns and metadata sources
- File operation reliability and integrity
- Performance with hundreds of books
- Interrupted operation recovery

## Error Handling

### Approach
- Use **exceptions** for exceptional conditions only
- Return **Result types** for expected failures (consider creating Result<T> type)
- Log errors with appropriate context
- Provide **helpful error messages** with suggested fixes
- Never swallow exceptions silently

### User-Facing Errors
- Clear, actionable error messages
- Suggest next steps or fixes
- Include relevant file paths and context
- Distinguish between user errors and system errors

## CLI Design Principles

### Commands
```bash
bookorganizer scan <source-path> [options]
bookorganizer preview <source-path> <dest-path> [options]
bookorganizer organize <source-path> <dest-path> [options]
bookorganizer verify <library-path> [options]
bookorganizer config [set|get|list] [options]
```

### Output Guidelines
- Use **Spectre.Console** for rich terminal output
- Show **progress bars** for long operations
- Use **tables** for structured data display
- Colorize output appropriately (errors=red, success=green, warnings=yellow)
- Respect `--quiet` and `--verbose` flags
- Support `--json` output for scripting

### Progressive Disclosure
- Don't overwhelm users with options upfront
- Provide sensible defaults
- Use interactive prompts for missing required information
- Allow non-interactive mode with `--yes` flag

## Key Considerations

### Data Safety
- **Never lose data** - verify operations before execution
- Implement **checksum validation** after file operations
- Support **dry-run/preview** mode for all destructive operations
- Create **operation manifests** for rollback capability
- Log all changes for audit trail

### Performance
- Target: Scan 1000 audiobook folders in under 30 seconds
- Show progress updates every 2 seconds for long operations
- Cache metadata extraction results
- Use parallel processing where safe (file scanning)
- Optimize hot paths identified through profiling

### Extensibility
- Plugin architecture for metadata providers
- Configurable folder naming templates
- Prepared for future network support (SMB/SSH)
- Clean separation allows future GUI/web interface

## Common Patterns

### Dependency Injection
```csharp
// Register services
services.AddSingleton<IMetadataExtractor, MetadataExtractor>();
services.AddScoped<IFileOperationService, FileOperationService>();
services.AddTransient<IScanningService, ScanningService>();
```

### Result Type Pattern
```csharp
public record Result<T>(bool IsSuccess, T? Value, string? Error);

public Result<BookMetadata> ExtractMetadata(string path)
{
    try
    {
        var metadata = /* extraction logic */;
        return new Result<BookMetadata>(true, metadata, null);
    }
    catch (Exception ex)
    {
        return new Result<BookMetadata>(false, null, ex.Message);
    }
}
```

### Configuration
```csharp
// appsettings.json structure
{
  "Library": {
    "DefaultSourcePath": "~/audiobooks",
    "DefaultDestinationPath": "~/library",
    "NamingTemplate": "{author}/{series}/{number} - {title}"
  },
  "MetadataProviders": {
    "CacheEnabled": true,
    "Providers": []
  }
}
```

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

## Resources

- [System.CommandLine Docs](https://learn.microsoft.com/dotnet/standard/commandline/)
- [TagLib-Sharp GitHub](https://github.com/mono/taglib-sharp)
- [Spectre.Console Docs](https://spectreconsole.net/)
- [.NET 9 What's New](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)
