﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Lumina.Data.Files;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Core.ObjectRepresentationWrapper;

public class WrapperTypeConverter : TypeConverter {
    [SuppressMessage("ReSharper", "PossibleMistakenCallToGetType.2")]
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) {
        // Do not convert RuntimeType
        if (sourceType == typeof(Type).GetType())
            return false;

        if (sourceType.IsPrimitive)
            return false;

        if (sourceType == typeof(string))
            return false;

        // Do not double-wrap
        if (IsWrappedType(sourceType))
            return false;

        return true;
    }

    public Type GetWrapperType(Type t) {
        if (IsWrappedType(t))
            return t;
        
        if (t.IsAssignableTo(typeof(Array)))
            return typeof(ArrayWrapper);

        if (t.IsAssignableTo(typeof(ICollection)))
            return typeof(ArrayWrapper);

        if (t.IsAssignableTo(typeof(ScdFile)))
            return typeof(ScdFileWrapper);

        if (t.IsAssignableTo(typeof(ExtraFormats.HavokTagfile.Node)))
            return typeof(HavokNodeWrapper);
        
        if (t.IsAssignableTo(typeof(ExtraFormats.HavokTagfile.Value.ValueArray)))
            return typeof(HavokArrayWrapper);

        return typeof(ObjectWrapper);
    }

    public Type GetWrapperType<T>() => GetWrapperType(typeof(T));

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value) {
        if (value is null)
            return null;

        var vt = value.GetType();
        if (IsWrappedType(vt))
            return value;

        switch (value) {
            case Array arr:
                return new ArrayWrapper(arr);
            case ICollection col:
                if (vt.IsGenericType && vt.TryFindTypedGenericParent(typeof(ICollection<>), out var typedGenericCollection)) {
                    var arr = Array.CreateInstance(typedGenericCollection.GenericTypeArguments[0], col.Count);
                    var i = 0;
                    foreach (var c in col)
                        arr.SetValue(c, i++);
                    return new ArrayWrapper(arr);
                }
                
                return new ArrayWrapper((from object? c in col select ConvertFrom(null, null, c)).ToArray());
            case ScdFile scdFile:
                return new ScdFileWrapper(scdFile);
            case ExtraFormats.HavokTagfile.Node havokNode:
                return new HavokNodeWrapper(havokNode);
            case ExtraFormats.HavokTagfile.Value.ValueArray havokValueArray:
                return new HavokArrayWrapper(havokValueArray);
            default:
                return new ObjectWrapper(value);
        }
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value,
        Type destinationType) {
        return destinationType == typeof(string) && value is ObjectWrapper or ArrayWrapper
            ? value.ToString()
            : base.ConvertTo(context, culture, value, destinationType);
    }

    public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

    public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value,
        Attribute[]? attributes) {
        return TypeDescriptor.GetProperties(value, attributes);
    }

    public bool IsWrappedType(Type? type) {
        while (type is not null) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BaseWrapper<>))
                return true;
            type = type.BaseType;
        }

        return false;
    }
}