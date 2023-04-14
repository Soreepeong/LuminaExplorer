using Lumina.Data;
using LuminaExplorer.LazySqPackTree;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Be.Windows.Forms;
using Lumina.Data.Attributes;
using Lumina.Data.Structs;
using Exception = System.Exception;

namespace LuminaExplorer.AppControl {
    public partial class FileViewControl : UserControl {
        private readonly Dictionary<string, MethodInfo> _getFileByExtension;
        private readonly Dictionary<uint, MethodInfo> _getFileBySignature;
        private readonly HexBox _hexbox;

        private VirtualFile? _file;
        private FileResource? _fileResource;

        public FileViewControl() {
            InitializeComponent();

            var fileResourceType = typeof(FileResource);
            var allResourceTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.GetTypes())
                .Where(x => fileResourceType.IsAssignableFrom(x) && x != fileResourceType)
                .ToArray();
            var genericGetFile = typeof(VirtualFile).GetMethod("GetFileTyped")!;
            _getFileByExtension = allResourceTypes.ToDictionary(
                x => (x.GetCustomAttribute<FileExtensionAttribute>()?.Extension ?? $".{x.Name[..^4]}")
                    .ToLowerInvariant(),
                x => genericGetFile.MakeGenericMethod(x));

            _getFileByExtension[".atex"] = _getFileByExtension[".tex"];

            _getFileBySignature = new() {
                {
                    0x42444553u, _getFileByExtension[".scd"]
                },
            };

            tabRaw.Controls.Add(_hexbox = new() {
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                Dock = DockStyle.Fill,
                Font = new(FontFamily.GenericMonospace, 11),
                VScrollBarVisible = true,
                ColumnInfoVisible = true,
                GroupSeparatorVisible = true,
                LineInfoVisible = true,
                StringViewVisible = true,
                ReadOnly = true,
            });
        }

        public void SetFile(VirtualFile? file) {
            _file = file;
            _fileResource = null;

            var tabsToHide = new List<TabPage> {
                tabProperties,
                tabRaw,
            };

            if (file is null) {
                // tabsToHide.ForEach(x => x.Hide());
                return;
            }

            var tabsToShow = new List<TabPage>();

            try {
                var possibleTypes = new List<MethodInfo>();

                switch (file.Metadata.Type) {
                    case FileType.Empty:
                        // TODO: deal with hidden files
                        throw new FileNotFoundException();

                    case FileType.Standard:
                        if (file.NameResolved) {
                            if (_getFileByExtension.TryGetValue(Path.GetExtension(file.Name).ToLowerInvariant(),
                                    out var type))
                                possibleTypes.Add(type);
                        }

                        if (file.Metadata.RawFileSize >= 4) {
                            // TODO: peek
                            if (_getFileBySignature.TryGetValue(BitConverter.ToUInt32(file.GetFile().Data[..4]),
                                    out var type))
                                possibleTypes.Add(type);
                        }

                        break;

                    case FileType.Model:
                        possibleTypes.Add(_getFileByExtension[".mdl"]);
                        break;

                    case FileType.Texture:
                        possibleTypes.Add(_getFileByExtension[".tex"]);
                        break;
                }

                possibleTypes.Reverse();
                foreach (var f in possibleTypes) {
                    try {
                        if (f.Invoke(file, null) is not FileResource fr)
                            continue;

                        _fileResource = fr;
                    } catch (Exception) {
                        // pass 
                    }
                }

                if (_fileResource is null)
                    _fileResource = file.GetFile();

            } catch (FileNotFoundException) {
                // TODO: show that the file is empty (placeholder)
            }

            if (_fileResource != null) {
                tabsToHide.Remove(tabRaw);
                tabsToShow.Add(tabRaw);
                tabsToHide.Remove(tabProperties);
                tabsToShow.Add(tabProperties);

                _hexbox.ByteProvider = new FileResourceByteProvider(_fileResource);
                propertyGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(_fileResource);
            } else {
                _hexbox.ByteProvider = null;
                propertyGrid.SelectedObject = null;
            }

        }

        [TypeConverter(typeof(WrapperTypeConverter))]
        private class Wrapper {
            public readonly object Inner;

            public Wrapper(object inner) {
                Inner = inner;
            }

            public override string ToString() => $"{Inner}";
        }

        [TypeConverter(typeof(WrapperTypeConverter))]
        private class ArrayWrapper {
            public readonly Array Array;
            public readonly int[] BaseIndices;
            public readonly int RangeFrom;
            public readonly int RangeTo;
            public readonly int RangeJumpUnit;

            public ArrayWrapper(Array array, int rangeFrom = 0, int rangeTo = -1, params int[] baseIndices) {
                Array = array;
                BaseIndices = baseIndices;
                RangeFrom = rangeFrom;
                RangeTo = rangeTo == -1 ? array.GetLength(baseIndices.Length) : rangeTo;

                RangeJumpUnit = 1;
                while (RangeTo - RangeFrom > 100 * RangeJumpUnit) {
                    RangeJumpUnit *= 100;
                }
            }

            public int Length => (RangeTo - RangeFrom + RangeJumpUnit - 1) / RangeJumpUnit;

            public bool IsFlat => BaseIndices.Length + 1 == Array.Rank;

            public override string ToString() {
                if (!BaseIndices.Any() && RangeFrom == 0 && RangeTo == Array.GetLength(0))
                    return $"{Array.GetType().GetElementType()!.Name}[{
                        string.Join(", ", Enumerable.Range(0, Array.Rank).Select(x => Array.GetLength(x)))
                    }]";

                if (!BaseIndices.Any())
                    return $"[{RangeFrom}..{RangeTo}]";
                
                return $"[{string.Join(", ", BaseIndices)}, {RangeFrom}..{RangeTo}]"; 
            }

            public string GetValueName(int i) {
                if (i < 0 || i >= Length)
                    throw new IndexOutOfRangeException();
                
                var len = 0;
                for (var t = RangeTo - 1; t > 0; t /= 10)
                    len++;

                if (RangeJumpUnit == 1)
                    return "[" + (RangeFrom + i).ToString().PadLeft(len, '0') + "]";
                
                return "[" +
                       (RangeFrom + i * RangeJumpUnit).ToString().PadLeft(len, '0') +
                       ".." +
                       Math.Min(RangeTo, (i + 1) * RangeJumpUnit).ToString().PadLeft(len, '0') +
                       "]";
            }

            public Type GetValueType(int i) {
                if (i < 0 || i >= Length)
                    throw new IndexOutOfRangeException();
                if (RangeJumpUnit == 1 && IsFlat)
                    return Array.GetType().GetElementType()!;
                return typeof(ArrayWrapper);
            }

            public object? this[int i] {
                get {
                    if (i < 0 || i >= Length)
                        throw new IndexOutOfRangeException();
                    if (RangeJumpUnit != 1) {
                        return new ArrayWrapper(Array,
                            RangeFrom + i * RangeJumpUnit,
                            Math.Min(RangeTo, (i + 1) * RangeJumpUnit),
                            BaseIndices);
                    }

                    if (!IsFlat) {
                        return new ArrayWrapper(
                            Array,
                            0,
                            -1,
                            BaseIndices.Append(RangeFrom + i).ToArray());
                    }

                    var obj = Array.GetValue(BaseIndices.Append(RangeFrom + i * RangeJumpUnit).ToArray());
                    if (obj is null)
                        return null;
                    if (!WrapperTypeConverter.Instance.CanConvertFrom(null, obj.GetType()))
                        return obj;
                    return WrapperTypeConverter.Instance.ConvertFrom(null, null, obj);
                }
            }
        }

        private class WrapperTypeConverter : TypeConverter {
            public static readonly WrapperTypeConverter Instance = new();
            
            [SuppressMessage("ReSharper", "PossibleMistakenCallToGetType.2")]
            public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) {
                // Do not convert RuntimeType
                if (sourceType == typeof(Type).GetType())
                    return false;

                if (sourceType.IsPrimitive)
                    return false;

                if (sourceType == typeof(string))
                    return false;

                return true;
            }

            public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value) {
                if (value is null)
                    return null;

                if (value is Array arr)
                    return new ArrayWrapper(arr);

                if (value is ICollection col)
                    return new ArrayWrapper(
                        (from object? c in col select ConvertFrom(null, null, c)).ToArray());

                return new Wrapper(value);
            }

            public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value,
                Type destinationType) {
                return destinationType == typeof(string) && value is Wrapper or ArrayWrapper
                    ? value.ToString()
                    : base.ConvertTo(context, culture, value, destinationType);
            }

            public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

            public override PropertyDescriptorCollection? GetProperties(ITypeDescriptorContext? context, object value,
                Attribute[]? attributes) {
                const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                switch (value) {
                    case ArrayWrapper arrayWrapper: {
                        var pds = new PropertyDescriptorCollection(null);

                        foreach (var i in Enumerable.Range(0, arrayWrapper.Length)) {
                            pds.Add(new LazyPropertyDescriptor(
                                typeof(ArrayWrapper),
                                arrayWrapper.GetValueName(i),
                                arrayWrapper.GetValueType(i),
                                () => arrayWrapper[i]));
                        }

                        return pds;
                    }
                    
                    case Wrapper wrapper: {
                        var type = wrapper.Inner.GetType();
                        var pds = new PropertyDescriptorCollection(null);

                        foreach (var info in type.GetFields(bindingFlags)) {
                            if (info.GetCustomAttributes(typeof(FixedBufferAttribute), false)
                                    .FirstOrDefault() is FixedBufferAttribute fixedAttr) {
                                // TODO: ?
                            }
                            if (CanConvertFrom(context, info.FieldType)) {
                                pds.Add(new LazyPropertyDescriptor(type, info.Name, typeof(Wrapper),
                                    () => ConvertFrom(null, null, info.GetValue(wrapper.Inner))));
                            } else {
                                pds.Add(new LazyPropertyDescriptor(type, info.Name, info.FieldType,
                                    () => info.GetValue(wrapper.Inner)));
                            }
                        }

                        foreach (var info in type.GetProperties(bindingFlags)) {
                            if (CanConvertFrom(context, info.PropertyType)) {
                                pds.Add(new LazyPropertyDescriptor(type, info.Name, typeof(Wrapper),
                                    () => ConvertFrom(null, null, info.GetValue(wrapper.Inner))));
                            } else {
                                pds.Add(new LazyPropertyDescriptor(type, info.Name, info.PropertyType,
                                    () => info.GetValue(wrapper.Inner)));
                            }
                        }

                        return pds;
                    }
                    
                    default:
                        return TypeDescriptor.GetProperties(value, attributes);
                }
            }

            private class LazyPropertyDescriptor : SimplePropertyDescriptor {
                private readonly Func<object?> _getter;

                public LazyPropertyDescriptor(Type componentType, string name, Type propertyType, Func<object?> getter)
                    : base(componentType, name, propertyType) {
                    _getter = getter;
                }

                public override object? GetValue(object? component) => _getter();

                public override void SetValue(object? component, object? value) => throw new NotSupportedException();

                public override bool IsReadOnly => true;
            }
        }

        private class FileResourceByteProvider : IByteProvider {
            private readonly FileResource _fileResource;

            public FileResourceByteProvider(FileResource fileResource) {
                _fileResource = fileResource;
            }

            public byte ReadByte(long index) => _fileResource.Data[index];

            public void WriteByte(long index, byte value) => throw new NotSupportedException();

            public void InsertBytes(long index, byte[] bs) => throw new NotSupportedException();

            public void DeleteBytes(long index, long length) => throw new NotSupportedException();

            public long Length => _fileResource.Data.LongLength;

            public event EventHandler? LengthChanged;

            public bool HasChanges() => false;

            public void ApplyChanges() => throw new NotSupportedException();

            public event EventHandler? Changed;

            public bool SupportsWriteByte() => false;

            public bool SupportsInsertBytes() => false;

            public bool SupportsDeleteBytes() => false;
        }
    }
}
