using System;
using System.IO;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

public struct ShaderNodeAliasSubCluster {
    public const int DataCapacity = 97;
    public const int SizeInBytes = 4 + DataCapacity * 4;

    public ushort OwnIndex;
    public ushort AliasCount;

    /// <summary>
    /// Fixed capacity data; contains <see cref="AliasCount"/> <see cref="ShaderNodeAlias"/>es sorted by selector,
    /// followed by data that is yet to be understood.
    /// </summary>
    public uint[] Data;

    public ShaderNodeAliasSubCluster(BinaryReader reader)
    {
        this.OwnIndex = reader.ReadUInt16();
        this.AliasCount = reader.ReadUInt16();
        this.Data = new uint[DataCapacity];
        for (var i = 0; i < DataCapacity; i++)
            this.Data[i] = reader.ReadUInt32();
    }

    private readonly int ValidAliasCount => Math.Min((int) this.AliasCount, DataCapacity / 2);

    public readonly ShaderNodeAlias[] Aliases {
        get {
            var res = new ShaderNodeAlias[this.ValidAliasCount];
            for (var i = 0; i < res.Length; i++)
                res[i] = new() { Selector = this.Data[i * 2], Node = this.Data[i * 2 + 1] };
            return res;
        }
    }

    public readonly ReadOnlySpan<uint> AdditionalData => this.Data.AsSpan(this.ValidAliasCount * 2);
}
