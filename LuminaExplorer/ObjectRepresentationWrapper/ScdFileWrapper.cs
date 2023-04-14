using System.ComponentModel;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Scd;

namespace LuminaExplorer.ObjectRepresentationWrapper; 

[TypeConverter(typeof(WrapperTypeConverter))]
public class ScdFileWrapper : ObjectWrapper {
    private readonly ScdFile _obj;

    internal ScdFileWrapper(ScdFile obj) : base(obj) {
        _obj = obj;
    }
    
    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) {
        var type = _obj.GetType();
        var catAttr = new CategoryAttribute("SCD Structures");
        var pds = base.GetProperties(attributes);

        _obj.Reader.Position = 0;
        var binaryHeader = new BinaryHeader(_obj.Reader);
        var scdHeader = _obj.Reader.ReadStructure<ScdHeader>();
        
        pds.Add(new SimplePropertyDescriptor(type, "(Header)", Converter.GetWrapperType<BinaryHeader>(),
            new(() => Converter.ConvertFrom(binaryHeader)), catAttr));

        pds.Add(new SimplePropertyDescriptor(type, "(ScdHeader)", Converter.GetWrapperType<ScdHeader>(),
            new(() => Converter.ConvertFrom(scdHeader)), catAttr));

        pds.Add(new SimplePropertyDescriptor(type, "(Sounds)", typeof(LazyExpandingArrayWrapper),
            new(() => new LazyExpandingArrayWrapper(
                Enumerable.Range(0, _obj.SoundDataCount)
                    .Select(x => new Lazy<object>(() => _obj.GetSound(x)))
                    .ToArray(),
                _obj.GetType().GetMethod("GetSound")!.ReturnType)), catAttr));

        pds.Add(new SimplePropertyDescriptor(type, "(Tracks)", typeof(LazyExpandingArrayWrapper),
            new(() => new LazyExpandingArrayWrapper(
                Enumerable.Range(0, _obj.TrackDataCount)
                    .Select(x => new Lazy<object>(() => _obj.GetTrack(x)))
                    .ToArray(),
                _obj.GetType().GetMethod("GetTrack")!.ReturnType)), catAttr));

        pds.Add(new SimplePropertyDescriptor(type, "(Audios)", typeof(LazyExpandingArrayWrapper),
            new(() => new LazyExpandingArrayWrapper(
                Enumerable.Range(0, _obj.AudioDataCount)
                    .Select(x => new Lazy<object>(() => _obj.GetAudio(x)))
                    .ToArray(),
                _obj.GetType().GetMethod("GetAudio")!.ReturnType)), catAttr));
        
        pds.Add(new SimplePropertyDescriptor(type, "(Layout)", Converter.GetWrapperType<SoundObject?>(),
            new(() => Converter.ConvertFrom(null, null, _obj.GetLayout())), catAttr));
        
        pds.Add(new SimplePropertyDescriptor(type, "(AttributeData)", Converter.GetWrapperType<AttributeData?>(),
            new(() => Converter.ConvertFrom(null, null, _obj.GetAttributeData())), catAttr));

        return pds;
    }
}
