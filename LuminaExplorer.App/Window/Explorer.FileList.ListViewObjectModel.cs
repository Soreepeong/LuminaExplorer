using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using BrightIdeasSoftware;
using JetBrains.Annotations;
using Lumina.Data.Structs;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private class
        ExplorerListViewDataSource : AbstractVirtualListDataSource, IDisposable, IReadOnlyList<VirtualObject> {
        private readonly VirtualSqPackTree _tree;

        private readonly LruCache<VirtualObject, ResultDisposableTask<Bitmap?>> _previews = new(128, true);
        private int _previewSize;
        private CancellationTokenSource _previewCancellationTokenSource = new();

        private VirtualFolder? _currentFolder;
        private Task<VirtualFolder>? _fileNameResolver;
        private List<VirtualObject> _objects = new();
        private CancellationTokenSource _sorterCancel = new();

        public ExplorerListViewDataSource(VirtualObjectListView volv, VirtualSqPackTree tree) : base(volv) {
            _tree = tree;
        }

        public void Dispose() {
            _previewCancellationTokenSource.Cancel();
            _sorterCancel.Cancel();
            _previews.Dispose();
        }

        public int PreviewCacheCapacity {
            get => _previews.Capacity;
            set => _previews.Capacity = value;
        }

        public VirtualFolder? CurrentFolder {
            get => _currentFolder;
            set {
                if (_currentFolder == value)
                    return;

                _currentFolder = value;
                if (_currentFolder is null) {
                    listView.ClearObjects();
                    return;
                }

                _previews.Flush();

                var fileNameResolver = _fileNameResolver = _tree.AsFileNamesResolved(_currentFolder);
                if (!fileNameResolver.IsCompletedSuccessfully)
                    listView.ClearObjects();
                else
                    listView.SelectedIndex = -1;

                fileNameResolver.ContinueWith(_ => {
                    if (_fileNameResolver != fileNameResolver)
                        return;
                    _fileNameResolver = null;

                    listView.SetObjects(_tree.GetFolders(_currentFolder).Select(x => new VirtualObject(_tree, x))
                        .Concat(_currentFolder.Files.Select(x => new VirtualObject(_tree, x))));
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        public int ImageThumbnailSize {
            get => _previewSize;
            set {
                if (_previewSize == value)
                    return;

                _previewSize = value;
                _previewCancellationTokenSource.Cancel();
                _previews.Flush();
                _previewCancellationTokenSource = new();
                listView.OwnerDraw = _previewSize > 0;

                var largeImageListSize = _previewSize == 0 ? 32 : _previewSize;
                listView.LargeImageList!.ImageSize = new(largeImageListSize, largeImageListSize);
                listView.Invalidate();
            }
        }

        public override object GetNthObject(int n) => _objects[n];

        public override int GetObjectCount() => _objects.Count;

        public override int GetObjectIndex(object model) => model is VirtualObject vo ? _objects.IndexOf(vo) : -1;

        public override void PrepareCache(int first, int last) {
            // throw new NotImplementedException();
        }

        public override int SearchText(string value, int first, int last, OLVColumn column)
            => DefaultSearchText(value, first, last, column, this);

        public override void Sort(OLVColumn column, SortOrder order) {
            _sorterCancel.Cancel();
            _sorterCancel = new();
            SortImpl(column, order, _sorterCancel.Token)
                .ContinueWith(result => {
                    if (!result.IsCompletedSuccessfully)
                        return;

                    _objects = result.Result;
                    listView.ClearCachedInfo();
                    listView.Invalidate();
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private Task<List<VirtualObject>> SortImpl(
            OLVColumn column,
            SortOrder order,
            CancellationToken cancellationToken) {
            var orderMultiplier = order == SortOrder.Descending ? -1 : 1;
            return _objects.SortAsync().With(column.AspectName switch {
                nameof(VirtualObject.FullPath) => (a, b) =>
                    string.Compare(a.FullPath, b.FullPath, StringComparison.InvariantCultureIgnoreCase) *
                    orderMultiplier,
                nameof(VirtualObject.Name) => (a, b) =>
                    (a.CompareByFolderOrFile(b) ?? a.CompareByName(b)) * orderMultiplier,
                nameof(VirtualObject.PackTypeString) => (a, b) => orderMultiplier * (
                    a.CompareByFolderOrFile(b) ??
                    (a.IsFolder ? a.CompareByName(b) : a.Lookup.Type.CompareTo(b.Lookup.Type))),
                nameof(VirtualObject.Hash1) => (a, b) => orderMultiplier * (
                    a.CompareByFolderOrFile(b) ??
                    (a.IsFolder ? a.CompareByName(b) : a.Hash1Value.CompareTo(b.Hash1Value))),
                nameof(VirtualObject.Hash2) => (a, b) => orderMultiplier * (
                    a.CompareByFolderOrFile(b) ??
                    (a.IsFolder ? a.CompareByName(b) : a.Hash2Value.CompareTo(b.Hash2Value))),
                nameof(VirtualObject.RawSize) => (a, b) => orderMultiplier * (
                    a.CompareByFolderOrFile(b) ??
                    (a.IsFolder ? a.CompareByName(b) : a.Lookup.Size.CompareTo(b.Lookup.Size))),
                nameof(VirtualObject.StoredSize) => (a, b) => orderMultiplier * (
                    a.CompareByFolderOrFile(b) ??
                    (a.IsFolder
                        ? a.CompareByName(b)
                        : a.Lookup.OccupiedSpaceUnits.CompareTo(b.Lookup.OccupiedSpaceUnits))),
                nameof(VirtualObject.ReservedSize) => (a, b) => orderMultiplier * (
                    a.CompareByFolderOrFile(b) ??
                    (a.IsFolder
                        ? a.CompareByName(b)
                        : a.Lookup.ReservedSpaceUnits.CompareTo(b.Lookup.ReservedSpaceUnits))),
                _ => throw new FailFastException($"Invalid column AspectName {column.AspectName}"),
            }).With(cancellationToken).Sort();
        }

        public override void AddObjects(ICollection modelObjects) => InsertObjects(_objects.Count, modelObjects);

        public override void InsertObjects(int index, ICollection modelObjects) =>
            _objects.InsertRange(index, modelObjects.Cast<VirtualObject>());

        public override void RemoveObjects(ICollection modelObjects) {
            foreach (var o in modelObjects)
                if (o is VirtualObject vo)
                    _objects.Remove(vo);

            if (!_objects.Any())
                _previewCancellationTokenSource.Cancel();
        }

        public override void SetObjects(IEnumerable collection) {
            _objects.Clear();
            _previewCancellationTokenSource.Cancel();

            _objects.AddRange(collection.Cast<VirtualObject>());
            if (!_objects.Any())
                return;

            _previewCancellationTokenSource = new();
        }

        public override void UpdateObject(int index, object modelObject) =>
            _objects[index] = (VirtualObject) modelObject;

        public bool TryGetThumbnail(VirtualObject virtualObject, [MaybeNullWhen(false)] out Bitmap bitmap) {
            if (!_previews.TryGet(virtualObject, out var task)) {
                _previews.Add(virtualObject, task = new(Task.Run(async () => {
                    var file = virtualObject.File;
                    VirtualFileLookup lookup;
                    try {
                        lookup = virtualObject.Lookup;
                    } catch (Exception e) {
                        Debug.WriteLine(e);
                        return null;
                    }

                    var canBeTexture = false;
                    canBeTexture |= lookup.Type == FileType.Texture;
                    canBeTexture |= file.Name.EndsWith(".atex", StringComparison.InvariantCultureIgnoreCase);
                    // may be an .atex file
                    canBeTexture |= !file.NameResolved && lookup is {Type: FileType.Standard, Size: > 256};

                    if (!canBeTexture)
                        return null;

                    try {
                        await using var stream = lookup.CreateStream();
                        return await QueuedThumbnailer.Instance.LoadFromTexStream(
                            ImageThumbnailSize,
                            ImageThumbnailSize,
                            stream,
                            _tree.PlatformId,
                            _previewCancellationTokenSource.Token).ConfigureAwait(false);
                    } catch (Exception e) {
                        Debug.WriteLine(e);
                    }

                    return null;
                })));

                task.Task.ContinueWith(res => {
                    if (res.IsCompletedSuccessfully)
                        listView.RefreshObject(virtualObject);
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }

            if ((task.Task.IsCompletedSuccessfully ? task.Task.Result : null) is { } b) {
                bitmap = b;
                return true;
            }

            bitmap = null!;
            return false;
        }

        public VirtualObject this[int n] => _objects[n];

        public IEnumerator<VirtualObject> GetEnumerator() => _objects.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _objects.GetEnumerator();

        public int Count => _objects.Count;
    }

    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Local
    private sealed class VirtualObject : IDisposable, INotifyPropertyChanged {
        private readonly VirtualFile? _file;
        private readonly VirtualFolder? _folder;
        private readonly Lazy<uint> _hash2;

        private string _name;
        private Lazy<VirtualFileLookup>? _lookup;
        private Lazy<string> _fullPath;

        public VirtualObject(VirtualSqPackTree tree, VirtualFile file) {
            _file = file;
            _name = file.Name;
            _fullPath = new(() => tree.GetFullPath(file));
            _lookup = new(() => tree.GetLookup(File));
            _hash2 = new(() => tree.GetFullPathHash(File));
        }

        public VirtualObject(VirtualSqPackTree tree, VirtualFolder folder) {
            _folder = folder;
            _name = folder.Name.Trim('/');
            _fullPath = new(() => tree.GetFullPath(folder));
            _hash2 = new(0u);
        }

        private void ReleaseUnmanagedResources() {
            if (_lookup is {IsValueCreated: true}) {
                _lookup.Value.Dispose();
                _lookup = null;
            }
        }

        public void Dispose() {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~VirtualObject() {
            ReleaseUnmanagedResources();
        }

        public bool IsFolder => _lookup is null;

        public VirtualFile File => _file ?? throw new InvalidOperationException();

        public VirtualFolder Folder => !IsFolder || _folder is null ? throw new InvalidOperationException() : _folder;

        public VirtualFileLookup Lookup => _lookup?.Value ?? throw new InvalidOperationException();

        public uint Hash1Value => IsFolder ? Folder.FolderHash : File.FileHash;

        public uint Hash2Value => _hash2.Value;

        [UsedImplicitly] public bool Checked { get; set; }

        public string Name {
            get => _name;
            set => SetField(ref _name, value);
        }

        public string PackTypeString => _lookup is null
            ? ""
            : _lookup.Value.Type is var x
                ? x switch {
                    FileType.Empty => "Placeholder",
                    FileType.Standard => "Standard",
                    FileType.Model => "Model",
                    FileType.Texture => "Texture",
                    _ => $"{x}",
                }
                : "<error>";

        public string Hash1 => $"{Hash1Value:X08}";

        public string Hash2 => IsFolder ? "" : $"{Hash2Value:X08}";

        public string RawSize => IsFolder ? "" : UiUtils.FormatSize(Lookup.Size);

        public string StoredSize => IsFolder ? "" : UiUtils.FormatSize(Lookup.OccupiedBytes);

        public string ReservedSize => IsFolder ? "" : UiUtils.FormatSize(Lookup.ReservedBytes);

        public string FullPath {
            get => _fullPath.Value;
            set => SetField(ref _fullPath, new(value));
        }

        public int CompareByName(VirtualObject other) =>
            !IsFolder && !other.IsFolder && File.NameResolved != other.File.NameResolved
                ? File.NameResolved ? -1 : 1
                : string.Compare(_name, other._name, StringComparison.InvariantCultureIgnoreCase);

        public int? CompareByFolderOrFile(VirtualObject other) => IsFolder == other.IsFolder ? null : IsFolder ? -1 : 1;

        #region Implementation of INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
            PropertyChanged?.Invoke(this, new(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
