using BookOrganizer.Infrastructure.Configuration;
using BookOrganizer.Infrastructure.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

try
{
    // Build DI container
    var services = new ServiceCollection();

    // Register services
    services.AddBookOrganizerServices();
    services.AddBookOrganizerLogging();

    // Build service provider
    var serviceProvider = services.BuildServiceProvider();

    // Get logger
    var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("BookOrganizer started");
    logger.LogInformation("Application initialized successfully");

    return 0;
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