using System;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShaderInput : IInputTable {
    public InputId InternalId { get; set; }
    public uint InputStringOffset { get; set; }
    public ushort InputStringSize { get; set; }
    public ushort IsTexture { get; set; }
    public ushort RegisterIndex { get; set; }
    public ushort RegisterCount { get; set; }

    public int StructureSize => this.RegisterCount * 16;
    public uint StructureSizeU => (uint) (this.RegisterCount * 16);

    public override string ToString() => Enum.IsDefined(typeof(InputId), this.InternalId)
        ? $"{this.InternalId}: R={this.RegisterIndex}; S={this.StructureSize}"
        : $"{(uint) this.InternalId:X08}: R={this.RegisterIndex}; S={this.StructureSize}";
}
