using BookOrganizer.Commands;
using BookOrganizer.Infrastructure.Configuration;
using BookOrganizer.Infrastructure.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Text;

// Register Windows-1250 and other code page encodings
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Display version at startup
Console.WriteLine($"BookOrganizer v{ThisAssembly.AssemblyInformationalVersion}");
Console.WriteLine();

// Build DI container
var services = new ServiceCollection();
services.AddBookOrganizerServices();
services.AddBookOrganizerLogging();

// Make service provider accessible to commands
Program.ServiceProvider = services.BuildServiceProvider();

try
{
    // Create root command
    var rootCommand = new RootCommand("BookOrganizer - Organize your audiobook library for Jellyfin");

    // Add commands
    rootCommand.Subcommands.Add(new ScanCommand());
    rootCommand.Subcommands.Add(new PreviewCommand());
    rootCommand.Subcommands.Add(new OrganizeCommand());
    rootCommand.Subcommands.Add(new ReorganizeCommand());
    rootCommand.Subcommands.Add(new ExportMetadataCommand());
    rootCommand.Subcommands.Add(new VerifyCommand());

    // Execute
    var parseResult = rootCommand.Parse(args);
    return await parseResult.InvokeAsync();
}
catch (BookOrganizerException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.Error.WriteLine($"Details: {ex.InnerException.Message}");
    }
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
    return 1;
}

// Make Program partial to allow access to ServiceProvider
public partial class Program
{
    public static IServiceProvider ServiceProvider { get; set; } = null!;
}