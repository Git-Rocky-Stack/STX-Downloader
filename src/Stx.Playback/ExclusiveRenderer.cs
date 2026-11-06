using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Stx.Core.Playback;

namespace Stx.Playback;

/// <summary>Why the device couldn't be had exclusively, in the readout's words.</summary>
public sealed class ExclusiveUnavailableException(string why) : Exception(why);

/// <summary>
/// A WAVEFORMATEXTENSIBLE with its valid bits set apart from the container (24 in 32, say). NAudio's own
/// WaveFormatExtensible ties the two together, so it can't describe 24-in-32.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
sealed class PcmFormat : WaveFormat
{
    readonly short _validBits;
    readonly int _channelMask;
    readonly Guid _subFormat = new("00000001-0000-0010-8000-00aa00389b71"); // KSDATAFORMAT_SUBTYPE_PCM

    public PcmFormat(int rate, int channels, DeviceFormat layout)
    {
        waveFormatTag = WaveFormatEncoding.Extensible;
        this.channels = (short)channels;
        sampleRate = rate;
        bitsPerSample = (short)layout.Container;
        blockAlign = (short)(channels * layout.Container / 8);
        averageBytesPerSecond = rate * blockAlign;
        extraSize = 22;
        _validBits = (short)layout.Valid;
        _channelMask = channels switch { 1 => 0x4, 2 => 0x3, 3 => 0x7, 4 => 0x33, 5 => 0x37, 6 => 0x3F, 7 => 0x13F, 8 => 0x63F, _ => 0 };
    }
}

/// <summary>
/// Holds the default output device exclusively, at exactly the layout and rate it's given, and plays raw PCM pulled
/// from a stream. Windows' mixer, resampler and volume aren't in the path, so the samples reach the device as they
/// are. Event-driven, one buffer period at a time.
/// </summary>
public sealed class ExclusiveRenderer : IDisposable
{
    const int AudclntEBufferSizeNotAligned = unchecked((int)0x88890019);
    const int AudclntEDeviceInUse = unchecked((int)0x8889000A);
    const int AudclntEExclusiveModeNotAllowed = unchecked((int)0x8889000E);

    readonly MMDevice _device;
    readonly AudioClient _client;
    readonly AudioRenderClient _render;
    readonly EventWaitHandle _ready = new(false, EventResetMode.AutoReset);
    readonly int _frameBytes;
    readonly int _bufferFrames;
    Stream? _source;
    Thread? _thread;
    volatile bool _stopping;
    volatile bool _paused;
    long _framesWritten;

    ExclusiveRenderer(MMDevice device, AudioClient client, DeviceFormat layout, int rate, int channels)
    {
        _device = device;
        _client = client;
        Layout = layout;
        Rate = rate;
        Channels = channels;
        _frameBytes = channels * layout.Container / 8;
        _client.SetEventHandle(_ready.SafeWaitHandle.DangerousGetHandle());
        _bufferFrames = _client.BufferSize;
        _render = _client.AudioRenderClient;
    }

    public DeviceFormat Layout { get; }
    public int Rate { get; }
    public int Channels { get; }
    public string DeviceName => _device.FriendlyName;

    /// <summary>Frames the device has been handed since the last <see cref="Play"/>.</summary>
    public long FramesWritten => Interlocked.Read(ref _framesWritten);

    /// <summary>How long a frame waits in the device's buffer: what a picture shown with this sound must allow for.</summary>
    public TimeSpan Latency => TimeSpan.FromSeconds((double)_bufferFrames / Rate);

    /// <summary>Raised once the stream runs dry and the last frame has been handed over. Raised off the pump thread.</summary>
    public event EventHandler? Finished;

    /// <summary>Raised when the device or the stream fails mid-play: a DAC unplugged, the endpoint invalidated, the
    /// decoder gone. Raised off the pump thread, which has stopped. Without it, the throw would end the whole app.</summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>
    /// Opens the default device exclusively with the first candidate layout it takes at <paramref name="rate"/>.
    /// Throws <see cref="ExclusiveUnavailableException"/> when none fits, another app holds it, or Windows forbids it.
    /// </summary>
    public static ExclusiveRenderer Open(int rate, int channels, IReadOnlyList<DeviceFormat> candidates)
    {
        var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        foreach (var layout in candidates)
        {
            var format = new PcmFormat(rate, channels, layout);
            var probe = device.AudioClient;
            bool supported;
            try { supported = probe.IsFormatSupported(AudioClientShareMode.Exclusive, format); }
            catch (COMException e) when (e.HResult is AudclntEDeviceInUse or AudclntEExclusiveModeNotAllowed) { probe.Dispose(); throw Unavailable(e.HResult); }
            if (!supported) { probe.Dispose(); continue; }

            long period = probe.DefaultDevicePeriod;
            try
            {
                probe.Initialize(AudioClientShareMode.Exclusive, AudioClientStreamFlags.EventCallback, period, period, format, Guid.Empty);
                return new ExclusiveRenderer(device, probe, layout, rate, channels);
            }
            catch (COMException e) when (e.HResult == AudclntEBufferSizeNotAligned)
            {
                // Windows' own remedy: ask how many frames it would take, and ask again for exactly that.
                int frames = probe.BufferSize;
                probe.Dispose();
                var aligned = device.AudioClient;
                long alignedPeriod = (long)(10_000_000.0 * frames / rate + 0.5);
                aligned.Initialize(AudioClientShareMode.Exclusive, AudioClientStreamFlags.EventCallback, alignedPeriod, alignedPeriod, format, Guid.Empty);
                return new ExclusiveRenderer(device, aligned, layout, rate, channels);
            }
            catch (COMException e) when (e.HResult is AudclntEDeviceInUse or AudclntEExclusiveModeNotAllowed)
            {
                probe.Dispose();
                throw Unavailable(e.HResult);
            }
        }
        throw new ExclusiveUnavailableException($"SHARED: THE DEVICE CAN'T TAKE {rate / 1000.0:0.#} KHZ AS IT IS");
    }

    static ExclusiveUnavailableException Unavailable(int hresult) => new(hresult == AudclntEDeviceInUse
        ? "SHARED: ANOTHER APP HAS THE DEVICE TO ITSELF"
        : "SHARED: WINDOWS DOESN'T ALLOW EXCLUSIVE MODE ON THIS DEVICE");

    /// <summary>Plays from <paramref name="source"/>, replacing anything playing. The stream is owned and closed by the caller.</summary>
    public void Play(Stream source)
    {
        StopThread();
        _source = source;
        _stopping = false;
        _paused = false;
        Interlocked.Exchange(ref _framesWritten, 0);
        _client.Reset();
        // StopThread signalled the event to wake the old pump. Left set, the new pump would fill a buffer the device
        // hasn't asked for yet, right after the pre-fill below has filled it.
        _ready.Reset();
        // The first buffer is filled before starting, as exclusive event mode expects.
        Fill(_bufferFrames);
        _thread = new Thread(Pump) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "STX bit-perfect" };
        _thread.Start();
        _client.Start();
    }

    public void Pause()
    {
        _paused = true;
        _client.Stop();
    }

    public void Resume()
    {
        _paused = false;
        _client.Start();
    }

    void Pump()
    {
        bool ended = false;
        try
        {
            while (!_stopping)
            {
                if (!_ready.WaitOne(2000)) { if (_paused) continue; break; }
                if (_stopping || _paused) continue;
                if (ended) break;
                ended = !Fill(_bufferFrames);
            }
        }
        catch (Exception e) when (e is COMException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            if (!_stopping) Raise(() => Faulted?.Invoke(this, e));
            return;
        }
        if (ended && !_stopping) Raise(() => Finished?.Invoke(this, EventArgs.Empty));
    }

    // Off the pump thread, so a handler that takes a lock the stopping thread holds can't keep this thread from ending.
    static void Raise(Action raise) => ThreadPool.QueueUserWorkItem(_ => raise());

    /// <summary>Stops handing frames to the device and waits for the pump to end, so the stream can be closed safely.</summary>
    public void Halt() => StopThread();

    // One period's worth. Exclusive event mode always takes the whole buffer. Short reads are padded with silence,
    // which ends the stream.
    bool Fill(int frames)
    {
        IntPtr buffer = _render.GetBuffer(frames);
        int bytes = frames * _frameBytes;
        var chunk = new byte[bytes];
        int read = 0, n;
        while (read < bytes && _source is { } source && (n = source.Read(chunk, read, bytes - read)) > 0) read += n;
        read -= read % _frameBytes;
        Marshal.Copy(chunk, 0, buffer, bytes);
        _render.ReleaseBuffer(frames, read == 0 ? AudioClientBufferFlags.Silent : AudioClientBufferFlags.None);
        Interlocked.Add(ref _framesWritten, read / _frameBytes);
        return read == bytes;
    }

    void StopThread()
    {
        _stopping = true;
        _ready.Set();
        if (_thread is { } t && t != Thread.CurrentThread) t.Join(3000);
        _thread = null;
        try { _client.Stop(); } catch (COMException) { }
    }

    public void Dispose()
    {
        StopThread();
        _render.Dispose();
        _client.Dispose();
        _device.Dispose();
        _ready.Dispose();
    }
}
