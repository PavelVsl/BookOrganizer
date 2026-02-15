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
    private readonly ScanViewModel _scanViewModel;
    private readonly PreviewViewModel _previewViewModel;
    private readonly OrganizeViewModel _organizeViewModel;

    public MainWindowViewModel(
        LibraryViewModel libraryViewModel,
        BatchRenameViewModel batchRenameViewModel,
        ScanViewModel scanViewModel,
        PreviewViewModel previewViewModel,
        OrganizeViewModel organizeViewModel)
    {
        _libraryViewModel = libraryViewModel;
        _batchRenameViewModel = batchRenameViewModel;
        _scanViewModel = scanViewModel;
        _previewViewModel = previewViewModel;
        _organizeViewModel = organizeViewModel;
        _currentView = libraryViewModel;
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => _libraryViewModel,
            1 => _batchRenameViewModel,
            2 => _scanViewModel,
            3 => _previewViewModel,
            4 => _organizeViewModel,
            _ => _libraryViewModel
        };
    }
}
