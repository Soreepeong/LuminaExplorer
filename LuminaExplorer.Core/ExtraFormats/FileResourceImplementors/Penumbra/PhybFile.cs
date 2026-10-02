using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.PhybStructs;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;

/// <summary>Physics (cloth/hair/etc. simulation) file.</summary>
/// <remarks>
/// Layout: u32 version, u32 data type (only if version != 0), u32 collision section offset,
/// u32 simulator section offset, collision section (up to the simulator section),
/// simulator section (u32 count, then simulator headers and data; offsets inside are relative to
/// simulator section offset + 4), optionally followed by <see cref="PhybExtendedData"/>.
/// </remarks>
[FileExtension(".phyb")]
public class PhybFile : FileResource {
    public const uint MagicExtendedDataPost = PhybExtendedData.MagicPost;
    public const uint MagicExtendedDataPre = PhybExtendedData.MagicPre;

    public uint Version;
    public uint DataType;
    public uint CollisionOffset;
    public uint SimulatorOffset;

    public PhybCollision Collision = new();
    public PhybSimulator[] Simulators = [];
    public PhybExtendedData? Extended;

    public override void LoadFile()
    {
        this.Version = this.Reader.ReadUInt32();
        this.DataType = this.Version > 0 ? this.Reader.ReadUInt32() : 0u;
        this.CollisionOffset = this.Reader.ReadUInt32();
        this.SimulatorOffset = this.Reader.ReadUInt32();

        this.Collision = this.SimulatorOffset > this.CollisionOffset
            ? PhybCollision.Read(this.Reader.WithSeek(this.CollisionOffset))
            : new();

        this.Extended = PhybExtendedData.Read(this.Data, out var extendedOffset);

        // Simulator section spans from the simulator offset to the extended data (or EOF).
        if (extendedOffset >= this.SimulatorOffset + 4L) {
            var numSimulators = this.Reader.WithSeek(this.SimulatorOffset).ReadUInt32();
            var sectionBase = this.SimulatorOffset + 4L;
            this.Simulators = new PhybSimulator[numSimulators];
            for (var i = 0; i < numSimulators; i++) {
                this.Reader.BaseStream.Position = sectionBase + (long) i * PhybSimulator.HeaderSize;
                this.Simulators[i] = PhybSimulator.Read(this.Reader, sectionBase);
            }
        }
    }
}
