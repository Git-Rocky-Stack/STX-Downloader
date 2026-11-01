using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stx.App.Primitives;
using Stx.Core.Settings;
using Stx.Look;
using Stx.ViewModels;
using Windows.Storage.Pickers;

namespace Stx.App.Panels;

/// <summary>Settings, as switches on a plate. Every change applies and saves at once.</summary>
public sealed partial class SettingsPanel : ContentControl
{
    public SettingsPanel(SettingsViewModel settings, Window? owner)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var switches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };
        switches.Children.Add(Switch("LIGHT MODE", settings.LightMode, v => settings.LightMode = v));
        switches.Children.Add(Switch("TRAY", settings.TrayMode, v => settings.TrayMode = v));
        switches.Children.Add(Switch("START WITH WINDOWS", settings.StartWithWindows, v => settings.StartWithWindows = v));
        switches.Children.Add(Switch("CLIPBOARD", settings.ClipboardDetect, v => settings.ClipboardDetect = v));
        switches.Children.Add(Switch("TOOL UPDATES", settings.ToolUpdates, v => settings.ToolUpdates = v));
        switches.Children.Add(Switch("REDUCE MOTION", settings.ReduceMotion, v => settings.ReduceMotion = v));

        var embed = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center };
        embed.Children.Add(Switch("TAGS", settings.EmbedTags, v => settings.EmbedTags = v));
        embed.Children.Add(Switch("COVER ART", settings.EmbedCoverArt, v => settings.EmbedCoverArt = v));
        embed.Children.Add(Switch("CHAPTERS", settings.EmbedChapters, v => settings.EmbedChapters = v));

        var download = new Rotary { Label = "Download", Items = SettingsViewModel.DownloadLabels };
        download.SelectedIndex = (int)settings.Download;
        download.SelectionChanged += (_, index) => settings.Download = (DownloadChoice)index;

        var parallel = new Rotary
        {
            Label = "Parallel downloads",
            Items = Enumerable.Range(1, AppSettings.MostParallel).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToList(),
        };
        parallel.SelectedIndex = settings.MaxParallel - 1;
        parallel.SelectionChanged += (_, index) => settings.MaxParallel = index + 1;

        var folder = new DisplayTextBox { Label = "Output folder", Text = settings.OutputFolder, Tone = DisplayTone.Amber };
        folder.LostFocus += (_, _) => { settings.OutputFolder = folder.Text; folder.Text = settings.OutputFolder; };
        var template = new DisplayTextBox { Label = "Filename template", Text = settings.FilenameTemplate, Tone = DisplayTone.Amber };
        template.LostFocus += (_, _) => { settings.FilenameTemplate = template.Text; template.Text = settings.FilenameTemplate; };

        var browse = new Pad { Content = "BROWSE", VerticalAlignment = VerticalAlignment.Center };
        browse.Click += async (_, _) =>
        {
            if (owner is null) return;
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            if (await picker.PickSingleFolderAsync() is { } chosen)
            {
                settings.OutputFolder = chosen.Path;
                folder.Text = settings.OutputFolder;
            }
        };

        var folderRow = new Grid { ColumnSpacing = 16 };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition());
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(new Display { Tone = DisplayTone.Amber, Padding = new Thickness(6), Content = folder });
        folderRow.Children.Add(browse);

        var fields = new StackPanel { Spacing = 8 };
        fields.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = "Output folder" });
        fields.Children.Add(folderRow);
        fields.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = "Filename template: {title} {id} {artist} {date} {height}" });
        fields.Children.Add(new Display { Tone = DisplayTone.Amber, Padding = new Thickness(6), Content = template });

        var top = new Grid { ColumnSpacing = 32 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        var dials = new StackPanel { Spacing = 16 };
        dials.Children.Add(download);
        dials.Children.Add(parallel);
        Grid.SetColumn(dials, 0);
        var rows = new StackPanel { Spacing = 16 };
        rows.Children.Add(switches);
        rows.Children.Add(embed);
        rows.Children.Add(fields);
        Grid.SetColumn(rows, 1);
        top.Children.Add(dials);
        top.Children.Add(rows);

        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Settings" });
        column.Children.Add(top);
        Content = new Faceplate { PanelName = "settings", Padding = new Thickness(36, 28, 36, 28), Content = column };
    }

    static PushSwitch Switch(string label, bool on, Action<bool> changed)
    {
        var button = new PushSwitch { Content = label, IsChecked = on };
        button.Checked += (_, _) => changed(true);
        button.Unchecked += (_, _) => changed(false);
        return button;
    }
}
