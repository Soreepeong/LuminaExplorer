using System.ComponentModel;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Field;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Value;

namespace LuminaExplorer.Core.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public sealed class HavokArrayWrapper : ArrayWrapper {
    private readonly FieldType _expectingType;

    internal HavokArrayWrapper(ValueArray array)
        : this(array.Values.ToArray(), array.InnerType) { }

    private HavokArrayWrapper(Array obj, FieldType expectingType) : this(obj, expectingType, Array.Empty<int>()) { }

    private HavokArrayWrapper(Array obj, FieldType expectingType, int[] baseIndices) :
        base(obj, baseIndices) {
        _expectingType = expectingType;
    }

    private HavokArrayWrapper(Array obj, FieldType expectingType, int rangeFrom, int rangeTo, int[] baseIndices) :
        base(obj, rangeFrom, rangeTo, baseIndices) {
        _expectingType = expectingType;
    }

    public override string ToString() {
        if (!BaseIndices.Any() && RangeFrom == 0 && RangeTo == Obj.GetLength(0))
            return
                $"HavokArray<{_expectingType}>[{string.Join(", ", Enumerable.Range(0, Obj.Rank).Select(x => Obj.GetLength(x)))}]";

        return base.ToString();
    }

    protected override object? TransformObject(object? obj) {
        return obj switch {
            ValueByte vb => vb.Value,
            ValueInt vi => vi.Value,
            ValueFloat vf => vf.Value,
            ValueString vs => vs.Value,
            ValueNode vn => vn.Node,
            ValueArray va => va,
            _ => base.TransformObject(obj)
        };
    }

    protected override Type TransformValueType(Type type) {
        if (type == typeof(ValueByte))
            return typeof(byte);
        if (type == typeof(ValueInt))
            return typeof(int);
        if (type == typeof(ValueFloat))
            return typeof(float);
        if (type == typeof(ValueString))
            return typeof(string);
        if (type == typeof(ValueNode))
            return typeof(Node);
        if (type == typeof(ValueArray))
            return type;
        return base.TransformValueType(type);
    }

    protected override ArrayWrapper CreateSubView(int[] baseIndices) =>
        new HavokArrayWrapper(Obj, _expectingType, baseIndices);

    protected override ArrayWrapper CreateSubView(int rangeFrom, int rangeTo, int[] baseIndices) =>
        new HavokArrayWrapper(Obj, _expectingType, rangeFrom, rangeTo, baseIndices);
}
