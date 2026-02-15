using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookOrganizer.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private int _selectedNavIndex;

    private readonly LibraryViewModel _libraryViewModel;

    public MainWindowViewModel(LibraryViewModel libraryViewModel)
    {
        _libraryViewModel = libraryViewModel;
        _currentView = libraryViewModel;
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => _libraryViewModel,
            _ => _libraryViewModel
        };
    }
}
