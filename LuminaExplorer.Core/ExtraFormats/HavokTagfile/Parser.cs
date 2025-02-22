using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LuminaExplorer.Core.ExtraFormats.HavokTagfile;

public class Parser {
    private const ulong HavokMagic = 0xD011FACECAB00D1E;

    internal readonly Dictionary<Tuple<string, int>, Definition> Definitions;
    internal readonly List<Definition?> OrderedDefinitions = [null];
    internal readonly List<Node?> Nodes = [];
    internal readonly List<int> References = [-1];
    internal readonly Dictionary<int, int> PendingReferences = new();

    private readonly BinaryReader _reader;
    private readonly List<string?> _strings = ["", null];

    private Parser(BinaryReader reader, Dictionary<Tuple<string, int>, Definition> definitions)
    {
        this._reader = reader;
        this.Definitions = definitions;
    }

    public int Version { get; private set; }

    internal int ReadInt()
    {
        var b = this._reader.ReadByte();
        var sign = (b & 1) == 1 ? -1 : 1;
        var val = (b >> 1) & 0x3F;

        var shift = 6;
        while ((b & 0x80) != 0) {
            b = this._reader.ReadByte();
            val |= (b & 0x7F) << shift;
            shift += 7;
        }

        return val * sign;
    }

    internal float ReadFloat() => this._reader.ReadSingle();

    internal byte ReadByte() => this._reader.ReadByte();

    internal byte[] ReadBytes(int length) => this._reader.ReadBytes(length);

    internal string? ReadStringNullable()
    {
        var indexOrLength = this.ReadInt();
        if (indexOrLength <= 0)
            return this._strings[-indexOrLength];

        this._strings.Add(Encoding.UTF8.GetString(this._reader.ReadBytes(indexOrLength)));
        return this._strings[^1];
    }

    internal string ReadString()
    {
        var res = this.ReadStringNullable();
        if (res is null)
            throw new NullReferenceException();
        return res;
    }

    private void _Parse()
    {
        if (this._reader.ReadUInt64() != HavokMagic)
            throw new InvalidDataException();

        while (true) {
            var tagType = (TagType) this.ReadInt();
            switch (tagType) {
                case TagType.Metadata:
                    this.Version = this.ReadInt();
                    if (this.Version != 3)
                        throw new NotSupportedException();
                    break;

                case TagType.Definition: {
                    var def = Definition.Read(this);
                    var defkey = Tuple.Create(def.Name, def.Version);
                    if (this.Definitions.TryGetValue(defkey, out var exdef))
                        def = exdef;
                    else
                        this.Definitions.Add(defkey, def);
                    this.OrderedDefinitions.Add(def);
                    break;
                }

                case TagType.Node:
                    Node.ReadAndInsert(this);
                    break;

                case TagType.EndOfFile:
                    var remainingFields = this.OrderedDefinitions
                        .Where(x => x is not null)
                        .SelectMany(x => x!.Fields)
                        .Select(x => x.FieldType)
                        .ToList();
                    var knownFields = remainingFields.ToHashSet();
                    while (remainingFields.Any()) {
                        var f = remainingFields[^1];
                        remainingFields.RemoveAt(remainingFields.Count - 1);

                        if (f.InnerType is not null && !knownFields.Contains(f.InnerType)) {
                            knownFields.Add(f.InnerType);
                            remainingFields.Add(f.InnerType);
                        }

                        if (f.ReferencedName is not null)
                            f.ReferenceDefinition = this.OrderedDefinitions.First(x => x?.Name == f.ReferencedName);
                    }

                    return;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public static Node Parse(
        BinaryReader reader,
        Dictionary<Tuple<string, int>, Definition>? definitions = null,
        bool closeAfter = false)
    {
        try {
            var parser = new Parser(reader, definitions ?? new());
            parser._Parse();
            return parser.Nodes.First()!;
        } finally {
            if (closeAfter)
                reader.Close();
        }
    }

    public static Node Parse(byte[] data, Dictionary<Tuple<string, int>, Definition>? definitions = null)
        => Parse(new BinaryReader(new MemoryStream(data)), definitions);
}
