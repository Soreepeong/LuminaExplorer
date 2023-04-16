using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public class ObjectWrapper : BaseWrapper<object> {
    internal ObjectWrapper(object obj) : base(obj) { }

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) {
        var pds = new PropertyDescriptorCollection(null);
        
        var obj = TransformObject(Obj);
        if (obj is null)
            return pds;
        
        var type = obj.GetType();

        var skipFields = false;
        skipFields |= obj is DictionaryEntry;
        skipFields |= type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
        if (!skipFields) {
            foreach (var info in type.GetFields(bindingFlags)) {
                if (info.Name.StartsWith('<') && info.Name.EndsWith(">k__BackingField"))
                    continue;

                if (info.GetCustomAttributes(typeof(ObsoleteAttribute), false).Any())
                    continue;

                var accessModifier = info.IsPublic ? "public" :
                    info.IsAssembly ? "internal" :
                    info.IsFamily ? "protected" :
                    info.IsFamilyOrAssembly ? "protected public" :
                    info.IsFamilyAndAssembly ? "private protected" :
                    info.IsPrivate ? "private" :
                    "";

                var category = info.DeclaringType?.ToString();
                var description = $"{accessModifier} {info.FieldType.GetCSharpTypeName()} {info.Name};";
                
                Type fieldType;
                Func<object?> valueResolver;
                if (info.TryGetCopyOfFixedArray(obj, out var array)) {
                    fieldType = Converter.GetWrapperType(array.GetType());
                    valueResolver = () => Converter.ConvertFrom(null, null, array);
                } else if (Converter.CanConvertFrom(null, info.FieldType)) {
                    fieldType = Converter.GetWrapperType(info.FieldType);
                    valueResolver = () => Converter.ConvertFrom(null, null, info.GetValue(obj));
                } else {
                    fieldType = info.FieldType;
                    valueResolver = () => info.GetValue(obj);
                }
                
                pds.Add(new SimplePropertyDescriptor(type, info.Name, fieldType, new(valueResolver), category, description));
            }
        }

        var skipProperties = false;
        skipProperties |= type.IsAssignableTo(typeof(ITuple));
        if (!skipProperties) {
            foreach (var info in type.GetProperties(bindingFlags)) {
                if (info.GetCustomAttributes(typeof(ObsoleteAttribute), false).Any())
                    continue;

                var getAccessModifier = info.GetMethod is null ? null :
                    info.GetMethod.IsPublic ? "get" :
                    info.GetMethod.IsAssembly ? "internal get" :
                    info.GetMethod.IsFamily ? "protected get" :
                    info.GetMethod.IsFamilyOrAssembly ? "protected public get" :
                    info.GetMethod.IsFamilyAndAssembly ? "private protected get" :
                    info.GetMethod.IsPrivate ? "private get" :
                    "??? get";
                var setAccessModifier = info.SetMethod is null ? null :
                    info.SetMethod.IsPublic ? "set" :
                    info.SetMethod.IsAssembly ? "internal set" :
                    info.SetMethod.IsFamily ? "protected set" :
                    info.SetMethod.IsFamilyOrAssembly ? "protected public set" :
                    info.SetMethod.IsFamilyAndAssembly ? "private protected set" :
                    info.SetMethod.IsPrivate ? "private set" :
                    "??? set";

                var accessModifiers = setAccessModifier is null && getAccessModifier is null ? ""
                    : getAccessModifier is null ? $"{setAccessModifier};"
                    : setAccessModifier is null ? $"{getAccessModifier};" 
                    : $"{getAccessModifier}; {setAccessModifier};";

                var category = info.DeclaringType?.ToString();
                var description = $"{info.PropertyType.GetCSharpTypeName()} {info.Name} {{ {accessModifiers} }};";
                
                Type fieldType;
                Func<object?> valueResolver;
                if (Converter.CanConvertFrom(null, info.PropertyType)) {
                    fieldType = Converter.GetWrapperType(info.PropertyType);
                    valueResolver = () => Converter.ConvertFrom(null, null, info.GetValue(obj));
                } else {
                    fieldType = info.PropertyType;
                    valueResolver = () => info.GetValue(obj);
                }
                
                pds.Add(new SimplePropertyDescriptor(type, info.Name, fieldType, new(valueResolver), category, description));
            }
        }

        return pds;
    }
}
