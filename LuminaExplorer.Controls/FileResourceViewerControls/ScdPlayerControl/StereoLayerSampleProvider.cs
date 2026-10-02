using System;
using System.Buffers;
using NAudio.Wave;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ScdPlayerControl;

/// <summary>
/// Converts streams with more than 2 channels into stereo.
/// </summary>
/// <remarks>
/// Multichannel BGM in FFXIV is usually a set of stereo layers (channels 1-2, 3-4, ...) that the game crossfades,
/// rather than surround sound. This provider can either mix all the layers, or pick a single one.
/// </remarks>
public sealed class StereoLayerSampleProvider : ISampleProvider {
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private readonly float _mixScale;

    public StereoLayerSampleProvider(ISampleProvider source)
    {
        this._source = source;
        this._sourceChannels = source.WaveFormat.Channels;
        this.LayerCount = (this._sourceChannels + 1) / 2;
        this._mixScale = 1f / MathF.Sqrt(this.LayerCount);
        this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int LayerCount { get; }

    /// <summary>Index of the stereo layer to play, or -1 to mix all layers.</summary>
    public int SelectedLayer { get; set; } = -1;

    public int Read(Span<float> buffer)
    {
        var frames = buffer.Length / 2;
        var srcCount = frames * this._sourceChannels;
        var tmp = ArrayPool<float>.Shared.Rent(srcCount);
        try {
            var read = this._source.Read(tmp.AsSpan(0, srcCount));
            var readFrames = read / this._sourceChannels;
            var layer = this.SelectedLayer;
            for (var f = 0; f < readFrames; f++) {
                var src = tmp.AsSpan(f * this._sourceChannels, this._sourceChannels);
                float l, r;
                if (layer >= 0 && layer < this.LayerCount) {
                    l = src[layer * 2];
                    r = layer * 2 + 1 < src.Length ? src[layer * 2 + 1] : l;
                } else {
                    l = r = 0;
                    for (var c = 0; c + 1 < src.Length; c += 2) {
                        l += src[c];
                        r += src[c + 1];
                    }

                    if (src.Length % 2 != 0) {
                        l += src[^1];
                        r += src[^1];
                    }

                    l *= this._mixScale;
                    r *= this._mixScale;
                }

                buffer[f * 2] = Math.Clamp(l, -1f, 1f);
                buffer[f * 2 + 1] = Math.Clamp(r, -1f, 1f);
            }

            return readFrames * 2;
        } finally {
            ArrayPool<float>.Shared.Return(tmp);
        }
    }
}
