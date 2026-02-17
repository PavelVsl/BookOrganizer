using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace BookOrganizer.Desktop.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }
}

public class BoolToStatusConverter : IValueConverter
{
    public static readonly BoolToStatusConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "\u2713 In library" : "\u2717 New";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
