using System;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShaderInput : IInputTable {
    public InputId InternalId { get; set; }
    public uint InputStringOffset { get; set; }
    public uint InputStringSize { get; set; }
    public ushort RegisterIndex { get; set; }
    public ushort RegisterCount { get; set; }

    public int StructureSize => RegisterCount * 16;
    public uint StructureSizeU => (uint) (RegisterCount * 16);

    public override string ToString() => Enum.IsDefined(typeof(InputId), InternalId)
        ? $"{InternalId}: R={RegisterIndex}; S={StructureSize}"
        : $"{(uint)InternalId:X08}: R={RegisterIndex}; S={StructureSize}";
}