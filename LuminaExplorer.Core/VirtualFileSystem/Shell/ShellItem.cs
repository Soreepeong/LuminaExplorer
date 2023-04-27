using System;

namespace LuminaExplorer.Core.VirtualFileSystem.Shell;

public class ShellItem : IEquatable<ShellItem> {
    public readonly ShellItemId Idl;
    public readonly ShellItemFlags Flags;

    public ShellItem(ShellItemId idl, string name, ShellItemFlags flags, ShellFolder? parent) {
        Idl = idl;
        Name = name;
        Flags = flags;
        ParentTyped = parent;
    }

    public ShellFolder? ParentTyped { get; }

    public string Name { get; }

    public bool Equals(ShellItem? other) {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Idl.Equals(other.Idl) && Equals(ParentTyped, other.ParentTyped);
    }

    public override bool Equals(object? obj) => Equals(obj as ShellItem);

    public override int GetHashCode() => HashCode.Combine(Idl, ParentTyped);

    public override string ToString() => Name;
}