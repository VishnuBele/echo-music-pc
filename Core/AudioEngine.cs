using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace EchoMusic.Core;

internal sealed class Win32WaveOut16Player : IDisposable
{
    private const uint WAVE_MAPPER = 0xFFFFFFFFu;
    private const ushort WAVE_FORMAT_PCM = 1;
    private const uint CALLBACK_EVENT = 0x00050000u;
    private const uint WHDR_DONE = 0x00000001u;
    private const uint WHDR_PREPARED = 0x00000002u;

    private const int BufferCount = 4;
    private const int FramesPerBuffer = 4800; // 100ms @ 48,000 Hz (400ms total queue - zero micro-underrun buzz!)
    private const int SamplesPerBuffer = FramesPerBuffer * EchoMasterMixer.Channels;
    private const int BytesPerBuffer = SamplesPerBuffer * sizeof(short);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct WAVEOUTCAPSW
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
        public uint dwSupport;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr phwo, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, int cbwh);
    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hwo);
    [DllImport("winmm.dll")]
    private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);
    [DllImport("winmm.dll")]
    private static extern uint waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int waveOutGetDevCapsW(IntPtr uDeviceID, out WAVEOUTCAPSW pwoc, int cbwoc);

    private readonly ISampleProvider _source;
    private readonly AutoResetEvent _callbackEvent = new(false);
    private readonly IntPtr[] _hdrPtrs = new IntPtr[BufferCount];
    private readonly IntPtr[] _dataPtrs = new IntPtr[BufferCount];
    private readonly bool[] _queued = new bool[BufferCount];
    private readonly object _queueLock = new();
    private IntPtr _hWaveOut;
    private Thread? _pumpThread;
    private volatile bool _running;
    private long _blocksSubmitted;

    public bool IsHealthy => _running && _hWaveOut != IntPtr.Zero;
    public long BlocksSubmitted => Interlocked.Read(ref _blocksSubmitted);

    public static List<(int Id, string Name)> EnumerateDevices()
    {
        var list = new List<(int Id, string Name)> { (-1, "Windows Default Output (Sound Mapper)") };
        if (!OperatingSystem.IsWindows()) return list;
        try
        {
            uint n = waveOutGetNumDevs();
            int cb = Marshal.SizeOf<WAVEOUTCAPSW>();
            for (int i = 0; i < (int)n; i++)
            {
                if (waveOutGetDevCapsW((IntPtr)i, out var caps, cb) == 0 && !string.IsNullOrWhiteSpace(caps.szPname))
                    list.Add((i, caps.szPname.Trim()));
            }
        }
        catch { }
        return list;
    }

    public Win32WaveOut16Player(ISampleProvider source, int deviceId = -1)
    {
        _source = source;
        var wfx = new WAVEFORMATEX
        {
            wFormatTag = WAVE_FORMAT_PCM,
            nChannels = EchoMasterMixer.Channels,
            nSamplesPerSec = EchoMasterMixer.SampleRate,
            wBitsPerSample = 16,
            nBlockAlign = EchoMasterMixer.Channels * sizeof(short),
            nAvgBytesPerSec = EchoMasterMixer.SampleRate * EchoMasterMixer.Channels * sizeof(short),
            cbSize = 0,
        };

        uint uDev = deviceId < 0 ? WAVE_MAPPER : (uint)deviceId;
        int rc = waveOutOpen(out _hWaveOut, uDev, ref wfx, _callbackEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
        if (rc != 0 && uDev != WAVE_MAPPER)
            rc = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref wfx, _callbackEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);

        if (rc != 0 || _hWaveOut == IntPtr.Zero)
            throw new InvalidOperationException($"waveOutOpen failed with MMSYSERR {rc}");

        try { waveOutSetVolume(_hWaveOut, 0xFFFFFFFFu); } catch { }

        int hdrSize = Marshal.SizeOf<WAVEHDR>();
        for (int i = 0; i < BufferCount; i++)
        {
            _dataPtrs[i] = Marshal.AllocHGlobal(BytesPerBuffer);
            _hdrPtrs[i] = Marshal.AllocHGlobal(hdrSize);
            var hdr = new WAVEHDR { lpData = _dataPtrs[i], dwBufferLength = BytesPerBuffer };
            Marshal.StructureToPtr(hdr, _hdrPtrs[i], false);
        }

        _running = true;
        _pumpThread = new Thread(AudioPumpLoop)
        {
            Name = "EchoWin32AudioPump",
            IsBackground = true,
            Priority = ThreadPriority.Highest,
        };
        _pumpThread.Start();
    }

    private void AudioPumpLoop()
    {
        var floatBuf = new float[SamplesPerBuffer];
        var shortBuf = new short[SamplesPerBuffer];
        int hdrSize = Marshal.SizeOf<WAVEHDR>();
        int flagsOffset = (int)Marshal.OffsetOf<WAVEHDR>(nameof(WAVEHDR.dwFlags));
        int consecutiveErrors = 0;
        uint ditherState = 0x9E3779B9u;

        while (_running)
        {
            bool wroteAny = false;
            lock (_queueLock)
            {
                for (int i = 0; i < BufferCount && _running; i++)
                {
                    if (_queued[i])
                    {
                        uint flags = (uint)Marshal.ReadInt32(_hdrPtrs[i], flagsOffset);
                        if ((flags & WHDR_DONE) == 0)
                            continue;

                        // Strictly follow Microsoft Win32 wdmaud contract: Unprepare completed header before refilling!
                        try { waveOutUnprepareHeader(_hWaveOut, _hdrPtrs[i], hdrSize); } catch { }
                        _queued[i] = false;
                    }

                    int got = 0;
                    try { got = _source.Read(floatBuf, 0, SamplesPerBuffer); } catch { got = 0; }
                    for (int sample = 0; sample < SamplesPerBuffer; sample++)
                    {
                        float v = sample < got ? floatBuf[sample] : 0f;
                        if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;

                        // Fast Xorshift32 TPDF dither (±0.5 LSB) eliminates 16-bit quantization distortion
                        ditherState ^= ditherState << 13;
                        ditherState ^= ditherState >> 17;
                        ditherState ^= ditherState << 5;
                        float r1 = (ditherState & 0xFFFFu) * (1.0f / 65536.0f);
                        float r2 = ((ditherState >> 16) & 0xFFFFu) * (1.0f / 65536.0f);
                        float dither = (r1 - r2) * 0.5f;

                        int scaled = (int)MathF.Round(v * 32766f + dither);
                        shortBuf[sample] = (short)(scaled > 32767 ? 32767 : (scaled < -32768 ? -32768 : scaled));
                    }

                    Marshal.Copy(shortBuf, 0, _dataPtrs[i], SamplesPerBuffer);
                    var freshHdr = new WAVEHDR { lpData = _dataPtrs[i], dwBufferLength = BytesPerBuffer };
                    Marshal.StructureToPtr(freshHdr, _hdrPtrs[i], false);

                    int prc = waveOutPrepareHeader(_hWaveOut, _hdrPtrs[i], hdrSize);
                    int wrc = prc == 0 ? waveOutWrite(_hWaveOut, _hdrPtrs[i], hdrSize) : prc;
                    if (wrc == 0)
                    {
                        _queued[i] = true;
                        wroteAny = true;
                        consecutiveErrors = 0;
                        Interlocked.Increment(ref _blocksSubmitted);
                    }
                    else
                    {
                        _queued[i] = false;
                        if (++consecutiveErrors > 25)
                        {
                            _running = false;
                            return;
                        }
                    }
                }
            }

            if (!wroteAny && _running)
                _callbackEvent.WaitOne(25);
        }
    }

    /// <summary>Immediately discard already-submitted audio when pausing or switching tracks.</summary>
    public void FlushQueuedAudio()
    {
        lock (_queueLock)
        {
            if (_hWaveOut == IntPtr.Zero) return;
            try { waveOutReset(_hWaveOut); } catch { }
            int hdrSize = Marshal.SizeOf<WAVEHDR>();
            for (int i = 0; i < BufferCount; i++)
            {
                if (_hdrPtrs[i] != IntPtr.Zero)
                    try { waveOutUnprepareHeader(_hWaveOut, _hdrPtrs[i], hdrSize); } catch { }
                _queued[i] = false;
            }
        }
        try { _callbackEvent.Set(); } catch { }
    }

    public void Dispose()
    {
        _running = false;
        try { _callbackEvent.Set(); } catch { }
        try { _pumpThread?.Join(350); } catch { }

        if (_hWaveOut != IntPtr.Zero)
        {
            int hdrSize = Marshal.SizeOf<WAVEHDR>();
            try { waveOutReset(_hWaveOut); } catch { }
            for (int i = 0; i < BufferCount; i++)
            {
                if (_hdrPtrs[i] != IntPtr.Zero)
                {
                    try { waveOutUnprepareHeader(_hWaveOut, _hdrPtrs[i], hdrSize); } catch { }
                }
            }
            try { waveOutClose(_hWaveOut); } catch { }
            _hWaveOut = IntPtr.Zero;
        }

        for (int i = 0; i < BufferCount; i++)
        {
            if (_hdrPtrs[i] != IntPtr.Zero) { Marshal.FreeHGlobal(_hdrPtrs[i]); _hdrPtrs[i] = IntPtr.Zero; }
            if (_dataPtrs[i] != IntPtr.Zero) { Marshal.FreeHGlobal(_dataPtrs[i]); _dataPtrs[i] = IntPtr.Zero; }
        }
        try { _callbackEvent.Dispose(); } catch { }
    }
}

public enum RepeatMode { Off = 0, All = 1, One = 2 }

public static class EqPresets
{
    public static readonly double[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public static readonly IReadOnlyDictionary<string, double[]> Map = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Flat"]           = new double[] {  0,  0,  0,  0,  0,  0,  0,  0,  0,  0 },
        ["Echo Signature"] = new double[] { 3.5, 4.0, 2.0, 0.5, -0.5, 0.5, 2.0, 3.0, 3.5, 2.5 },
        ["Bass Boost"]     = new double[] { 6.5, 5.5, 4.0, 1.5,  0,  0,  0,  0.5, 1.0, 1.0 },
        ["Vocal Clarity"]  = new double[] { -2, -1.5, -0.5, 1.5, 3.5, 4.5, 3.5, 1.5, 0, -1 },
        ["Electronic"]     = new double[] { 4.5, 4.0, 1.5,  0, -1.5, 1.5, 1.0, 2.5, 4.0, 4.5 },
        ["Rock"]           = new double[] { 4.5, 3.5, 2.0, -1, -1.5, 0.5, 2.5, 3.5, 4.0, 4.0 },
        ["Hip-Hop"]        = new double[] { 5.5, 4.5, 2.0, 1.5, -1, -1, 1.5, 1.0, 2.5, 3.0 },
        ["Acoustic"]       = new double[] { 3.0, 2.5, 1.5, 0.5, 1.5, 1.5, 2.5, 3.0, 2.5, 1.5 },
        ["Classical"]      = new double[] { 3.5, 2.5, 1.0,  0,  0,  0, -0.5, 1.5, 2.5, 3.0 },
        ["R&B"]            = new double[] { 3.5, 5.5, 3.5, 1.0, -1.5, -0.5, 2.0, 2.5, 3.0, 3.5 },
        ["Late Night"]     = new double[] { 2.5, 2.0, 1.0, 0.5, 1.0, 1.5, 0.5, -1.0, -2.0, -3.0 },
        ["Treble Sparkle"] = new double[] {  0,  0,  0,  0, 0.5, 1.5, 3.0, 4.5, 5.5, 6.0 },
    };
}

/// <summary>
/// Wraps any track source (WebM/Opus stream, offline .webm, or local MP3/FLAC/WAV) into a uniform 48kHz stereo float deck
/// with an asynchronous 6-second ahead PCM ring buffer so the real-time WASAPI/WaveOut audio thread NEVER blocks on network I/O!
/// </summary>
internal sealed class DeckState : IDisposable
{
    private const int RingCapacityFloats = EchoMasterMixer.SampleRate * EchoMasterMixer.Channels * 8; // 8 sec stereo float32
    private readonly ISampleProvider _provider;
    private readonly IDisposable _disposable;
    private readonly WebmOpusSampleSource? _webm;
    private readonly AudioFileReader? _naudioFile;
    private readonly BoundedRangeHttpStream? _httpStream;

    private readonly float[] _ring = new float[RingCapacityFloats];
    private readonly object _ringLock = new();
    // Decoder providers (notably WebM/Opus) are not seek-safe while Read is in progress.
    // Serialize the background decode read with the seek operation.
    private readonly object _providerLock = new();
    private readonly CancellationTokenSource _pumpCts = new();
    private int _readPos;
    private int _writePos;
    private int _availableFloats;
    private long _playedFrames;
    private double _seekBaseSec;
    private volatile bool _sourceEof;
    private int _seekGeneration;
    private bool _pendingSeek;
    private double _pendingSeekSeconds;

    public TrackItem Track { get; }
    public ResolvedStream? StreamInfo { get; }
    public float NormalizationGain { get; }
    public double FadeGain { get; set; } = 1.0;

    public DeckState(
        TrackItem track,
        ISampleProvider provider,
        IDisposable disposable,
        WebmOpusSampleSource? webm,
        AudioFileReader? naudioFile,
        BoundedRangeHttpStream? httpStream,
        ResolvedStream? streamInfo)
    {
        Track = track;
        _provider = provider;
        _disposable = disposable;
        _webm = webm;
        _naudioFile = naudioFile;
        _httpStream = httpStream;
        StreamInfo = streamInfo;

        double ldb = streamInfo?.LoudnessDb ?? 0.0;
        NormalizationGain = ldb > 0.2 ? (float)Math.Clamp(Math.Pow(10.0, -ldb / 20.0), 0.30, 1.0) : 1.0f;

        // Pre-fill initial ~120ms synchronously so playback starts the exact millisecond deck is mounted, then pump async!
        FillChunkSynchronously(9600 * 2);
        _ = Task.Run(() => BackgroundDecodeLoopAsync(_pumpCts.Token));
    }

    private void FillChunkSynchronously(int targetFloats)
    {
        var tmp = new float[targetFloats];
        int got = 0;
        try { got = _provider.Read(tmp, 0, tmp.Length); } catch { }
        if (got <= 0) return;
        lock (_ringLock)
        {
            WriteToRingLocked(tmp, got);
        }
    }

    private async Task BackgroundDecodeLoopAsync(CancellationToken ct)
    {
        var scratch = new float[4800 * 2]; // 100ms chunks
        while (!ct.IsCancellationRequested)
        {
            int free;
            long generation;
            double? seekRequest = null;
            lock (_ringLock)
            {
                free = RingCapacityFloats - _availableFloats;
                generation = _seekGeneration;
                if (_pendingSeek)
                {
                    seekRequest = _pendingSeekSeconds;
                    _pendingSeek = false;
                }
            }

            if (seekRequest is double seekSeconds)
            {
                try
                {
                    // Seeking a range-backed WebM may need network I/O. It is intentionally run only
                    // by this worker, never while holding up the UI thread handling the user's seek.
                    lock (_providerLock)
                    {
                        bool stillCurrent;
                        lock (_ringLock) stillCurrent = generation == _seekGeneration;
                        if (stillCurrent)
                        {
                            if (_webm is not null) _webm.Seek(seekSeconds);
                            else if (_naudioFile is not null)
                                _naudioFile.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seekSeconds, 0, _naudioFile.TotalTime.TotalSeconds));
                        }
                    }
                }
                catch
                {
                    lock (_ringLock)
                    {
                        if (generation == _seekGeneration) _sourceEof = true;
                    }
                }

                lock (_ringLock)
                {
                    if (generation != _seekGeneration) continue; // a newer scrub target superseded this one
                    free = RingCapacityFloats - _availableFloats;
                }
            }

            if (_sourceEof || free < scratch.Length)
            {
                try { await Task.Delay(18, ct).ConfigureAwait(false); } catch { break; }
                continue;
            }

            int n = 0;
            try
            {
                lock (_providerLock)
                    n = _provider.Read(scratch, 0, scratch.Length);
            }
            catch
            {
                n = 0;
            }

            lock (_ringLock)
            {
                if (generation != _seekGeneration)
                    continue; // discard stale audio after a seek that arrived during the read
                if (n <= 0)
                {
                    _sourceEof = true;
                }
                else
                {
                    WriteToRingLocked(scratch, n);
                }
            }
        }
    }

    private void WriteToRingLocked(float[] src, int count)
    {
        int toWrite = Math.Min(count, RingCapacityFloats - _availableFloats);
        int first = Math.Min(toWrite, RingCapacityFloats - _writePos);
        Array.Copy(src, 0, _ring, _writePos, first);
        if (toWrite > first)
            Array.Copy(src, first, _ring, 0, toWrite - first);
        _writePos = (_writePos + toWrite) % RingCapacityFloats;
        _availableFloats += toWrite;
    }

    public double PositionSeconds
    {
        get
        {
            lock (_ringLock)
                return _seekBaseSec + (double)_playedFrames / EchoMasterMixer.SampleRate;
        }
    }

    public double DurationSeconds
    {
        get
        {
            if (_webm is not null && _webm.DurationSeconds > 0) return _webm.DurationSeconds;
            if (_naudioFile is not null) return _naudioFile.TotalTime.TotalSeconds;
            if (StreamInfo is { DurationSeconds: > 0 }) return StreamInfo.DurationSeconds;
            return Track.DurationSeconds;
        }
    }

    public double BufferedFraction => _httpStream?.BufferedFraction ?? 1.0;

    public bool IsFinished
    {
        get
        {
            lock (_ringLock)
                return _sourceEof && _availableFloats == 0;
        }
    }

    public void Seek(double seconds)
    {
        double clamped = Math.Max(0, DurationSeconds > 0 ? Math.Min(seconds, DurationSeconds - 0.1) : seconds);
        lock (_ringLock)
        {
            _seekGeneration++;
            _readPos = 0;
            _writePos = 0;
            _availableFloats = 0;
            _playedFrames = 0;
            _seekBaseSec = clamped;
            _sourceEof = false;
            _pendingSeekSeconds = clamped;
            _pendingSeek = true;
        }
    }

    /// <summary>
    /// Lock-fast non-blocking read for the real-time audio hardware callback.
    /// Returns >0 samples even during brief network underruns (padded with 0s) and ONLY returns 0 at true track EOF!
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        lock (_ringLock)
        {
            if (_availableFloats == 0)
            {
                if (_sourceEof) return 0; // True end of song!
                // Network momentarily buffering: return silence without prematurely ending track!
                Array.Clear(buffer, offset, count);
                return count;
            }

            int take = Math.Min(count, _availableFloats);
            int first = Math.Min(take, RingCapacityFloats - _readPos);
            Array.Copy(_ring, _readPos, buffer, offset, first);
            if (take > first)
                Array.Copy(_ring, 0, buffer, offset + first, take - first);

            _readPos = (_readPos + take) % RingCapacityFloats;
            _availableFloats -= take;
            _playedFrames += take / EchoMasterMixer.Channels;

            if (take < count)
                Array.Clear(buffer, offset + take, count - take);
            return _sourceEof ? take : count;
        }
    }

    public void Dispose()
    {
        try { _pumpCts.Cancel(); } catch { }
        try { _disposable.Dispose(); } catch { }
    }
}

/// <summary>
/// Master 48kHz stereo float DSP pipeline:
/// Dual-deck crossfade -> Speed resampler -> 10-band Biquad EQ + Bass Shelf -> Schroeder Stereo Hall Reverb -> Master Volume -> 32-band FFT Spectrum.
/// </summary>
/// <summary>
/// 64-bit Double-Precision Direct-Form II Transposed Biquad Filter (Audio EQ Cookbook).
/// Eliminates single-precision IIR pole quantization noise at low frequencies (31Hz–95Hz @ 48kHz).
/// </summary>
internal struct DoubleBiquad
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    public static DoubleBiquad PeakingEq(double fs, double f0, double q, double dbGain)
    {
        double a = Math.Pow(10.0, dbGain / 40.0);
        double w0 = 2.0 * Math.PI * f0 / fs;
        double cosW0 = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * q);

        double b0 = 1.0 + alpha * a;
        double b1 = -2.0 * cosW0;
        double b2 = 1.0 - alpha * a;
        double a0 = 1.0 + alpha / a;
        double a1 = -2.0 * cosW0;
        double a2 = 1.0 - alpha / a;
        return new DoubleBiquad { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0 };
    }

    public static DoubleBiquad LowShelf(double fs, double f0, double slope, double dbGain)
    {
        double a = Math.Pow(10.0, dbGain / 40.0);
        double w0 = 2.0 * Math.PI * f0 / fs;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / 2.0 * Math.Sqrt(Math.Max(0.001, (a + 1.0 / a) * (1.0 / slope - 1.0) + 2.0));
        double twoSqrtAAlpha = 2.0 * Math.Sqrt(a) * alpha;

        double b0 = a * ((a + 1.0) - (a - 1.0) * cosW0 + twoSqrtAAlpha);
        double b1 = 2.0 * a * ((a - 1.0) - (a + 1.0) * cosW0);
        double b2 = a * ((a + 1.0) - (a - 1.0) * cosW0 - twoSqrtAAlpha);
        double a0 = (a + 1.0) + (a - 1.0) * cosW0 + twoSqrtAAlpha;
        double a1 = -2.0 * ((a - 1.0) + (a + 1.0) * cosW0);
        double a2 = (a + 1.0) + (a - 1.0) * cosW0 - twoSqrtAAlpha;
        return new DoubleBiquad { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0 };
    }

    public void UpdateCoefficientsFrom(DoubleBiquad target)
    {
        _b0 = target._b0;
        _b1 = target._b1;
        _b2 = target._b2;
        _a1 = target._a1;
        _a2 = target._a2;
    }

    public double Transform(double x)
    {
        double y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }
}

public sealed class EchoMasterMixer : ISampleProvider
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    private const int FftSize = 512;

    private readonly object _dspLock = new();
    private readonly DoubleBiquad[,] _eqFilters = new DoubleBiquad[Channels, 10];
    private readonly DoubleBiquad[] _bassShelf = new DoubleBiquad[Channels];
    private bool _eqActive;
    private readonly Complex[] _fftWork = new Complex[FftSize];

    // Schroeder stereo lush reverb lines (4 combs + 2 allpasses per channel)
    private static readonly int[] CombLengthsL = { 1687, 1901, 2053, 2251 };
    private static readonly int[] CombLengthsR = { 1709, 1931, 2081, 2273 };
    private static readonly int[] AllpassLengths = { 347, 113 };
    private readonly float[][] _combL = CombLengthsL.Select(n => new float[n]).ToArray();
    private readonly float[][] _combR = CombLengthsR.Select(n => new float[n]).ToArray();
    private readonly int[] _combIdxL = new int[4];
    private readonly int[] _combIdxR = new int[4];
    private readonly float[][] _apL = AllpassLengths.Select(n => new float[n]).ToArray();
    private readonly float[][] _apR = AllpassLengths.Select(n => new float[n]).ToArray();
    private readonly int[] _apIdxL = new int[2];
    private readonly int[] _apIdxR = new int[2];
    private bool _reverbWasActive;

    // Variable speed fractional resampler state (Vinyl mode)
    private float[] _speedSrcBuf = new float[16384];
    private int _speedSrcFrames;
    private double _speedFracPos;
    private float _prevL, _prevR;

    // Constant-Pitch WSOLA Overlap-Add Time-Stretcher state (Pitch-Locked mode)
    private const int WsolaWinFrames = 1920; // 40ms @ 48kHz
    private const int WsolaHalfFrames = 960; // 20ms synthesis hop
    private const int WsolaSearchFrames = 480; // ±10ms waveform similarity search
    private readonly float[] _wsolaInput = new float[32768 * Channels];
    private int _wsolaInputFrames;
    private double _wsolaAnalysisPos;
    private readonly float[] _wsolaOutQueue = new float[32768 * Channels];
    private int _wsolaOutReadFrame;
    private int _wsolaOutReadyFrames;
    private readonly float[] _wsolaOverlapTail = new float[WsolaHalfFrames * Channels];
    private bool _wsolaPrimed;

    // FFT ring & smoothed 32-band spectrum
    private readonly float[] _fftRing = new float[FftSize];
    private int _fftWrite;
    private readonly float[] _spectrum32 = new float[32];
    private readonly object _specLock = new();

    private float[] _mixScratchA = new float[16384];
    private float[] _mixScratchB = new float[16384];

    internal DeckState? PrimaryDeck;
    internal DeckState? CrossfadeOutDeck;
    internal double CrossfadeRemainingSec;
    internal double CrossfadeTotalSec;

    private volatile bool _isPaused = true;
    public bool IsPaused { get => _isPaused; set => _isPaused = value; }
    public float MasterVolume { get; set; } = 0.85f;
    public bool IsMuted { get; set; }
    public bool NormalizeLoudness { get; set; } = true;
    public double PlaybackSpeed { get; set; } = 1.0;
    public bool PitchLockSpeed { get; set; }
    public bool AmbientReverb { get; set; }
    public float ReverbMix { get; set; } = 0.24f;
    public event Action? PrimaryTrackEnded;

    private int _testChimeSamplesLeft;
    private int _testChimeTotalSamples;
    public float LastOutputPeak { get; private set; }

    public void TriggerTestChime()
    {
        _testChimeTotalSamples = (int)(SampleRate * 0.72);
        _testChimeSamplesLeft = _testChimeTotalSamples;
    }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    public EchoMasterMixer()
    {
        UpdateEqualizer(new double[10], 0);
    }

    public void UpdateEqualizer(double[] bandsDb, double bassBoostDb)
    {
        lock (_dspLock)
        {
            double clampedBass = Math.Clamp(bassBoostDb, 0, 12);
            bool anyActive = clampedBass > 0.05;
            for (int ch = 0; ch < Channels; ch++)
            {
                for (int b = 0; b < 10; b++)
                {
                    double gain = Math.Clamp(b < bandsDb.Length ? bandsDb[b] : 0, -12, 12);
                    if (Math.Abs(gain) > 0.05) anyActive = true;
                    _eqFilters[ch, b].UpdateCoefficientsFrom(
                        DoubleBiquad.PeakingEq(SampleRate, EqPresets.Frequencies[b], 1.41, gain));
                }
                _bassShelf[ch].UpdateCoefficientsFrom(
                    DoubleBiquad.LowShelf(SampleRate, 95.0, 0.8, clampedBass));
            }
            _eqActive = anyActive;
        }
    }

    public void CopySpectrum32(Span<float> dest)
    {
        lock (_specLock)
        {
            int n = Math.Min(dest.Length, _spectrum32.Length);
            _spectrum32.AsSpan(0, n).CopyTo(dest);
        }
    }

    public void ResetSpeedBuffer()
    {
        lock (_dspLock)
        {
            _speedSrcFrames = 0;
            _speedFracPos = 0;
            _wsolaInputFrames = 0;
            _wsolaAnalysisPos = 0;
            _wsolaOutReadFrame = 0;
            _wsolaOutReadyFrames = 0;
            _wsolaPrimed = false;
            Array.Clear(_wsolaOverlapTail);
        }
    }

    public void ResetReverbState()
    {
        lock (_dspLock)
        {
            ClearReverbStateLocked();
            _reverbWasActive = false;
        }
    }

    private void ClearReverbStateLocked()
    {
        foreach (var line in _combL) Array.Clear(line);
        foreach (var line in _combR) Array.Clear(line);
        foreach (var line in _apL) Array.Clear(line);
        foreach (var line in _apR) Array.Clear(line);
        Array.Clear(_combIdxL);
        Array.Clear(_combIdxR);
        Array.Clear(_apIdxL);
        Array.Clear(_apIdxR);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        try
        {
            return ReadCore(buffer, offset, count);
        }
        catch
        {
            // NEVER let an exception escape to WASAPI / WaveOut hardware thread!
            Array.Clear(buffer, offset, count);
            return count;
        }
    }

    private int ReadCore(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        var primary = PrimaryDeck;
        if ((IsPaused || primary is null) && _testChimeSamplesLeft <= 0)
        {
            LastOutputPeak = 0f;
            DecaySpectrum();
            return count; // emit silence so hardware device stays warm & click-free!
        }

        if (IsPaused || primary is null)
        {
            MixTestChimeIfActive(buffer, offset, count);
            return count;
        }

        int outFrames = count / Channels;
        double speed = Math.Clamp(PlaybackSpeed, 0.5, 1.75);
        int producedSamples;

        if (Math.Abs(speed - 1.0) < 0.005)
        {
            producedSamples = ReadRawFromPrimaryAndCrossfade(primary, buffer, offset, count);
        }
        else if (PitchLockSpeed)
        {
            producedSamples = ReadWsolaPitchLocked(primary, buffer, offset, outFrames, speed);
        }
        else
        {
            producedSamples = ReadSpeedResampled(primary, buffer, offset, outFrames, speed);
        }

        if (producedSamples == 0)
        {
            DecaySpectrum();
            Task.Run(() => PrimaryTrackEnded?.Invoke());
            return count;
        }

        float normGain = NormalizeLoudness ? primary.NormalizationGain : 1.0f;
        float vol = IsMuted ? 0f : Math.Clamp(MasterVolume, 0f, 1.25f) * normGain;
        bool doReverb = AmbientReverb && ReverbMix > 0.01f;
        float wet = Math.Clamp(ReverbMix, 0f, 0.65f);
        float dry = 1.0f - wet * 0.38f;

        lock (_dspLock)
        {
            if (doReverb)
            {
                _reverbWasActive = true;
            }
            else if (_reverbWasActive)
            {
                // Do not resume an old delay tail when wet is turned back on later.
                ClearReverbStateLocked();
                _reverbWasActive = false;
            }

            bool runEq = _eqActive;
            for (int i = 0; i < producedSamples; i += 2)
            {
                float l = buffer[offset + i];
                float r = buffer[offset + i + 1];

                // 64-bit Double-Precision 10-band Graphic EQ + Bass Shelf (100% bypassed when flat!)
                if (runEq)
                {
                    double dl = _bassShelf[0].Transform(l);
                    double dr = _bassShelf[1].Transform(r);
                    for (int b = 0; b < 10; b++)
                    {
                        dl = _eqFilters[0, b].Transform(dl);
                        dr = _eqFilters[1, b].Transform(dr);
                    }
                    l = (float)dl;
                    r = (float)dr;
                }

                // Lush Stereo Schroeder Hall Reverb (Ambient / Slowed+Reverb DSP)
                if (doReverb)
                {
                    float monoIn = (l + r) * 0.25f;
                    float revL = 0f, revR = 0f;
                    const float feedback = 0.80f;
                    for (int c = 0; c < 4; c++)
                    {
                        var cl = _combL[c];
                        int il = _combIdxL[c];
                        float outL = cl[il];
                        cl[il] = monoIn + outL * feedback;
                        _combIdxL[c] = (il + 1) % cl.Length;
                        revL += outL;

                        var cr = _combR[c];
                        int ir = _combIdxR[c];
                        float outR = cr[ir];
                        cr[ir] = monoIn + outR * feedback;
                        _combIdxR[c] = (ir + 1) % cr.Length;
                        revR += outR;
                    }
                    for (int a = 0; a < 2; a++)
                    {
                        var al = _apL[a];
                        int ial = _apIdxL[a];
                        float bl = al[ial];
                        float nl = -revL * 0.5f + bl;
                        al[ial] = revL + bl * 0.5f;
                        _apIdxL[a] = (ial + 1) % al.Length;
                        revL = nl;

                        var ar = _apR[a];
                        int iar = _apIdxR[a];
                        float br = ar[iar];
                        float nr = -revR * 0.5f + br;
                        ar[iar] = revR + br * 0.5f;
                        _apIdxR[a] = (iar + 1) % ar.Length;
                        revR = nr;
                    }
                    l = l * dry + revL * wet * 0.45f;
                    r = r * dry + revR * wet * 0.45f;
                }

                // True C1-continuous smooth soft-knee limiter + Master Volume (zero step discontinuity!)
                l = SmoothLimit(l * vol);
                r = SmoothLimit(r * vol);
                buffer[offset + i] = l;
                buffer[offset + i + 1] = r;

                _fftRing[_fftWrite] = (l + r) * 0.5f;
                _fftWrite = (_fftWrite + 1) & (FftSize - 1);
            }
        }

        ComputeSpectrumFromRing();
        if (_testChimeSamplesLeft > 0)
            MixTestChimeIfActive(buffer, offset, count);

        float pk = 0f;
        for (int i = 0; i < count; i += 8)
        {
            float a = MathF.Abs(buffer[offset + i]);
            if (a > pk) pk = a;
        }
        LastOutputPeak = pk;
        return count;
    }

    private void MixTestChimeIfActive(float[] buffer, int offset, int count)
    {
        float vol = Math.Max(0.35f, MasterVolume);
        for (int i = 0; i < count && _testChimeSamplesLeft > 0; i += 2)
        {
            int idx = _testChimeTotalSamples - _testChimeSamplesLeft;
            double t = (double)idx / SampleRate;
            double env = Math.Min(1.0, t / 0.015) * Math.Exp(-t * 4.2);
            double c5 = Math.Sin(2.0 * Math.PI * 523.25 * t);
            double e5 = Math.Sin(2.0 * Math.PI * 659.25 * t);
            double g5 = Math.Sin(2.0 * Math.PI * 783.99 * t);
            float sampleL = (float)((c5 * 0.45 + e5 * 0.32 + g5 * 0.23) * env * 0.55 * vol);
            float sampleR = (float)((c5 * 0.35 + e5 * 0.35 + g5 * 0.30) * env * 0.55 * vol);
            buffer[offset + i] = SmoothLimit(buffer[offset + i] + sampleL);
            buffer[offset + i + 1] = SmoothLimit(buffer[offset + i + 1] + sampleR);
            _testChimeSamplesLeft--;
        }
    }

    private int ReadRawFromPrimaryAndCrossfade(DeckState primary, float[] dst, int offset, int count)
    {
        if (_mixScratchA.Length < count) Array.Resize(ref _mixScratchA, count * 2);
        int readA = primary.Read(_mixScratchA, 0, count);
        var fadeDeck = CrossfadeOutDeck;
        if (fadeDeck is null || CrossfadeRemainingSec <= 0 || CrossfadeTotalSec <= 0)
        {
            if (readA > 0) Array.Copy(_mixScratchA, 0, dst, offset, readA);
            return readA;
        }

        if (_mixScratchB.Length < count) Array.Resize(ref _mixScratchB, count * 2);
        int readB = fadeDeck.Read(_mixScratchB, 0, count);
        int frames = Math.Max(readA, readB) / Channels;
        double dt = 1.0 / SampleRate;

        for (int f = 0; f < frames; f++)
        {
            double progress = 1.0 - Math.Clamp(CrossfadeRemainingSec / CrossfadeTotalSec, 0.0, 1.0);
            // Equal-power sin/cos crossfade curve!
            float gainIn = (float)Math.Sin(progress * Math.PI * 0.5);
            float gainOut = (float)Math.Cos(progress * Math.PI * 0.5);
            int idx = f * 2;
            float aL = idx < readA ? _mixScratchA[idx] : 0f;
            float aR = idx + 1 < readA ? _mixScratchA[idx + 1] : 0f;
            float bL = idx < readB ? _mixScratchB[idx] : 0f;
            float bR = idx + 1 < readB ? _mixScratchB[idx + 1] : 0f;
            dst[offset + idx] = aL * gainIn + bL * gainOut;
            dst[offset + idx + 1] = aR * gainIn + bR * gainOut;
            CrossfadeRemainingSec -= dt;
        }

        if (CrossfadeRemainingSec <= 0 || readB == 0)
        {
            CrossfadeOutDeck = null;
            Task.Run(() => fadeDeck.Dispose());
        }
        return frames * Channels;
    }

    private int ReadSpeedResampled(DeckState primary, float[] dst, int offset, int outFrames, double speed)
    {
        int writtenFrames = 0;
        while (writtenFrames < outFrames)
        {
            int idx0 = (int)_speedFracPos;
            if (idx0 + 1 >= _speedSrcFrames)
            {
                if (_speedSrcFrames > 0)
                {
                    _prevL = _speedSrcBuf[(_speedSrcFrames - 1) * 2];
                    _prevR = _speedSrcBuf[(_speedSrcFrames - 1) * 2 + 1];
                }
                _speedFracPos -= _speedSrcFrames;
                if (_speedFracPos < 0) _speedFracPos = 0;
                int got = ReadRawFromPrimaryAndCrossfade(primary, _speedSrcBuf, 0, 4096);
                _speedSrcFrames = got / Channels;
                if (_speedSrcFrames == 0) break;
                continue;
            }

            float frac = (float)(_speedFracPos - idx0);
            float l0 = idx0 < 0 ? _prevL : _speedSrcBuf[idx0 * 2];
            float r0 = idx0 < 0 ? _prevR : _speedSrcBuf[idx0 * 2 + 1];
            float l1 = _speedSrcBuf[(idx0 + 1) * 2];
            float r1 = _speedSrcBuf[(idx0 + 1) * 2 + 1];

            dst[offset + writtenFrames * 2] = l0 + (l1 - l0) * frac;
            dst[offset + writtenFrames * 2 + 1] = r0 + (r1 - r0) * frac;
            writtenFrames++;
            _speedFracPos += speed;
        }
        return writtenFrames * Channels;
    }

    private int ReadWsolaPitchLocked(DeckState primary, float[] dst, int offset, int outFrames, double speed)
    {
        int producedFrames = 0;
        while (producedFrames < outFrames)
        {
            if (_wsolaOutReadyFrames > 0)
            {
                int take = Math.Min(outFrames - producedFrames, _wsolaOutReadyFrames);
                Array.Copy(_wsolaOutQueue, _wsolaOutReadFrame * Channels, dst, offset + producedFrames * Channels, take * Channels);
                _wsolaOutReadFrame += take;
                _wsolaOutReadyFrames -= take;
                if (_wsolaOutReadyFrames == 0) _wsolaOutReadFrame = 0;
                producedFrames += take;
                continue;
            }

            // Ensure sufficient lookahead in _wsolaInput
            int neededInputFrames = (int)_wsolaAnalysisPos + WsolaWinFrames + WsolaSearchFrames + 64;
            while (_wsolaInputFrames < neededInputFrames)
            {
                if (_wsolaAnalysisPos > 8192)
                {
                    int drop = (int)_wsolaAnalysisPos - WsolaSearchFrames;
                    if (drop > 0 && drop < _wsolaInputFrames)
                    {
                        int remain = _wsolaInputFrames - drop;
                        Array.Copy(_wsolaInput, drop * Channels, _wsolaInput, 0, remain * Channels);
                        _wsolaInputFrames = remain;
                        _wsolaAnalysisPos -= drop;
                    }
                }
                int spaceSamples = _wsolaInput.Length - _wsolaInputFrames * Channels;
                if (spaceSamples < 2048) break;
                int got = ReadRawFromPrimaryAndCrossfade(primary, _wsolaInput, _wsolaInputFrames * Channels, Math.Min(8192, spaceSamples));
                if (got <= 0) break;
                _wsolaInputFrames += got / Channels;
            }

            int center = Math.Max(0, (int)Math.Round(_wsolaAnalysisPos));
            if (center + WsolaWinFrames >= _wsolaInputFrames)
                break;

            int bestStart = center;
            if (_wsolaPrimed)
            {
                int minS = Math.Max(0, center - WsolaSearchFrames);
                int maxS = Math.Min(_wsolaInputFrames - WsolaWinFrames - 1, center + WsolaSearchFrames);
                float bestCorr = float.MinValue;
                // Coarse 4-frame stride cross-correlation + fine ±4 refinement for crisp transient alignment
                for (int cand = minS; cand <= maxS; cand += 4)
                {
                    float corr = 0f;
                    for (int k = 0; k < WsolaHalfFrames; k += 8)
                    {
                        int a = (cand + k) * 2;
                        int b = k * 2;
                        corr += _wsolaInput[a] * _wsolaOverlapTail[b] + _wsolaInput[a + 1] * _wsolaOverlapTail[b + 1];
                    }
                    if (corr > bestCorr) { bestCorr = corr; bestStart = cand; }
                }
            }

            // Synthesize WsolaHalfFrames of Hann overlap-added constant-pitch output
            int writeBase = (_wsolaOutReadFrame + _wsolaOutReadyFrames) * Channels;
            for (int i = 0; i < WsolaHalfFrames; i++)
            {
                float fadeIn = 0.5f * (1f - MathF.Cos(MathF.PI * i / WsolaHalfFrames));
                int srcIdx = (bestStart + i) * 2;
                int tailIdx = i * 2;
                float outL = _wsolaPrimed ? _wsolaOverlapTail[tailIdx] + _wsolaInput[srcIdx] * fadeIn : _wsolaInput[srcIdx];
                float outR = _wsolaPrimed ? _wsolaOverlapTail[tailIdx + 1] + _wsolaInput[srcIdx + 1] * fadeIn : _wsolaInput[srcIdx + 1];
                _wsolaOutQueue[writeBase + i * 2] = outL;
                _wsolaOutQueue[writeBase + i * 2 + 1] = outR;
            }

            // Store second half pre-windowed with fadeOut for next frame's seamless overlap
            for (int i = 0; i < WsolaHalfFrames; i++)
            {
                float fadeOut = 0.5f * (1f + MathF.Cos(MathF.PI * i / WsolaHalfFrames));
                int srcIdx = (bestStart + WsolaHalfFrames + i) * 2;
                _wsolaOverlapTail[i * 2] = _wsolaInput[srcIdx] * fadeOut;
                _wsolaOverlapTail[i * 2 + 1] = _wsolaInput[srcIdx + 1] * fadeOut;
            }

            _wsolaPrimed = true;
            _wsolaOutReadyFrames += WsolaHalfFrames;
            _wsolaAnalysisPos = bestStart + WsolaHalfFrames * speed;
        }
        return producedFrames * Channels;
    }

    /// <summary>
    /// Mathematically C1-continuous soft-knee limiter:
    /// Exact linear wire for |x| <= 0.88f; above 0.88f smoothly transitions via Tanh with slope=1.0 at the knee,
    /// asymptotically bounded at ±0.995f so 16-bit DAC quantization never clips or steps!
    /// </summary>
    public static float SmoothLimit(float x)
    {
        if (float.IsNaN(x) || float.IsInfinity(x)) return 0f;
        const float knee = 0.88f;
        const float headroom = 0.115f; // 0.88 + 0.115 = 0.995f max
        if (x > knee)
            return knee + headroom * MathF.Tanh((x - knee) / headroom);
        if (x < -knee)
            return -knee - headroom * MathF.Tanh((-x - knee) / headroom);
        return x;
    }

    private void DecaySpectrum()
    {
        lock (_specLock)
        {
            for (int i = 0; i < 32; i++) _spectrum32[i] *= 0.82f;
        }
    }

    private void ComputeSpectrumFromRing()
    {
        int rp = _fftWrite;
        for (int i = 0; i < FftSize; i++)
        {
            float hann = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
            _fftWork[i].X = _fftRing[(rp + i) & (FftSize - 1)] * hann;
            _fftWork[i].Y = 0f;
        }
        // NAudio FastFourierTransform (m = 9 for 2^9 = 512) - zero heap allocation!
        FastFourierTransform.FFT(true, 9, _fftWork);

        lock (_specLock)
        {
            for (int band = 0; band < 32; band++)
            {
                // Logarithmic bin grouping across 20Hz..18kHz
                int binStart = Math.Max(1, (int)Math.Pow(220.0, band / 32.0));
                int binEnd = Math.Max(binStart + 1, (int)Math.Pow(220.0, (band + 1) / 32.0));
                binEnd = Math.Min(binEnd, FftSize / 2);

                float peak = 0f;
                for (int k = binStart; k < binEnd; k++)
                {
                    float mag = MathF.Sqrt(_fftWork[k].X * _fftWork[k].X + _fftWork[k].Y * _fftWork[k].Y);
                    if (mag > peak) peak = mag;
                }
                // Convert to perceptual 0..1 visual bar height
                float db = 20f * MathF.Log10(peak * 42f + 1e-5f);
                float norm = Math.Clamp((db + 38f) / 40f, 0f, 1f);
                float prev = _spectrum32[band];
                _spectrum32[band] = norm > prev ? prev * 0.35f + norm * 0.65f : prev * 0.78f + norm * 0.22f;
            }
        }
    }
}

public sealed class AudioEngine : IDisposable
{
    private readonly InnerTubeClient _yt;
    private readonly StreamResolver _resolver;
    private readonly AppStore _store;
    private readonly EchoMasterMixer _mixer = new();
    private readonly WindowsSmtcBridge _smtc;
    private readonly DiscordRpcClient _discord = new();
    private readonly SponsorBlockService _sponsorBlock;

    private Win32WaveOut16Player? _win32WaveOut;
    private IWavePlayer? _waveOut;
    private CancellationTokenSource? _linuxPumpCts;
    public string ActiveAudioBackendLabel { get; private set; } = "Linux DSP Pump";
    public float CurrentOutputPeak => _mixer.LastOutputPeak;
    public long HardwareBlocksPlayed => _win32WaveOut?.BlocksSubmitted ?? 0;

    public void PlayTestChime() => _mixer.TriggerTestChime();
    public List<(int Id, string Name)> GetOutputDevices() => Win32WaveOut16Player.EnumerateDevices();
    private CancellationTokenSource? _loadCts;
    private readonly object _loadStateLock = new();
    private int _trackLoadGeneration;
    private Task<DeckState?>? _prewarmedNextTask;
    private string? _prewarmedNextId;
    private string? _lightPrewarmNextId;
    private bool _crossfadeTriggeredForTrack;
    private DeckState? _historyDeck;
    private PlayHistoryEntry? _historyEntry;
    private long _historyLastPersistedMs;
    private bool _playlistHistoryRecordedForQueue;

    // SponsorBlock per-track state
    private List<SkipSegment> _activeSkipSegments = new();
    private int _sponsorBlockRequestGeneration;
    private readonly HashSet<int> _triggeredSkipIndices = new();
    private DateTimeOffset _lastExternalSyncUtc = DateTimeOffset.MinValue;

    // Sleep timer
    private DateTimeOffset? _sleepDeadlineUtc;
    private bool _sleepEndOfTrack;

    public List<TrackItem> Queue { get; } = new();
    public PlaybackCollectionContext? QueueSource { get; private set; }
    public int CurrentIndex { get; private set; } = -1;
    public TrackItem? CurrentTrack => CurrentIndex >= 0 && CurrentIndex < Queue.Count ? Queue[CurrentIndex] : null;
    public ResolvedStream? CurrentStreamInfo => _mixer.PrimaryDeck?.StreamInfo;

    public bool IsPlaying => !IsLoading && !_mixer.IsPaused && _mixer.PrimaryDeck is not null;
    public bool IsLoading { get; private set; }
    public bool ShuffleEnabled { get; private set; }
    public RepeatMode Repeat { get; private set; } = RepeatMode.Off;
    public string? LastError { get; private set; }

    public double PositionSeconds => IsLoading ? 0 : (_mixer.PrimaryDeck?.PositionSeconds ?? 0);
    public double DurationSeconds => IsLoading
        ? (CurrentTrack?.DurationSeconds ?? 0)
        : (_mixer.PrimaryDeck?.DurationSeconds ?? (CurrentTrack?.DurationSeconds ?? 0));
    public double BufferedFraction => IsLoading ? 0 : (_mixer.PrimaryDeck?.BufferedFraction ?? 0);

    public TimeSpan? SleepRemaining =>
        _sleepEndOfTrack
            ? TimeSpan.FromSeconds(Math.Max(0, DurationSeconds - PositionSeconds))
            : _sleepDeadlineUtc is { } dl
                ? (dl > DateTimeOffset.UtcNow ? dl - DateTimeOffset.UtcNow : TimeSpan.Zero)
                : null;

    public event Action? StateChanged;
    public event Action<TrackItem?>? TrackChanged;
    public event Action<string>? ToastRequested;

    public AudioEngine(InnerTubeClient yt, StreamResolver resolver, AppStore store)
    {
        _yt = yt;
        _resolver = resolver;
        _store = store;
        _sponsorBlock = new SponsorBlockService(yt.SharedHttp);
        _smtc = new WindowsSmtcBridge(this, () => _store.Settings.SmtcEnabled);

        if (_store.Settings.Volume <= 0.01)
            _store.Settings.Volume = 0.85;
        ApplySettingsFromStore();
        _mixer.PrimaryTrackEnded += OnPrimaryTrackEnded;
        StateChanged += PushExternalPresenceDebounced;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _audioEndpointWatcher = new WindowsAudioEndpointWatcher();
                _audioEndpointWatcher.OutputEndpointsChanged += ScheduleAudioEndpointRefresh;
            }
            catch { }
        }
        InitializeOutputBackend();
    }


    public void ApplySettingsFromStore()
    {
        var s = _store.Settings;
        _mixer.MasterVolume = (float)s.Volume;
        _mixer.NormalizeLoudness = s.NormalizeVolume;
        _mixer.PlaybackSpeed = s.PlaybackSpeed;
        _mixer.PitchLockSpeed = s.PitchLockSpeed;
        _mixer.AmbientReverb = s.AmbientReverb;
        _mixer.ReverbMix = (float)s.ReverbMix;
        _mixer.UpdateEqualizer(s.EqBands, s.BassBoostDb);
        _discord.IsEnabled = s.DiscordRpcEnabled;
        _smtc.SetEnabled(s.SmtcEnabled);
        PushExternalPresenceDebounced();
    }

    private void PushExternalPresenceDebounced()
    {
        var tr = CurrentTrack;
        bool playing = IsPlaying;
        double pos = PositionSeconds;
        double dur = DurationSeconds;
        _smtc.UpdateTrackAndState(tr, playing, pos, dur);
        _ = Task.Run(() => _discord.UpdatePresenceAsync(tr, playing, pos, dur));
    }

    private readonly object _deviceLock = new();
    private readonly object _deviceChangeDebounceLock = new();
    private readonly SemaphoreSlim _outputFlushGate = new(1, 1);
    private DateTimeOffset _lastDeviceHealAttempt = DateTimeOffset.MinValue;
    private WindowsAudioEndpointWatcher? _audioEndpointWatcher;
    private CancellationTokenSource? _deviceChangeDebounce;

    private void InitializeOutputBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            TryStartWindowsOutputDevice();
            return;
        }

        // Non-Windows (Linux Xvfb / PulseAudio / headless verification):
        _linuxPumpCts = new CancellationTokenSource();
        var ct = _linuxPumpCts.Token;
        _ = Task.Run(async () =>
        {
            var buf = new float[960 * 2]; // 20ms @ 48kHz stereo
            var sw = Stopwatch.StartNew();
            long tickIndex = 0;
            while (!ct.IsCancellationRequested)
            {
                _mixer.Read(buf, 0, buf.Length);
                TickMaintenance();
                tickIndex++;
                long targetMs = tickIndex * 20;
                int sleepMs = (int)(targetMs - sw.ElapsedMilliseconds);
                if (sleepMs > 1)
                    await Task.Delay(sleepMs, ct).ConfigureAwait(false);
                else
                    await Task.Yield();
            }
        }, ct);
    }

    public void RestartAudioOutputBackend()
    {
        if (OperatingSystem.IsWindows())
            TryStartWindowsOutputDevice();
    }

    private void ScheduleAudioEndpointRefresh()
    {
        CancellationTokenSource debounce;
        lock (_deviceChangeDebounceLock)
        {
            _deviceChangeDebounce?.Cancel();
            _deviceChangeDebounce?.Dispose();
            _deviceChangeDebounce = debounce = new CancellationTokenSource();
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(450, debounce.Token).ConfigureAwait(false);
                if (debounce.IsCancellationRequested) return;

                if (_store.Settings.FollowWindowsDefaultAudioDevice)
                {
                    _store.Settings.AudioDeviceId = -1;
                    _store.SaveDebounced();
                }
                else if (!_store.Settings.AudioDeviceId.Equals(-1)
                    && !Win32WaveOut16Player.EnumerateDevices().Any(d => d.Id == _store.Settings.AudioDeviceId))
                {
                    // A manually selected endpoint disappeared. Rejoin the system default automatically.
                    _store.Settings.AudioDeviceId = -1;
                    _store.Settings.FollowWindowsDefaultAudioDevice = true;
                    _store.SaveDebounced();
                }
                RestartAudioOutputBackend();
            }
            catch (OperationCanceledException) { }
            catch { }
        });
    }

    private void TryStartWindowsOutputDevice()
    {
        lock (_deviceLock)
        {
            try { _win32WaveOut?.Dispose(); } catch { }
            _win32WaveOut = null;
            try { _waveOut?.Stop(); _waveOut?.Dispose(); } catch { }
            _waveOut = null;

            string pref = _store.Settings.AudioBackend ?? "Win32WaveOut";
            int devId = _store.Settings.AudioDeviceId;

            // Ordered cascade starting with user preference (defaults to direct Win32 16-bit 48kHz PCM!)
            var order = new List<string> { pref, "Win32WaveOut", "DirectSound", "NAudioWaveOut", "WasapiPolling" }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var mode in order)
            {
                try
                {
                    if (mode.Equals("Win32WaveOut", StringComparison.OrdinalIgnoreCase))
                    {
                        _win32WaveOut = new Win32WaveOut16Player(_mixer, devId);
                        ActiveAudioBackendLabel = $"Win32 WaveOut 16-bit PCM (48kHz)";
                        LastError = null;
                        return;
                    }
                    if (mode.Equals("DirectSound", StringComparison.OrdinalIgnoreCase))
                    {
                        var ds = new DirectSoundOut(95);
                        ds.Init(new SampleToWaveProvider16(_mixer));
                        ds.Play();
                        _waveOut = ds;
                        ActiveAudioBackendLabel = "DirectSound 16-bit PCM (48kHz)";
                        LastError = null;
                        return;
                    }
                    if (mode.Equals("NAudioWaveOut", StringComparison.OrdinalIgnoreCase))
                    {
                        var wo = new WaveOutEvent { DesiredLatency = 105, NumberOfBuffers = 4, DeviceNumber = devId };
                        wo.Init(new SampleToWaveProvider16(_mixer));
                        wo.Play();
                        _waveOut = wo;
                        ActiveAudioBackendLabel = "NAudio WaveOutEvent 16-bit PCM";
                        LastError = null;
                        return;
                    }
                    if (mode.Equals("WasapiPolling", StringComparison.OrdinalIgnoreCase))
                    {
                        // Polling mode ONLY (useEventSync: false avoids WasapiOut event-sync silent bug!)
                        var ws = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, false, 100);
                        ws.Init(_mixer);
                        ws.Play();
                        _waveOut = ws;
                        ActiveAudioBackendLabel = "WASAPI Shared Polling";
                        LastError = null;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                }
            }
        }
    }

    public void CopySpectrum32(Span<float> dest) => _mixer.CopySpectrum32(dest);

    public void TickMaintenance()
    {
        // Self-healing Windows audio device watchdog (recovers automatically if user switches headphones/Bluetooth!)
        if (OperatingSystem.IsWindows())
        {
            bool healthy = (_win32WaveOut is { IsHealthy: true }) || (_waveOut is { PlaybackState: PlaybackState.Playing });
            if (!healthy && (DateTimeOffset.UtcNow - _lastDeviceHealAttempt).TotalSeconds >= 2.5)
            {
                _lastDeviceHealAttempt = DateTimeOffset.UtcNow;
                TryStartWindowsOutputDevice();
            }
        }

        // Sleep timer check
        if (_sleepDeadlineUtc is { } dl && DateTimeOffset.UtcNow >= dl)
        {
            _sleepDeadlineUtc = null;
            Pause();
            ToastRequested?.Invoke("Sleep timer paused playback");
        }

        // While a newly selected track is resolving, do not let the previous deck advance,
        // trigger SponsorBlock/crossfade, or publish its old timeline for the new title.
        if (IsLoading)
        {
            if ((DateTimeOffset.UtcNow - _lastExternalSyncUtc).TotalSeconds >= 5.0)
            {
                _lastExternalSyncUtc = DateTimeOffset.UtcNow;
                _smtc.UpdateTrackAndState(CurrentTrack, false, 0, CurrentTrack?.DurationSeconds ?? 0);
            }
            return;
        }

        // Gapless pre-warm & Crossfade check
        var deck = _mixer.PrimaryDeck;
        if (deck is null) return;
        RecordCurrentHistoryIfAny(forceUpdate: false);
        if (_mixer.IsPaused) return;
        double dur = deck.DurationSeconds;
        double pos = deck.PositionSeconds;

        // 1. SponsorBlock non-music / intro / outro auto-skip
        var skipSnapshot = _activeSkipSegments;
        if (_store.Settings.SponsorBlockEnabled && skipSnapshot.Count > 0)
        {
            for (int i = 0; i < skipSnapshot.Count; i++)
            {
                var seg = skipSnapshot[i];
                if (pos >= Math.Max(0, seg.StartSec - 0.06) && pos < seg.EndSec - 0.25 && _triggeredSkipIndices.Add(i))
                {
                    double jumpTo = Math.Min(dur > 1 ? dur - 0.25 : seg.EndSec, seg.EndSec);
                    Seek(jumpTo);
                    ToastRequested?.Invoke($"SponsorBlock skipped {seg.Category.Replace('_', ' ')} ({(int)(seg.EndSec - seg.StartSec)}s)");
                    pos = jumpTo;
                    break;
                }
            }
        }

        // Periodic SMTC timeline keepalive every ~5s
        if ((DateTimeOffset.UtcNow - _lastExternalSyncUtc).TotalSeconds >= 5.0)
        {
            _lastExternalSyncUtc = DateTimeOffset.UtcNow;
            _smtc.UpdateTrackAndState(CurrentTrack, IsPlaying, pos, dur);
        }

        if (dur <= 8) return;

        double rem = dur - pos;
        int xfadeSec = _store.Settings.CrossfadeSeconds;

        // With Gapless enabled, build the full next deck 15 seconds early. When disabled, still
        // resolve/verify the next stream near the end so NextAsync does less network work on click;
        // this lighter warm-up does not decode or hold a second audio deck.
        double prewarmWindow = _store.Settings.GaplessPreload ? 16.0 : 8.0;
        if (rem <= prewarmWindow && rem > 1.0)
        {
            var nextIdx = PeekNextIndex();
            if (nextIdx >= 0 && nextIdx < Queue.Count)
            {
                var nextTrack = Queue[nextIdx];
                if (_store.Settings.GaplessPreload)
                {
                    if (_prewarmedNextId != nextTrack.Id)
                    {
                        _prewarmedNextId = nextTrack.Id;
                        _prewarmedNextTask = Task.Run(() => BuildDeckAsync(nextTrack, CancellationToken.None));
                    }
                }
                else if (!ResourceSaverState.IsActive && _lightPrewarmNextId != nextTrack.Id)
                {
                    _lightPrewarmNextId = nextTrack.Id;
                    _ = Task.Run(async () =>
                    {
                        try { await _resolver.ResolveVerifiedAsync(nextTrack.Id, preferWebmOpus: true, forceRefresh: false, CancellationToken.None); }
                        catch { /* Best-effort warm-up; normal playback retries resolution if it fails. */ }
                    });
                }
            }
        }

        // Crossfade trigger
        if (xfadeSec > 0 && !_crossfadeTriggeredForTrack && rem <= xfadeSec && rem > 0.4 && Repeat != RepeatMode.One)
        {
            var nextIdx = PeekNextIndex();
            if (nextIdx >= 0 && nextIdx < Queue.Count)
            {
                _crossfadeTriggeredForTrack = true;
                _ = PerformCrossfadeAdvanceAsync(nextIdx, xfadeSec);
            }
        }
    }

    private int PeekNextIndex()
    {
        if (Queue.Count == 0) return -1;
        if (CurrentIndex + 1 < Queue.Count) return CurrentIndex + 1;
        return Repeat == RepeatMode.All ? 0 : -1;
    }

    // --------------------------------------------------------- Queue & Playback Controls

    public async Task PlayQueueAsync(
        IEnumerable<TrackItem> tracks,
        int startIndex = 0,
        bool autoRadioIfSingle = true,
        PlaybackCollectionContext? collectionContext = null)
    {
        var list = tracks.Select(t => t.Clone()).ToList();
        if (list.Count == 0) return;

        // Save the outgoing session before changing the collection metadata.
        RecordCurrentHistoryIfAny();
        QueueSource = collectionContext;
        _playlistHistoryRecordedForQueue = false;
        Queue.Clear();
        Queue.AddRange(list);
        CurrentIndex = Math.Clamp(startIndex, 0, Queue.Count - 1);

        if (ShuffleEnabled && Queue.Count > 2)
        {
            var first = Queue[CurrentIndex];
            Queue.RemoveAt(CurrentIndex);
            ShuffleInPlace(Queue);
            Queue.Insert(0, first);
            CurrentIndex = 0;
        }

        await LoadCurrentTrackAsync();
        if (collectionContext is not null && QueueSource == collectionContext && IsPlaying)
            RecordQueueSourceIfPending();

        // If user clicked a single song, populate intelligent Echo Brain + Radio continuation in background!
        if (autoRadioIfSingle && Queue.Count == 1 && !Queue[0].IsLocal && _store.Settings.AutoplayEchoBrain)
        {
            var seed = Queue[0];
            _ = Task.Run(async () =>
            {
                try
                {
                    var radio = await _yt.GetRadioQueueAsync(seed.Id, seed.PlaylistId);
                    var filtered = radio.Where(r => r.Id != seed.Id && (!_store.Settings.HideVideoSongs || !r.IsVideo) && !_store.IsTrackExcluded(r)).Take(25).ToList();
                    if (filtered.Count > 0 && CurrentTrack?.Id == seed.Id && Queue.Count == 1)
                    {
                        Queue.AddRange(filtered);
                        StateChanged?.Invoke();
                    }
                }
                catch { /* non-fatal */ }
            });
        }
    }

    public void RemoveDiscoveryExcludedQueuedTracks()
    {
        if (Queue.Count == 0) return;
        for (int i = Queue.Count - 1; i >= 0; i--)
        {
            if (i == CurrentIndex) continue; // Do not interrupt the track the listener explicitly started.
            var item = Queue[i];
            bool filtered = _store.IsTrackExcluded(item) || (_store.Settings.HideVideoSongs && item.IsVideo);
            if (!filtered) continue;
            Queue.RemoveAt(i);
            if (i < CurrentIndex) CurrentIndex--;
        }
        if (Queue.Count == 0) CurrentIndex = 0;
        else CurrentIndex = Math.Clamp(CurrentIndex, 0, Queue.Count - 1);
        StateChanged?.Invoke();
    }

    public Task PlaySingleAsync(TrackItem track) => PlayQueueAsync(new[] { track }, 0, autoRadioIfSingle: true);

    public void PlayNextInQueue(TrackItem track)
    {
        var copy = track.Clone();
        if (Queue.Count == 0)
        {
            _ = PlaySingleAsync(copy);
            return;
        }
        int insertAt = Math.Min(Queue.Count, CurrentIndex + 1);
        Queue.Insert(insertAt, copy);
        _prewarmedNextId = null;
        ToastRequested?.Invoke($"Playing next: {copy.Title}");
        StateChanged?.Invoke();
    }

    public void AddToQueue(TrackItem track)
    {
        var copy = track.Clone();
        if (Queue.Count == 0)
        {
            _ = PlaySingleAsync(copy);
            return;
        }
        Queue.Add(copy);
        ToastRequested?.Invoke($"Added to queue: {copy.Title}");
        StateChanged?.Invoke();
    }

    public void RemoveFromQueueAt(int index)
    {
        if (index < 0 || index >= Queue.Count) return;
        bool removingCurrent = index == CurrentIndex;
        Queue.RemoveAt(index);
        if (Queue.Count == 0)
        {
            Stop();
            CurrentIndex = -1;
            TrackChanged?.Invoke(null);
            StateChanged?.Invoke();
            return;
        }
        if (index < CurrentIndex) CurrentIndex--;
        else if (removingCurrent)
        {
            CurrentIndex = Math.Min(CurrentIndex, Queue.Count - 1);
            _ = LoadCurrentTrackAsync();
        }
        _prewarmedNextId = null;
        StateChanged?.Invoke();
    }

    public void MoveInQueue(int fromIdx, int toIdx)
    {
        if (fromIdx < 0 || fromIdx >= Queue.Count || toIdx < 0 || toIdx >= Queue.Count || fromIdx == toIdx) return;
        var cur = CurrentTrack;
        var item = Queue[fromIdx];
        Queue.RemoveAt(fromIdx);
        Queue.Insert(toIdx, item);
        if (cur is not null) CurrentIndex = Queue.IndexOf(cur);
        _prewarmedNextId = null;
        StateChanged?.Invoke();
    }

    public void ClearUpcomingQueue()
    {
        if (CurrentIndex < 0 || Queue.Count <= CurrentIndex + 1) return;
        Queue.RemoveRange(CurrentIndex + 1, Queue.Count - (CurrentIndex + 1));
        _prewarmedNextId = null;
        StateChanged?.Invoke();
    }

    public async Task JumpToQueueIndexAsync(int index)
    {
        if (index < 0 || index >= Queue.Count) return;
        RecordCurrentHistoryIfAny();
        CurrentIndex = index;
        await LoadCurrentTrackAsync();
    }

    public void TogglePlayPause()
    {
        if (IsLoading) return;
        if (_mixer.PrimaryDeck is null)
        {
            if (CurrentTrack is not null) _ = LoadCurrentTrackAsync();
            return;
        }
        bool pause = !_mixer.IsPaused;
        _mixer.IsPaused = pause;
        if (pause) FlushQueuedOutput();
        StateChanged?.Invoke();
    }

    private void FlushQueuedOutputDeferred()
    {
        _ = Task.Run(() =>
        {
            if (!_outputFlushGate.Wait(0)) return; // coalesce rapid scrubbing; one reset is enough
            try { FlushQueuedOutput(); }
            finally { _outputFlushGate.Release(); }
        });
    }

    private void FlushQueuedOutput()
    {
        try { _win32WaveOut?.FlushQueuedAudio(); } catch { }
        if (_waveOut is { PlaybackState: PlaybackState.Playing } wave)
        {
            try
            {
                wave.Stop();
                wave.Play(); // The mixer is paused, so restarted buffers contain silence until playback resumes.
            }
            catch { }
        }
    }

    public void Pause()
    {
        RecordCurrentHistoryIfAny();
        _mixer.IsPaused = true;
        FlushQueuedOutput();
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        if (!IsLoading && _mixer.PrimaryDeck is not null)
        {
            _mixer.IsPaused = false;
            StateChanged?.Invoke();
        }
    }

    public void Stop()
    {
        RecordCurrentHistoryIfAny();
        DeckState? old;
        DeckState? fading;
        lock (_loadStateLock)
        {
            _loadCts?.Cancel();
            _trackLoadGeneration++;
            IsLoading = false;
            _mixer.IsPaused = true;
            old = _mixer.PrimaryDeck;
            fading = _mixer.CrossfadeOutDeck;
            _mixer.PrimaryDeck = null;
            _mixer.CrossfadeOutDeck = null;
        }
        FlushQueuedOutput();
        _mixer.ResetReverbState();
        old?.Dispose();
        fading?.Dispose();
        StateChanged?.Invoke();
    }

    public void Seek(double seconds)
    {
        if (IsLoading) return;
        var deck = _mixer.PrimaryDeck;
        if (deck is null) return;
        deck.Seek(seconds);
        _mixer.ResetSpeedBuffer();
        _mixer.ResetReverbState();
        // Reset hardware buffers off the UI thread; driver stop/reset calls can block while scrubbing.
        FlushQueuedOutputDeferred();
        StateChanged?.Invoke();
    }

    public void SetVolume(double vol01)
    {
        vol01 = Math.Clamp(vol01, 0, 1);
        _store.Settings.Volume = vol01;
        _mixer.MasterVolume = (float)vol01;
        if (vol01 <= 0.001 && _store.Settings.PauseOnMute && IsPlaying)
            Pause();
        _store.SaveDebounced();
        StateChanged?.Invoke();
    }

    public void ToggleMute()
    {
        _mixer.IsMuted = !_mixer.IsMuted;
        if (_mixer.IsMuted && _store.Settings.PauseOnMute && IsPlaying)
            Pause();
        StateChanged?.Invoke();
    }

    public bool IsMuted => _mixer.IsMuted;

    public void ToggleShuffle()
    {
        ShuffleEnabled = !ShuffleEnabled;
        if (ShuffleEnabled && Queue.Count > CurrentIndex + 2)
        {
            var upcoming = Queue.Skip(CurrentIndex + 1).ToList();
            Queue.RemoveRange(CurrentIndex + 1, upcoming.Count);
            ShuffleInPlace(upcoming);
            Queue.AddRange(upcoming);
            _prewarmedNextId = null;
        }
        StateChanged?.Invoke();
    }

    public void CycleRepeat()
    {
        Repeat = Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
        StateChanged?.Invoke();
    }

    public void SetSleepTimer(int minutes, bool endOfTrack = false)
    {
        if (minutes <= 0 && !endOfTrack)
        {
            _sleepDeadlineUtc = null;
            _sleepEndOfTrack = false;
            ToastRequested?.Invoke("Sleep timer turned off");
        }
        else if (endOfTrack)
        {
            _sleepEndOfTrack = true;
            _sleepDeadlineUtc = null;
            ToastRequested?.Invoke("Sleep timer set for end of current song");
        }
        else
        {
            _sleepEndOfTrack = false;
            _sleepDeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
            ToastRequested?.Invoke($"Sleep timer set for {minutes} min");
        }
        StateChanged?.Invoke();
    }

    public async Task NextAsync()
    {
        if (Queue.Count == 0) return;
        RecordCurrentHistoryIfAny();

        if (CurrentIndex + 1 < Queue.Count)
        {
            CurrentIndex++;
            await LoadCurrentTrackAsync();
            return;
        }
        if (Repeat == RepeatMode.All)
        {
            CurrentIndex = 0;
            await LoadCurrentTrackAsync();
            return;
        }
        if (_store.Settings.AutoplayEchoBrain && CurrentTrack is { IsLocal: false } seed)
        {
            try
            {
                var more = await _yt.GetRadioQueueAsync(seed.Id);
                var fresh = more.Where(m => Queue.All(q => q.Id != m.Id) && !_store.IsTrackExcluded(m) && (!_store.Settings.HideVideoSongs || !m.IsVideo)).Take(18).ToList();
                if (fresh.Count > 0)
                {
                    Queue.AddRange(fresh);
                    CurrentIndex++;
                    await LoadCurrentTrackAsync();
                    return;
                }
            }
            catch { }
        }
        Pause();
        Seek(0);
    }

    public async Task PreviousAsync()
    {
        if (Queue.Count == 0) return;
        if (PositionSeconds > 3.5)
        {
            Seek(0);
            return;
        }
        RecordCurrentHistoryIfAny();
        if (CurrentIndex > 0) CurrentIndex--;
        else if (Repeat == RepeatMode.All) CurrentIndex = Queue.Count - 1;
        await LoadCurrentTrackAsync();
    }

    private void OnPrimaryTrackEnded()
    {
        if (_sleepEndOfTrack)
        {
            _sleepEndOfTrack = false;
            Pause();
            ToastRequested?.Invoke("Sleep timer paused at end of track");
            return;
        }
        if (Repeat == RepeatMode.One && _mixer.PrimaryDeck is not null)
        {
            RecordCurrentHistoryIfAny();
            Seek(0);
            Resume();
            return;
        }
        _ = NextAsync();
    }

    private async Task PerformCrossfadeAdvanceAsync(int nextIdx, int xfadeSec)
    {
        int generation = Volatile.Read(ref _trackLoadGeneration);
        string? outgoingTrackId = CurrentTrack?.Id;
        try
        {
            RecordCurrentHistoryIfAny();
            if (nextIdx < 0 || nextIdx >= Queue.Count) return;
            var nextTrack = Queue[nextIdx];
            DeckState? nextDeck = null;
            if (_prewarmedNextId == nextTrack.Id && _prewarmedNextTask is not null)
                nextDeck = await _prewarmedNextTask;
            nextDeck ??= await BuildDeckAsync(nextTrack, CancellationToken.None);
            if (nextDeck is null) return;

            bool stale;
            lock (_loadStateLock)
            {
                stale = generation != _trackLoadGeneration
                    || IsLoading
                    || _mixer.IsPaused
                    || CurrentTrack?.Id != outgoingTrackId;
                if (!stale)
                {
                    CurrentIndex = nextIdx;
                    _prewarmedNextId = null;
                    _prewarmedNextTask = null;
                    _crossfadeTriggeredForTrack = false;

                    var oldPrimary = _mixer.PrimaryDeck;
                    _mixer.CrossfadeOutDeck?.Dispose();
                    _mixer.CrossfadeOutDeck = oldPrimary;
                    _mixer.CrossfadeTotalSec = xfadeSec;
                    _mixer.CrossfadeRemainingSec = xfadeSec;
                    _mixer.PrimaryDeck = nextDeck;
                    _mixer.IsPaused = false;
                }
            }
            if (stale)
            {
                nextDeck.Dispose();
                return;
            }

            TrackChanged?.Invoke(nextTrack);
            StateChanged?.Invoke();
        }
        catch { /* fallback to normal end-of-track */ }
    }

    public void RefreshSponsorBlockForCurrentTrack()
    {
        Interlocked.Increment(ref _sponsorBlockRequestGeneration);
        _activeSkipSegments = new();
        _triggeredSkipIndices.Clear();
        var track = CurrentTrack;
        if (track is null || track.IsLocal || !_store.Settings.SponsorBlockEnabled) return;
        FetchSponsorBlockSegments(track, _loadCts?.Token ?? CancellationToken.None);
    }

    private void FetchSponsorBlockSegments(TrackItem track, CancellationToken ct)
    {
        int generation = Interlocked.Increment(ref _sponsorBlockRequestGeneration);
        string categories = _store.Settings.SponsorBlockAllSegments
            ? "sponsor,selfpromo,interaction,intro,outro,preview,hook,filler,music_offtopic,poi_highlight,exclusive_access,chapter"
            : "intro,outro";
        _ = Task.Run(async () =>
        {
            try
            {
                var segs = await _sponsorBlock.GetSegmentsAsync(track.Id, categories, ct);
                if (!ct.IsCancellationRequested && generation == Volatile.Read(ref _sponsorBlockRequestGeneration) && CurrentTrack?.Id == track.Id && _store.Settings.SponsorBlockEnabled)
                    _activeSkipSegments = segs;
            }
            catch (OperationCanceledException) { }
            catch { }
        }, CancellationToken.None);
    }

    private async Task LoadCurrentTrackAsync()
    {
        var track = CurrentTrack;
        if (track is null) return;

        CancellationTokenSource cts;
        int generation;
        DeckState? outgoing;
        DeckState? fading;
        lock (_loadStateLock)
        {
            _loadCts?.Cancel();
            cts = new CancellationTokenSource();
            _loadCts = cts;
            generation = ++_trackLoadGeneration;
            IsLoading = true;
            LastError = null;
            _mixer.IsPaused = true;
            outgoing = _mixer.PrimaryDeck;
            fading = _mixer.CrossfadeOutDeck;
            _mixer.PrimaryDeck = null;
            _mixer.CrossfadeOutDeck = null;
        }
        var ct = cts.Token;

        // Flush device buffers and detach the outgoing deck before resolving the next stream.
        // This prevents both queued audio and a stale deck from leaking into the new selection.
        FlushQueuedOutput();
        outgoing?.Dispose();
        fading?.Dispose();
        _mixer.ResetSpeedBuffer();
        _mixer.ResetReverbState();
        _crossfadeTriggeredForTrack = false;
        _activeSkipSegments = new();
        _triggeredSkipIndices.Clear();
        TrackChanged?.Invoke(track);
        StateChanged?.Invoke();

        if (!track.IsLocal && _store.Settings.SponsorBlockEnabled)
            FetchSponsorBlockSegments(track, ct);

        try
        {
            DeckState? newDeck = null;
            if (_prewarmedNextId == track.Id && _prewarmedNextTask is not null)
            {
                try { newDeck = await _prewarmedNextTask; } catch { newDeck = null; }
                _prewarmedNextId = null;
                _prewarmedNextTask = null;
            }

            newDeck ??= await BuildDeckAsync(track, ct);

            bool stale;
            lock (_loadStateLock)
            {
                stale = ct.IsCancellationRequested
                    || generation != _trackLoadGeneration
                    || !ReferenceEquals(_loadCts, cts);
                if (!stale && newDeck is not null)
                {
                    if (track.DurationSeconds <= 0 && newDeck.DurationSeconds > 0)
                    {
                        track.DurationSeconds = (int)Math.Round(newDeck.DurationSeconds);
                        track.DurationText = TrackItem.FormatSeconds(track.DurationSeconds);
                    }

                    var old = _mixer.PrimaryDeck;
                    var oldFade = _mixer.CrossfadeOutDeck;
                    _mixer.CrossfadeOutDeck = null;
                    _mixer.ResetSpeedBuffer();
                    _mixer.PrimaryDeck = newDeck;
                    _mixer.IsPaused = false;
                    old?.Dispose();
                    oldFade?.Dispose();
                    IsLoading = false;
                }
                else if (!stale)
                {
                    IsLoading = false;
                    LastError = "Could not resolve an audio stream for this track.";
                }
            }

            if (stale)
            {
                newDeck?.Dispose();
                return;
            }
            StateChanged?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A newer track selection or Stop superseded this request.
        }
        catch (Exception ex)
        {
            bool current;
            lock (_loadStateLock)
            {
                current = generation == _trackLoadGeneration && ReferenceEquals(_loadCts, cts);
                if (current)
                {
                    IsLoading = false;
                    LastError = ex.Message;
                }
            }
            if (current)
            {
                ToastRequested?.Invoke($"Playback error: {ex.Message}");
                StateChanged?.Invoke();
            }
        }
    }

    private async Task<DeckState?> BuildDeckAsync(TrackItem track, CancellationToken ct)
    {
        // 1. Local file or Offline Downloaded file?
        string? offlinePath = track.LocalFilePath ?? _store.TryGetDownloadedFilePath(track.Id);
        if (!string.IsNullOrEmpty(offlinePath) && File.Exists(offlinePath))
        {
            if (offlinePath.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
            {
                var fs = new FileStream(offlinePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var webm = new WebmOpusSampleSource(fs, track.DurationSeconds, ownsStream: true);
                return new DeckState(track, webm, webm, webm, null, null, null);
            }
            if (OperatingSystem.IsWindows())
            {
                var reader = new AudioFileReader(offlinePath);
                ISampleProvider sp = reader;
                if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
                if (sp.WaveFormat.SampleRate != EchoMasterMixer.SampleRate)
                    sp = new WdlResamplingSampleProvider(sp, EchoMasterMixer.SampleRate);
                return new DeckState(track, sp, reader, null, reader, null, null);
            }
        }

        // 2. Online YouTube Music Stream: resolve & byte-verify via VISIONOS -> ANDROID_VR -> IOS -> PIPED
        var verified = await _resolver.ResolveVerifiedAsync(track.Id, preferWebmOpus: true, forceRefresh: false, ct);
        var resolved = verified.Stream;
        var httpStream = new BoundedRangeHttpStream(_yt.SharedHttp, resolved, verified.InitialPage0);

        if (resolved.MimeType.Contains("webm", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var webm = new WebmOpusSampleSource(httpStream, resolved.DurationSeconds, ownsStream: true);
                return new DeckState(track, webm, webm, webm, null, httpStream, resolved);
            }
            catch when (OperatingSystem.IsWindows())
            {
                httpStream.Dispose();
                // Automatic Windows MediaFoundation AAC (itag 140) fallback if WebM header was unusual
                var mp4Verified = await _resolver.ResolveVerifiedAsync(track.Id, preferWebmOpus: false, forceRefresh: true, ct);
                var mp4Stream = new BoundedRangeHttpStream(_yt.SharedHttp, mp4Verified.Stream, mp4Verified.InitialPage0);
                var mf = new StreamMediaFoundationReader(mp4Stream);
                ISampleProvider sp = mf.ToSampleProvider();
                if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
                if (sp.WaveFormat.SampleRate != EchoMasterMixer.SampleRate)
                    sp = new WdlResamplingSampleProvider(sp, EchoMasterMixer.SampleRate);
                return new DeckState(track, sp, mf, null, null, mp4Stream, mp4Verified.Stream);
            }
        }

        // Native Windows MediaFoundation decoder for audio/mp4 (itag 140/139)
        if (OperatingSystem.IsWindows())
        {
            var mf = new StreamMediaFoundationReader(httpStream);
            ISampleProvider sp = mf.ToSampleProvider();
            if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
            if (sp.WaveFormat.SampleRate != EchoMasterMixer.SampleRate)
                sp = new WdlResamplingSampleProvider(sp, EchoMasterMixer.SampleRate);
            return new DeckState(track, sp, mf, null, null, httpStream, resolved);
        }

        httpStream.Dispose();
        throw new InvalidOperationException($"Unsupported container: {resolved.MimeType}");
    }

    private void RecordCurrentHistoryIfAny(bool forceUpdate = true)
    {
        var deck = _mixer.PrimaryDeck;
        if (deck is null) return;
        if (!ReferenceEquals(_historyDeck, deck))
        {
            _historyDeck = deck;
            _historyEntry = null;
            _historyLastPersistedMs = 0;
        }

        long ms = (long)(deck.PositionSeconds * 1000);
        if (ms < 4000) return; // ignore micro-clicks
        bool skipped = deck.DurationSeconds > 20 && deck.PositionSeconds < Math.Min(25, deck.DurationSeconds * 0.25);
        if (_historyEntry is null)
        {
            // Record the source playlist only once per playback queue, after audible playback begins.
            RecordQueueSourceIfPending();
            _historyEntry = _store.RecordPlay(deck.Track, ms, skipped);
            _historyLastPersistedMs = ms;
            return;
        }

        if (!forceUpdate && ms - _historyLastPersistedMs < 15_000) return;
        _store.UpdatePlayHistory(_historyEntry, ms, skipped);
        _historyLastPersistedMs = ms;
    }

    private void RecordQueueSourceIfPending()
    {
        if (_playlistHistoryRecordedForQueue || QueueSource is not { } source) return;
        _store.RecordCollectionPlay(source);
        _playlistHistoryRecordedForQueue = true;
    }

    private static void ShuffleInPlace<T>(IList<T> list)
    {
        var rng = Random.Shared;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public async Task PlayLocalFilePathsAsync(IEnumerable<string> paths)
    {
        var tracks = new List<TrackItem>();
        foreach (var file in paths.Where(File.Exists))
        {
            string title = Path.GetFileNameWithoutExtension(file);
            string artist = "Local File";
            string? album = null;
            int durSec = 0;
            try
            {
                using var tf = TagLib.File.Create(file);
                if (!string.IsNullOrWhiteSpace(tf.Tag.Title)) title = tf.Tag.Title;
                if (tf.Tag.Performers is { Length: > 0 } p && !string.IsNullOrWhiteSpace(p[0])) artist = string.Join(", ", p);
                if (!string.IsNullOrWhiteSpace(tf.Tag.Album)) album = tf.Tag.Album;
                durSec = (int)Math.Round(tf.Properties.Duration.TotalSeconds);
            }
            catch { }
            tracks.Add(new TrackItem
            {
                Id = "local:" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(file)))[..16],
                Title = title,
                Artists = new List<ArtistRef> { new() { Name = artist } },
                Album = album is null ? null : new AlbumRef { Name = album },
                DurationSeconds = durSec,
                DurationText = durSec > 0 ? TrackItem.FormatSeconds(durSec) : "",
                LocalFilePath = file,
            });
        }
        if (tracks.Count > 0)
            await PlayQueueAsync(tracks, 0, autoRadioIfSingle: false);
    }

    public void Dispose()
    {
        RecordCurrentHistoryIfAny();
        _loadCts?.Cancel();
        _linuxPumpCts?.Cancel();
        try { _win32WaveOut?.Dispose(); } catch { }
        try { _waveOut?.Stop(); _waveOut?.Dispose(); } catch { }
        lock (_deviceChangeDebounceLock)
        {
            _deviceChangeDebounce?.Cancel();
            _deviceChangeDebounce?.Dispose();
            _deviceChangeDebounce = null;
        }
        if (_audioEndpointWatcher is not null)
        {
            _audioEndpointWatcher.OutputEndpointsChanged -= ScheduleAudioEndpointRefresh;
            _audioEndpointWatcher.Dispose();
            _audioEndpointWatcher = null;
        }
        _smtc.Dispose();
        _discord.Dispose();
        _mixer.PrimaryDeck?.Dispose();
        _mixer.CrossfadeOutDeck?.Dispose();
    }
}
