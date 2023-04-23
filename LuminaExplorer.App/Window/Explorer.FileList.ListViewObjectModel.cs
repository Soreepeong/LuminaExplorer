using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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

        private VirtualObjectImageLoader _previewCache;
        private int _previewSize;

        private VirtualFolder? _currentFolder;
        private Task<VirtualFolder>? _fileNameResolver;
        private List<VirtualObject> _objects = new();
        private CancellationTokenSource _sorterCancel = new();
        private Task _sortTask = Task.CompletedTask;

        public ExplorerListViewDataSource(VirtualObjectListView volv, VirtualSqPackTree tree, int numPreviewerThreads)
            : base(volv) {
            _tree = tree;
            _previewCache = new(numPreviewerThreads);
            _previewCache.ImageLoaded += PreviewImageLoaded;
        }

        public void Dispose() {
            _sorterCancel.Cancel();
            _previewCache.Dispose();
            _objects.AsParallel().ForAll(x => x.Dispose());
            _objects.Clear();
        }

        public int SortThreads { get; set; }

        public int PreviewThreads {
            get => _previewCache.Threads;
            set {
                if (_previewCache.Threads == value)
                    return;

                var newPreviewCache = new VirtualObjectImageLoader(value) {
                    Capacity = _previewCache.Capacity,
                    CropThresholdAspectRatioRatio = _previewCache.CropThresholdAspectRatioRatio,
                    InterpolationMode = _previewCache.InterpolationMode,
                };
                _previewCache.Dispose();
                _previewCache = newPreviewCache;
            }
        }

        public int PreviewCacheCapacity {
            get => _previewCache.Capacity;
            set => _previewCache.Capacity = value;
        }

        public float PreviewCropThresholdAspectRatioRatio {
            get => _previewCache.CropThresholdAspectRatioRatio;
            set => _previewCache.CropThresholdAspectRatioRatio = value;
        }

        public IReadOnlyList<VirtualObject> ObjectList => _objects;

        public InterpolationMode PreviewInterpolationMode {
            get => _previewCache.InterpolationMode;
            set => _previewCache.InterpolationMode = value;
        }

        public VirtualFolder? CurrentFolder {
            get => _currentFolder;
            set {
                if (_currentFolder == value)
                    return;

                _currentFolder = value;
                if (_currentFolder is null) {
                    listView.SetObjects(Array.Empty<object>());
                    return;
                }

                var fileNameResolver = _fileNameResolver = _tree.AsFileNamesResolved(_currentFolder);
                if (!fileNameResolver.IsCompletedSuccessfully)
                    listView.SetObjects(Array.Empty<object>());
                else
                    listView.SelectedIndex = -1;

                fileNameResolver
                    .ContinueWith(_ => {
                            if (_fileNameResolver != fileNameResolver)
                                return;
                            _fileNameResolver = null;

                            listView.SetObjects(_tree.GetFolders(_currentFolder)
                                .Select(x => new VirtualObject(_tree, x))
                                .Concat(_currentFolder.Files.Select(x => new VirtualObject(_tree, x))));
                        }, default,
                        TaskContinuationOptions.DenyChildAttach,
                        TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        public int ImageThumbnailSize {
            get => _previewSize;
            set {
                if (_previewSize == value)
                    return;

                _previewCache.Width = _previewCache.Height = _previewSize = value;

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

            var orderMultiplier = order == SortOrder.Descending ? -1 : 1;
            _sortTask = _sortTask.ContinueWith(
                _ => _objects.SortIntoNewListAsync()
                    .With(column.AspectName switch {
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
                    })
                    .WithTaskScheduler(TaskScheduler.Default)
                    .WithThreads(SortThreads)
                    .WithCancellationToken(_sorterCancel.Token)
                    .WithProgrssCallback(progress => Debug.Print("Sort progress: {0:0.00}%", 100 * progress))
                    .WithOrderMap()
                    .Sort()
                    .ContinueWith(result => {
                            if (!result.IsCompletedSuccessfully)
                                return;

                            var newSelectedIndices = listView.SelectedIndices
                                .Cast<int>()
                                .Select(x => result.Result.ReverseOrderMap![x])
                                .ToArray();
                            var focusedObject = listView.FocusedObject;
                            _objects = result.Result.Data;
                            listView.ClearCachedInfo();
                            listView.UpdateVirtualListSize();
                            listView.SelectedIndices.Clear();
                            foreach (var si in newSelectedIndices)
                                listView.SelectedIndices.Add(si);
                            listView.FocusedObject =focusedObject;
                            listView.Invalidate();
                        }, default,
                        TaskContinuationOptions.DenyChildAttach,
                        TaskScheduler.FromCurrentSynchronizationContext()), default,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        public override void AddObjects(ICollection modelObjects) => InsertObjects(_objects.Count, modelObjects);

        public override void InsertObjects(int index, ICollection modelObjects) {
            _sorterCancel.Cancel();
            _sortTask.Wait();
            _objects.InsertRange(index, modelObjects.Cast<VirtualObject>());
        }

        public override void RemoveObjects(ICollection modelObjects) {
            foreach (var o in modelObjects) {
                if (o is VirtualObject vo) {
                    var i = _objects.IndexOf(vo);
                    if (i != -1) {
                        _sorterCancel.Cancel();
                        _sortTask.Wait();

                        _objects[i].Dispose();
                        _objects.RemoveAt(i);
                    }
                }
            }
        }

        public override void SetObjects(IEnumerable collection) {
            _sorterCancel.Cancel();
            _sortTask.Wait();

            foreach (var o in _objects)
                o.Dispose();

            _objects.Clear();
            _objects.AddRange(collection.Cast<VirtualObject>());
        }

        public override void UpdateObject(int index, object modelObject) {
            if (_objects[index] == modelObject)
                return;

            _sorterCancel.Cancel();
            _sortTask.Wait();

            _objects[index].Dispose();
            _objects[index] = (VirtualObject) modelObject;
        }

        public VirtualObject this[int n] => _objects[n];

        public IEnumerator<VirtualObject> GetEnumerator() => _objects.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _objects.GetEnumerator();

        public int Count => _objects.Count;

        public bool TryGetThumbnail(VirtualObject virtualObject, [MaybeNullWhen(false)] out Bitmap bitmap) =>
            _previewCache.TryGetBitmap(virtualObject, out bitmap);

        private void PreviewImageLoaded(VirtualObject arg1, VirtualFile arg2, Bitmap arg3) =>
            listView.BeginInvoke(() => listView.RefreshObject(arg1));
    }

    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Local
    private sealed class VirtualObject : IDisposable, INotifyPropertyChanged {
        private readonly VirtualFile? _file;
        private readonly VirtualFolder? _folder;
        private readonly Lazy<uint> _hash2;

        private string _name;
        private Lazy<VirtualFileLookup>? _lookup;
        private Lazy<string> _fullPath;

        public readonly PlatformId PlatformId;

        public VirtualObject(VirtualSqPackTree tree, VirtualFile file) {
            PlatformId = tree.PlatformId;
            _file = file;
            _name = file.Name;
            _fullPath = new(() => tree.GetFullPath(file));
            _lookup = new(() => tree.GetLookup(File));
            _hash2 = new(() => tree.GetFullPathHash(File));
        }

        public VirtualObject(VirtualSqPackTree tree, VirtualFolder folder) {
            PlatformId = tree.PlatformId;
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

        public bool TryGetLookup([MaybeNullWhen(false)] out VirtualFileLookup lookup) {
            lookup = _lookup?.Value;
            return lookup is not null;
        }

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
