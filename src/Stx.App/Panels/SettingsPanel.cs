using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Core.Settings;
using Stx.Look;
using Stx.ViewModels;
using Windows.Storage.Pickers;

namespace Stx.App.Panels;

/// <summary>Settings, as switches on a plate. Every change applies and saves at once.</summary>
public sealed partial class SettingsPanel : ContentControl
{
    public SettingsPanel(SettingsViewModel settings, Window? owner, Action? openGuide = null, Action? openTutorial = null)
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

        // How hard playback may work: AUTO picks from the PC, then ECO, BALANCED and MAX.
        var performance = new Rotary { Label = "Performance", Items = SettingsViewModel.PerformanceLabels };
        performance.SelectedIndex = (int)settings.Performance;
        performance.SelectionChanged += (_, index) => settings.Performance = (Stx.Core.Playback.PlaybackPerformance)index;

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
        dials.Children.Add(download.Captioned());
        dials.Children.Add(parallel.Captioned());
        Grid.SetColumn(dials, 0);

        var help = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Top };
        if (openGuide is not null) help.Children.Add(HelpPad("USER GUIDE", openGuide));
        if (openTutorial is not null) help.Children.Add(HelpPad("TUTORIAL", openTutorial));
        // On the shorter row of switches, so they squeeze nothing when the window is narrow.
        var secondRow = new Grid { ColumnSpacing = 16 };
        secondRow.ColumnDefinitions.Add(new ColumnDefinition());
        secondRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(help, 1);
        secondRow.Children.Add(embed);
        secondRow.Children.Add(help);

        var rows = new StackPanel { Spacing = 16 };
        rows.Children.Add(switches);
        rows.Children.Add(secondRow);
        // Playback's knob sits beside the fields, which are about as tall as a knob; the left column holds the download knobs.
        var fieldsRow = new Grid { ColumnSpacing = 24 };
        fieldsRow.ColumnDefinitions.Add(new ColumnDefinition());
        fieldsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var playback = performance.Captioned();
        playback.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(playback, 1);
        fieldsRow.Children.Add(fields);
        fieldsRow.Children.Add(playback);
        rows.Children.Add(fieldsRow);
        Grid.SetColumn(rows, 1);
        top.Children.Add(dials);
        top.Children.Add(rows);

        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Settings" });
        column.Children.Add(top);
        column.Children.Add(WatchedFolders(settings, owner));
        Content = new Faceplate { PanelName = "settings", Padding = new Thickness(36, 28, 36, 28), Content = column };
    }

    // The folders whose media joins the Library: Videos and Music to begin with. Removing one only stops watching it.
    static FrameworkElement WatchedFolders(SettingsViewModel settings, Window? owner)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 132, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Watched folders");
        var add = new Pad { Content = "ADD FOLDER", VerticalAlignment = VerticalAlignment.Top };
        var remove = new Pad { Content = "REMOVE FOLDER", VerticalAlignment = VerticalAlignment.Top, IsEnabled = false };

        void Fill()
        {
            list.Items.Clear();
            foreach (string folder in settings.WatchedFolders)
            {
                var item = new ListViewItem { MinHeight = 44, Content = new GlowText { Tone = DisplayTone.Amber, Text = folder }, Tag = folder };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, "Watched: " + folder);
                list.Items.Add(item);
            }
            remove.IsEnabled = false;
        }

        list.SelectionChanged += (_, _) => remove.IsEnabled = list.SelectedItem is not null;
        remove.Click += (_, _) =>
        {
            if ((list.SelectedItem as ListViewItem)?.Tag is string folder) settings.RemoveWatchedFolder(folder);
            Fill();
        };
        add.Click += async (_, _) =>
        {
            if (owner is null) return;
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            if (await picker.PickSingleFolderAsync() is { } chosen) settings.AddWatchedFolder(chosen.Path);
            Fill();
        };
        Fill();

        var pads = new StackPanel { Spacing = 12 };
        pads.Children.Add(add);
        pads.Children.Add(remove);
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(pads, 1);
        row.Children.Add(new Display { Tone = DisplayTone.Amber, Padding = new Thickness(6), Content = list });
        row.Children.Add(pads);

        var section = new StackPanel { Spacing = 8 };
        section.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = "Watched folders: their media joins the Library" });
        section.Children.Add(row);
        return section;
    }

    static Pad HelpPad(string text, Action open)
    {
        var pad = new Pad { Content = text };
        pad.Click += (_, _) => open();
        return pad;
    }

    static PushSwitch Switch(string label, bool on, Action<bool> changed)
    {
        var button = new PushSwitch { Content = label, IsChecked = on };
        button.Checked += (_, _) => changed(true);
        button.Unchecked += (_, _) => changed(false);
        return button;
    }
}
