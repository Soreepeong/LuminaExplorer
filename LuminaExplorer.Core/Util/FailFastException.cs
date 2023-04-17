namespace LuminaExplorer.Core.Util; 

public class FailFastException : Exception{
    public FailFastException(string? s, Exception? e = null) => Environment.FailFast(s, e);
}
