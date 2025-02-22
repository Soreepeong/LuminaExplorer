namespace LuminaExplorer.Core.ExtraFormats.GltfInterop;

public struct TypedVec4<T> where T : unmanaged {
    public T V1 = default;
    public T V2 = default;
    public T V3 = default;
    public T V4 = default;

    public TypedVec4()
    { }

    public TypedVec4(T v1, T v2, T v3, T v4)
    {
        this.V1 = v1;
        this.V2 = v2;
        this.V3 = v3;
        this.V4 = v4;
    }
}
