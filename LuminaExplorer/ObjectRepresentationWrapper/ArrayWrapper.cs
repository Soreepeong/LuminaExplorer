using System.Collections;
using System.ComponentModel;
using LuminaExplorer.Util;

namespace LuminaExplorer.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public class ArrayWrapper : BaseWrapper<Array> {
    public readonly int[] BaseIndices;
    public readonly int RangeFrom;
    public readonly int RangeTo;
    public readonly int RangeJumpUnit;

    internal ArrayWrapper(Array obj) : this(obj, Array.Empty<int>()) { }

    internal ArrayWrapper(Array obj, int[] baseIndices)
        : this(obj, 0, obj.GetLength(baseIndices.Length), baseIndices) { }

    protected ArrayWrapper(Array obj, int rangeFrom, int rangeTo, int[] baseIndices) : base(obj) {
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
                new(() => this[i]),
                null,
                null));
        }

        return pds;
    }

    public string GetValueName(int i) {
        if (i < 0 || i >= Length)
            throw new IndexOutOfRangeException();

        if (RangeJumpUnit != 1)
            return $"[{RangeFrom + i * RangeJumpUnit}..{Math.Min(RangeTo, (i + 1) * RangeJumpUnit)}]";
        
        var obj = Obj.GetValue(BaseIndices.Append(RangeFrom + i * RangeJumpUnit).ToArray());
        obj = TransformObject(obj);
        if (obj is null)
            return $"[{RangeFrom + i}]";

        var objType = obj.GetType();
        if (objType.IsDerivedFromGenericParent(typeof(BaseWrapper<>))) {
            if (objType.GetField("Obj")?.GetValue(obj) is { } obj2) {
                obj = obj2;
                objType = obj2.GetType();
            }
        }

        switch (obj) {
            case DictionaryEntry de:
                obj = de.Key;
                break;
            default: {
                if (objType.IsGenericType) {
                    if (objType.GetGenericTypeDefinition() == typeof(Tuple<>) &&
                        objType.GetGenericArguments().Length >= 2) {
                        obj = objType.GetProperty("Item1")!.GetValue(obj);
                    } else if (objType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) {
                        obj = objType.GetProperty("Key")!.GetValue(obj);
                    } else
                        return $"[{RangeFrom + i}]";
                } else
                    return $"[{RangeFrom + i}]";

                break;
            }
        }

        return $"[{RangeFrom + i}] {obj}";
    }

    public Type GetValueType(int i) {
        if (i < 0 || i >= Length)
            throw new IndexOutOfRangeException();
        if (RangeJumpUnit == 1 && IsFlat) {
            var et = TransformValueType(Obj.GetType().GetElementType()!);
            return Converter.CanConvertFrom(null, et) ? Converter.GetWrapperType(et) : et;
        }

        return GetType();
    }

    public object? this[int i] {
        get {
            if (i < 0 || i >= Length)
                throw new IndexOutOfRangeException();
            if (RangeJumpUnit != 1) {
                return CreateSubView(
                    RangeFrom + i * RangeJumpUnit,
                    Math.Min(RangeTo, RangeFrom + (i + 1) * RangeJumpUnit),
                    BaseIndices);
            }

            if (!IsFlat)
                return CreateSubView(BaseIndices.Append(RangeFrom + i).ToArray());

            var obj = Obj.GetValue(BaseIndices.Append(RangeFrom + i * RangeJumpUnit).ToArray());
            obj = TransformObject(obj);
            if (obj is null)
                return null;
            
            return Converter.CanConvertFrom(null, obj.GetType()) ? Converter.ConvertFrom(null, null, obj) : obj;
        }
    }

    protected virtual Type TransformValueType(Type type) => type;

    protected virtual ArrayWrapper CreateSubView(int[] baseIndices) => new(Obj, baseIndices);
    
    protected virtual ArrayWrapper CreateSubView(int rangeFrom, int rangeTo, int[] baseIndices) => new(Obj, rangeFrom, rangeTo, baseIndices);
}
