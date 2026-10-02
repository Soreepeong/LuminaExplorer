using System;
using System.Buffers;
using System.IO;
using System.Threading;
using LuminaExplorer.Core.Audio;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ScdPlayerControl;

public static class ScdAudioExport {
    /// <summary>
    /// Decodes the entry and writes it as a 16-bit PCM RIFF WAVE file, keeping all channels.
    /// Loop points, if any, are written as a smpl chunk.
    /// </summary>
    public static void WriteDecodedWav(
        ScdAudioEntry entry,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using var source = ScdSampleSource.Create(entry);
        var channels = source.WaveFormat.Channels;
        var sampleRate = source.WaveFormat.SampleRate;
        using var writer = new RiffWaveWriter(
            stream,
            1,
            channels,
            sampleRate,
            sampleRate * channels * 2,
            channels * 2,
            16,
            loop: entry.GetLoopTuple(),
            leaveOpen: true);

        var floats = ArrayPool<float>.Shared.Rent(4096 * channels);
        var shorts = ArrayPool<short>.Shared.Rent(4096 * channels);
        try {
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(floats.AsSpan(0, 4096 * channels));
                if (read <= 0)
                    break;

                for (var i = 0; i < read; i++)
                    shorts[i] = (short) Math.Clamp(MathF.Round(floats[i] * 32767f), short.MinValue, short.MaxValue);
                writer.Write(shorts.AsSpan(0, read));
            }
        } finally {
            ArrayPool<float>.Shared.Return(floats);
            ArrayPool<short>.Shared.Return(shorts);
        }
    }
}
