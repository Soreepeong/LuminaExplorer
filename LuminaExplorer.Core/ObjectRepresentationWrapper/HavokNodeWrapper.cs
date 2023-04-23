using System;
using System.ComponentModel;
using LuminaExplorer.Core.ExtraFormats.HavokAnimation;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile;
using LuminaExplorer.Core.ExtraFormats.HavokTagfile.Value;

namespace LuminaExplorer.Core.ObjectRepresentationWrapper;

[TypeConverter(typeof(WrapperTypeConverter))]
public class HavokNodeWrapper : BaseWrapper<Node> {
    internal HavokNodeWrapper(Node obj) : base(obj) { }

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) {
        var pds = new PropertyDescriptorCollection(null);

        var obj = TransformObject(Obj);
        if (obj is null)
            return pds;

        var type = Obj.GetType();
        for (var i = 0; i < Obj.Definition.NestedFields.Count; i++) {
            var k = Obj.Definition.NestedFields[i];
            var v = Obj.Values[i];
            switch (v) {
                case ValueByte vb:
                    pds.Add(new SimplePropertyDescriptor(type, k.Name, Converter.GetWrapperType<byte>(),
                        new(() => Converter.ConvertFrom(vb.Value)), k.FieldOwner.Name, null));
                    break;
                case ValueInt vi:
                    pds.Add(new SimplePropertyDescriptor(type, k.Name, Converter.GetWrapperType<int>(),
                        new(() => Converter.ConvertFrom(vi.Value)), k.FieldOwner.Name, null));
                    break;
                case ValueFloat vf:
                    pds.Add(new SimplePropertyDescriptor(type, k.Name, Converter.GetWrapperType<float>(),
                        new(() => Converter.ConvertFrom(vf.Value)), k.FieldOwner.Name, null));
                    break;
                case ValueString vs:
                    pds.Add(new SimplePropertyDescriptor(type, k.Name, Converter.GetWrapperType<string>(),
                        new(() => Converter.ConvertFrom(vs.Value)), k.FieldOwner.Name, null));
                    break;
                case ValueNode vn:
                    pds.Add(new SimplePropertyDescriptor(type, k.Name, typeof(HavokNodeWrapper),
                        new(() => new HavokNodeWrapper(vn.Node)), k.FieldOwner.Name, null));
                    break;
                case ValueArray va:
                    pds.Add(new SimplePropertyDescriptor(type, k.Name, Converter.GetWrapperType<ValueArray>(),
                        new(() => Converter.ConvertFrom(va)), k.FieldOwner.Name, null));
                    break;
                case null:
                    pds.Add(new SimplePropertyDescriptor(type, k.Name, typeof(void),
                        new(() => null), k.FieldOwner.Name, null));
                    break;
            }
        }

        if (Obj.Definition.Name == "hkaAnimationBinding") {
            pds.Add(new SimplePropertyDescriptor(
                type,
                "(Animation)"
                , Converter.GetWrapperType<AnimationSet>(),
                new(() => Converter.ConvertFrom(AnimationSet.Decode(Obj))),
                "(Parsed)", null));
        }

        return pds;
    }
}
