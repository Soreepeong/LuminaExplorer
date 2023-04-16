namespace LuminaExplorer.Core.Util; 

public static class PrimitiveExtensions {
    public static bool IsWhiteSpace(this uint n) => n < 0x10000 && char.IsWhiteSpace((char) n);
}
