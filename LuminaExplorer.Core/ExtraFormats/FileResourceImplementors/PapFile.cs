using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Lumina.Data;
using Lumina.Data.Attributes;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.Tmb;
using LuminaExplorer.Core.ExtraFormats.HavokAnimation;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Value;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;

[FileExtension(".pap")]
public class PapFile : FileResource {
    public PapHeader Header;
    public List<PapAnimation> Animations = [];
    public byte[] HavokData = [];
    public byte[] Timeline = [];

    public readonly Dictionary<Tuple<string, int>, Definition> HavokDefinitions = new();

    /// <summary>Root node of the embedded Havok tagfile, or null if the file has no animations.</summary>
    public Node? HavokRootNode;

    public AnimationSet[] AnimationBindings = [];

    private bool _timelineParsed;
    private TmbTimeline? _parsedTimeline;

    public Exception? LoadException { get; private set; }

    /// <summary>Exception that occurred while parsing <see cref="Timeline"/> on first access to
    /// <see cref="ParsedTimeline"/>, if any. Some old (version 1.1) pap files use an older TMB layout.</summary>
    public Exception? TimelineLoadException { get; private set; }

    /// <summary>The parsed timeline, or null if there is none or it could not be parsed.</summary>
    public TmbTimeline? ParsedTimeline {
        get {
            if (this._timelineParsed)
                return this._parsedTimeline;

            try {
                if (TmbTimeline.IsTimeline(this.Timeline))
                    this._parsedTimeline = new(this.Timeline);
            } catch (Exception e) {
                this.TimelineLoadException = e;
            }

            this._timelineParsed = true;
            return this._parsedTimeline;
        }
    }

    public override void LoadFile()
    {
        // Callers may try to load arbitrary files as pap (e.g. brute forcing unknown file names), so reject
        // anything that is obviously not a pap file without throwing.
        if (this.Data.Length < PapHeader.Size) {
            this.LoadException = new InvalidDataException("Not enough data for a pap header.");
            return;
        }

        this.Header = new(this.Reader);
        if (this.Header.Magic != PapHeader.MagicValue) {
            this.LoadException = new InvalidDataException("Not a pap file.");
            return;
        }

        if (this.Header.AnimationCount < 0 ||
            this.Header.InfoOffset < PapHeader.Size ||
            this.Header.InfoOffset + (long) this.Header.AnimationCount * PapAnimation.Size > this.Data.Length ||
            this.Header.HavokDataOffset < 0 ||
            this.Header.HavokDataOffset > this.Header.TimelineOffset ||
            this.Header.TimelineOffset > this.Data.Length) {
            this.LoadException = new InvalidDataException("Invalid pap header.");
            return;
        }

        try {
            this.Reader.BaseStream.Position = this.Header.InfoOffset;
            this.Animations = Enumerable.Range(0, this.Header.AnimationCount).Select(_ => new PapAnimation(this.Reader))
                .ToList();

            this.HavokData = this.Data[this.Header.HavokDataOffset..this.Header.TimelineOffset];
            this.Timeline = this.Data[this.Header.TimelineOffset..];

            // Files without any animation (timeline-only paps) have no Havok data at all; the "Havok data" region
            // then only consists of the alignment padding after the header.
            if (this.Animations.Count == 0 && this.HavokData.Length < 8)
                return;

            this.HavokRootNode = Parser.Parse(this.HavokData, this.HavokDefinitions);

            this.AnimationBindings = this.Animations
                .Select(x => AnimationSet.Decode(this.GetAnimationBindingNode(x.Index))).ToArray();
        } catch (Exception e) {
            this.LoadException = e;
        }
    }

    public Node GetAnimationBindingNode(int bindingIndex)
    {
        if (bindingIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, null);
        if (this.HavokRootNode?.AsMap.GetValueOrDefault("namedVariants") is not ValueArray namedVariants)
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
        public const int Size = 0x1A;

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
        public const int Size = 0x28;

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
