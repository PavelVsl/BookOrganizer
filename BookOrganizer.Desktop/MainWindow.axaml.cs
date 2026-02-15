using Avalonia.Controls;
using Avalonia.Input;
using BookOrganizer.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Input;

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
        // Cmd+S / Ctrl+S: Save current detail in Library view
        if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            if (DataContext is MainWindowViewModel mainVm &&
                mainVm.CurrentView is LibraryViewModel libraryVm)
            {
                switch (libraryVm.SelectedDetail)
                {
                    case BookDetailViewModel { IsDirty: true } book:
                        book.SaveCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case AuthorDetailViewModel { IsDirty: true } author:
                        author.SaveCommand.Execute(null);
                        e.Handled = true;
                        break;
                    case SeriesDetailViewModel { IsDirty: true } series:
                        series.SaveCommand.Execute(null);
                        e.Handled = true;
                        break;
                }
            }
        }
    }
}
