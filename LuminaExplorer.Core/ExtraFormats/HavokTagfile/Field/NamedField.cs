namespace LuminaExplorer.Core.ExtraFormats.HavokTagfile.Field;

public class NamedField {
    public readonly string Name;
    public readonly FieldType FieldType;

    public NamedField(string name, FieldType fieldType) {
        Name = name;
        FieldType = fieldType;
    }

    public Definition FieldOwner { get; internal set; } = null!;

    public override string ToString() => $"{Name}({FieldType})";

    internal static NamedField Read(Parser parser) {
        var name = parser.ReadString();
        var fieldType = FieldType.Read(parser);
        return new(name, fieldType);
    }
}
