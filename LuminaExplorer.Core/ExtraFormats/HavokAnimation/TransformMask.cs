using System.IO;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.HavokAnimation;

public class TransformMask {
    public byte Quantization;
    public VectorType Translation;
    public QuaternionType Rotation;
    public VectorType Scale;

    public TransformMask(BinaryReader reader)
    {
        reader.ReadInto(out this.Quantization);
        reader.ReadInto(out this.Translation);
        reader.ReadInto(out this.Rotation);
        reader.ReadInto(out this.Scale);
    }

    public ScalarQuantization TranslationQuantization {
        get => (ScalarQuantization) (this.Quantization & 0b11);
        set => this.Quantization = (byte) ((this.Quantization & 0b11111100) | (int) value);
    }

    public QuaternionQuantization RotationQuantization {
        get => (QuaternionQuantization) ((this.Quantization >> 2) & 0b1111);
        set => this.Quantization = (byte) ((this.Quantization & 0b11000011) | ((int) value << 2));
    }

    public ScalarQuantization ScaleQuantization {
        get => (ScalarQuantization) ((this.Quantization >> 6) & 0b11);
        set => this.Quantization = (byte) ((this.Quantization & 0b00111111) | ((int) value << 6));
    }
}
