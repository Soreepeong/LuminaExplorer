using System;
using System.IO;
using System.Numerics;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.PhybStructs;

/// <summary>A physics simulator.</summary>
/// <remarks>
/// Header (72 bytes): 8x u8 counts (collisions, collision connectors, chains, connectors, attracts, pins, springs,
/// post alignments), <see cref="PhybSimulatorParams"/> (32 bytes), 8x u32 offsets of the corresponding lists.
/// Offsets are relative to the simulator section base (simulator section offset + 4, i.e. right after the
/// simulator count); 0xCCCCCCCC means "none".
/// </remarks>
public class PhybSimulator {
    public const int HeaderSize = 8 + PhybSimulatorParams.Size + 8 * 4;
    public const uint NoOffset = 0xCCCCCCCCu;

    public PhybSimulatorParams Params;

    public PhybCollisionData[] Collisions = [];
    public PhybCollisionData[] CollisionConnectors = [];
    public PhybChain[] Chains = [];
    public PhybConnector[] Connectors = [];
    public PhybAttract[] Attracts = [];
    public PhybPin[] Pins = [];
    public PhybSpring[] Springs = [];
    public PhybPostAlignment[] PostAlignments = [];

    /// <summary>Reads a simulator header at the current position, and its lists relative to <paramref name="sectionBase"/>.</summary>
    public static PhybSimulator Read(BinaryReader r, long sectionBase)
    {
        var counts = r.ReadBytes(8);
        var ret = new PhybSimulator {Params = PhybSimulatorParams.Read(r)};
        var offsets = new uint[8];
        for (var i = 0; i < offsets.Length; i++)
            offsets[i] = r.ReadUInt32();

        var headerEnd = r.BaseStream.Position;

        ret.Collisions = ReadList(r, sectionBase, offsets[0], counts[0], PhybCollisionData.Read);
        ret.CollisionConnectors = ReadList(r, sectionBase, offsets[1], counts[1], PhybCollisionData.Read);
        ret.Chains = ReadList(r, sectionBase, offsets[2], counts[2], x => PhybChain.Read(x, sectionBase));
        ret.Connectors = ReadList(r, sectionBase, offsets[3], counts[3], PhybConnector.Read);
        ret.Attracts = ReadList(r, sectionBase, offsets[4], counts[4], PhybAttract.Read);
        ret.Pins = ReadList(r, sectionBase, offsets[5], counts[5], PhybPin.Read);
        ret.Springs = ReadList(r, sectionBase, offsets[6], counts[6], PhybSpring.Read);
        ret.PostAlignments = ReadList(r, sectionBase, offsets[7], counts[7], PhybPostAlignment.Read);

        r.BaseStream.Position = headerEnd;
        return ret;
    }

    internal static T[] ReadList<T>(BinaryReader r, long sectionBase, uint offset, int count, Func<BinaryReader, T> read)
    {
        if (count == 0)
            return [];

        r.BaseStream.Position = sectionBase + (offset == NoOffset ? 0 : offset);
        var ret = new T[count];
        for (var i = 0; i < count; i++)
            ret[i] = read(r);
        return ret;
    }
}

/// <summary>Simulator parameters (32 bytes).</summary>
public struct PhybSimulatorParams {
    public const int Size = 32;

    public Vector3 Gravity;
    public Vector3 Wind;
    public short ConstraintLoop;
    public short CollisionLoop;
    public PhybSimulatorFlags Flags;
    public byte Group;
    public ushort Padding;

    public static PhybSimulatorParams Read(BinaryReader r) => new() {
        Gravity = r.ReadSingleVector3(),
        Wind = r.ReadSingleVector3(),
        ConstraintLoop = r.ReadInt16(),
        CollisionLoop = r.ReadInt16(),
        Flags = (PhybSimulatorFlags) r.ReadByte(),
        Group = r.ReadByte(),
        Padding = r.ReadUInt16(),
    };
}

[Flags]
public enum PhybSimulatorFlags : byte {
    Simulating = 0x01,
    CollisionsHandled = 0x02,
    ContinuousCollisions = 0x04,
    UsingGroundPlane = 0x08,
    FixedLength = 0x10,
}

/// <summary>A chain of nodes (bones) simulated together.</summary>
/// <remarks>
/// Header (48 bytes): u16 collision count, u16 node count, 5x f32, vec3 last bone offset, u32 type,
/// u32 collision list offset, u32 node list offset (offsets relative to the simulator section base).
/// </remarks>
public class PhybChain {
    public const int HeaderSize = 48;

    public float Dampening;
    public float MaxSpeed;
    public float Friction;
    public float CollisionDampening;
    public float RepulsionStrength;
    public Vector3 LastBoneOffset;
    public PhybChainType Type;

    public PhybCollisionData[] Collisions = [];
    public PhybNode[] Nodes = [];

    public static PhybChain Read(BinaryReader r, long sectionBase)
    {
        var numCollisions = r.ReadUInt16();
        var numNodes = r.ReadUInt16();
        var ret = new PhybChain {
            Dampening = r.ReadSingle(),
            MaxSpeed = r.ReadSingle(),
            Friction = r.ReadSingle(),
            CollisionDampening = r.ReadSingle(),
            RepulsionStrength = r.ReadSingle(),
            LastBoneOffset = r.ReadSingleVector3(),
            Type = (PhybChainType) r.ReadUInt32(),
        };
        var collisionOffset = r.ReadUInt32();
        var nodeOffset = r.ReadUInt32();

        var headerEnd = r.BaseStream.Position;
        ret.Collisions = PhybSimulator.ReadList(r, sectionBase, collisionOffset, numCollisions, PhybCollisionData.Read);
        ret.Nodes = PhybSimulator.ReadList(r, sectionBase, nodeOffset, numNodes, PhybNode.Read);
        r.BaseStream.Position = headerEnd;
        return ret;
    }
}

public enum PhybChainType : uint {
    Sphere = 0,
    Capsule = 1,
}

/// <summary>Reference to a collision object by name (36 bytes).</summary>
public struct PhybCollisionData {
    public string Name;
    public PhybCollisionType Type;

    public static PhybCollisionData Read(BinaryReader r) => new() {
        Name = PhybCollision.ReadName(r),
        Type = (PhybCollisionType) r.ReadUInt32(),
    };
}

public enum PhybCollisionType : uint {
    Both = 0,
    Outside = 1,
    Inside = 2,
}

/// <summary>A simulated node (bone) of a chain (84 bytes).</summary>
public struct PhybNode {
    public string BoneName;
    public float Radius;
    public float AttractByAnimation;
    public float WindScale;
    public float GravityScale;
    public float ConeMaxAngle;
    public Vector3 ConeAxisOffset;
    public Vector3 ConstraintPlaneNormal;
    public uint CollisionFlag;
    public uint ContinuousCollisionFlag;

    public static PhybNode Read(BinaryReader r) => new() {
        BoneName = PhybCollision.ReadName(r),
        Radius = r.ReadSingle(),
        AttractByAnimation = r.ReadSingle(),
        WindScale = r.ReadSingle(),
        GravityScale = r.ReadSingle(),
        ConeMaxAngle = r.ReadSingle(),
        ConeAxisOffset = r.ReadSingleVector3(),
        ConstraintPlaneNormal = r.ReadSingleVector3(),
        CollisionFlag = r.ReadUInt32(),
        ContinuousCollisionFlag = r.ReadUInt32(),
    };
}

/// <summary>Connects two nodes, possibly of different chains (32 bytes).</summary>
public struct PhybConnector {
    public short ChainId1;
    public short ChainId2;
    public short NodeId1;
    public short NodeId2;
    public float CollisionRadius;
    public float Friction;
    public float Dampening;
    public float Repulsion;
    public uint CollisionFlag;
    public uint ContinuousCollisionFlag;

    public static PhybConnector Read(BinaryReader r) => new() {
        ChainId1 = r.ReadInt16(),
        ChainId2 = r.ReadInt16(),
        NodeId1 = r.ReadInt16(),
        NodeId2 = r.ReadInt16(),
        CollisionRadius = r.ReadSingle(),
        Friction = r.ReadSingle(),
        Dampening = r.ReadSingle(),
        Repulsion = r.ReadSingle(),
        CollisionFlag = r.ReadUInt32(),
        ContinuousCollisionFlag = r.ReadUInt32(),
    };
}

/// <summary>Attracts a node towards a bone (52 bytes).</summary>
public struct PhybAttract {
    public string BoneName;
    public Vector3 BoneOffset;
    public short ChainId;
    public short NodeId;
    public float Stiffness;

    public static PhybAttract Read(BinaryReader r) => new() {
        BoneName = PhybCollision.ReadName(r),
        BoneOffset = r.ReadSingleVector3(),
        ChainId = r.ReadInt16(),
        NodeId = r.ReadInt16(),
        Stiffness = r.ReadSingle(),
    };
}

/// <summary>Pins a node to a bone (48 bytes).</summary>
public struct PhybPin {
    public string BoneName;
    public Vector3 BoneOffset;
    public short ChainId;
    public short NodeId;

    public static PhybPin Read(BinaryReader r) => new() {
        BoneName = PhybCollision.ReadName(r),
        BoneOffset = r.ReadSingleVector3(),
        ChainId = r.ReadInt16(),
        NodeId = r.ReadInt16(),
    };
}

/// <summary>A spring between two nodes (16 bytes).</summary>
public struct PhybSpring {
    public short ChainId1;
    public short ChainId2;
    public short NodeId1;
    public short NodeId2;
    public float StretchStiffness;
    public float CompressStiffness;

    public static PhybSpring Read(BinaryReader r) => new() {
        ChainId1 = r.ReadInt16(),
        ChainId2 = r.ReadInt16(),
        NodeId1 = r.ReadInt16(),
        NodeId2 = r.ReadInt16(),
        StretchStiffness = r.ReadSingle(),
        CompressStiffness = r.ReadSingle(),
    };
}

/// <summary>Post-simulation alignment of a node against a collision object (36 bytes).</summary>
public struct PhybPostAlignment {
    public string CollisionName;
    public short ChainId;
    public short NodeId;

    public static PhybPostAlignment Read(BinaryReader r) => new() {
        CollisionName = PhybCollision.ReadName(r),
        ChainId = r.ReadInt16(),
        NodeId = r.ReadInt16(),
    };
}
