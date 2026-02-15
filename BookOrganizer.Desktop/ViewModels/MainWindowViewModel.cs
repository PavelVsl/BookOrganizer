using CommunityToolkit.Mvvm.ComponentModel;

namespace BookOrganizer.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private int _selectedNavIndex;

    public LibraryViewModel Library => _libraryViewModel;
    private readonly LibraryViewModel _libraryViewModel;
    private readonly ToolsViewModel _toolsViewModel;

    public MainWindowViewModel(
        LibraryViewModel libraryViewModel,
        ToolsViewModel toolsViewModel)
    {
        _libraryViewModel = libraryViewModel;
        _toolsViewModel = toolsViewModel;
        _currentView = libraryViewModel;
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => _libraryViewModel,
            1 => _toolsViewModel,
            _ => _libraryViewModel
        };
    }
}
