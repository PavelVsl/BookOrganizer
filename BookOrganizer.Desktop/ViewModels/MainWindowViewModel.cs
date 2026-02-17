using BookOrganizer.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookOrganizer.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private int _selectedNavIndex;

    public LibraryViewModel Library => _libraryViewModel;
    public PublishQueueService PublishQueue { get; }

    private readonly LibraryViewModel _libraryViewModel;
    private readonly ToolsViewModel _toolsViewModel;
    private readonly AbsLibraryViewModel _absLibraryViewModel;

    public MainWindowViewModel(
        LibraryViewModel libraryViewModel,
        ToolsViewModel toolsViewModel,
        AbsLibraryViewModel absLibraryViewModel,
        PublishQueueService publishQueue)
    {
        _libraryViewModel = libraryViewModel;
        _toolsViewModel = toolsViewModel;
        _absLibraryViewModel = absLibraryViewModel;
        PublishQueue = publishQueue;
        _currentView = libraryViewModel;
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => _libraryViewModel,
            1 => _toolsViewModel,
            2 => _absLibraryViewModel,
            _ => _libraryViewModel
        };
    }
}
