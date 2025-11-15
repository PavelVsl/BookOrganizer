using BookOrganizer.Commands;
using BookOrganizer.Infrastructure.Configuration;
using BookOrganizer.Infrastructure.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.CommandLine;

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
    rootCommand.AddCommand(new ScanCommand());
    rootCommand.AddCommand(new PreviewCommand());
    rootCommand.AddCommand(new OrganizeCommand());

    // Execute
    return await rootCommand.InvokeAsync(args);
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