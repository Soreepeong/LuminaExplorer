namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShaderHeader {
    public uint BlobOffset;
    public uint BlobSize;
    public ushort ConstantCount;
    public ushort SamplerCount;
    public ushort UavCount;
    public ushort TextureCount;

    public int NumInputs => this.ConstantCount + this.SamplerCount + this.UavCount + this.TextureCount;

    public override string ToString() =>
        $"C={this.ConstantCount} S={this.SamplerCount} U={this.UavCount} T={this.TextureCount}";
}
