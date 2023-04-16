using System.Collections.Immutable;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Field;

namespace LuminaExplorer.Core.ExtraFormats.HavokTagfile.Value;

public class ValueArray : IValue {
    public readonly FieldType InnerType;
    public readonly IList<IValue?> Values;

    public ValueArray(IList<IValue?> values, FieldType innerType) {
        Values = values;
        InnerType = innerType;
    }

    public override string ToString() => Values.Count switch {
        0 => "ValueArray(empty)",
        1 => "ValueArray(1 item)",
        _ => $"ValueArray({Values.Count} items)",
    };

    internal static ValueArray Read(Parser parser, FieldType innerType) {
        if (innerType is null)
            throw new InvalidDataException("Array cannot have null innerType");
        return IValue.ReadVector(parser, innerType, parser.ReadInt());
    }

    internal static ValueArray Read(Parser parser, FieldType innerType, int count) {
        if (innerType is null)
            throw new InvalidDataException("Array cannot have null innerType");
        return new(
            Enumerable.Range(0, count)
                .Select(_ => IValue.Read(parser, innerType))
                .ToImmutableList(),
            innerType);
    }

    internal static ValueArray ReadVector(Parser parser, FieldType innerType, int innerCount,
        FieldType outerType, int outerCount) {
        if (innerType is null)
            throw new InvalidDataException("Array cannot have null innerType");
        if (innerCount == 4)
            innerCount = parser.ReadInt();

        return new(Enumerable.Range(0, outerCount)
            .Select(_ => (IValue?) new ValueArray(
                Enumerable.Range(0, innerCount)
                    .Select(_ => IValue.Read(parser, innerType))
                    .ToImmutableList(), innerType))
            .ToImmutableList(), outerType);
    }
}
