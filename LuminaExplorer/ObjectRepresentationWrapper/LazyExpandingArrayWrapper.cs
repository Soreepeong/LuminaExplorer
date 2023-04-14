using System.ComponentModel;
using System.Reflection;

namespace LuminaExplorer.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public class LazyExpandingArrayWrapper : ArrayWrapper {
    private readonly Type _expectingType;

    internal LazyExpandingArrayWrapper(Array obj, Type expectingType) : this(obj, expectingType, Array.Empty<int>()) { }

    internal LazyExpandingArrayWrapper(Array obj, Type expectingType, int[] baseIndices) :
        base(obj, baseIndices) {
        _expectingType = expectingType;
    }

    protected LazyExpandingArrayWrapper(Array obj, Type expectingType, int rangeFrom, int rangeTo, int[] baseIndices) :
        base(obj, rangeFrom, rangeTo, baseIndices) {
        _expectingType = expectingType;
    }

    public override string ToString() {
        if (!BaseIndices.Any() && RangeFrom == 0 && RangeTo == Obj.GetLength(0))
            return
                $"{_expectingType.Name}[{string.Join(", ", Enumerable.Range(0, Obj.Rank).Select(x => Obj.GetLength(x)))}]";

        return base.ToString();
    }

    protected override object? TransformObject(object? obj) {
        if (obj?.GetType().GetGenericTypeDefinition() == typeof(Lazy<>))
            return obj.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)!.GetValue(obj);

        return base.TransformObject(obj);
    }

    protected override Type TransformValueType(Type type) =>
        type.GetGenericTypeDefinition() == typeof(Lazy<>)
            ? type.GetGenericArguments()[0]
            : base.TransformValueType(type);

    protected override ArrayWrapper CreateSubView(int[] baseIndices) =>
        new LazyExpandingArrayWrapper(Obj, _expectingType, baseIndices);

    protected override ArrayWrapper CreateSubView(int rangeFrom, int rangeTo, int[] baseIndices) =>
        new LazyExpandingArrayWrapper(Obj, _expectingType, rangeFrom, rangeTo, baseIndices);
}
