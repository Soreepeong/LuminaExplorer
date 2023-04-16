using System.ComponentModel;
using System.Reflection;

namespace LuminaExplorer.Core.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public abstract class BaseWrapper<T> : ICustomTypeDescriptor {
    protected static readonly WrapperTypeConverter Converter = new();
    
    protected const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    public readonly T Obj;

    protected BaseWrapper(T obj) {
        if (obj is null)
            throw new NullReferenceException();
        Obj = obj;
    }

    public override string ToString() => $"{Obj}";

    public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(Obj!);

    public string? GetClassName() => TypeDescriptor.GetClassName(Obj!);

    public string? GetComponentName() => TypeDescriptor.GetComponentName(Obj!);

    public TypeConverter? GetConverter() => TypeDescriptor.GetConverter(Obj!);

    public EventDescriptor? GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(Obj!);

    public PropertyDescriptor? GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(Obj!);

    public object? GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(Obj!, editorBaseType);

    public EventDescriptorCollection GetEvents() => GetEvents(null);

    public EventDescriptorCollection GetEvents(Attribute[]? attributes) =>
        TypeDescriptor.GetEvents(Obj!, attributes, false);

    public PropertyDescriptorCollection GetProperties() => GetProperties(null);

    public abstract PropertyDescriptorCollection GetProperties(Attribute[]? attributes);

    // Probably wrong, but don't care, unless something breaks.
    public object? GetPropertyOwner(PropertyDescriptor? pd) => this;

    protected virtual object? TransformObject(object? obj) => obj;

    protected class SimplePropertyDescriptor : PropertyDescriptor {
        private readonly Lazy<object?> _resolver;

        public SimplePropertyDescriptor(
            Type componentType,
            string name,
            Type propertyType,
            Lazy<object?> resolver,
            string? categoryName,
            string? description)
            : base(name, new Attribute?[] {
                categoryName is null ? null : new CategoryAttribute(categoryName),
                description is null ? null : new DescriptionAttribute(description)
            }.Where(x => x != null).ToArray()!) {
            ComponentType = componentType;
            PropertyType = propertyType;
            _resolver = resolver;
        }

        public override Type ComponentType { get; }

        public override bool IsReadOnly => true;

        public override Type PropertyType { get; }

        public override bool CanResetValue(object component) => false;

        public override object? GetValue(object? component) => _resolver.Value;

        public override void ResetValue(object component) => throw new NotSupportedException();

        public override void SetValue(object? component, object? value) => throw new NotSupportedException();

        public override bool ShouldSerializeValue(object component) => false;
    }
}
