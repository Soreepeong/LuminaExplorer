namespace LuminaExplorer.Core.Util;

public static class TypeExtensions {
    // https://stackoverflow.com/a/37184228/1800296
    public static bool IsDerivedFromGenericParent(this Type? type, Type parentType) {
        if (!parentType.IsGenericType)
            throw new ArgumentException(@"Type must be generic", nameof(parentType));

        if (type == null || type == typeof(object))
            return false;
        
        if (type.IsGenericType && type.GetGenericTypeDefinition() == parentType)
            return true;

        return type.BaseType.IsDerivedFromGenericParent(parentType)
               || type.GetInterfaces().Any(t => t.IsDerivedFromGenericParent(parentType));
    }
    
    public static bool TryFindTypedGenericParent(this Type? type, Type parentType, out Type resolvedParentType) {
        resolvedParentType = type!;
        
        if (!parentType.IsGenericType)
            throw new ArgumentException(@"Type must be generic", nameof(parentType));

        if (type == null || type == typeof(object))
            return false;
        
        if (type.IsGenericType && type.GetGenericTypeDefinition() == parentType)
            return true;

        if (type.BaseType.TryFindTypedGenericParent(parentType, out resolvedParentType))
            return true;
        
        foreach (var iface in type.GetInterfaces())
            if (iface.TryFindTypedGenericParent(parentType, out resolvedParentType))
                return true;
        
        return false;
    }
}
