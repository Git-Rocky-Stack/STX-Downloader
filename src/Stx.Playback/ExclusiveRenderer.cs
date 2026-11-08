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
    Run? _run;
    int _runs;
    Thread? _thread;
    volatile bool _paused;
    long _framesWritten;
    int _disposed;

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

    /// <summary>Which <see cref="Play"/> is current: each one is a new run. Zero before the first.</summary>
    public int CurrentRun => Volatile.Read(ref _runs);

    /// <summary>Raised once the stream runs dry and the last frame has been handed over, with the run that ended.
    /// Raised off the pump thread; a handler compares the run with <see cref="CurrentRun"/>, since a seek may have
    /// started a new one in between.</summary>
    public event EventHandler<int>? Finished;

    /// <summary>Raised when the device or the stream fails mid-play: a DAC unplugged, the endpoint invalidated, the
    /// decoder gone. Raised off the pump thread, which has stopped. Without it, the throw would end the whole app.</summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>
    /// Opens the default device exclusively with the first candidate layout it takes at <paramref name="rate"/>.
    /// Throws <see cref="ExclusiveUnavailableException"/> when none fits, another app holds it, or Windows forbids it.
    /// </summary>
    public static ExclusiveRenderer Open(int rate, int channels, IReadOnlyList<DeviceFormat> candidates)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        try
        {
            foreach (var layout in candidates)
            {
                var format = new PcmFormat(rate, channels, layout);
                if (TryInitialize(device, format, rate) is not { } client) continue;
                try { return new ExclusiveRenderer(device, client, layout, rate, channels); }
                catch { client.Dispose(); throw; }
            }
        }
        catch
        {
            // Independent review, finding 11: every failure but two known HRESULTs used to leave the device (and a
            // realigned client) for the finalizer. Whatever ends the open now gives them back at once.
            device.Dispose();
            throw;
        }
        device.Dispose();
        throw new ExclusiveUnavailableException($"SHARED: THE DEVICE CAN'T TAKE {rate / 1000.0:0.#} KHZ AS IT IS");
    }

    // An AudioClient initialized exclusively at the format, or null when the device doesn't take it. The client is
    // disposed on every path that doesn't return it.
    static AudioClient? TryInitialize(MMDevice device, PcmFormat format, int rate)
    {
        var probe = device.AudioClient;
        try
        {
            if (!probe.IsFormatSupported(AudioClientShareMode.Exclusive, format)) { probe.Dispose(); return null; }
            long period = probe.DefaultDevicePeriod;
            try
            {
                probe.Initialize(AudioClientShareMode.Exclusive, AudioClientStreamFlags.EventCallback, period, period, format, Guid.Empty);
                return probe;
            }
            catch (COMException e) when (e.HResult == AudclntEBufferSizeNotAligned)
            {
                // Windows' own remedy: ask how many frames it would take, and ask again for exactly that.
                int frames = probe.BufferSize;
                probe.Dispose();
                probe = device.AudioClient;
                long alignedPeriod = (long)(10_000_000.0 * frames / rate + 0.5);
                probe.Initialize(AudioClientShareMode.Exclusive, AudioClientStreamFlags.EventCallback, alignedPeriod, alignedPeriod, format, Guid.Empty);
                return probe;
            }
        }
        catch (COMException e) when (e.HResult is AudclntEDeviceInUse or AudclntEExclusiveModeNotAllowed)
        {
            probe.Dispose();
            throw Unavailable(e.HResult);
        }
        catch
        {
            probe.Dispose();
            throw;
        }
    }

    static ExclusiveUnavailableException Unavailable(int hresult) => new(hresult == AudclntEDeviceInUse
        ? "SHARED: ANOTHER APP HAS THE DEVICE TO ITSELF"
        : "SHARED: WINDOWS DOESN'T ALLOW EXCLUSIVE MODE ON THIS DEVICE");

    /// <summary>Plays from <paramref name="source"/>, replacing anything playing. The stream is owned and closed by the caller.</summary>
    public void Play(Stream source)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        StopThread();
        var run = new Run(Interlocked.Increment(ref _runs), source, new byte[_bufferFrames * _frameBytes]);
        _run = run;
        _paused = false;
        Interlocked.Exchange(ref _framesWritten, 0);
        _client.Reset();
        // StopThread signalled the event to wake the old pump. Left set, the new pump would fill a buffer the device
        // hasn't asked for yet, right after the pre-fill below has filled it.
        _ready.Reset();
        // The first buffer is filled before starting, as exclusive event mode expects.
        Fill(run);
        _thread = new Thread(() => Pump(run)) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "STX bit-perfect" };
        _thread.Start();
        _client.Start();
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        _paused = true;
        _client.Stop();
    }

    public void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        _paused = false;
        _client.Start();
    }

    /// <summary>How long the device may go without asking for sound while playing before STX says it stopped.</summary>
    static readonly TimeSpan Stalled = TimeSpan.FromSeconds(2);

    void Pump(Run run)
    {
        bool ended = false;
        try
        {
            while (!run.Stopping)
            {
                if (!_ready.WaitOne(Stalled))
                {
                    if (_paused || run.Stopping) continue;
                    // Review 2026-11-07: this used to end the pump quietly, leaving the plate on PLAYING with the time
                    // frozen (a USB DAC that hangs does this). Now the plate says the device stopped.
                    throw new TimeoutException("IT HASN'T ASKED FOR SOUND IN 2 SECONDS");
                }
                if (run.Stopping || _paused) continue;
                if (ended) break;
                ended = !Fill(run);
            }
        }
        catch (Exception e)
        {
            // Every failure becomes Faulted. One thrown on this thread would end the whole app.
            if (!run.Stopping) Raise(run, () => Faulted?.Invoke(this, e));
            return;
        }
        if (ended && !run.Stopping) Raise(run, () => Finished?.Invoke(this, run.Id));
    }

    // Off the pump thread, so a handler that takes a lock the stopping thread holds can't keep this thread from ending.
    // A run halted meanwhile says nothing.
    static void Raise(Run run, Action raise) => ThreadPool.QueueUserWorkItem(_ => { if (!run.Stopping) raise(); });

    /// <summary>Stops handing frames to the device and waits for the pump to end, so the stream can be closed safely.</summary>
    public void Halt()
    {
        if (_disposed == 0) StopThread();
    }

    // One period's worth. Exclusive event mode always takes the whole buffer. Short reads are padded with silence,
    // which ends the stream. The source is read before the device's buffer is taken, so a pump stuck in a read from a
    // stalled share holds nothing of the device's; once halted, it never touches the device again (review 2026-11-07:
    // a pump that outlived Halt's wait used to carry on beside the next one).
    bool Fill(Run run)
    {
        int frames = _bufferFrames;
        int bytes = frames * _frameBytes;
        var chunk = run.Chunk;
        int read = 0, n;
        while (read < bytes && !run.Stopping && (n = run.Source.Read(chunk, read, bytes - read)) > 0) read += n;
        if (run.Stopping) return false;
        read -= read % _frameBytes;
        Array.Clear(chunk, read, bytes - read);
        IntPtr buffer = _render.GetBuffer(frames);
        Marshal.Copy(chunk, 0, buffer, bytes);
        _render.ReleaseBuffer(frames, read == 0 ? AudioClientBufferFlags.Silent : AudioClientBufferFlags.None);
        Interlocked.Add(ref _framesWritten, read / _frameBytes);
        return read == bytes;
    }

    void StopThread()
    {
        if (_run is { } run) run.Stopping = true;
        _ready.Set();
        // A pump stuck in a read can outlive this wait; it has stopped, and touches nothing more (see Fill).
        if (_thread is { } t && t != Thread.CurrentThread) t.Join(3000);
        _thread = null;
        try { _client.Stop(); } catch (COMException) { }
    }

    // One Play: its stream, its own buffer (a pump left behind in a read must not write into the next one's), and
    // whether it has been told to stop.
    sealed class Run(int id, Stream source, byte[] chunk)
    {
        public int Id { get; } = id;
        public Stream Source { get; } = source;
        public byte[] Chunk { get; } = chunk;
        volatile bool _stopping;
        public bool Stopping { get => _stopping; set => _stopping = value; }
    }

    /// <summary>Lets the device go. Safe to call more than once; only the first call does anything.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        StopThread();
        _render.Dispose();
        _client.Dispose();
        _device.Dispose();
        _ready.Dispose();
    }
}
