using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BookOrganizer.Desktop.ViewModels;

namespace BookOrganizer.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;

        // Hide in-window menu on macOS (native menu bar is used instead)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            InWindowMenu.IsVisible = false;
        }
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Cmd+S / Ctrl+S: Save current detail in Library view
        if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            if (Vm is { } mainVm)
            {
                switch (mainVm.Library.SelectedDetail)
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

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    // --- NativeMenu click handlers (macOS native menu bar) ---

    private void OnOpenLibrary(object? sender, EventArgs e) =>
        Vm?.OpenLibraryCommand.Execute(null);

    private void OnOpenSettings(object? sender, EventArgs e) =>
        Vm?.OpenSettingsCommand.Execute(null);

    private void OnRefreshLibrary(object? sender, EventArgs e) =>
        Vm?.Library.LoadLibraryCommand.Execute(null);

    private void OnScanMetadata(object? sender, EventArgs e) =>
        Vm?.Library.ScanMetadataCommand.Execute(null);

    private void OnExportNfo(object? sender, EventArgs e) =>
        Vm?.Library.ExportNfoCommand.Execute(null);

    private void OnReorganize(object? sender, EventArgs e) =>
        Vm?.Library.ReorganizeCommand.Execute(null);

    private void OnVerifyLibrary(object? sender, EventArgs e) =>
        Vm?.Library.VerifyLibraryCommand.Execute(null);

    private void OnDetectSynonyms(object? sender, EventArgs e) =>
        Vm?.Library.DetectSynonymsCommand.Execute(null);

    private void OnPublishAll(object? sender, EventArgs e) =>
        Vm?.Library.PublishAllCommand.Execute(null);

    private void OnCheckAbsDuplicates(object? sender, EventArgs e) =>
        Vm?.Library.CheckAbsDuplicatesCommand.Execute(null);

    private void OnRefreshAbsLibrary(object? sender, EventArgs e) =>
        Vm?.Library.AbsLibraryVm.RefreshCommand.Execute(null);

    private void OnShowAbout(object? sender, EventArgs e) =>
        Vm?.ShowAboutCommand.Execute(null);
}
