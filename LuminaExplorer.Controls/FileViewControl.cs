using Be.Windows.Forms;
using Lumina.Data;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.ObjectRepresentationWrapper;

namespace LuminaExplorer.Controls;

public partial class FileViewControl : UserControl {
    private readonly HexBox _hexbox;

    private VirtualSqPackTree? _vspTree;
    private VirtualFile? _file;
    private FileResource? _fileResource;

    public FileViewControl() {
        InitializeComponent();

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

    public void ClearFile() {
        _vspTree = null;
        _file = null;
        _fileResource = null;
        _hexbox.ByteProvider = null;
        propertyGrid.SelectedObject = null;
        tabs.Hide();
    }

    public void SetFile(VirtualSqPackTree? vspTree, VirtualFile? file) {
        _vspTree = vspTree;
        _file = file;
        _fileResource = null;

        if (file is null || _vspTree is null) {
            ClearFile();
            return;
        }

        try {
            _vspTree.GetLookup(file).AsFileResource().ContinueWith(fr => {
                if (_file != file || _vspTree != vspTree)
                    return;

                if (!fr.IsCompletedSuccessfully) {
                    ClearFile();
                    return;
                }
                
                _fileResource = fr.Result;
                _hexbox.ByteProvider = new FileResourceByteProvider(_fileResource);
                propertyGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(_fileResource);
                tabs.Show();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        } catch (Exception) {
            // TODO: show errors
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
