using System.ComponentModel;

namespace LuminaExplorer.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public class ArrayWrapper : BaseWrapper<Array> {
    public readonly int[] BaseIndices;
    public readonly int RangeFrom;
    public readonly int RangeTo;
    public readonly int RangeJumpUnit;

    internal ArrayWrapper(Array obj, params int[] baseIndices)
        : this(obj, 0, obj.GetLength(baseIndices.Length), baseIndices) { }

    private ArrayWrapper(Array obj, int rangeFrom, int rangeTo, params int[] baseIndices) : base(obj) {
        BaseIndices = baseIndices;
        RangeFrom = rangeFrom;
        RangeTo = rangeTo;

        RangeJumpUnit = 1;
        while (RangeTo - RangeFrom > 100 * RangeJumpUnit) {
            RangeJumpUnit *= 100;
        }
    }

    public int Length => (RangeTo - RangeFrom + RangeJumpUnit - 1) / RangeJumpUnit;

    public bool IsFlat => BaseIndices.Length + 1 == Obj.Rank;

    public override string ToString() {
        if (!BaseIndices.Any() && RangeFrom == 0 && RangeTo == Obj.GetLength(0))
            return
                $"{Obj.GetType().GetElementType()!.Name}[{string.Join(", ", Enumerable.Range(0, Obj.Rank).Select(x => Obj.GetLength(x)))}]";

        if (!BaseIndices.Any())
            return $"[{RangeFrom}..{RangeTo}]";

        return $"[{string.Join(", ", BaseIndices)}, {RangeFrom}..{RangeTo}]";
    }

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) {
        var pds = new PropertyDescriptorCollection(null);

        foreach (var i in Enumerable.Range(0, Length)) {
            pds.Add(new SimplePropertyDescriptor(
                typeof(ArrayWrapper),
                GetValueName(i),
                GetValueType(i),
                new(() => this[i])));
        }

        return pds;
    }

    public string GetValueName(int i) {
        if (i < 0 || i >= Length)
            throw new IndexOutOfRangeException();

        return RangeJumpUnit == 1
            ? $"[{RangeFrom + i}]"
            : $"[{RangeFrom + i * RangeJumpUnit}..{Math.Min(RangeTo, (i + 1) * RangeJumpUnit)}]";
    }

    public Type GetValueType(int i) {
        if (i < 0 || i >= Length)
            throw new IndexOutOfRangeException();
        if (RangeJumpUnit == 1 && IsFlat) {
            var et = Obj.GetType().GetElementType()!;
            return WrapperTypeConverter.Instance.CanConvertFrom(null, et)
                ? WrapperTypeConverter.Instance.GetWrappingType(et)
                : et;
        }

        return typeof(ArrayWrapper);
    }

    public object? this[int i] {
        get {
            if (i < 0 || i >= Length)
                throw new IndexOutOfRangeException();
            if (RangeJumpUnit != 1) {
                return new ArrayWrapper(Obj,
                    RangeFrom + i * RangeJumpUnit,
                    Math.Min(RangeTo, RangeFrom + (i + 1) * RangeJumpUnit),
                    BaseIndices);
            }

            if (!IsFlat)
                return new ArrayWrapper(Obj, BaseIndices.Append(RangeFrom + i).ToArray());

            var obj = Obj.GetValue(BaseIndices.Append(RangeFrom + i * RangeJumpUnit).ToArray());
            if (obj is null)
                return null;
            if (!WrapperTypeConverter.Instance.CanConvertFrom(null, obj.GetType()))
                return obj;
            return WrapperTypeConverter.Instance.ConvertFrom(null, null, obj);
        }
    }
}
