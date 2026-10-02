using System;
using System.IO;
using System.Text;

namespace LuminaExplorer.Core.Audio;

/// <summary>Minimal RIFF WAVE writer supporting arbitrary format tags, fact and smpl (loop) chunks.</summary>
public sealed class RiffWaveWriter : IDisposable {
    private readonly Stream _stream;
    private readonly BinaryWriter _writer;
    private readonly bool _leaveOpen;
    private readonly long _riffStart;
    private readonly long _dataSizeOffset;
    private readonly int _sampleRate;
    private readonly (long Start, long EndExclusive)? _loop;
    private bool _finished;

    /// <param name="stream">Seekable output stream.</param>
    /// <param name="formatTag">WAVE format tag; 1 for PCM, 2 for MS-ADPCM.</param>
    /// <param name="channels">Number of channels.</param>
    /// <param name="sampleRate">Sample rate.</param>
    /// <param name="averageBytesPerSecond">Average bytes per second.</param>
    /// <param name="blockAlign">Block alignment.</param>
    /// <param name="bitsPerSample">Bits per sample.</param>
    /// <param name="formatExtra">Extra format bytes after cbSize. Not written for PCM if empty.</param>
    /// <param name="factFrames">Frame count to write as a fact chunk, if any.</param>
    /// <param name="loop">Loop to write as a smpl chunk, in frames, if any.</param>
    /// <param name="leaveOpen">Whether to leave <paramref name="stream"/> open on dispose.</param>
    public RiffWaveWriter(
        Stream stream,
        ushort formatTag,
        int channels,
        int sampleRate,
        int averageBytesPerSecond,
        int blockAlign,
        int bitsPerSample,
        ReadOnlySpan<byte> formatExtra = default,
        long? factFrames = null,
        (long Start, long EndExclusive)? loop = null,
        bool leaveOpen = false)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        this._stream = stream;
        this._leaveOpen = leaveOpen;
        this._sampleRate = sampleRate;
        this._loop = loop;
        this._writer = new(stream, Encoding.ASCII, true);

        this._riffStart = stream.Position;
        this._writer.Write("RIFF"u8);
        this._writer.Write(0u);
        this._writer.Write("WAVE"u8);

        var writeExtra = formatTag != 1 || !formatExtra.IsEmpty;
        this._writer.Write("fmt "u8);
        this._writer.Write((uint) (16 + (writeExtra ? 2 + formatExtra.Length : 0)));
        this._writer.Write(formatTag);
        this._writer.Write((ushort) channels);
        this._writer.Write((uint) sampleRate);
        this._writer.Write((uint) averageBytesPerSecond);
        this._writer.Write((ushort) blockAlign);
        this._writer.Write((ushort) bitsPerSample);
        if (writeExtra) {
            this._writer.Write((ushort) formatExtra.Length);
            this._writer.Write(formatExtra);
        }

        if ((16 + (writeExtra ? 2 + formatExtra.Length : 0)) % 2 != 0)
            this._writer.Write((byte) 0);

        if (factFrames is { } frames) {
            this._writer.Write("fact"u8);
            this._writer.Write(4u);
            this._writer.Write((uint) frames);
        }

        this._writer.Write("data"u8);
        this._dataSizeOffset = stream.Position;
        this._writer.Write(0u);
    }

    public long DataLength { get; private set; }

    public void Write(ReadOnlySpan<byte> data)
    {
        this._writer.Write(data);
        this.DataLength += data.Length;
    }

    public void Write(ReadOnlySpan<short> samples)
    {
        Span<byte> buf = stackalloc byte[4096];
        while (!samples.IsEmpty) {
            var n = Math.Min(samples.Length, buf.Length / 2);
            for (var i = 0; i < n; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(buf[(i * 2)..], samples[i]);
            this.Write(buf[..(n * 2)]);
            samples = samples[n..];
        }
    }

    /// <summary>Finalizes chunk sizes. Called automatically on dispose.</summary>
    public void Finish()
    {
        if (this._finished)
            return;
        this._finished = true;

        if (this.DataLength % 2 != 0)
            this._writer.Write((byte) 0);

        if (this._loop is var (loopStart, loopEndExclusive) && loopEndExclusive > loopStart) {
            this._writer.Write("smpl"u8);
            this._writer.Write(36u + 24u);
            this._writer.Write(0u); // manufacturer
            this._writer.Write(0u); // product
            this._writer.Write((uint) (1_000_000_000L / Math.Max(1, this._sampleRate))); // sample period (ns)
            this._writer.Write(60u); // MIDI unity note
            this._writer.Write(0u); // MIDI pitch fraction
            this._writer.Write(0u); // SMPTE format
            this._writer.Write(0u); // SMPTE offset
            this._writer.Write(1u); // number of loops
            this._writer.Write(0u); // sampler data
            this._writer.Write(0u); // cue point ID
            this._writer.Write(0u); // type: forward
            this._writer.Write((uint) loopStart);
            this._writer.Write((uint) (loopEndExclusive - 1)); // inclusive
            this._writer.Write(0u); // fraction
            this._writer.Write(0u); // play count: infinite
        }

        var end = this._stream.Position;
        this._stream.Position = this._dataSizeOffset;
        this._writer.Write((uint) this.DataLength);
        this._stream.Position = this._riffStart + 4;
        this._writer.Write((uint) (end - this._riffStart - 8));
        this._stream.Position = end;
        this._writer.Flush();
    }

    public void Dispose()
    {
        this.Finish();
        this._writer.Dispose();
        if (!this._leaveOpen)
            this._stream.Dispose();
    }
}
