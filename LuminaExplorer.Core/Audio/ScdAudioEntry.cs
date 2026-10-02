using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Scd;

namespace LuminaExplorer.Core.Audio;

/// <summary>
/// A decoded view of an audio entry of a <see cref="ScdFile"/>, independent from the <see cref="ScdFile"/> instance
/// it came from (so it can be used from any thread).
/// </summary>
/// <remarks>
/// Loop point units, as stored in SCD files:
/// <list type="bullet">
/// <item>Ogg Vorbis: byte offsets into the stream after the Ogg header pages, pointing to the start of the pages
/// containing the loop points. The end is commonly equal to the stream size. The sample-exact loop points are in
/// the LOOPSTART and LOOPEND (inclusive) Vorbis comments, which are used when present.</item>
/// <item>MS-ADPCM: sample frame indices; the end is inclusive.</item>
/// <item>PCM: byte offsets.</item>
/// </list>
/// All loop properties exposed by this class are normalized to sample frames, with an exclusive end.
/// </remarks>
public sealed class ScdAudioEntry {
    private ScdAudioEntry(int index, ScdFile.Audio audio)
    {
        var desc = audio.AudioBasicDesc;
        this.Index = index;
        this.Codec = (ScdAudioCodec) (uint) desc.Format;
        this.Channels = (int) desc.Channel;
        this.SampleRate = (int) desc.Rate;
        this.StreamSize = desc.Size;
        this.RawLoopStart = desc.LoopStart;
        this.RawLoopEnd = desc.LoopEnd;
        this.Data = audio.AudioData ?? [];

        try {
            switch (this.Codec) {
                case ScdAudioCodec.Pcm16 when this.Channels > 0: {
                    this.TotalFrames = this.Data.Length / (2 * this.Channels);
                    if (desc.LoopEnd > desc.LoopStart) {
                        this.LoopStartFrame = desc.LoopStart / (2 * this.Channels);
                        this.LoopEndFrame = desc.LoopEnd / (2 * this.Channels);
                    }

                    this.IsPlayable = true;
                    break;
                }

                case ScdAudioCodec.MsAdpcm when audio.AudioDataHeader is AdpcmWaveFormat wf && this.Channels > 0: {
                    this.AdpcmBlockAlign = wf.BlockAlign;
                    this.AdpcmSamplesPerBlock = wf.SamplesPerBlock;
                    this.AdpcmCoefficients = ReadCoefficients(wf);
                    this.TotalFrames = MsAdpcmDecoder.GetTotalFrames(this.Data.Length, wf.BlockAlign, this.Channels);
                    if (desc.LoopEnd > desc.LoopStart) {
                        this.LoopStartFrame = desc.LoopStart;
                        this.LoopEndFrame = desc.LoopEnd + 1L;
                    }

                    this.IsPlayable = wf.BlockAlign >= 7 * this.Channels;
                    if (!this.IsPlayable)
                        this.UnsupportedReason = $"Invalid MS-ADPCM block alignment {wf.BlockAlign}";
                    break;
                }

                case ScdAudioCodec.OggVorbis when audio.AudioDataHeader is OggVorbisSeekTableHeader oh: {
                    this.OggHeaderSize = (int) oh.OggHeaderSize;
                    this.TotalFrames = GetOggGranuleBefore(this.Data, int.MaxValue);
                    this.VorbisComments = ReadVorbisComments(this.Data);
                    if (desc.LoopEnd > desc.LoopStart) {
                        // The byte offsets in the entry header only point to the pages containing the loop points.
                        // Sample-exact loop points are stored as Vorbis comments; prefer those.
                        if (this.TryGetVorbisCommentNumber("LOOPSTART", out var loopStart) &&
                            this.TryGetVorbisCommentNumber("LOOPEND", out var loopEnd)) {
                            this.LoopStartFrame = loopStart;
                            this.LoopEndFrame = loopEnd + 1;
                        } else {
                            this.LoopStartFrame = GetOggGranuleBefore(
                                this.Data,
                                this.OggHeaderSize + (long) desc.LoopStart);
                            this.LoopEndFrame = GetOggGranuleBefore(
                                this.Data,
                                this.OggHeaderSize + (long) desc.LoopEnd);
                        }
                    }

                    this.IsPlayable = this.Data.Length >= 4 && this.Data.AsSpan(0, 4).SequenceEqual("OggS"u8);
                    if (!this.IsPlayable)
                        this.UnsupportedReason = "Ogg stream not found";
                    break;
                }

                case ScdAudioCodec.Empty:
                    this.UnsupportedReason = "Empty entry";
                    break;

                default:
                    this.UnsupportedReason = $"Unsupported codec: {this.Codec.GetDisplayName()}";
                    break;
            }
        } catch (Exception e) {
            this.IsPlayable = false;
            this.UnsupportedReason = $"Failed to parse: {e.Message}";
        }

        if (this.LoopStartFrame is { } ls && this.LoopEndFrame is { } le) {
            if (this.TotalFrames > 0)
                le = Math.Min(le, this.TotalFrames);
            if (le <= ls || ls < 0) {
                this.LoopStartFrame = null;
                this.LoopEndFrame = null;
            } else {
                this.LoopEndFrame = le;
            }
        }
    }

    /// <summary>Index of this entry in the audio table of the SCD file.</summary>
    public int Index { get; }

    public ScdAudioCodec Codec { get; }

    public int Channels { get; }

    public int SampleRate { get; }

    /// <summary>Size of the stream, as declared in the entry header.</summary>
    public uint StreamSize { get; }

    public uint RawLoopStart { get; }

    public uint RawLoopEnd { get; }

    /// <summary>
    /// Stream data. For Ogg Vorbis, this is a complete (decrypted) Ogg file, including the header pages.
    /// </summary>
    public byte[] Data { get; }

    public int OggHeaderSize { get; }

    /// <summary>User comments of the Vorbis stream (e.g. "LOOPSTART=123").</summary>
    public IReadOnlyList<string> VorbisComments { get; } = [];

    public int AdpcmBlockAlign { get; }

    public int AdpcmSamplesPerBlock { get; }

    public short[] AdpcmCoefficients { get; } = [];

    /// <summary>Total number of sample frames, or -1 if unknown.</summary>
    public long TotalFrames { get; } = -1;

    public long? LoopStartFrame { get; }

    /// <summary>Exclusive end of the loop in sample frames.</summary>
    public long? LoopEndFrame { get; }

    public bool HasLoop => this.LoopStartFrame is not null;

    public bool IsPlayable { get; }

    public string? UnsupportedReason { get; }

    public TimeSpan Duration => this.TotalFrames < 0 || this.SampleRate <= 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds((double) this.TotalFrames / this.SampleRate);

    public string CodecName => this.Codec.GetDisplayName();

    /// <summary>Extension of the file created by <see cref="WriteNativeFile"/>.</summary>
    public string? NativeFileExtension => this.Codec switch {
        ScdAudioCodec.OggVorbis when this.IsPlayable => ".ogg",
        ScdAudioCodec.MsAdpcm when this.IsPlayable => ".wav",
        ScdAudioCodec.Pcm16 when this.IsPlayable => ".wav",
        _ => null,
    };

    /// <summary>Converts a sample frame index into time.</summary>
    public TimeSpan FramesToTime(long frames) =>
        this.SampleRate <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double) frames / this.SampleRate);

    /// <summary>
    /// Writes the stream in its native encoding: Ogg Vorbis as-is, MS-ADPCM and PCM wrapped in a RIFF WAVE file.
    /// </summary>
    public void WriteNativeFile(Stream stream)
    {
        switch (this.Codec) {
            case ScdAudioCodec.OggVorbis when this.IsPlayable:
                stream.Write(this.Data);
                break;

            case ScdAudioCodec.MsAdpcm when this.IsPlayable: {
                Span<byte> extra = stackalloc byte[4 + this.AdpcmCoefficients.Length * 2];
                BinaryPrimitives.WriteUInt16LittleEndian(extra, (ushort) this.AdpcmSamplesPerBlock);
                BinaryPrimitives.WriteUInt16LittleEndian(extra[2..], (ushort) (this.AdpcmCoefficients.Length / 2));
                for (var i = 0; i < this.AdpcmCoefficients.Length; i++)
                    BinaryPrimitives.WriteInt16LittleEndian(extra[(4 + i * 2)..], this.AdpcmCoefficients[i]);

                using var writer = new RiffWaveWriter(
                    stream,
                    2,
                    this.Channels,
                    this.SampleRate,
                    (int) ((long) this.SampleRate * this.AdpcmBlockAlign / Math.Max(1, this.AdpcmSamplesPerBlock)),
                    this.AdpcmBlockAlign,
                    4,
                    extra,
                    this.TotalFrames,
                    this.GetLoopTuple(),
                    true);
                writer.Write(this.Data);
                break;
            }

            case ScdAudioCodec.Pcm16 when this.IsPlayable: {
                using var writer = new RiffWaveWriter(
                    stream,
                    1,
                    this.Channels,
                    this.SampleRate,
                    this.SampleRate * this.Channels * 2,
                    this.Channels * 2,
                    16,
                    loop: this.GetLoopTuple(),
                    leaveOpen: true);
                writer.Write(this.Data.AsSpan(0, (int) (this.TotalFrames * this.Channels * 2)));
                break;
            }

            default:
                throw new NotSupportedException(this.UnsupportedReason ?? "Not supported");
        }
    }

    /// <summary>Decodes PCM or MS-ADPCM streams into interleaved 16-bit samples.</summary>
    /// <exception cref="NotSupportedException">If the codec cannot be decoded by this function.</exception>
    public short[] DecodeToPcm16()
    {
        switch (this.Codec) {
            case ScdAudioCodec.Pcm16 when this.IsPlayable: {
                var res = new short[this.TotalFrames * this.Channels];
                for (var i = 0; i < res.Length; i++)
                    res[i] = BinaryPrimitives.ReadInt16LittleEndian(this.Data.AsSpan(i * 2));
                return res;
            }

            case ScdAudioCodec.MsAdpcm when this.IsPlayable:
                return MsAdpcmDecoder.Decode(this.Data, this.AdpcmBlockAlign, this.Channels, this.AdpcmCoefficients);

            default:
                throw new NotSupportedException(this.UnsupportedReason ?? "Not supported by this function");
        }
    }

    /// <summary>Loop as (start, exclusive end) in sample frames, if any.</summary>
    public (long Start, long EndExclusive)? GetLoopTuple() =>
        this.LoopStartFrame is { } ls && this.LoopEndFrame is { } le ? (ls, le) : null;

    /// <summary>Reads all non-empty audio entries from a SCD file.</summary>
    /// <remarks>
    /// The given file is not touched other than reading <see cref="FileResource.Data"/>; a private copy is parsed,
    /// so that this function can safely run in a background thread while the original instance is in use.
    /// </remarks>
    public static List<ScdAudioEntry> ReadAll(ScdFile scd, CancellationToken cancellationToken = default)
    {
        var copy = ClonePrivately(scd);
        var res = new List<ScdAudioEntry>();
        for (var i = 0; i < copy.AudioDataCount; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            ScdFile.Audio audio;
            try {
                audio = copy.GetAudio(i);
            } catch (Exception) {
                continue;
            }

            if (audio.AudioBasicDesc.Format == AudioFormat.Empty || audio.AudioBasicDesc.Size == 0)
                continue;
            res.Add(new(i, audio));
        }

        return res;
    }

    private static ScdFile ClonePrivately(ScdFile scd)
    {
        const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var file = new ScdFile();
        typeof(FileResource).GetProperty("FileInfo", bindingFlags)!.SetValue(file, scd.FileInfo);
        typeof(FileResource).GetProperty("FilePath", bindingFlags)!.SetValue(file, scd.FilePath);
        typeof(FileResource).GetProperty("Data", bindingFlags)!.SetValue(file, scd.Data);
        typeof(FileResource).GetProperty("Reader", bindingFlags)!.SetValue(
            file,
            new LuminaBinaryReader(scd.Data, scd.Reader.PlatformId));
        typeof(FileResource).GetMethod("LoadFile", bindingFlags)!.Invoke(file, null);
        return file;
    }

    private static unsafe short[] ReadCoefficients(AdpcmWaveFormat wf)
    {
        var count = Math.Clamp((int) wf.NumCoef, 0, 7);
        if (count == 0)
            return (short[]) MsAdpcmDecoder.StandardCoefficients.Clone();

        var res = new short[count * 2];
        for (var i = 0; i < res.Length; i++)
            res[i] = wf.Coef[i];
        return res;
    }

    private bool TryGetVorbisCommentNumber(string key, out long value)
    {
        foreach (var comment in this.VorbisComments) {
            var eq = comment.IndexOf('=');
            if (eq == key.Length &&
                comment.StartsWith(key, StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(comment.AsSpan(eq + 1).Trim(), out value) &&
                value >= 0)
                return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Reads the user comments from the Vorbis comment header of an Ogg Vorbis stream.</summary>
    public static IReadOnlyList<string> ReadVorbisComments(ReadOnlySpan<byte> ogg)
    {
        // Reassemble the second packet of the logical stream, which is the comment header.
        var packet = new List<byte>();
        var packetIndex = 0;
        var pos = 0;
        while (pos + 27 <= ogg.Length && ogg.Slice(pos, 4).SequenceEqual("OggS"u8)) {
            var segmentCount = ogg[pos + 26];
            var dataPos = pos + 27 + segmentCount;
            if (dataPos > ogg.Length)
                break;

            foreach (var lacing in ogg.Slice(pos + 27, segmentCount)) {
                if (dataPos + lacing > ogg.Length)
                    return [];
                if (packetIndex == 1)
                    packet.AddRange(ogg.Slice(dataPos, lacing));
                dataPos += lacing;
                if (lacing < 255 && ++packetIndex >= 2)
                    return ParseVorbisCommentPacket(packet.ToArray());
            }

            pos = dataPos;
        }

        return [];
    }

    private static IReadOnlyList<string> ParseVorbisCommentPacket(ReadOnlySpan<byte> packet)
    {
        var res = new List<string>();
        try {
            if (packet.Length < 7 || packet[0] != 3 || !packet.Slice(1, 6).SequenceEqual("vorbis"u8))
                return res;

            var p = 7;
            var vendorLength = BinaryPrimitives.ReadInt32LittleEndian(packet[p..]);
            p += 4 + vendorLength;
            var count = BinaryPrimitives.ReadInt32LittleEndian(packet[p..]);
            p += 4;
            for (var i = 0; i < count; i++) {
                var len = BinaryPrimitives.ReadInt32LittleEndian(packet[p..]);
                p += 4;
                res.Add(System.Text.Encoding.UTF8.GetString(packet.Slice(p, len)));
                p += len;
            }
        } catch (ArgumentOutOfRangeException) {
            // truncated; return what we have
        }

        return res;
    }

    /// <summary>
    /// Gets the granule position (= number of sample frames decoded) at the end of the last Ogg page that ends at
    /// or before <paramref name="byteOffset"/>.
    /// </summary>
    public static long GetOggGranuleBefore(ReadOnlySpan<byte> ogg, long byteOffset)
    {
        var granule = 0L;
        var pos = 0;
        while (pos + 27 <= ogg.Length && ogg.Slice(pos, 4).SequenceEqual("OggS"u8)) {
            var segmentCount = ogg[pos + 26];
            if (pos + 27 + segmentCount > ogg.Length)
                break;

            var pageSize = 27 + segmentCount;
            foreach (var lacing in ogg.Slice(pos + 27, segmentCount))
                pageSize += lacing;

            if (pos + pageSize > byteOffset || pos + pageSize > ogg.Length)
                break;

            var pageGranule = BinaryPrimitives.ReadInt64LittleEndian(ogg[(pos + 6)..]);
            if (pageGranule != -1)
                granule = pageGranule;
            pos += pageSize;
        }

        return granule;
    }
}
