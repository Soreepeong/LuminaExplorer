using Lumina.Data;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShpkHeader {
    public const uint MagicValue = 0x6b506853;

    public uint Magic;
    public uint Version;
    public DirectXVersion DirectXVersion;
    public uint FileSize;
    public uint ShaderBytecodeBlockOffset;
    public uint InputStringBlockOffset;
    public uint VertexShaderCount;
    public uint PixelShaderCount;
    public uint MaterialParamSize;
    public uint MaterialParamCount;
    public uint ConstantCount;
    public uint SamplerCount;
    public uint UavCount;
    public uint SystemKeyCount;
    public uint SceneKeyCount;
    public uint MaterialKeyCount;
    public uint NodeCount;
    public uint ItemCount;

    public override string ToString() =>
        $"{DirectXVersion}: V={VertexShaderCount} P={PixelShaderCount} H1={MaterialParamCount} U1={MaterialParamSize} " +
        $"NSP={ConstantCount} NRP={SamplerCount}";
}

public struct ShaderMaterialParam {
    public uint Id;
    public ushort ByteOffset;
    public ushort ByteSize;
}

public struct ShaderKey {
    public uint Id;
    public uint DefaultValue;
}

public struct ShaderNodePass {
    public uint Id;
    public uint VertexShader;
    public uint PixelShader;
}

public struct ShaderNode {
    public uint Id;
    public byte[] PassIndices;
    public uint[] SystemKeys;
    public uint[] SceneKeys;
    public uint[] MaterialKeys;
    public uint[] SubViewKeys;
    public ShaderNodePass[] Passes;
}

public struct ShaderItem {
    public uint Id;
    public uint Node;
}
