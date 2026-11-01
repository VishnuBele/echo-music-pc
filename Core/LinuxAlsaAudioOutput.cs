using System.Runtime.InteropServices;

namespace EchoMusic.Core;

/// <summary>Blocking 48 kHz stereo float output through ALSA's default device.</summary>
internal sealed class LinuxAlsaAudioOutput : IDisposable
{
    private const int StreamPlayback = 0;
    private const int AccessRwInterleaved = 3;
    private const int FormatFloatLe = 14;
    private const uint Channels = 2;
    private const uint SampleRate = 48_000;
    private const uint LatencyMicroseconds = 200_000;
    private IntPtr _pcm;

    private LinuxAlsaAudioOutput(IntPtr pcm) => _pcm = pcm;

    public static LinuxAlsaAudioOutput OpenDefault()
    {
        int result = snd_pcm_open(out var pcm, "default", StreamPlayback, 0);
        if (result < 0)
            throw new InvalidOperationException($"Could not open the ALSA default playback device: {AlsaError(result)}");

        result = snd_pcm_set_params(pcm, FormatFloatLe, AccessRwInterleaved, Channels, SampleRate,
            softResample: 1, LatencyMicroseconds);
        if (result < 0)
        {
            snd_pcm_close(pcm);
            throw new InvalidOperationException($"Could not configure Linux audio output: {AlsaError(result)}");
        }
        return new LinuxAlsaAudioOutput(pcm);
    }

    public unsafe void Write(float[] interleavedStereo, int frames)
    {
        if (_pcm == IntPtr.Zero) throw new ObjectDisposedException(nameof(LinuxAlsaAudioOutput));
        if (frames <= 0) return;
        fixed (float* start = interleavedStereo)
        {
            float* current = start;
            ulong remaining = (ulong)frames;
            while (remaining > 0)
            {
                long written = snd_pcm_writei(_pcm, (IntPtr)current, remaining);
                if (written < 0)
                {
                    int recovered = snd_pcm_recover(_pcm, checked((int)written), 1);
                    if (recovered < 0)
                        throw new InvalidOperationException($"Linux audio playback failed: {AlsaError(recovered)}");
                    continue;
                }
                if (written == 0)
                    throw new InvalidOperationException("Linux audio device stopped accepting samples.");
                remaining -= (ulong)written;
                current += written * (int)Channels;
            }
        }
    }

    public void Dispose()
    {
        var pcm = Interlocked.Exchange(ref _pcm, IntPtr.Zero);
        if (pcm == IntPtr.Zero) return;
        try { snd_pcm_drop(pcm); } catch { }
        try { snd_pcm_close(pcm); } catch { }
    }

    private static string AlsaError(int code)
    {
        try { return Marshal.PtrToStringAnsi(snd_strerror(code)) ?? $"ALSA error {code}"; }
        catch { return $"ALSA error {code}"; }
    }

    [DllImport("libasound.so.2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_open(out IntPtr pcm, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int stream, int mode);

    [DllImport("libasound.so.2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_set_params(IntPtr pcm, int format, int access, uint channels,
        uint rate, int softResample, uint latency);

    [DllImport("libasound.so.2", CallingConvention = CallingConvention.Cdecl)]
    private static extern long snd_pcm_writei(IntPtr pcm, IntPtr buffer, ulong frames);

    [DllImport("libasound.so.2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_recover(IntPtr pcm, int error, int silent);

    [DllImport("libasound.so.2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_drop(IntPtr pcm);

    [DllImport("libasound.so.2", CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_close(IntPtr pcm);

    [DllImport("libasound.so.2", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr snd_strerror(int error);
}
