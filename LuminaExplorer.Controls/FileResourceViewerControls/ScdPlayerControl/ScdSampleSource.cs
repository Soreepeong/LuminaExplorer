using System;
using System.IO;
using LuminaExplorer.Core.Audio;
using NAudio.Vorbis;
using NAudio.Wave;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ScdPlayerControl;

/// <summary>A seekable source of interleaved float samples, addressed in sample frames.</summary>
public abstract class ScdSampleSource : ISampleProvider, IDisposable {
    public abstract WaveFormat WaveFormat { get; }

    /// <summary>Total number of sample frames.</summary>
    public abstract long LengthFrames { get; }

    /// <summary>Current position in sample frames.</summary>
    public abstract long PositionFrames { get; set; }

    public abstract int Read(Span<float> buffer);

    public virtual void Dispose()
    { }

    /// <summary>Creates a sample source for the given entry.</summary>
    /// <remarks>May take a while; do not call from the UI thread.</remarks>
    /// <exception cref="NotSupportedException">If the entry is not playable.</exception>
    public static ScdSampleSource Create(ScdAudioEntry entry)
    {
        if (!entry.IsPlayable)
            throw new NotSupportedException(entry.UnsupportedReason ?? "Not supported");

        return entry.Codec switch {
            ScdAudioCodec.OggVorbis => new VorbisSource(entry.Data),
            ScdAudioCodec.Pcm16 or ScdAudioCodec.MsAdpcm => new Pcm16BufferSource(
                entry.DecodeToPcm16(),
                entry.SampleRate,
                entry.Channels),
            _ => throw new NotSupportedException(entry.UnsupportedReason ?? "Not supported"),
        };
    }

    private sealed class Pcm16BufferSource : ScdSampleSource {
        private readonly short[] _samples;
        private readonly int _channels;
        private long _position;

        public Pcm16BufferSource(short[] samples, int sampleRate, int channels)
        {
            this._samples = samples;
            this._channels = channels;
            this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public override WaveFormat WaveFormat { get; }

        public override long LengthFrames => this._samples.Length / this._channels;

        public override long PositionFrames {
            get => this._position;
            set => this._position = Math.Clamp(value, 0, this.LengthFrames);
        }

        public override int Read(Span<float> buffer)
        {
            var available = (this.LengthFrames - this._position) * this._channels;
            var count = (int) Math.Min(buffer.Length / this._channels * this._channels, available);
            var src = this._samples.AsSpan((int) (this._position * this._channels), count);
            for (var i = 0; i < count; i++)
                buffer[i] = src[i] / 32768f;
            this._position += count / this._channels;
            return count;
        }
    }

    private sealed class VorbisSource : ScdSampleSource {
        private readonly byte[] _data;
        private readonly int _bytesPerFrame;
        private readonly long _lengthFrames;
        private VorbisWaveReader _reader;

        public VorbisSource(byte[] data)
        {
            this._data = data;
            this._reader = new(new MemoryStream(data, false), true);
            this._bytesPerFrame = this._reader.WaveFormat.BlockAlign;
            this._lengthFrames = this._reader.Length / this._bytesPerFrame;
        }

        public override WaveFormat WaveFormat => this._reader.WaveFormat;

        public override long LengthFrames => this._lengthFrames;

        public override long PositionFrames {
            get => this._reader.Position / this._bytesPerFrame;
            set {
                value = Math.Clamp(value, 0, this._lengthFrames);
                try {
                    this._reader.Position = value * this._bytesPerFrame;
                } catch (InvalidDataException) {
                    // NVorbis fails to seek into pages whose granule position does not match the sum of the packet
                    // lengths, which happens on the last page of the stream (end trimming).
                    // Seek to an earlier point that works, and decode forward from there.
                    this.SeekBySkipping(value);
                }
            }
        }

        private void SeekBySkipping(long target)
        {
            var back = (long) this.WaveFormat.SampleRate;
            while (true) {
                var from = Math.Max(0, target - back);
                this._reader.Dispose();
                this._reader = new(new MemoryStream(this._data, false), true);
                try {
                    if (from > 0)
                        this._reader.Position = from * this._bytesPerFrame;
                } catch (InvalidDataException) when (from > 0) {
                    back *= 2;
                    continue;
                }

                var channels = this.WaveFormat.Channels;
                var buffer = new float[4096 * channels];
                var remaining = (target - from) * channels;
                while (remaining > 0) {
                    var read = this._reader.Read(buffer.AsSpan(0, (int) Math.Min(buffer.Length, remaining)));
                    if (read <= 0)
                        break;
                    remaining -= read;
                }

                return;
            }
        }

        public override int Read(Span<float> buffer)
        {
            var channels = this.WaveFormat.Channels;
            return this._reader.Read(buffer[..(buffer.Length / channels * channels)]);
        }

        public override void Dispose() => this._reader.Dispose();
    }
}
