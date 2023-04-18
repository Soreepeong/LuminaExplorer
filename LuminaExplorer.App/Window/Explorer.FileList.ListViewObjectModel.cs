using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BrightIdeasSoftware;
using JetBrains.Annotations;
using Lumina.Data.Structs;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private class ExplorerListViewDataSource : AbstractVirtualListDataSource, IDisposable, IReadOnlyList<VirtualObject> {
        private readonly VirtualSqPackTree _tree;
        
        private readonly LruCache<VirtualObject, ResultDisposableTask<Bitmap?>> _previews = new(128);
        private int _previewSize;
        private CancellationTokenSource _previewCancellationTokenSource = new();

        private VirtualFolder? _currentFolder;
        private Task<VirtualFolder>? _fileNameResolver;
        private readonly List<VirtualObject> _objects = new();

        public ExplorerListViewDataSource(VirtualObjectListView volv, VirtualSqPackTree tree) : base(volv) {
            _tree = tree;
        }

        public void Dispose() {
            _previewCancellationTokenSource.Cancel();
            _previews.Dispose();
        }

        public VirtualFolder? CurrentFolder {
            // get => _currentFolder;
            set {
                if (_currentFolder == value)
                    return;

                _currentFolder = value;
                if (_currentFolder is null) {
                    listView.ClearObjects();
                    return;
                }

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
            _objects.Sort((a, b) => {
                int c;
                if (column.AspectName == nameof(VirtualObject.FullPath)) {
                    c = string.Compare(a.FullPath, b.FullPath, StringComparison.InvariantCultureIgnoreCase);
                } else {
                    c = a.IsFolder switch {
                        true when !b.IsFolder => -1,
                        false when b.IsFolder => 1,
                        // Case when both are folders:
                        true when b.IsFolder => column.AspectName switch {
                            nameof(VirtualObject.Name) =>
                                string.Compare(a.Name, b.Name, StringComparison.InvariantCultureIgnoreCase),
                            nameof(VirtualObject.PackTypeString) => 0,
                            nameof(VirtualObject.Hash1) => a.Hash1Value.CompareTo(b.Hash1Value),
                            nameof(VirtualObject.Hash2) => 0,
                            nameof(VirtualObject.RawSize) => 0,
                            nameof(VirtualObject.StoredSize) => 0,
                            nameof(VirtualObject.ReservedSize) => 0,
                            _ => 0,
                        },
                        // Case when both are files:
                        _ => column.AspectName switch {
                            nameof(VirtualObject.Name) =>
                                string.Compare(a.Name, b.Name, StringComparison.InvariantCultureIgnoreCase),
                            nameof(VirtualObject.PackTypeString) => a.Lookup.Type.CompareTo(b.Lookup.Type),
                            nameof(VirtualObject.Hash1) => a.Hash1Value.CompareTo(b.Hash1Value),
                            nameof(VirtualObject.Hash2) => a.Hash2Value.CompareTo(b.Hash2Value),
                            nameof(VirtualObject.RawSize) => a.Lookup.Size.CompareTo(b.Lookup.Size),
                            nameof(VirtualObject.StoredSize) =>
                                a.Lookup.OccupiedBytes.CompareTo(b.Lookup.OccupiedBytes),
                            nameof(VirtualObject.ReservedSize) => a.Lookup.ReservedBytes.CompareTo(
                                b.Lookup.ReservedBytes),
                            _ => 0,
                        },
                    };
                }

                return order switch {
                    SortOrder.None or SortOrder.Ascending => c,
                    SortOrder.Descending => -c,
                    _ => throw new InvalidOperationException(),
                };
            });
        }

        public override void AddObjects(ICollection modelObjects) => InsertObjects(_objects.Count, modelObjects);

        public override void InsertObjects(int index, ICollection modelObjects) {
            _objects.InsertRange(index, modelObjects.Cast<VirtualObject>());
            foreach (var o in _objects.Skip(index).Take(modelObjects.Count))
                o.GetThumbnail += GetThumbnail;
        }

        public override void RemoveObjects(ICollection modelObjects) {
            foreach (var o in modelObjects)
                if (o is VirtualObject vo)
                    _objects.Remove(vo);
            
            if (!_objects.Any())
                _previewCancellationTokenSource.Cancel();
        }

        public override void SetObjects(IEnumerable collection) {
            foreach (var o in _objects)
                o.GetThumbnail -= GetThumbnail;
            _objects.Clear();
            _previewCancellationTokenSource.Cancel();
            
            _objects.AddRange(collection.Cast<VirtualObject>());
            if (!_objects.Any())
                return;
            
            _previewCancellationTokenSource = new();
            foreach (var o in _objects)
                o.GetThumbnail += GetThumbnail;
        }

        public override void UpdateObject(int index, object modelObject) {
            _objects[index].GetThumbnail -= GetThumbnail;
            _objects[index] = (VirtualObject) modelObject;
            _objects[index].GetThumbnail += GetThumbnail;
        }

        private Bitmap? GetThumbnail(VirtualObject virtualObject) {
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

            return task.Task.IsCompletedSuccessfully ? task.Task.Result : null;
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

        private Lazy<string> _name;
        private Lazy<VirtualFileLookup>? _lookup;
        private Lazy<string> _fullPath;

        public VirtualObject(VirtualSqPackTree tree, VirtualFile file) {
            Image = ImageListIndexFile;
            _file = file;
            _name = new(() => file.Name);
            _fullPath = new(() => tree.GetFullPath(file));
            _lookup = new(() => tree.GetLookup(File));
            _hash2 = new(() => tree.GetFullPathHash(File));
        }

        public VirtualObject(VirtualSqPackTree tree, VirtualFolder folder) {
            Image = ImageListIndexFolder;
            _folder = folder;
            _name = new(folder.Name.Trim('/'));
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

        // GetThumbnail?.Invoke(this, _imageCancellationTokenSource.Token)
        public event GetThumbnailDelegate? GetThumbnail;

        public delegate Bitmap? GetThumbnailDelegate(VirtualObject virtualObject);

        public bool IsFolder => _lookup is null;

        public VirtualFile File => _file ?? throw new InvalidOperationException();

        public VirtualFolder Folder => !IsFolder || _folder is null ? throw new InvalidOperationException() : _folder;

        public VirtualFileLookup Lookup => _lookup?.Value ?? throw new InvalidOperationException();

        public uint Hash1Value => IsFolder ? Folder.FolderHash : File.FileHash;

        public uint Hash2Value => _hash2.Value;

        [UsedImplicitly] public bool Checked { get; set; }

        public string Name {
            get => _name.Value;
            set => SetField(ref _name, new(value));
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

        [UsedImplicitly] public object? Image { get; }
        
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
