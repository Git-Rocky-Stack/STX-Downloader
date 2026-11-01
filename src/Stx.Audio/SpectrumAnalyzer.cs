using System.Runtime.InteropServices;

namespace Stx.Audio;

public readonly record struct SpectrumBand(double Hz, double Db);

public readonly record struct EnvelopePoint(double LeftDb, double RightDb);

public sealed record SpectrumResult(
    IReadOnlyList<SpectrumBand> Spectrum,
    IReadOnlyList<EnvelopePoint> Envelope,
    double? RollOffHz,
    double PeakHz,
    double BinHz,
    double PeakLeftDb,
    double PeakRightDb);

/// <summary>Measures decoded audio, read as interleaved 32-bit float. Everything comes from the samples: nothing is estimated from the format.</summary>
public static class SpectrumAnalyzer
{
    public const int Bands = 96;
    public const int EnvelopePoints = 200;

    const int WindowSize = 4096;
    const int Hop = WindowSize / 2;
    const double LowestHz = 20;
    const double SilencePower = 1e-14;
    const double FloorDb = -200;
    const double SmoothingHz = 200;
    /// <summary>A lossy encoder's cutoff is a cliff: at least this far down within this span.</summary>
    const double CliffDb = 18;
    const double CliffSpanHz = 1000;
    const double ReferenceHz = 1500;
    const double KneeDb = 12;

    public static SpectrumResult Analyze(Stream pcm, int sampleRate, int channels = 1)
    {
        var hann = new double[WindowSize];
        for (int i = 0; i < WindowSize; i++) hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (WindowSize - 1));

        var power = new double[WindowSize / 2];
        var window = new double[WindowSize];
        var re = new double[WindowSize];
        var im = new double[WindowSize];
        int filled = 0, windows = 0, sinceEnvelope = 0;
        int envelopeBlock = Math.Max(1, sampleRate / 20);
        float peakLeft = 0, peakRight = 0, blockLeft = 0, blockRight = 0;
        var blocks = new List<EnvelopePoint>();

        var buffer = new byte[64 * 1024 - 64 * 1024 % (4 * channels)];
        int held = 0;
        int read;
        while ((read = pcm.Read(buffer, held, buffer.Length - held)) > 0)
        {
            int usable = (held + read) / (4 * channels) * (4 * channels);
            var samples = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, usable));
            for (int i = 0; i < samples.Length; i += channels)
            {
                float left = samples[i], right = channels > 1 ? samples[i + 1] : left;
                float absLeft = Math.Abs(left), absRight = Math.Abs(right);
                peakLeft = Math.Max(peakLeft, absLeft);
                peakRight = Math.Max(peakRight, absRight);
                blockLeft = Math.Max(blockLeft, absLeft);
                blockRight = Math.Max(blockRight, absRight);
                if (++sinceEnvelope == envelopeBlock)
                {
                    blocks.Add(new EnvelopePoint(ToDb(blockLeft), ToDb(blockRight)));
                    blockLeft = blockRight = 0;
                    sinceEnvelope = 0;
                }

                window[filled++] = (left + right) / 2;
                if (filled == WindowSize)
                {
                    AddWindow(window, hann, re, im, power);
                    windows++;
                    Array.Copy(window, Hop, window, 0, WindowSize - Hop);
                    filled = WindowSize - Hop;
                }
            }
            held = held + read - usable;
            if (held > 0) Array.Copy(buffer, usable, buffer, 0, held);
        }
        if (sinceEnvelope > 0) blocks.Add(new EnvelopePoint(ToDb(blockLeft), ToDb(blockRight)));

        double binHz = (double)sampleRate / WindowSize;
        if (windows > 0)
            for (int i = 0; i < power.Length; i++) power[i] /= windows;

        return new SpectrumResult(
            windows > 0 ? ToBands(power, binHz, sampleRate) : [],
            Resample(blocks),
            RollOff(power, binHz),
            Enumerable.Range(0, power.Length).MaxBy(i => power[i]) * binHz,
            binHz,
            ToDb(peakLeft),
            ToDb(peakRight));
    }

    static void AddWindow(double[] window, double[] hann, double[] re, double[] im, double[] power)
    {
        for (int i = 0; i < WindowSize; i++)
        {
            re[i] = window[i] * hann[i];
            im[i] = 0;
        }
        Fft.Transform(re, im);
        for (int i = 0; i < power.Length; i++) power[i] += re[i] * re[i] + im[i] * im[i];
    }

    static double? RollOff(double[] power, double binHz)
    {
        if (power.Max() < SilencePower) return null;

        var db = power.Select(p => p > 0 ? Math.Max(FloorDb, 10 * Math.Log10(p)) : FloorDb).ToArray();
        var smooth = Smooth(db, Math.Max(1, (int)(SmoothingHz / binHz)));

        // A cutoff is a cliff: the level falls hard within a short span. A natural slope never does, and a file
        // with no cliff reports the top of its band, so a clean file is never accused of a cutoff it doesn't have.
        int span = Math.Max(1, (int)(CliffSpanHz / binHz));
        int foot = -1;
        for (int i = smooth.Length - 1 - span; i >= 0 && foot < 0; i--)
            if (smooth[i] - smooth[i + span] >= CliffDb && smooth[i] > FloorDb + CliffDb) foot = i + span;
        if (foot < 0) return (smooth.Length - 1) * binHz;

        // Its top is where the level leaves the passband just below it.
        int below = Math.Max(1, (int)(ReferenceHz / binHz));
        var reference = smooth[Math.Max(0, foot - 2 * below)..Math.Max(1, foot - below)];
        double passband = reference.Order().ElementAt(reference.Length / 2);
        for (int i = foot; i >= 0; i--)
            if (smooth[i] >= passband - KneeDb) return i * binHz;
        return foot * binHz;
    }

    /// <summary>Centered, so smoothing can't shift a frequency up or down.</summary>
    static double[] Smooth(double[] db, int width)
    {
        var smooth = new double[db.Length];
        for (int i = 0; i < db.Length; i++)
        {
            int from = Math.Max(0, i - width / 2), to = Math.Min(db.Length, i + width / 2 + 1);
            double sum = 0;
            for (int k = from; k < to; k++) sum += db[k];
            smooth[i] = sum / (to - from);
        }
        return smooth;
    }

    static List<SpectrumBand> ToBands(double[] power, double binHz, int sampleRate)
    {
        var bands = new List<SpectrumBand>(Bands);
        double top = sampleRate / 2.0, ratio = Math.Pow(top / LowestHz, 1.0 / Bands);
        double low = LowestHz;
        for (int band = 0; band < Bands; band++)
        {
            double high = low * ratio;
            int from = Math.Clamp((int)(low / binHz), 0, power.Length - 1);
            int to = Math.Clamp((int)(high / binHz), from + 1, power.Length);
            double level = 0;
            for (int i = from; i < to; i++) level = Math.Max(level, power[i]);
            bands.Add(new SpectrumBand(Math.Sqrt(low * high), level > 0 ? Math.Max(FloorDb, 10 * Math.Log10(level)) : FloorDb));
            low = high;
        }
        return bands;
    }

    /// <summary>A fixed number of points, whatever the length. Each keeps the loudest moment it covers.</summary>
    static IReadOnlyList<EnvelopePoint> Resample(List<EnvelopePoint> blocks)
    {
        if (blocks.Count == 0) return [];
        var points = new EnvelopePoint[EnvelopePoints];
        for (int p = 0; p < EnvelopePoints; p++)
        {
            int from = p * blocks.Count / EnvelopePoints;
            int to = Math.Max(from + 1, (p + 1) * blocks.Count / EnvelopePoints);
            double left = double.NegativeInfinity, right = double.NegativeInfinity;
            for (int i = from; i < to; i++)
            {
                left = Math.Max(left, blocks[i].LeftDb);
                right = Math.Max(right, blocks[i].RightDb);
            }
            points[p] = new EnvelopePoint(left, right);
        }
        return points;
    }

    static double ToDb(float linear) => linear > 0 ? 20 * Math.Log10(linear) : double.NegativeInfinity;
}
