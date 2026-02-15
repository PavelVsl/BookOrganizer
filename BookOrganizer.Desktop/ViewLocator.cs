using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookOrganizer.Desktop;

public class ViewLocator : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is null)
            return new TextBlock { Text = "No view selected" };

        var name = param.GetType().FullName!
            .Replace("ViewModel", "View")
            .Replace("BookOrganizer.Desktop.ViewModels.", "BookOrganizer.Desktop.Views.");

        var type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "View not found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ObservableObject;
    }
}
