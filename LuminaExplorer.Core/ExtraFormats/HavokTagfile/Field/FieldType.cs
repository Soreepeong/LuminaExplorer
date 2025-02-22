using System;

namespace LuminaExplorer.Core.ExtraFormats.HavokTagfile.Field;

public class FieldType {
    public readonly FieldArrayType ArrayType;
    public readonly FieldElementType ElementType;
    public readonly FieldType? InnerType;
    public readonly string? ReferencedName;
    public readonly int Length;

    private FieldType(
        FieldElementType elementType,
        FieldArrayType arrayType = FieldArrayType.NotAnArray,
        FieldType? innerType = null,
        string? referencedName = null,
        int length = 0
    )
    {
        this.ElementType = elementType;
        this.ArrayType = arrayType;
        this.InnerType = innerType;
        this.ReferencedName = referencedName;
        this.Length = length;
    }

    public Definition? ReferenceDefinition { get; internal set; }

    public override string ToString() =>
        this.ReferencedName == null
            ? this.ArrayType switch {
                FieldArrayType.NotAnArray => $"{this.ElementType}",
                FieldArrayType.VariableLength when this.ElementType == FieldElementType.Array => $"{this.InnerType}[?]",
                FieldArrayType.FixedLength when this.ElementType == FieldElementType.Array =>
                    $"{this.InnerType}[{this.Length}]",
                _ => $"{this.ElementType}[INVALID]",
            }
            : this.ArrayType switch {
                FieldArrayType.NotAnArray => $"{this.ElementType}<{this.ReferencedName}>",
                FieldArrayType.VariableLength when this.ElementType == FieldElementType.Array =>
                    $"{this.InnerType}<{this.ReferencedName}>[?]",
                FieldArrayType.FixedLength when this.ElementType == FieldElementType.Array =>
                    $"{this.InnerType}<{this.ReferencedName}>[{this.Length}]",
                _ => $"{this.ElementType}<{this.ReferencedName}>[INVALID]",
            };

    internal static FieldType Read(Parser parser)
    {
        var rawType = parser.ReadInt();
        var storedType = (FieldStoredType) (rawType & 0xF);
        var sequenceType = (FieldArrayType) (rawType >> 4);

        var fixedLength = sequenceType == FieldArrayType.FixedLength ? parser.ReadInt() : 0;

        var fieldType = storedType switch {
            FieldStoredType.Void => SingleVoid,
            FieldStoredType.Byte => SingleByte,
            FieldStoredType.Integer => SingleInteger,
            FieldStoredType.Float => SingleFloat,
            FieldStoredType.Array4 => FixedFloatArray(4),
            FieldStoredType.Array8 => FixedFloatArray(8),
            FieldStoredType.Array12 => FixedFloatArray(12),
            FieldStoredType.Array16 => FixedFloatArray(16),
            FieldStoredType.Reference => Reference(parser.ReadString()),
            FieldStoredType.Struct => Struct(parser.ReadString()),
            FieldStoredType.String => SingleString,
            _ => throw new ArgumentOutOfRangeException(nameof(storedType), storedType, null),
        };

        return sequenceType switch {
            FieldArrayType.FixedLength => WrapFixedArray(fieldType, fixedLength),
            FieldArrayType.VariableLength => WrapVariableArray(fieldType),
            _ => fieldType,
        };
    }

    public static FieldType FixedFloatArray(int length) => new(
        elementType: FieldElementType.Array,
        arrayType: FieldArrayType.FixedLength,
        innerType: SingleFloat,
        length: length);

    public static FieldType Reference(string referenceName) => new(
        elementType: FieldElementType.Reference,
        referencedName: referenceName);

    public static FieldType Struct(string structName) => new(
        elementType: FieldElementType.Struct,
        referencedName: structName);

    public static FieldType WrapFixedArray(FieldType innerType, int length) => new(
        elementType: FieldElementType.Array,
        arrayType: FieldArrayType.FixedLength,
        innerType: innerType,
        length: length);

    public static FieldType WrapVariableArray(FieldType innerType) => new(
        elementType: FieldElementType.Array,
        arrayType: FieldArrayType.VariableLength,
        innerType: innerType);

    public static readonly FieldType SingleVoid = new(FieldElementType.Void);
    public static readonly FieldType SingleByte = new(FieldElementType.Byte);
    public static readonly FieldType SingleReference = new(FieldElementType.Reference);
    public static readonly FieldType SingleInteger = new(FieldElementType.Integer);
    public static readonly FieldType SingleFloat = new(FieldElementType.Float);
    public static readonly FieldType SingleString = new(FieldElementType.String);
}
