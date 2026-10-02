using System.IO;
using System.Numerics;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.PhybStructs;

/// <summary>Collision objects of a physics file.</summary>
/// <remarks>Layout: 5x u8 counts (capsules, ellipsoids, normal planes, three-point planes, spheres), 3 bytes padding, then the objects.</remarks>
public class PhybCollision {
    public PhybCapsule[] Capsules = [];
    public PhybEllipsoid[] Ellipsoids = [];
    public PhybNormalPlane[] NormalPlanes = [];
    public PhybThreePointPlane[] ThreePointPlanes = [];
    public PhybSphere[] Spheres = [];

    public static PhybCollision Read(BinaryReader r)
    {
        var numCapsules = r.ReadByte();
        var numEllipsoids = r.ReadByte();
        var numNormalPlanes = r.ReadByte();
        var numThreePointPlanes = r.ReadByte();
        var numSpheres = r.ReadByte();
        r.ReadBytes(3);

        var ret = new PhybCollision {
            Capsules = new PhybCapsule[numCapsules],
            Ellipsoids = new PhybEllipsoid[numEllipsoids],
            NormalPlanes = new PhybNormalPlane[numNormalPlanes],
            ThreePointPlanes = new PhybThreePointPlane[numThreePointPlanes],
            Spheres = new PhybSphere[numSpheres],
        };
        for (var i = 0; i < numCapsules; i++)
            ret.Capsules[i] = PhybCapsule.Read(r);
        for (var i = 0; i < numEllipsoids; i++)
            ret.Ellipsoids[i] = PhybEllipsoid.Read(r);
        for (var i = 0; i < numNormalPlanes; i++)
            ret.NormalPlanes[i] = PhybNormalPlane.Read(r);
        for (var i = 0; i < numThreePointPlanes; i++)
            ret.ThreePointPlanes[i] = PhybThreePointPlane.Read(r);
        for (var i = 0; i < numSpheres; i++)
            ret.Spheres[i] = PhybSphere.Read(r);
        return ret;
    }

    internal static string ReadName(BinaryReader r) => r.ReadFString(32);
}

/// <summary>Capsule collision object (124 bytes).</summary>
public struct PhybCapsule {
    public string Name;
    public string StartBone;
    public string EndBone;
    public Vector3 StartOffset;
    public Vector3 EndOffset;
    public float Radius;

    public static PhybCapsule Read(BinaryReader r) => new() {
        Name = PhybCollision.ReadName(r),
        StartBone = PhybCollision.ReadName(r),
        EndBone = PhybCollision.ReadName(r),
        StartOffset = r.ReadSingleVector3(),
        EndOffset = r.ReadSingleVector3(),
        Radius = r.ReadSingle(),
    };
}

/// <summary>Ellipsoid collision object (116 bytes).</summary>
public struct PhybEllipsoid {
    public string Name;
    public string Bone;
    public Vector3 Offset1;
    public Vector3 Offset2;
    public Vector3 Offset3;
    public Vector3 Offset4;
    public float Radius;

    public static PhybEllipsoid Read(BinaryReader r) => new() {
        Name = PhybCollision.ReadName(r),
        Bone = PhybCollision.ReadName(r),
        Offset1 = r.ReadSingleVector3(),
        Offset2 = r.ReadSingleVector3(),
        Offset3 = r.ReadSingleVector3(),
        Offset4 = r.ReadSingleVector3(),
        Radius = r.ReadSingle(),
    };
}

/// <summary>Plane collision object defined by a normal (92 bytes).</summary>
public struct PhybNormalPlane {
    public string Name;
    public string Bone;
    public Vector3 Offset;
    public Vector3 Normal;
    public float Thickness;

    public static PhybNormalPlane Read(BinaryReader r) => new() {
        Name = PhybCollision.ReadName(r),
        Bone = PhybCollision.ReadName(r),
        Offset = r.ReadSingleVector3(),
        Normal = r.ReadSingleVector3(),
        Thickness = r.ReadSingle(),
    };
}

/// <summary>Plane collision object defined by three points (168 bytes).</summary>
public struct PhybThreePointPlane {
    public string Name;
    public string Bone;
    public Vector4 Unknown1;
    public Vector4 Unknown2;
    public Vector4 Unknown3;
    public Vector4 Unknown4;
    public Vector3 Offset;
    public Vector3 Unknown5;
    public Vector3 Unknown6;
    public float Thickness;

    public static PhybThreePointPlane Read(BinaryReader r) => new() {
        Name = PhybCollision.ReadName(r),
        Bone = PhybCollision.ReadName(r),
        Unknown1 = ReadVector4(r),
        Unknown2 = ReadVector4(r),
        Unknown3 = ReadVector4(r),
        Unknown4 = ReadVector4(r),
        Offset = r.ReadSingleVector3(),
        Unknown5 = r.ReadSingleVector3(),
        Unknown6 = r.ReadSingleVector3(),
        Thickness = r.ReadSingle(),
    };

    private static Vector4 ReadVector4(BinaryReader r) =>
        new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}

/// <summary>Sphere collision object (80 bytes).</summary>
public struct PhybSphere {
    public string Name;
    public string Bone;
    public Vector3 Offset;
    public float Radius;

    public static PhybSphere Read(BinaryReader r) => new() {
        Name = PhybCollision.ReadName(r),
        Bone = PhybCollision.ReadName(r),
        Offset = r.ReadSingleVector3(),
        Radius = r.ReadSingle(),
    };
}
