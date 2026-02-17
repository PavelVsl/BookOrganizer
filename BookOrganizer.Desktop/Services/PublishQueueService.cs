using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Avalonia.Threading;
using BookOrganizer.Desktop.ViewModels;
using BookOrganizer.Models;
using BookOrganizer.Services.Audiobookshelf;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.Services;

public record PublishQueueItem(BookNode Book, BookMetadata Metadata, string AbsLibraryFolder);

public partial class PublishQueueService : ObservableObject
{
    private readonly IPublishingService _publishingService;
    private readonly ILogger<PublishQueueService> _logger;
    private readonly Channel<PublishQueueItem> _channel = Channel.CreateUnbounded<PublishQueueItem>();

    private CancellationTokenSource? _cts;
    private Task? _processingTask;
    private int _totalEnqueued;

    [ObservableProperty] private bool _isPublishing;
    [ObservableProperty] private int _queueCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private string? _currentBookTitle;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _showStatus;

    public PublishQueueService(IPublishingService publishingService, ILogger<PublishQueueService> logger)
    {
        _publishingService = publishingService;
        _logger = logger;
    }

    public void Enqueue(BookNode book, BookMetadata metadata, string absLibraryFolder)
    {
        EnqueueRange([(book, metadata)], absLibraryFolder);
    }

    public void EnqueueRange(IEnumerable<(BookNode Book, BookMetadata Metadata)> books, string absLibraryFolder)
    {
        var items = books.ToList();
        if (items.Count == 0) return;

        foreach (var (book, metadata) in items)
        {
            _channel.Writer.TryWrite(new PublishQueueItem(book, metadata, absLibraryFolder));
        }

        // Update queue count on UI thread
        Dispatcher.UIThread.Post(() =>
        {
            _totalEnqueued += items.Count;
            QueueCount += items.Count;
            UpdateProgressText();
            ShowStatus = true;
        });

        EnsureProcessing();
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();

        // Drain remaining items from channel
        while (_channel.Reader.TryRead(out _)) { }

        Dispatcher.UIThread.Post(() =>
        {
            QueueCount = 0;
            IsPublishing = false;
            CurrentBookTitle = null;
            ProgressText = FailedCount > 0
                ? $"Cancelled. {CompletedCount} published, {FailedCount} failed."
                : $"Cancelled. {CompletedCount} published.";
        });
    }

    [RelayCommand]
    private void Dismiss()
    {
        if (!IsPublishing)
        {
            ShowStatus = false;
            CompletedCount = 0;
            FailedCount = 0;
            _totalEnqueued = 0;
            ProgressText = "";
        }
    }

    private void EnsureProcessing()
    {
        if (_processingTask is { IsCompleted: false })
            return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _processingTask = Task.Run(async () => await ProcessQueueAsync(ct), ct);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        Dispatcher.UIThread.Post(() => IsPublishing = true);

        try
        {
            while (await _channel.Reader.WaitToReadAsync(ct))
            {
                while (_channel.Reader.TryRead(out var item))
                {
                    ct.ThrowIfCancellationRequested();

                    Dispatcher.UIThread.Post(() =>
                    {
                        CurrentBookTitle = $"\"{item.Book.Title}\" by {item.Book.Author}";
                        UpdateProgressText();
                    });

                    try
                    {
                        var result = await _publishingService.PublishBookAsync(
                            item.Book.Path, item.Metadata, item.AbsLibraryFolder, ct);

                        Dispatcher.UIThread.Post(() =>
                        {
                            QueueCount--;
                            if (result.Success)
                            {
                                CompletedCount++;
                                item.Book.IsPublished = true;
                            }
                            else
                            {
                                FailedCount++;
                                _logger.LogWarning("Failed to publish {Path}: {Error}",
                                    item.Book.Path, result.Error);
                            }
                            UpdateProgressText();
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error publishing {Path}", item.Book.Path);
                        Dispatcher.UIThread.Post(() =>
                        {
                            QueueCount--;
                            FailedCount++;
                            UpdateProgressText();
                        });
                    }
                }

                // No more items currently in channel — check if we should exit
                if (_channel.Reader.Count == 0)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on cancel
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsPublishing = false;
                CurrentBookTitle = null;
                QueueCount = 0;

                if (FailedCount > 0)
                    ProgressText = $"Done: {CompletedCount} published, {FailedCount} failed.";
                else if (CompletedCount > 0)
                    ProgressText = $"Done: {CompletedCount} published.";
                else
                    ProgressText = "";
            });
        }
    }

    private void UpdateProgressText()
    {
        if (IsPublishing && CurrentBookTitle != null)
        {
            var done = CompletedCount + FailedCount;
            ProgressText = $"Publishing {done + 1}/{_totalEnqueued}: {CurrentBookTitle}";
        }
    }
}
