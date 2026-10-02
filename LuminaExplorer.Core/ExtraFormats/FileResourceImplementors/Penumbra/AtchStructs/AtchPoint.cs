namespace LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra.AtchStructs;

/// <summary>An attachment point, with one entry per state.</summary>
public class AtchPoint {
    public AtchType Type;
    public bool Accessory;
    public AtchEntry[] Entries = [];

    public string Name => this.Type.ToName();

    public override string ToString() => $"{this.Type.ToAbbreviation()} ({this.Name}){(this.Accessory ? " [Accessory]" : "")}";
}
