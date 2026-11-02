using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stx.App.Primitives;
using Stx.Audio;
using Stx.Look;
using Stx.ViewModels;

namespace Stx.App.Panels;

/// <summary>What's really in the file: measured facts, the real spectrum, real meters, and an honest verdict on each output.</summary>
public sealed partial class SourceTruthPanel : ContentControl
{
    readonly SourceTruthViewModel _truth;
    readonly GlowText _source = new() { Tone = DisplayTone.Amber }, _verdict = new() { Tone = DisplayTone.Amber }, _rollOff = new() { Tone = DisplayTone.Amber, Dimmed = true }, _loudness = new() { Tone = DisplayTone.Amber, Dimmed = true };
    readonly SpectrumDisplay _spectrum = new() { Height = 112 };
    readonly Bargraph _left = new() { Zones = BargraphZones.Vu }, _right = new() { Zones = BargraphZones.Vu };
    readonly Rotary _output = new() { Label = "Output format", Items = SourceTruthViewModel.OutputLabels };
    readonly Pad _convert = new() { Content = "CONVERT", HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock _warning = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = 13 };
    readonly StateView _empty = new() { State = ViewState.Empty, Message = "SELECT A SAVED ITEM" };
    readonly Display _facts;
    /// <summary>Runs only while a sweep plays. At rest, nothing here does any work.</summary>
    public DispatcherTimer SweepTimer => _sweep;

    readonly DispatcherTimer _sweep = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / MeterTimeline.FramesPerSecond) };

    public SourceTruthPanel(SourceTruthViewModel truth, Action? convert = null)
    {
        _truth = truth;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(_spectrum, "Measured spectrum");

        var lines = new StackPanel();
        foreach (var line in new[] { _source, _verdict, _rollOff, _loudness }) lines.Children.Add(line);
        _facts = new Display { Tone = DisplayTone.Amber, Height = 104, Padding = new Thickness(14, 8, 14, 8), Content = lines };

        var meters = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        meters.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = "Left" });
        meters.Children.Add(_left);
        meters.Children.Add(new EngravedLabel { Kind = LabelKind.Label, Text = "Right" });
        meters.Children.Add(_right);
        meters.Children.Add(_warning);
        meters.Children.Add(_convert);

        var controls = new Grid { ColumnSpacing = 24 };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(meters, 1);
        controls.Children.Add(_output);
        controls.Children.Add(meters);

        var column = new StackPanel { Spacing = 12 };
        column.Children.Add(new EngravedLabel { Kind = LabelKind.Title, Text = "Source truth" });
        column.Children.Add(_empty);
        column.Children.Add(_facts);
        column.Children.Add(_spectrum);
        column.Children.Add(controls);
        Content = new Faceplate { PanelName = "source-truth", Padding = new Thickness(36, 28, 36, 28), Content = column };

        _output.SelectionChanged += (_, index) => truth.SelectedOutputIndex = index;
        _convert.Click += (_, _) => convert?.Invoke();
        _sweep.Tick += (_, _) =>
        {
            truth.AdvanceSweep();
            if (!truth.IsSweeping) _sweep.Stop();
        };
        truth.PropertyChanged += (_, _) => Paint();
        Loaded += (_, _) => { LookState.Changed += PaintInk; PaintInk(); };
        Unloaded += (_, _) => { LookState.Changed -= PaintInk; _sweep.Stop(); };
        PaintInk();
        Paint();
    }

    void PaintInk() => _warning.Foreground = LookBrushes.From(LookTokens.For(LookState.Mode)["Ink"]);

    void Paint()
    {
        _empty.Visibility = _truth.HasContent ? Visibility.Collapsed : Visibility.Visible;
        _facts.Visibility = _truth.HasContent ? Visibility.Visible : Visibility.Collapsed;
        _source.Text = _truth.SourceLine;
        _verdict.Text = _truth.VerdictLine;
        _rollOff.Text = _truth.RollOffLine;
        _loudness.Text = _truth.LoudnessLine;
        _spectrum.SetData(_truth.Spectrum.Select(b => b.Hz).ToList(), _truth.Spectrum.Select(b => b.Db).ToList(), _truth.RollOffHz);

        var meters = _truth.Meters;
        _left.Value = meters.Left;
        _left.Peak = meters.PeakLeft;
        _right.Value = meters.Right;
        _right.Peak = meters.PeakRight;

        _warning.Text = _truth.Warning;
        _warning.Visibility = _truth.Warning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _convert.IsEnabled = _truth.CanConvert;
        _output.SelectedIndex = _truth.SelectedOutputIndex;
        if (_truth.IsSweeping && !_sweep.IsEnabled) _sweep.Start();
    }
}
