using Lumina.Data;
using LuminaExplorer.LazySqPackTree;
using System.Reflection;
using Be.Windows.Forms;
using Lumina.Data.Attributes;
using Lumina.Data.Structs;
using LuminaExplorer.ObjectRepresentationWrapper;

namespace LuminaExplorer.AppControl; 

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

            _fileResource ??= file.GetFile();

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