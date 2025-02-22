using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public class ArrayWrapper : BaseWrapper<Array> {
    public readonly int[] BaseIndices;
    public readonly int RangeFrom;
    public readonly int RangeTo;
    public readonly int RangeJumpUnit;

    internal ArrayWrapper(Array obj) : this(obj, [])
    { }

    internal ArrayWrapper(Array obj, int[] baseIndices)
        : this(obj, 0, obj.GetLength(baseIndices.Length), baseIndices)
    { }

    protected ArrayWrapper(Array obj, int rangeFrom, int rangeTo, int[] baseIndices) : base(obj)
    {
        this.BaseIndices = baseIndices;
        this.RangeFrom = rangeFrom;
        this.RangeTo = rangeTo;

        this.RangeJumpUnit = 1;
        while (this.RangeTo - this.RangeFrom > 100 * this.RangeJumpUnit) {
            this.RangeJumpUnit *= 100;
        }
    }

    public int Length => (this.RangeTo - this.RangeFrom + this.RangeJumpUnit - 1) / this.RangeJumpUnit;

    public bool IsFlat => this.BaseIndices.Length + 1 == this.Obj.Rank;

    public bool IsTopLevel => !this.BaseIndices.Any() && this.RangeFrom == 0 && this.RangeTo == this.Obj.GetLength(0);

    public override string ToString()
    {
        if (this.IsTopLevel)
            return $"{this.Obj.GetType().GetElementType()!.GetCSharpTypeName()}" +
                $"[{string.Join(", ", Enumerable.Range(0, this.Obj.Rank).Select(x => this.Obj.GetLength(x)))}]";

        return this.BaseIndices.Any()
            ? $"[{string.Join(", ", this.BaseIndices)}, {this.RangeFrom}..{this.RangeTo}]"
            : $"[{this.RangeFrom}..{this.RangeTo}]";
    }

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
    {
        var pds = new PropertyDescriptorCollection(null);

        foreach (var i in Enumerable.Range(0, this.Length)) {
            pds.Add(
                new SimplePropertyDescriptor(
                    typeof(ArrayWrapper),
                    this.GetValueName(i),
                    this.GetValueType(i),
                    new(() => this[i]),
                    null,
                    null));
        }

        return pds;
    }

    public string GetValueName(int i)
    {
        if (i < 0 || i >= this.Length)
            throw new IndexOutOfRangeException();

        if (this.RangeJumpUnit != 1)
            return
                $"[{this.RangeFrom + i * this.RangeJumpUnit}..{Math.Min(this.RangeTo, (i + 1) * this.RangeJumpUnit)}]";

        var obj = this.Obj.GetValue(this.BaseIndices.Append(this.RangeFrom + i * this.RangeJumpUnit).ToArray());
        obj = this.TransformObject(obj);
        if (obj is null)
            return $"[{this.RangeFrom + i}]";

        var objType = obj.GetType();
        if (objType.TryFindTypedGenericParent(typeof(BaseWrapper<>), out _)) {
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
                        return $"[{this.RangeFrom + i}]";
                } else
                    return $"[{this.RangeFrom + i}]";

                break;
            }
        }

        return $"[{this.RangeFrom + i}] {obj}";
    }

    public Type GetValueType(int i)
    {
        if (i < 0 || i >= this.Length)
            throw new IndexOutOfRangeException();
        if (this.RangeJumpUnit == 1 && this.IsFlat) {
            var et = this.TransformValueType(this.Obj.GetType().GetElementType()!);
            return Converter.CanConvertFrom(null, et) ? Converter.GetWrapperType(et) : et;
        }

        return this.GetType();
    }

    public object? this[int i] {
        get {
            if (i < 0 || i >= this.Length)
                throw new IndexOutOfRangeException();
            if (this.RangeJumpUnit != 1) {
                return this.CreateSubView(
                    this.RangeFrom + i * this.RangeJumpUnit,
                    Math.Min(this.RangeTo, this.RangeFrom + (i + 1) * this.RangeJumpUnit),
                    this.BaseIndices);
            }

            if (!this.IsFlat)
                return this.CreateSubView(this.BaseIndices.Append(this.RangeFrom + i).ToArray());

            var obj = this.Obj.GetValue(this.BaseIndices.Append(this.RangeFrom + i * this.RangeJumpUnit).ToArray());
            obj = this.TransformObject(obj);
            if (obj is null)
                return null;

            return Converter.CanConvertFrom(null, obj.GetType()) ? Converter.ConvertFrom(null, null, obj) : obj;
        }
    }

    protected virtual Type TransformValueType(Type type) => type;

    protected virtual ArrayWrapper CreateSubView(int[] baseIndices) => new(this.Obj, baseIndices);

    protected virtual ArrayWrapper CreateSubView(int rangeFrom, int rangeTo, int[] baseIndices) =>
        new(this.Obj, rangeFrom, rangeTo, baseIndices);
}
