using System;
using System.Buffers.Binary;

namespace LuminaExplorer.Core.Audio;

/// <summary>Decoder for Microsoft ADPCM (WAVE_FORMAT_ADPCM, 0x0002) blocks.</summary>
public static class MsAdpcmDecoder {
    /// <summary>The 7 standard coefficient pairs, used by every known encoder.</summary>
    public static readonly short[] StandardCoefficients = [
        256, 0, 512, -256, 0, 0, 192, 64, 240, 0, 460, -208, 392, -232,
    ];

    private static readonly int[] AdaptationTable = [
        230, 230, 230, 230, 307, 409, 512, 614,
        768, 614, 512, 409, 307, 230, 230, 230,
    ];

    /// <summary>Gets the number of sample frames stored in a (possibly partial) block.</summary>
    public static int GetFramesInBlock(int blockBytes, int channels) =>
        blockBytes < 7 * channels ? 0 : 2 + (blockBytes - 7 * channels) * 2 / channels;

    /// <summary>Gets the number of sample frames stored in the whole stream.</summary>
    public static long GetTotalFrames(long dataLength, int blockAlign, int channels) =>
        dataLength / blockAlign * GetFramesInBlock(blockAlign, channels) +
        GetFramesInBlock((int) (dataLength % blockAlign), channels);

    /// <summary>Decodes a whole stream into interleaved 16-bit PCM.</summary>
    public static short[] Decode(
        ReadOnlySpan<byte> data,
        int blockAlign,
        int channels,
        ReadOnlySpan<short> coefficients)
    {
        if (channels <= 0 || blockAlign < 7 * channels)
            throw new ArgumentException($"Invalid MS-ADPCM block alignment {blockAlign} for {channels} channel(s).");

        var output = new short[GetTotalFrames(data.Length, blockAlign, channels) * channels];
        var outputSpan = output.AsSpan();
        while (!data.IsEmpty) {
            var block = data[..Math.Min(blockAlign, data.Length)];
            var frames = DecodeBlock(block, channels, coefficients, outputSpan);
            outputSpan = outputSpan[(frames * channels)..];
            data = data[block.Length..];
        }

        return output;
    }

    /// <summary>Decodes one block into interleaved 16-bit PCM.</summary>
    /// <returns>Number of sample frames written.</returns>
    public static int DecodeBlock(
        ReadOnlySpan<byte> block,
        int channels,
        ReadOnlySpan<short> coefficients,
        Span<short> output)
    {
        var frames = GetFramesInBlock(block.Length, channels);
        if (frames == 0)
            return 0;

        if (coefficients.Length < 2)
            coefficients = StandardCoefficients;
        var numCoefficientPairs = coefficients.Length / 2;

        Span<int> coef1 = stackalloc int[channels];
        Span<int> coef2 = stackalloc int[channels];
        Span<int> delta = stackalloc int[channels];
        Span<int> sample1 = stackalloc int[channels];
        Span<int> sample2 = stackalloc int[channels];

        var offset = 0;
        for (var c = 0; c < channels; c++) {
            var predictor = Math.Min(block[offset++], numCoefficientPairs - 1);
            coef1[c] = coefficients[predictor * 2];
            coef2[c] = coefficients[predictor * 2 + 1];
        }

        for (var c = 0; c < channels; c++, offset += 2)
            delta[c] = BinaryPrimitives.ReadInt16LittleEndian(block[offset..]);
        for (var c = 0; c < channels; c++, offset += 2)
            sample1[c] = BinaryPrimitives.ReadInt16LittleEndian(block[offset..]);
        for (var c = 0; c < channels; c++, offset += 2)
            sample2[c] = BinaryPrimitives.ReadInt16LittleEndian(block[offset..]);

        for (var c = 0; c < channels; c++) {
            output[c] = (short) sample2[c];
            output[channels + c] = (short) sample1[c];
        }

        var outIndex = 2 * channels;
        var outEnd = frames * channels;
        for (; offset < block.Length && outIndex < outEnd; offset++) {
            for (var shift = 4; shift >= 0 && outIndex < outEnd; shift -= 4, outIndex++) {
                var c = outIndex % channels;
                var nibble = (block[offset] >> shift) & 0xF;
                var signedNibble = nibble >= 8 ? nibble - 16 : nibble;

                var predicted = ((sample1[c] * coef1[c]) + (sample2[c] * coef2[c])) >> 8;
                predicted = Math.Clamp(predicted + signedNibble * delta[c], short.MinValue, short.MaxValue);

                output[outIndex] = (short) predicted;
                sample2[c] = sample1[c];
                sample1[c] = predicted;
                delta[c] = Math.Max(16, (AdaptationTable[nibble] * delta[c]) >> 8);
            }
        }

        return frames;
    }
}
