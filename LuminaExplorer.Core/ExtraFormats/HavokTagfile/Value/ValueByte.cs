using System.Collections.Immutable;
using System.Linq;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Field;

namespace LuminaExplorer.Core.ExtraFormats.HavokTagfile.Value;

public class ValueByte : IValue {
    public readonly byte Value;

    public ValueByte(byte value) {
        Value = value;
    }

    public override string ToString() => $"{Value}";

    public static implicit operator byte(ValueByte d) => d.Value;

    internal static ValueByte Read(Parser parser) => new(parser.ReadByte());

    internal static ValueArray ReadVector(Parser parser, int count)
        => new(Enumerable.Range(0, count).Select(_ => (IValue?) Read(parser)).ToImmutableList(), FieldType.SingleByte);
}
