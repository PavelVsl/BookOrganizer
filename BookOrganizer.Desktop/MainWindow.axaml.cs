using Avalonia.Controls;
using Avalonia.Input;
using BookOrganizer.Desktop.ViewModels;

namespace BookOrganizer.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Cmd+S / Ctrl+S: Save current book metadata
        if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            if (DataContext is MainWindowViewModel mainVm &&
                mainVm.CurrentView is LibraryViewModel libraryVm &&
                libraryVm.SelectedBookDetail is { IsDirty: true } detail)
            {
                detail.SaveCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
