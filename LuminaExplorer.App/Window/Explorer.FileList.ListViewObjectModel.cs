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
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private class
        ExplorerListViewDataSource : AbstractVirtualListDataSource, IDisposable, IReadOnlyList<VirtualObject> {
        private readonly IVirtualFileSystem _vfs;

        private VirtualObjectImageLoader _previewCache;
        private int _previewSize;

        private IVirtualFolder? _currentFolder;
        private Task<IVirtualFolder>? _fileNameResolver;
        private List<VirtualObject> _objects = new();
        private CancellationTokenSource _sorterCancel = new();
        private Task _sortTask = Task.CompletedTask;

        public ExplorerListViewDataSource(VirtualObjectListView volv, IVirtualFileSystem vfs, int numPreviewerThreads)
            : base(volv)
        {
            this._vfs = vfs;
            this._previewCache = new(numPreviewerThreads);
            this._previewCache.ImageLoaded += this.PreviewImageLoaded;
        }

        public void Dispose()
        {
            this._sorterCancel.Cancel();
            this._previewCache.Dispose();
            this._objects.AsParallel().ForAll(x => x.Dispose());
            this._objects.Clear();
        }

        public int SortThreads { get; set; }

        public int PreviewThreads {
            get => this._previewCache.Threads;
            set {
                if (this._previewCache.Threads == value)
                    return;

                var newPreviewCache = new VirtualObjectImageLoader(value) {
                    Capacity = this._previewCache.Capacity,
                    CropThresholdAspectRatioRatio = this._previewCache.CropThresholdAspectRatioRatio,
                    InterpolationMode = this._previewCache.InterpolationMode,
                };
                this._previewCache.Dispose();
                this._previewCache = newPreviewCache;
            }
        }

        public int PreviewCacheCapacity {
            get => this._previewCache.Capacity;
            set => this._previewCache.Capacity = value;
        }

        public float PreviewCropThresholdAspectRatioRatio {
            get => this._previewCache.CropThresholdAspectRatioRatio;
            set => this._previewCache.CropThresholdAspectRatioRatio = value;
        }

        public IReadOnlyList<VirtualObject> ObjectList => this._objects;

        public InterpolationMode PreviewInterpolationMode {
            get => this._previewCache.InterpolationMode;
            set => this._previewCache.InterpolationMode = value;
        }

        public IVirtualFolder? CurrentFolder {
            get => this._currentFolder;
            set {
                if (Equals(this._currentFolder, value))
                    return;

                this._currentFolder = value;
                if (this._currentFolder is null) {
                    this.listView.SetObjects(Array.Empty<object>());
                    return;
                }

                var fileNameResolver = this._fileNameResolver = this._vfs.AsFileNamesResolved(this._currentFolder);
                if (!fileNameResolver.IsCompletedSuccessfully)
                    this.listView.SetObjects(Array.Empty<object>());
                else
                    this.listView.SelectedIndex = -1;

                fileNameResolver
                    .ContinueWith(
                        _ => {
                            if (this._fileNameResolver != fileNameResolver)
                                return;
                            this._fileNameResolver = null;

                            this.listView.SetObjects(
                                this._vfs.GetFolders(this._currentFolder)
                                    .Select(x => new VirtualObject(this._vfs, x))
                                    .Concat(
                                        this._vfs.GetFiles(this._currentFolder)
                                            .Select(x => new VirtualObject(this._vfs, x))));
                        },
                        default,
                        TaskContinuationOptions.DenyChildAttach,
                        TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        public int ImageThumbnailSize {
            get => this._previewSize;
            set {
                if (this._previewSize == value)
                    return;

                this._previewCache.Width = this._previewCache.Height = this._previewSize = value;

                var largeImageListSize = this._previewSize == 0 ? 32 : this._previewSize;
                this.listView.LargeImageList!.ImageSize = new(largeImageListSize, largeImageListSize);
                this.listView.Invalidate();
            }
        }

        public override object GetNthObject(int n) => this._objects[n];

        public override int GetObjectCount() => this._objects.Count;

        public override int GetObjectIndex(object model) => model is VirtualObject vo ? this._objects.IndexOf(vo) : -1;

        public override void PrepareCache(int first, int last)
        {
            // throw new NotImplementedException();
        }

        public override int SearchText(string value, int first, int last, OLVColumn column)
            => DefaultSearchText(value, first, last, column, this);

        public override void Sort(OLVColumn column, SortOrder order)
        {
            this._sorterCancel.Cancel();
            this._sorterCancel = new();

            var orderMultiplier = order == SortOrder.Descending ? -1 : 1;
            this._sortTask = this._sortTask.ContinueWith(
                _ => this._objects.SortIntoNewListAsync()
                    .With(
                        column.AspectName switch {
                            nameof(VirtualObject.FullPath) => (a, b) =>
                                MiscUtils.CompareNatural(a.FullPath, b.FullPath) *
                                orderMultiplier,
                            nameof(VirtualObject.Name) => (a, b) =>
                                (a.CompareByFolderOrFile(b) ?? a.CompareByName(b)) * orderMultiplier,
                            nameof(VirtualObject.PackTypeString) => (a, b) => orderMultiplier * (
                                a.CompareByFolderOrFile(b) ??
                                (a.IsFolder ? a.CompareByName(b) : a.Lookup.Type.CompareTo(b.Lookup.Type))),
                            nameof(VirtualObject.Hash1) => (a, b) => orderMultiplier * (
                                a.CompareByFolderOrFile(b) ??
                                (a.IsFolder
                                    ? a.CompareByName(b)
                                    : MiscUtils.CompareNullable(a.Hash1Value, b.Hash1Value))),
                            nameof(VirtualObject.Hash2) => (a, b) => orderMultiplier * (
                                a.CompareByFolderOrFile(b) ??
                                (a.IsFolder
                                    ? a.CompareByName(b)
                                    : MiscUtils.CompareNullable(a.Hash2Value, b.Hash2Value))),
                            nameof(VirtualObject.RawSize) => (a, b) => orderMultiplier * (
                                a.CompareByFolderOrFile(b) ??
                                (a.IsFolder ? a.CompareByName(b) : a.Lookup.Size.CompareTo(b.Lookup.Size))),
                            nameof(VirtualObject.StoredSize) => (a, b) => orderMultiplier * (
                                a.CompareByFolderOrFile(b) ??
                                (a.IsFolder
                                    ? a.CompareByName(b)
                                    : a.Lookup.OccupiedBytes.CompareTo(b.Lookup.OccupiedBytes))),
                            nameof(VirtualObject.ReservedSize) => (a, b) => orderMultiplier * (
                                a.CompareByFolderOrFile(b) ??
                                (a.IsFolder
                                    ? a.CompareByName(b)
                                    : a.Lookup.ReservedBytes.CompareTo(b.Lookup.ReservedBytes))),
                            _ => throw new FailFastException($"Invalid column AspectName {column.AspectName}"),
                        })
                    .WithTaskScheduler(TaskScheduler.Default)
                    .WithThreads(this.SortThreads)
                    .WithCancellationToken(this._sorterCancel.Token)
                    .WithProgrssCallback(progress => Debug.Print("Sort progress: {0:0.00}%", 100 * progress))
                    .WithOrderMap()
                    .Sort()
                    .ContinueWith(
                        result => {
                            if (!result.IsCompletedSuccessfully)
                                return;

                            var newSelectedIndices = this.listView.SelectedIndices
                                .Cast<int>()
                                .Select(x => result.Result.ReverseOrderMap![x])
                                .ToArray();
                            var focusedObject = this.listView.FocusedObject;
                            this._objects = result.Result.Data;
                            this.listView.ClearCachedInfo();
                            this.listView.UpdateVirtualListSize();
                            this.listView.SelectedIndices.Clear();
                            foreach (var si in newSelectedIndices) this.listView.SelectedIndices.Add(si);
                            this.listView.FocusedObject = focusedObject;
                            this.listView.Invalidate();
                        },
                        default,
                        TaskContinuationOptions.DenyChildAttach,
                        TaskScheduler.FromCurrentSynchronizationContext()),
                default,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        public override void AddObjects(ICollection modelObjects) =>
            this.InsertObjects(this._objects.Count, modelObjects);

        public override void InsertObjects(int index, ICollection modelObjects)
        {
            this._sorterCancel.Cancel();
            this._sortTask.Wait();
            this._objects.InsertRange(index, modelObjects.Cast<VirtualObject>());
        }

        public override void RemoveObjects(ICollection modelObjects)
        {
            foreach (var o in modelObjects) {
                if (o is VirtualObject vo) {
                    var i = this._objects.IndexOf(vo);
                    if (i != -1) {
                        this._sorterCancel.Cancel();
                        this._sortTask.Wait();

                        this._objects[i].Dispose();
                        this._objects.RemoveAt(i);
                    }
                }
            }
        }

        public override void SetObjects(IEnumerable collection)
        {
            this._sorterCancel.Cancel();
            this._sortTask.Wait();

            foreach (var o in this._objects)
                o.Dispose();

            this._objects.Clear();
            this._objects.AddRange(collection.Cast<VirtualObject>());
        }

        public override void UpdateObject(int index, object modelObject)
        {
            if (this._objects[index] == modelObject)
                return;

            this._sorterCancel.Cancel();
            this._sortTask.Wait();

            this._objects[index].Dispose();
            this._objects[index] = (VirtualObject) modelObject;
        }

        public VirtualObject this[int n] => this._objects[n];

        public IEnumerator<VirtualObject> GetEnumerator() => this._objects.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this._objects.GetEnumerator();

        public int Count => this._objects.Count;

        public bool TryGetThumbnail(
            VirtualObject virtualObject,
            [MaybeNullWhen(false)] out Bitmap bitmap,
            out bool isAssociationIcon) =>
            this._previewCache.TryGetBitmap(virtualObject, out bitmap, out isAssociationIcon);

        private void PreviewImageLoaded(VirtualObject arg1, IVirtualFile arg2, Bitmap arg3) =>
            this.listView.BeginInvoke(() => this.listView.RefreshObject(arg1));
    }

    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Local
    private sealed class VirtualObject : IDisposable, INotifyPropertyChanged {
        private readonly IVirtualFile? _file;
        private readonly IVirtualFolder? _folder;
        private readonly Lazy<uint?> _hash2;

        private string _name;
        private Lazy<IVirtualFileLookup>? _lookup;
        private Lazy<string> _fullPath;

        public readonly PlatformId PlatformId;

        public VirtualObject(IVirtualFileSystem tree, IVirtualFile file)
        {
            this.PlatformId = tree is SqpackFileSystem sqfs ? sqfs.PlatformId : PlatformId.Win32;
            this._file = file;
            this._name = file.Name;
            this._fullPath = new(() => tree.GetFullPath(file));
            this._lookup = new(() => tree.GetLookup(this.File));
            this._hash2 = new(() => tree.GetFullPathHash(this.File));
        }

        public VirtualObject(IVirtualFileSystem tree, IVirtualFolder folder)
        {
            this.PlatformId = tree is SqpackFileSystem sqfs ? sqfs.PlatformId : PlatformId.Win32;
            this._folder = folder;
            this._name = folder.Name.Trim('/');
            this._fullPath = new(() => tree.GetFullPath(folder));
            this._hash2 = new((uint?) null);
        }

        private void ReleaseUnmanagedResources()
        {
            if (this._lookup is { IsValueCreated: true }) {
                this._lookup.Value.Dispose();
                this._lookup = null;
            }
        }

        public void Dispose()
        {
            this.ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~VirtualObject()
        {
            this.ReleaseUnmanagedResources();
        }

        public bool IsFolder => this._lookup is null;

        public IVirtualFile File => this._file ?? throw new InvalidOperationException();

        public IVirtualFolder Folder => !this.IsFolder || this._folder is null
            ? throw new InvalidOperationException()
            : this._folder;

        public IVirtualFileLookup Lookup => this._lookup?.Value ?? throw new InvalidOperationException();

        public bool TryGetLookup([MaybeNullWhen(false)] out IVirtualFileLookup lookup)
        {
            lookup = this._lookup?.Value;
            return lookup is not null;
        }

        public uint? Hash1Value => this.IsFolder ? this.Folder.PathHash : this.File.NameHash;

        public uint? Hash2Value => this._hash2.Value;

        [UsedImplicitly] public bool Checked { get; set; }

        public string Name {
            get => this._name;
            set => this.SetField(ref this._name, value);
        }

        public string PackTypeString =>
            this._lookup is null
                ? ""
                : this._lookup.Value.Type is var x
                    ? x switch {
                        FileType.Empty => "Placeholder",
                        FileType.Standard => "Standard",
                        FileType.Model => "Model",
                        FileType.Texture => "Texture",
                        _ => $"{x}",
                    }
                    : "<error>";

        public string Hash1 => this.Hash1Value is null ? "" : $"{this.Hash1Value.Value:X08}";

        public string Hash2 => this.Hash2Value is null ? "" : $"{this.Hash2Value.Value:X08}";

        public string RawSize => this.IsFolder ? "" : UiUtils.FormatSize(this.Lookup.Size);

        public string StoredSize => this.IsFolder ? "" : UiUtils.FormatSize(this.Lookup.OccupiedBytes);

        public string ReservedSize => this.IsFolder ? "" : UiUtils.FormatSize(this.Lookup.ReservedBytes);

        public string FullPath {
            get => this._fullPath.Value;
            set => this.SetField(ref this._fullPath, new(value));
        }

        public int CompareByName(VirtualObject other) =>
            !this.IsFolder && !other.IsFolder && this.File.NameResolved != other.File.NameResolved
                ? this.File.NameResolved ? -1 : 1
                : MiscUtils.CompareNatural(this._name, other._name);

        public int? CompareByFolderOrFile(VirtualObject other) =>
            this.IsFolder == other.IsFolder ? null : this.IsFolder ? -1 : 1;

        #region Implementation of INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            this.OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
