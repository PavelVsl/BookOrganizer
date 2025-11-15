using BookOrganizer.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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