// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lupe.Core.Resources;

namespace Lupe.UI;

/// <summary>Rückfragen (Ja/Nein) – Avalonia hat kein MessageBox; aufgebaut wie die übrigen Lupe-Fenster.</summary>
internal static class Dialogs
{
    /// <summary>Fragt nach; true nur bei „Ja“ (Esc, Schließen und „Nein“ sind nein – Nein ist vorgewählt).</summary>
    public static async Task<bool> ConfirmAsync(Visual owner, string title, string text, bool warning = false)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            CanMinimize = false,
            CanMaximize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.SetResource(TemplatedControl.BackgroundProperty, "ApplicationBackgroundBrush");
        dialog.SetResource(TemplatedControl.FontFamilyProperty, "UiFont");
        dialog.SetResource(TemplatedControl.ForegroundProperty, "TextFillColorPrimaryBrush");

        var icon = new TextBlock { Text = warning ? "\uE7BA" : "\uE946", FontSize = 22, Margin = new Thickness(0, 2, 16, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.Classes.Add("icon");
        icon.SetResource(TextBlock.ForegroundProperty, warning ? "SystemFillColorCautionBrush" : "AccentTextFillColorPrimaryBrush");
        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        message.Classes.Add("body");
        var yes = new Button { Content = Strings.App_Dialog_Yes, MinWidth = 96 };
        var no = new Button { Content = Strings.App_Dialog_No, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        no.Classes.Add("accent");
        bool answer = false;
        yes.Click += (_, _) =>
        {
            answer = true;
            dialog.Close(true);
        };
        no.Click += (_, _) => dialog.Close(false);
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) dialog.Close(false);
        };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        body.Children.Add(icon);
        Grid.SetColumn(message, 1);
        body.Children.Add(message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(body);
        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.Opened += (_, _) => no.Focus();

        if (TopLevel.GetTopLevel(owner) is Window window && window.IsVisible) return await dialog.ShowDialog<bool>(window);
        var done = new TaskCompletionSource<bool>();
        dialog.Closed += (_, _) => done.TrySetResult(answer);
        dialog.Show();
        return await done.Task;
    }
}
