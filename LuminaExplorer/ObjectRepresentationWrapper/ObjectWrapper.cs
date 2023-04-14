using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LuminaExplorer.Util;

namespace LuminaExplorer.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public class ObjectWrapper : BaseWrapper<object> {
    internal ObjectWrapper(object obj) : base(obj) { }

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) {
        var pds = new PropertyDescriptorCollection(null);
        
        var obj = TransformObject(Obj);
        if (obj is null)
            return pds;
        
        var type = obj.GetType();

        var categoryAttributes = new Dictionary<Type, CategoryAttribute>();

        var skipFields = false;
        skipFields |= obj is DictionaryEntry;
        skipFields |= type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
        if (!skipFields) {
            foreach (var info in type.GetFields(bindingFlags)) {
                if (info.Name.StartsWith('<') && info.Name.EndsWith(">k__BackingField"))
                    continue;

                if (info.GetCustomAttributes(typeof(ObsoleteAttribute), false).Any())
                    continue;

                CategoryAttribute? catAttr = null;
                if (info.DeclaringType != null && !categoryAttributes.TryGetValue(info.DeclaringType, out catAttr))
                    categoryAttributes.Add(info.DeclaringType, catAttr = new(info.DeclaringType.ToString()));

                if (info.TryGetCopyOfFixedArray(obj, out var array)) {
                    pds.Add(new SimplePropertyDescriptor(type, info.Name, Converter.GetWrapperType(array.GetType()),
                        new(() => Converter.ConvertFrom(null, null, array)), catAttr));
                } else if (Converter.CanConvertFrom(null, info.FieldType)) {
                    pds.Add(new SimplePropertyDescriptor(type, info.Name, Converter.GetWrapperType(info.FieldType),
                        new(() => Converter.ConvertFrom(null, null, info.GetValue(obj))), catAttr));
                } else {
                    pds.Add(new SimplePropertyDescriptor(type, info.Name, info.FieldType,
                        new(() => info.GetValue(obj)), catAttr));
                }
            }
        }

        var skipProperties = false;
        skipProperties |= type.IsAssignableTo(typeof(ITuple));
        if (!skipProperties) {
            foreach (var info in type.GetProperties(bindingFlags)) {
                if (info.GetCustomAttributes(typeof(ObsoleteAttribute), false).Any())
                    continue;

                CategoryAttribute? catAttr = null;
                if (info.DeclaringType != null && !categoryAttributes.TryGetValue(info.DeclaringType, out catAttr))
                    categoryAttributes.Add(info.DeclaringType, catAttr = new(info.DeclaringType.ToString()));

                if (Converter.CanConvertFrom(null, info.PropertyType)) {
                    pds.Add(new SimplePropertyDescriptor(type, info.Name, Converter.GetWrapperType(info.PropertyType),
                        new(() => Converter.ConvertFrom(null, null, info.GetValue(obj))), catAttr));
                } else {
                    pds.Add(new SimplePropertyDescriptor(type, info.Name, info.PropertyType,
                        new(() => info.GetValue(obj)), catAttr));
                }
            }
        }

        return pds;
    }
}
