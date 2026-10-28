using System.Buffers.Binary;
using Concentus;
using NAudio.Wave;

namespace EchoMusic.Core;

/// <summary>
/// Pure managed C# WebM / Matroska EBML demuxer + Concentus RFC-6716 Opus 48kHz stereo float32 decoder.
/// Plays YouTube Music's highest-quality audio streams (itag 251/250/249 WebM/Opus) and offline .webm files
/// natively on 100% of Windows 10/11 (including Windows N) and Linux systems with zero external binaries.
/// </summary>
public sealed class WebmOpusSampleSource : ISampleProvider, IDisposable
{
    public const int OutputSampleRate = 48000;
    public const int OutputChannels = 2;
    private const int MaxOpusFrameSamples = 5760; // 120ms @ 48kHz

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly object _lock = new();
    private readonly IOpusDecoder _decoder;
    private readonly float[] _decodeScratch = new float[MaxOpusFrameSamples * OutputChannels];
    private readonly List<(double TimeSec, long ClusterPos)> _cuePoints = new();

    private long _segmentDataStart;
    private long _firstClusterOffset;
    private long _timestampScaleNs = 1_000_000; // default 1ms
    private double _durationSec;
    private int _sourceChannels = 2;
    private int _preSkipSamples;

    private long _currentClusterTimeUnits;
    private long _clusterEndPos = long.MaxValue;
    private double _currentPositionSec;
    private bool _eof;

    private float[] _pcmRing = new float[MaxOpusFrameSamples * OutputChannels * 4];
    private int _pcmReadIdx;
    private int _pcmCount;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(OutputSampleRate, OutputChannels);
    public double DurationSeconds => _durationSec;

    public double CurrentTimeSeconds
    {
        get { lock (_lock) return _currentPositionSec; }
    }

    public WebmOpusSampleSource(Stream stream, double fallbackDurationSec = 0, bool ownsStream = true)
    {
        _stream = stream;
        _ownsStream = ownsStream;
        _durationSec = fallbackDurationSec;

        ParseHeaderUntilFirstCluster();
        _decoder = OpusCodecFactory.CreateDecoder(OutputSampleRate, _sourceChannels);
    }

    private void ParseHeaderUntilFirstCluster()
    {
        _stream.Position = 0;
        while (_stream.Position < _stream.Length)
        {
            long elemStart = _stream.Position;
            if (!TryReadElementHeader(out ulong id, out long size)) break;

            if (id == 0x1A45DFA3) // EBML Header
            {
                SkipBytes(size);
            }
            else if (id == 0x18538067) // Segment
            {
                _segmentDataStart = _stream.Position;
                // Continue reading top-level children inside Segment
            }
            else if (id == 0x1549A966) // Info
            {
                ParseInfo(_stream.Position + size);
            }
            else if (id == 0x1654AE6B) // Tracks
            {
                ParseTracks(_stream.Position + size);
            }
            else if (id == 0x1C53BB6B) // Cues
            {
                ParseCues(_stream.Position + size);
            }
            else if (id == 0x1F43B675) // First Cluster!
            {
                _firstClusterOffset = elemStart;
                _stream.Position = elemStart;
                return;
            }
            else
            {
                if (size < 0 || _stream.Position + size > _stream.Length) break;
                SkipBytes(size);
            }
        }
    }

    private void ParseInfo(long endPos)
    {
        double rawDuration = 0;
        while (_stream.Position < endPos)
        {
            if (!TryReadElementHeader(out ulong id, out long size)) break;
            if (id == 0x2AD7B1) // TimestampScale
                _timestampScaleNs = (long)ReadUnsignedInt((int)size);
            else if (id == 0x4489) // Duration (float 4 or 8)
                rawDuration = ReadFloat((int)size);
            else
                SkipBytes(size);
        }
        if (rawDuration > 0)
        {
            var computed = rawDuration * _timestampScaleNs / 1_000_000_000.0;
            if (computed > 0.5) _durationSec = computed;
        }
        _stream.Position = Math.Min(endPos, _stream.Length);
    }

    private void ParseTracks(long endPos)
    {
        while (_stream.Position < endPos)
        {
            if (!TryReadElementHeader(out ulong id, out long size)) break;
            long childEnd = _stream.Position + size;
            if (id == 0xAE) // TrackEntry
            {
                while (_stream.Position < childEnd)
                {
                    if (!TryReadElementHeader(out ulong tid, out long tsize)) break;
                    long subEnd = _stream.Position + tsize;
                    if (tid == 0x63A2 && tsize >= 12) // CodecPrivate (OpusHead)
                    {
                        var head = ReadExact((int)tsize);
                        if (head.Length >= 12 && head[0] == (byte)'O' && head[1] == (byte)'p')
                        {
                            _sourceChannels = Math.Clamp((int)head[9], 1, 2);
                            _preSkipSamples = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(10, 2));
                        }
                    }
                    else if (tid == 0xE1) // Audio settings
                    {
                        while (_stream.Position < subEnd)
                        {
                            if (!TryReadElementHeader(out ulong aid, out long asize)) break;
                            if (aid == 0x9F) _sourceChannels = Math.Clamp((int)ReadUnsignedInt((int)asize), 1, 2);
                            else SkipBytes(asize);
                        }
                    }
                    else
                    {
                        SkipBytes(tsize);
                    }
                    _stream.Position = Math.Min(subEnd, _stream.Length);
                }
            }
            else
            {
                SkipBytes(size);
            }
            _stream.Position = Math.Min(childEnd, _stream.Length);
        }
        _stream.Position = Math.Min(endPos, _stream.Length);
    }

    private void ParseCues(long endPos)
    {
        while (_stream.Position < endPos)
        {
            if (!TryReadElementHeader(out ulong id, out long size)) break;
            long cpEnd = _stream.Position + size;
            if (id == 0xBB) // CuePoint
            {
                ulong cueTime = 0;
                ulong clusterRel = 0;
                bool hasPos = false;
                while (_stream.Position < cpEnd)
                {
                    if (!TryReadElementHeader(out ulong cid, out long csize)) break;
                    long subEnd = _stream.Position + csize;
                    if (cid == 0xB3) cueTime = ReadUnsignedInt((int)csize);
                    else if (cid == 0xB7) // CueTrackPositions
                    {
                        while (_stream.Position < subEnd)
                        {
                            if (!TryReadElementHeader(out ulong pid, out long psize)) break;
                            if (pid == 0xF1) { clusterRel = ReadUnsignedInt((int)psize); hasPos = true; }
                            else SkipBytes(psize);
                        }
                    }
                    else SkipBytes(csize);
                    _stream.Position = Math.Min(subEnd, _stream.Length);
                }
                if (hasPos)
                {
                    double tSec = (double)cueTime * _timestampScaleNs / 1_000_000_000.0;
                    _cuePoints.Add((tSec, _segmentDataStart + (long)clusterRel));
                }
            }
            else SkipBytes(size);
            _stream.Position = Math.Min(cpEnd, _stream.Length);
        }
        _stream.Position = Math.Min(endPos, _stream.Length);
    }

    public void Seek(double targetSeconds)
    {
        lock (_lock)
        {
            targetSeconds = Math.Clamp(targetSeconds, 0, _durationSec > 0 ? _durationSec : targetSeconds);
            _pcmReadIdx = 0;
            _pcmCount = 0;
            _eof = false;
            _decoder.ResetState();

            if (_cuePoints.Count > 0)
            {
                var best = _cuePoints[0];
                foreach (var cp in _cuePoints)
                {
                    if (cp.TimeSec <= targetSeconds) best = cp;
                    else break;
                }
                _stream.Position = Math.Clamp(best.ClusterPos, _firstClusterOffset, _stream.Length);
                _currentPositionSec = best.TimeSec;
            }
            else if (_durationSec > 1 && _stream.Length > _firstClusterOffset)
            {
                // Approximate byte jump & resync to next Cluster magic 0x1F 0x43 0xB6 0x75
                if (targetSeconds <= 0.5)
                {
                    _stream.Position = _firstClusterOffset;
                    _currentPositionSec = 0;
                }
                else
                {
                    double frac = Math.Clamp(targetSeconds / _durationSec, 0, 0.97);
                    long approx = _firstClusterOffset + (long)((_stream.Length - _firstClusterOffset) * frac);
                    long clusterPos = ScanForClusterMagic(Math.Max(_firstClusterOffset, approx - 32768));
                    _stream.Position = clusterPos >= 0 ? clusterPos : _firstClusterOffset;
                    _currentPositionSec = targetSeconds;
                }
            }
            else
            {
                _stream.Position = _firstClusterOffset;
                _currentPositionSec = 0;
            }
            _clusterEndPos = long.MaxValue;
        }
    }

    private long ScanForClusterMagic(long startPos)
    {
        _stream.Position = startPos;
        var buf = new byte[65536];
        for (int tries = 0; tries < 8 && _stream.Position < _stream.Length; tries++)
        {
            long basePos = _stream.Position;
            int n = _stream.Read(buf, 0, buf.Length);
            if (n < 4) break;
            for (int i = 0; i <= n - 4; i++)
            {
                if (buf[i] == 0x1F && buf[i + 1] == 0x43 && buf[i + 2] == 0xB6 && buf[i + 3] == 0x75)
                    return basePos + i;
            }
            if (n == buf.Length) _stream.Position = basePos + n - 3;
        }
        return -1;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            int written = 0;
            while (written < count)
            {
                if (_pcmCount > 0)
                {
                    int take = Math.Min(count - written, _pcmCount);
                    Array.Copy(_pcmRing, _pcmReadIdx, buffer, offset + written, take);
                    _pcmReadIdx += take;
                    _pcmCount -= take;
                    if (_pcmCount == 0) _pcmReadIdx = 0;
                    written += take;
                    _currentPositionSec += (double)(take / OutputChannels) / OutputSampleRate;
                    continue;
                }

                if (_eof || !DecodeNextPacket())
                {
                    _eof = true;
                    break;
                }
            }
            return written;
        }
    }

    private bool DecodeNextPacket()
    {
        while (_stream.Position < _stream.Length)
        {
            if (_stream.Position >= _clusterEndPos)
                _clusterEndPos = long.MaxValue;

            if (!TryReadElementHeader(out ulong id, out long size))
                return false;

            if (id == 0x1F43B675) // Cluster
            {
                _clusterEndPos = size < 0 ? long.MaxValue : _stream.Position + size;
                continue;
            }
            if (id == 0xE7) // Cluster Timestamp
            {
                _currentClusterTimeUnits = (long)ReadUnsignedInt((int)size);
                _currentPositionSec = _currentClusterTimeUnits * _timestampScaleNs / 1_000_000_000.0;
                continue;
            }
            if (id == 0xA0) // BlockGroup - step inside to read 0xA1 Block
            {
                continue;
            }
            if (id is 0xA3 or 0xA1) // SimpleBlock or Block
            {
                long blockEnd = _stream.Position + size;
                if (size <= 4 || blockEnd > _stream.Length)
                {
                    SkipBytes(Math.Max(0, size));
                    continue;
                }
                var block = ReadExact((int)size);
                DecodeBlockBytes(block);
                if (_pcmCount > 0) return true;
                continue;
            }

            // Skip any other metadata / cues / void elements encountered mid-stream
            if (size < 0 || _stream.Position + size > _stream.Length)
                return false;
            SkipBytes(size);
        }
        return false;
    }

    private void DecodeBlockBytes(byte[] block)
    {
        int pos = 0;
        if (!TryReadVintFromSpan(block, ref pos, out _)) return; // track number
        if (pos + 3 > block.Length) return;

        short relTime = BinaryPrimitives.ReadInt16BigEndian(block.AsSpan(pos, 2));
        byte flags = block[pos + 2];
        pos += 3;

        long ptsUnits = _currentClusterTimeUnits + relTime;
        if (ptsUnits >= 0)
            _currentPositionSec = ptsUnits * _timestampScaleNs / 1_000_000_000.0;

        int lacing = (flags >> 1) & 0x03;
        if (lacing == 0)
        {
            DecodeSingleOpusFrame(block.AsSpan(pos));
            return;
        }

        if (pos >= block.Length) return;
        int frameCount = block[pos++] + 1;
        var sizes = new int[frameCount];

        if (lacing == 1) // Xiph lacing
        {
            int sum = 0;
            for (int i = 0; i < frameCount - 1; i++)
            {
                int sz = 0;
                while (pos < block.Length)
                {
                    byte b = block[pos++];
                    sz += b;
                    if (b < 255) break;
                }
                sizes[i] = sz;
                sum += sz;
            }
            sizes[frameCount - 1] = Math.Max(0, (block.Length - pos) - sum);
        }
        else if (lacing == 2) // Fixed-size lacing
        {
            int each = (block.Length - pos) / frameCount;
            for (int i = 0; i < frameCount; i++) sizes[i] = each;
        }
        else // EBML lacing
        {
            int sum = 0;
            long prev = 0;
            for (int i = 0; i < frameCount - 1; i++)
            {
                if (i == 0)
                {
                    if (!TryReadVintFromSpan(block, ref pos, out ulong first)) return;
                    prev = (long)first;
                }
                else
                {
                    if (!TryReadSignedVintFromSpan(block, ref pos, out long delta)) return;
                    prev += delta;
                }
                sizes[i] = (int)Math.Max(0, prev);
                sum += sizes[i];
            }
            sizes[frameCount - 1] = Math.Max(0, (block.Length - pos) - sum);
        }

        for (int i = 0; i < frameCount; i++)
        {
            if (sizes[i] <= 0 || pos + sizes[i] > block.Length) break;
            DecodeSingleOpusFrame(block.AsSpan(pos, sizes[i]));
            pos += sizes[i];
        }
    }

    private void DecodeSingleOpusFrame(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty) return;
        try
        {
            int samplesPerCh = _decoder.Decode(packet, _decodeScratch, MaxOpusFrameSamples, false);
            if (samplesPerCh <= 0) return;

            int skipPerCh = 0;
            if (_preSkipSamples > 0)
            {
                skipPerCh = Math.Min(_preSkipSamples, samplesPerCh);
                _preSkipSamples -= skipPerCh;
                if (skipPerCh >= samplesPerCh) return;
            }

            int validPerCh = samplesPerCh - skipPerCh;
            int outFloats = validPerCh * OutputChannels;
            EnsureRingCapacity(outFloats);

            int writePos = _pcmReadIdx + _pcmCount;
            if (_sourceChannels == 2)
            {
                Array.Copy(_decodeScratch, skipPerCh * 2, _pcmRing, writePos, outFloats);
            }
            else
            {
                for (int i = 0; i < validPerCh; i++)
                {
                    float s = _decodeScratch[skipPerCh + i];
                    _pcmRing[writePos + i * 2] = s;
                    _pcmRing[writePos + i * 2 + 1] = s;
                }
            }
            _pcmCount += outFloats;
        }
        catch
        {
            // Corrupted packet: skip cleanly without crashing audio thread
        }
    }

    private void EnsureRingCapacity(int extraFloats)
    {
        if (_pcmReadIdx + _pcmCount + extraFloats <= _pcmRing.Length) return;
        if (_pcmReadIdx > 0 && _pcmCount + extraFloats <= _pcmRing.Length)
        {
            Array.Copy(_pcmRing, _pcmReadIdx, _pcmRing, 0, _pcmCount);
            _pcmReadIdx = 0;
            return;
        }
        var next = new float[Math.Max(_pcmRing.Length * 2, _pcmCount + extraFloats + 8192)];
        if (_pcmCount > 0) Array.Copy(_pcmRing, _pcmReadIdx, next, 0, _pcmCount);
        _pcmRing = next;
        _pcmReadIdx = 0;
    }

    // --------------------------------------------------------- EBML Primitives

    private bool TryReadElementHeader(out ulong id, out long size)
    {
        id = 0;
        size = 0;
        int b0 = _stream.ReadByte();
        if (b0 <= 0) return false;
        int idLen = LeadingZeroCountByte((byte)b0) + 1;
        if (idLen > 4) return false;
        id = (ulong)b0;
        for (int i = 1; i < idLen; i++)
        {
            int bi = _stream.ReadByte();
            if (bi < 0) return false;
            id = (id << 8) | (ulong)(byte)bi;
        }

        int s0 = _stream.ReadByte();
        if (s0 <= 0) return false;
        int sLen = LeadingZeroCountByte((byte)s0) + 1;
        if (sLen > 8) return false;
        ulong mask = (ulong)(0xFF >> sLen);
        ulong val = (ulong)(byte)s0 & mask;
        ulong allOnes = mask;
        for (int i = 1; i < sLen; i++)
        {
            int bi = _stream.ReadByte();
            if (bi < 0) return false;
            val = (val << 8) | (ulong)(byte)bi;
            allOnes = (allOnes << 8) | 0xFF;
        }
        size = val == allOnes ? -1 : (long)val;
        return true;
    }

    private static bool TryReadVintFromSpan(ReadOnlySpan<byte> buf, ref int pos, out ulong val)
    {
        val = 0;
        if (pos >= buf.Length || buf[pos] == 0) return false;
        byte b0 = buf[pos++];
        int len = LeadingZeroCountByte(b0) + 1;
        if (len > 8 || pos + len - 1 > buf.Length) return false;
        val = (ulong)(b0 & (0xFF >> len));
        for (int i = 1; i < len; i++)
            val = (val << 8) | buf[pos++];
        return true;
    }

    private static bool TryReadSignedVintFromSpan(ReadOnlySpan<byte> buf, ref int pos, out long val)
    {
        val = 0;
        if (pos >= buf.Length || buf[pos] == 0) return false;
        byte b0 = buf[pos];
        int len = LeadingZeroCountByte(b0) + 1;
        if (!TryReadVintFromSpan(buf, ref pos, out ulong raw)) return false;
        long bias = (1L << (len * 7 - 1)) - 1;
        val = (long)raw - bias;
        return true;
    }

    private static int LeadingZeroCountByte(byte b)
    {
        int n = 0;
        for (int bit = 7; bit >= 0; bit--)
        {
            if ((b & (1 << bit)) != 0) break;
            n++;
        }
        return n;
    }

    private ulong ReadUnsignedInt(int byteCount)
    {
        ulong v = 0;
        for (int i = 0; i < byteCount; i++)
        {
            int b = _stream.ReadByte();
            if (b < 0) break;
            v = (v << 8) | (ulong)(byte)b;
        }
        return v;
    }

    private double ReadFloat(int byteCount)
    {
        var b = ReadExact(byteCount);
        if (byteCount == 4 && b.Length == 4)
            return BinaryPrimitives.ReadSingleBigEndian(b);
        if (byteCount == 8 && b.Length == 8)
            return BinaryPrimitives.ReadDoubleBigEndian(b);
        return 0;
    }

    private byte[] ReadExact(int count)
    {
        var arr = new byte[Math.Max(0, count)];
        int off = 0;
        while (off < arr.Length)
        {
            int n = _stream.Read(arr, off, arr.Length - off);
            if (n <= 0) break;
            off += n;
        }
        return off == arr.Length ? arr : arr[..off];
    }

    private void SkipBytes(long count)
    {
        if (count <= 0) return;
        _stream.Position = Math.Min(_stream.Length, _stream.Position + count);
    }

    public void Dispose()
    {
        if (_ownsStream) _stream.Dispose();
    }
}
