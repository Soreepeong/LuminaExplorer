using System.ComponentModel;
using LuminaExplorer.Util;

namespace LuminaExplorer.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public class ObjectWrapper : BaseWrapper<object> {
    internal ObjectWrapper(object obj) : base(obj) { }

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) {
        var type = Obj.GetType();
        var pds = new PropertyDescriptorCollection(null);

        foreach (var info in type.GetFields(bindingFlags)) {
            if (info.TryGetCopyOfFixedArray(Obj, out var array)) {
                pds.Add(new SimplePropertyDescriptor(type, info.Name, typeof(ObjectWrapper),
                    new(() => WrapperTypeConverter.Instance.ConvertFrom(null, null, array))));
            } else if (WrapperTypeConverter.Instance.CanConvertFrom(null, info.FieldType)) {
                pds.Add(new SimplePropertyDescriptor(type, info.Name, typeof(ObjectWrapper),
                    new(() => WrapperTypeConverter.Instance.ConvertFrom(null, null, info.GetValue(Obj)))));
            } else {
                pds.Add(new SimplePropertyDescriptor(type, info.Name, info.FieldType,
                    new(() => info.GetValue(Obj))));
            }
        }

        foreach (var info in type.GetProperties(bindingFlags)) {
            if (WrapperTypeConverter.Instance.CanConvertFrom(null, info.PropertyType)) {
                pds.Add(new SimplePropertyDescriptor(type, info.Name, typeof(ObjectWrapper),
                    new(() => WrapperTypeConverter.Instance.ConvertFrom(null, null, info.GetValue(Obj)))));
            } else {
                pds.Add(new SimplePropertyDescriptor(type, info.Name, info.PropertyType,
                    new(() => info.GetValue(Obj))));
            }
        }

        return pds;
    }
}
