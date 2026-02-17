using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Services.Audiobookshelf;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class AbsLibraryViewModel : ObservableObject
{
    private readonly IAbsApiClient _absApiClient;
    private readonly ILogger<AbsLibraryViewModel> _logger;
    private readonly AppSettings _settings;

    [ObservableProperty] private ObservableCollection<AbsAuthorNode> _authors = [];
    [ObservableProperty] private object? _selectedItem;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Configure Audiobookshelf in Tools > Audiobookshelf to get started.";
    [ObservableProperty] private int _itemCount;

    public AbsLibraryViewModel(
        IAbsApiClient absApiClient,
        ILogger<AbsLibraryViewModel> logger,
        AppSettings settings)
    {
        _absApiClient = absApiClient;
        _logger = logger;
        _settings = settings;
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.AbsServerUrl) ||
            string.IsNullOrWhiteSpace(_settings.AbsApiKey) ||
            string.IsNullOrWhiteSpace(_settings.AbsLibraryId))
        {
            StatusText = "ABS not configured. Go to Tools > Audiobookshelf to set up.";
            return;
        }

        IsLoading = true;
        Authors.Clear();
        StatusText = "Loading Audiobookshelf library...";

        try
        {
            if (!_absApiClient.IsConfigured)
                _absApiClient.Configure(_settings.AbsServerUrl, _settings.AbsApiKey);

            var items = await _absApiClient.GetLibraryItemsAsync(_settings.AbsLibraryId, ct);
            ItemCount = items.Count;

            // Build tree: Author > Book
            var byAuthor = items
                .GroupBy(i => i.Media?.Metadata?.AuthorName ?? "Unknown")
                .OrderBy(g => g.Key);

            foreach (var group in byAuthor)
            {
                var authorNode = new AbsAuthorNode { Name = group.Key };

                // Group by series within author
                var withSeries = group
                    .Where(i => !string.IsNullOrWhiteSpace(i.Media?.Metadata?.SeriesName))
                    .GroupBy(i => i.Media!.Metadata!.SeriesName!);

                var withoutSeries = group
                    .Where(i => string.IsNullOrWhiteSpace(i.Media?.Metadata?.SeriesName));

                foreach (var seriesGroup in withSeries.OrderBy(g => g.Key))
                {
                    var seriesNode = new AbsSeriesNode { Name = seriesGroup.Key };
                    foreach (var item in seriesGroup.OrderBy(i => i.Media?.Metadata?.Title))
                    {
                        seriesNode.Books.Add(new AbsBookNode
                        {
                            Id = item.Id,
                            Title = item.Media?.Metadata?.Title ?? "Unknown",
                            Author = item.Media?.Metadata?.AuthorName,
                            Series = item.Media?.Metadata?.SeriesName
                        });
                    }
                    authorNode.Children.Add(seriesNode);
                }

                foreach (var item in withoutSeries.OrderBy(i => i.Media?.Metadata?.Title))
                {
                    authorNode.Children.Add(new AbsBookNode
                    {
                        Id = item.Id,
                        Title = item.Media?.Metadata?.Title ?? "Unknown",
                        Author = item.Media?.Metadata?.AuthorName
                    });
                }

                Authors.Add(authorNode);
            }

            StatusText = $"Loaded {items.Count} item(s) from {Authors.Count} author(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load ABS library");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ScanLibraryAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.AbsLibraryId))
        {
            StatusText = "No library selected.";
            return;
        }

        try
        {
            StatusText = "Triggering ABS library scan...";
            await _absApiClient.ScanLibraryAsync(_settings.AbsLibraryId, ct);
            StatusText = "Scan triggered. Refreshing...";
            await RefreshAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger ABS scan");
            StatusText = $"Scan error: {ex.Message}";
        }
    }
}

public class AbsAuthorNode
{
    public required string Name { get; init; }
    public ObservableCollection<object> Children { get; } = [];
}

public class AbsSeriesNode
{
    public required string Name { get; init; }
    public ObservableCollection<AbsBookNode> Books { get; } = [];
}

public class AbsBookNode
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Author { get; init; }
    public string? Series { get; init; }
}
