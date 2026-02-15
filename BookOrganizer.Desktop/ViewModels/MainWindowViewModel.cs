using CommunityToolkit.Mvvm.ComponentModel;

namespace BookOrganizer.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private int _selectedNavIndex;

    private readonly LibraryViewModel _libraryViewModel;
    private readonly BatchRenameViewModel _batchRenameViewModel;

    public MainWindowViewModel(
        LibraryViewModel libraryViewModel,
        BatchRenameViewModel batchRenameViewModel)
    {
        _libraryViewModel = libraryViewModel;
        _batchRenameViewModel = batchRenameViewModel;
        _currentView = libraryViewModel;
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => _libraryViewModel,
            1 => _batchRenameViewModel,
            _ => _libraryViewModel
        };
    }
}
