using System;
using NAudio.Wave;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ScdPlayerControl;

/// <summary>
/// Wraps a <see cref="ScdSampleSource"/>, seamlessly jumping from the loop end back to the loop start when looping is
/// enabled. Thread-safe with respect to the audio thread calling <see cref="Read"/> and the UI thread seeking.
/// </summary>
public sealed class LoopingSampleProvider : ISampleProvider, IDisposable {
    private readonly object _lock = new();
    private readonly ScdSampleSource _source;
    private readonly int _channels;

    public LoopingSampleProvider(ScdSampleSource source, long? loopStartFrame, long? loopEndFrame)
    {
        this._source = source;
        this._channels = source.WaveFormat.Channels;

        if (loopStartFrame is { } ls && loopEndFrame is { } le) {
            le = Math.Min(le, source.LengthFrames);
            if (0 <= ls && ls < le) {
                this.LoopStartFrame = ls;
                this.LoopEndFrame = le;
            }
        }
    }

    public WaveFormat WaveFormat => this._source.WaveFormat;

    public long? LoopStartFrame { get; }

    public long? LoopEndFrame { get; }

    public bool HasLoop => this.LoopStartFrame is not null;

    /// <summary>Whether to loop between <see cref="LoopStartFrame"/> and <see cref="LoopEndFrame"/>, or, if the
    /// stream has no loop points, the whole stream.</summary>
    public bool LoopEnabled { get; set; }

    public long LengthFrames => this._source.LengthFrames;

    public long PositionFrames {
        get {
            lock (this._lock)
                return this._source.PositionFrames;
        }
        set {
            lock (this._lock)
                this._source.PositionFrames = value;
        }
    }

    public int Read(Span<float> buffer)
    {
        lock (this._lock) {
            var total = 0;
            var restartsWithoutProgress = 0;
            buffer = buffer[..(buffer.Length / this._channels * this._channels)];
            while (total < buffer.Length) {
                var want = buffer.Length - total;
                var looping = this.LoopEnabled;
                var loopStart = this.LoopStartFrame ?? 0;
                var loopEnd = this.LoopEndFrame ?? this._source.LengthFrames;
                if (looping) {
                    var pos = this._source.PositionFrames;
                    if (pos >= loopEnd) {
                        this._source.PositionFrames = pos = loopStart;
                        restartsWithoutProgress++;
                    }

                    want = (int) Math.Min(want, (loopEnd - pos) * this._channels);
                }

                var read = want <= 0 ? 0 : this._source.Read(buffer.Slice(total, want));
                if (read <= 0) {
                    if (!looping || restartsWithoutProgress > 2)
                        break;

                    // Source ended before the declared loop end; restart.
                    this._source.PositionFrames = loopStart;
                    restartsWithoutProgress++;
                    continue;
                }

                restartsWithoutProgress = 0;
                total += read;
            }

            return total;
        }
    }

    public void Dispose()
    {
        lock (this._lock)
            this._source.Dispose();
    }
}
