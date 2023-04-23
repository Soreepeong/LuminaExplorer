using System.Collections.Immutable;
using System.Linq;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Field;

namespace LuminaExplorer.Core.ExtraFormats.HavokTagfile;

public class Definition {
    public readonly string Name;
    public readonly int Version;
    public readonly Definition? Parent;
    public readonly ImmutableList<NamedField> Fields;
    public readonly ImmutableList<NamedField> NestedFields;

    public Definition(string name, int version, Definition? parent, ImmutableList<NamedField> fields) {
        Name = name;
        Version = version;
        Parent = parent;
        Fields = fields;
        NestedFields = (parent?.NestedFields ?? ImmutableList<NamedField>.Empty).Concat(Fields).ToImmutableList();
    }

    public override string ToString() => $"{Name}(v{Version})";

    internal static Definition Read(Parser parser) {
        var name = parser.ReadString();
        var version = parser.ReadInt();
        var parent = parser.OrderedDefinitions[parser.ReadInt()];
        var numFields = parser.ReadInt();
        var fields = Enumerable.Range(0, numFields).Select(_ => NamedField.Read(parser)).ToImmutableList();
        var definition = new Definition(name, version, parent, fields);
        foreach (var f in fields)
            f.FieldOwner = definition;
        return definition;
    }
}
