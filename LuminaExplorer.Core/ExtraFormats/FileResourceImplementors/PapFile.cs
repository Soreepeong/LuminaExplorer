using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.ExtraFormats.HavokAnimation;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Value;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

[FileExtension(".pap")]
public class PapFile : FileResource {
    public PapHeader Header;
    public List<PapAnimation> Animations = null!;
    public byte[] HavokData = null!;
    public byte[] Timeline = null!;

    public readonly Dictionary<Tuple<string, int>, Definition> HavokDefinitions = new();
    public Node HavokRootNode = null!;
    public AnimationSet[] AnimationBindings = null!;

    public Exception? LoadException { get; private set; }

    public override void LoadFile()
    {
        try {
            this.Header = new(this.Reader);
            if (this.Header.Magic != PapHeader.MagicValue)
                throw new InvalidDataException();

            this.Reader.BaseStream.Position = this.Header.InfoOffset;
            this.Animations = Enumerable.Range(0, this.Header.AnimationCount).Select(_ => new PapAnimation(this.Reader))
                .ToList();

            this.HavokData = this.Data[this.Header.HavokDataOffset..this.Header.TimelineOffset];
            this.Timeline = this.Data[this.Header.TimelineOffset..];

            this.HavokRootNode = Parser.Parse(this.HavokData, this.HavokDefinitions);

            this.AnimationBindings = this.Animations
                .Select((_, i) => AnimationSet.Decode(this.GetAnimationBindingNode(i))).ToArray();
        } catch (Exception e) {
            this.LoadException = e;
        }
    }

    public Node GetAnimationBindingNode(int bindingIndex)
    {
        if (bindingIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, null);
        if (this.HavokRootNode.AsMap.GetValueOrDefault("namedVariants") is not ValueArray namedVariants)
            throw new(); // care later about errmsg
        if (namedVariants.Values.FirstOrDefault() is not ValueNode namedVariant0)
            throw new();
        if (namedVariant0.Node.AsMap.GetValueOrDefault("variant") is not ValueNode variant)
            throw new();
        if (variant.Node.AsMap.GetValueOrDefault("bindings") is not ValueArray bindings)
            throw new();
        if (bindings.Values.Count <= bindingIndex)
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, null);
        if (bindings.Values[bindingIndex] is not ValueNode binding)
            throw new();
        return binding.Node;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PapHeader {
        public const uint MagicValue = 0x20706170;

        public uint Magic;
        public uint Version; // always 0x00020001?
        public short AnimationCount;
        public ushort ModelId;
        public SkeletonTargetModelClassification ModelClassification;
        public int InfoOffset;
        public int HavokDataOffset;
        public int TimelineOffset;

        public PapHeader(BinaryReader r)
        {
            r.ReadInto(out this.Magic);
            r.ReadInto(out this.Version);
            r.ReadInto(out this.AnimationCount);
            r.ReadInto(out this.ModelId);
            r.ReadInto(out this.ModelClassification);
            r.ReadInto(out this.InfoOffset);
            r.ReadInto(out this.HavokDataOffset);
            r.ReadInto(out this.TimelineOffset);
        }
    }

    public class PapAnimation {
        public string Name;
        public short Unknown20;
        public ushort Index;
        public short Unknown24;
        public short Unknown26;

        public PapAnimation(BinaryReader r)
        {
            var nameBytes = r.ReadBytes(0x20);
            this.Name = Encoding.UTF8.GetString(nameBytes, 0, nameBytes.TakeWhile(x => x != 0).Count());
            r.ReadInto(out this.Unknown20);
            r.ReadInto(out this.Index);
            r.ReadInto(out this.Unknown24);
            r.ReadInto(out this.Unknown26);
        }
    }
}
