namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

/// <summary>Node alias cluster, present since version 0x0E01.</summary>
public struct ShaderNodeAliasCluster {
    // Note that the order is reversed compared to the other places where sub view keys appear.
    public uint SubViewValue2;
    public uint SubViewValue1;
    public uint Unknown;
    public ShaderNodeAliasSubCluster[] SubClusters;
}
