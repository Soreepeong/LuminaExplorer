using System;
using System.ComponentModel;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BrightIdeasSoftware;
using JetBrains.Annotations;
using Lumina.Data.Files;
using Lumina.Data.Structs;
using Lumina.Models.Materials;
using Lumina.Models.Models;
using LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.Penumbra;
using LuminaExplorer.Core.ExtraFormats.GenericAnimation;
using LuminaExplorer.Core.ExtraFormats.GltfInterop;
using LuminaExplorer.Core.ObjectRepresentationWrapper;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.VirtualFileSystem;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.App.Window.FileViewers;

public partial class ModelViewer : Form {
    private const int MinimumDefaultWidth = 1280;
    private const int MinimumDefaultHeight = 720;

    private static readonly Guid ModelViewerSaveToGuid = Guid.Parse("afab3c8d-6d9f-4813-afaf-a49536297d7e");

    private readonly CancellationTokenSource _closeToken = new();

    private bool _isFullScreen;
    private FormBorderStyle _nonFullScreenBorderStyle;
    private FormWindowState _nonFullScreenWindowState;
    private Size _nonFullScreenSize;
    private bool _nonFullScreenControlBox;

    private CancellationTokenSource? _loadCancelTokenSource;

    private readonly AnimationListDataSource _source;

    public ModelViewer()
    {
        this.InitializeComponent();

        this.MainLeftSplitter.Panel1Collapsed = true;
        this.MainRightSplitter.Panel2Collapsed = true;

        this.PropertyPanelGrid.PreviewKeyDown += this.PropertyPanelGridOnPreviewKeyDown;

        this.Viewer.MouseActivity.MiddleClick += this.MouseActivityOnMiddleClick;
        this.Viewer.PreviewKeyDown += this.ViewerOnPreviewKeyDown;
        this.Viewer.MouseDown += this.ViewerOnMouseDown;
        this.Viewer.AnimationSpeedChanged += this.ViewerOnAnimationSpeedChanged;
        this.Viewer.AnimationPlayingChanged += this.ViewerOnAnimationPlayingChanged;

        this.AnimationListView.SelectedIndexChanged += this.AnimationListViewOnSelectedIndexChanged;
        this.AnimationListView.VirtualListDataSource = this._source = new(this.AnimationListView);
        this.AnimationListView.Sorting = SortOrder.Ascending;
        this.AnimationListView.PrimarySortColumn = this.AnimationListViewColumnFileName;

        this.AnimationEnabledCheckbox.CheckedChanged += this.AnimationEnabledCheckboxOnCheckedChanged;
        this.AnimationSpeedTrackBar.ValueChanged += this.AnimationSpeedTrackBarOnValueChanged;

        this.RendererComboBox.SelectedIndex = (int) this.Viewer.RendererType;
        this.RendererComboBox.SelectedIndexChanged += this.RendererComboBoxOnSelectedIndexChanged;
        this.Viewer.RendererTypeChanged += this.ViewerOnRendererTypeChanged;
    }

    /// <summary>Gets or sets the renderer used to show the model.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModelRendererType RendererType {
        get => this.Viewer.RendererType;
        set => this.Viewer.RendererType = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsFullScreen {
        get => this._isFullScreen;
        set {
            if (value == this._isFullScreen)
                return;

            using var redrawLock = new ControlExtensions.ScopedDisableRedraw(this);

            if (!value) {
                this.ControlBox = this._nonFullScreenControlBox;
                this.FormBorderStyle = this._nonFullScreenBorderStyle;
                this.WindowState = this._nonFullScreenWindowState;
                this.Size = this._nonFullScreenSize;
            } else {
                this._nonFullScreenSize = this.Size;

                this._nonFullScreenControlBox = this.ControlBox;
                this.ControlBox = false;

                this._nonFullScreenBorderStyle = this.FormBorderStyle;
                this.FormBorderStyle = FormBorderStyle.None;

                this._nonFullScreenWindowState = this.WindowState;
                // Setting to normal then maximized is required to enter fullscreen, covering Windows task bar.
                this.WindowState = FormWindowState.Normal;
                this.WindowState = FormWindowState.Maximized;
            }

            this._isFullScreen = value;
        }
    }

    private void AnimationEnabledCheckboxOnCheckedChanged(object? sender, EventArgs e) =>
        this.Viewer.AnimationPlaying = this.AnimationEnabledCheckbox.Checked;

    private void AnimationSpeedTrackBarOnValueChanged(object? sender, EventArgs e) =>
        this.Viewer.AnimationSpeed = this.AnimationSpeedTrackBar.Value / 100f;

    private void AnimationListViewOnSelectedIndexChanged(object? sender, EventArgs e) =>
        this.Viewer.Animations = this.AnimationListView.SelectedObjects.Cast<AnimationListEntry>()
            .Select(x => x.AnimationTask)
            .ToArray();

    private void RendererComboBoxOnSelectedIndexChanged(object? sender, EventArgs e) =>
        this.Viewer.RendererType = (ModelRendererType) Math.Max(0, this.RendererComboBox.SelectedIndex);

    private void ViewerOnRendererTypeChanged(object? sender, EventArgs e) =>
        this.RendererComboBox.SelectedIndex = (int) this.Viewer.RendererType;

    private void MouseActivityOnMiddleClick(Point cursor) => this.IsFullScreen = !this.IsFullScreen;

    private void ViewerOnAnimationPlayingChanged(object? sender, EventArgs e) =>
        this.AnimationEnabledCheckbox.Checked = this.Viewer.AnimationPlaying;

    private void ViewerOnAnimationSpeedChanged(object? sender, EventArgs e)
    {
        this.AnimationSpeedTrackBar.Value = (int) Math.Round(this.Viewer.AnimationSpeed * 100);
        this.AnimationSpeedLabel.Text = $"Animation Speed: {this.Viewer.AnimationSpeed:0.00}x";
    }

    private void ViewerOnMouseDown(object? sender, MouseEventArgs e)
    {
        this.Viewer.Focus();
    }

    private void ViewerOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        switch (e.KeyCode) {
            case Keys.PageUp:
            case Keys.PageDown:
                if (!this._source.Any())
                    break;
                var direction = e.KeyCode == Keys.PageDown ? 1 : -1;
                int index;
                if (this.AnimationListView.SelectedIndices.Count == 0)
                    index = 0;
                else {
                    index = this.AnimationListView.SelectedIndices[0];
                    this.AnimationListView.Items[index].Selected = false;
                    index = Math.Clamp(index + direction, 0, this._source.Count - 1);
                }

                this.AnimationListView.Items[index].Selected = true;
                break;
            case Keys.Space:
                this.AnimationEnabledCheckbox.Checked = !this.AnimationEnabledCheckbox.Checked;
                break;
            case Keys.Tab:
                this.MainLeftSplitter.Panel1Collapsed = !this.MainLeftSplitter.Panel1Collapsed;
                this.MainRightSplitter.Panel2Collapsed = !this.MainRightSplitter.Panel2Collapsed;
                e.IsInputKey = true;
                break;
            case Keys.Enter:
                this.IsFullScreen = !this.IsFullScreen;
                break;
            case Keys.Escape:
                if (this.IsFullScreen)
                    this.IsFullScreen = false;
                else if (!this.MainLeftSplitter.Panel1Collapsed) {
                    this.MainLeftSplitter.Panel1Collapsed = true;
                    this.MainRightSplitter.Panel2Collapsed = true;
                } else
                    this.Close();

                break;
        }
    }

    private void PropertyPanelGridOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
    {
        switch (e.KeyCode) {
            case Keys.Tab:
                this.MainLeftSplitter.Panel1Collapsed = !this.MainLeftSplitter.Panel1Collapsed;
                this.MainRightSplitter.Panel2Collapsed = !this.MainRightSplitter.Panel2Collapsed;
                break;
        }
    }

    public void SetFile(IVirtualFileSystem vfs, IVirtualFolder root, IVirtualFile file, MdlFile? mdlFile)
    {
        this.Text = vfs.GetFullPath(file);

        this._loadCancelTokenSource?.Cancel();
        this._loadCancelTokenSource = null;

        Task<MdlFile> task;
        var cts = this._loadCancelTokenSource = new();
        if (mdlFile is not null) {
            task = Task.FromResult(mdlFile);
        } else {
            task = Task.Factory.StartNew(
                async () => {
                    using var lookup = vfs.GetLookup(file);
                    return await lookup.AsFileResource<MdlFile>(cts.Token);
                },
                cts.Token).Unwrap();
        }

        this.Viewer.SetModel(vfs, root, task);
        task.ContinueWith(
            r => {
                if (!r.IsCompletedSuccessfully)
                    return;
                this.PropertyPanelGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(r.Result);
            },
            this._loadCancelTokenSource.Token,
            TaskContinuationOptions.None,
            TaskScheduler.FromCurrentSynchronizationContext());

        this._source.SetObjects(Array.Empty<AnimationListEntry>());
        this.AnimationListView.Items.Clear();
        Task.WhenAll(this.Viewer.ModelInfoResolverTask!, task).ContinueWith(
            async r => {
                if (!r.IsCompletedSuccessfully)
                    return;

                mdlFile = task.Result;

                if (this.Viewer.ModelInfoResolverTask!.Result.FindSklbPath(mdlFile!.FilePath.Path).FirstOrDefault() is
                    not { } sklbPath)
                    return;

                var sklbFile = await vfs.LocateFile(root, sklbPath);
                if (sklbFile is null)
                    return;

                SklbFile sklb;
                using (var lookup = vfs.GetLookup(sklbFile))
                    sklb = await lookup.AsFileResource<SklbFile>(cts.Token);

                if (file.Parent.Parent?.Parent?.Parent?.Parent is { } modelBaseFolder &&
                    await vfs.LocateFolder(modelBaseFolder, "animation/") is { } animationFolder) {
                    var listLock = new object();
                    var paps = new List<PapFile>();

                    var sw = new Stopwatch();
                    sw.Start();

                    void Flush(bool force)
                    {
                        var entries = new List<AnimationListEntry>();
                        lock (listLock) {
                            if (sw.ElapsedMilliseconds < 500 && !force)
                                return;

                            entries.AddRange(
                                paps.Where(
                                        p =>
                                            p.LoadException is null &&
                                            p.Header.ModelClassification == sklb.VersionedHeader.ModelClassification &&
                                            p.Header.ModelId == sklb.VersionedHeader.ModelId)
                                    .SelectMany(x => x.Animations.Select((_, i) => new AnimationListEntry(x, i))));
                            paps.Clear();
                        }

                        sw.Reset();

                        this.Viewer.RunOnUiThread(
                            () => {
                                this.AnimationListView.AddObjects(entries);

                                sw.Start();
                            });
                    }

                    void OnFileFound(IVirtualFile papFile)
                    {
                        using var lookup = vfs.GetLookup(papFile);
                        lookup
                            .AsFileResource<PapFile>(cts.Token)
                            .ContinueWith(
                                r2 => {
                                    if (r2.Result.LoadException is not null)
                                        return;
                                    lock (listLock)
                                        paps.Add(r2.Result);
                                    Flush(false);
                                },
                                cts.Token);
                    }

                    await Task.WhenAll(
                        vfs.Search(animationFolder, "*.pap", null, null, OnFileFound, cancellationToken: cts.Token),
                        this.Viewer.ModelInfoResolverTask!.ContinueWith(
                            async ra => {
                                if (!ra.IsCompletedSuccessfully)
                                    return;

                                var charaFolder = await vfs.LocateFolder(root, "chara/");
                                if (charaFolder is null)
                                    return;

                                foreach (var f in vfs.GetFolders(await vfs.AsFoldersResolved(charaFolder))) {
                                    if (f is not SqpackFolder { IsUnknownContainer: true } unknownFolder)
                                        continue;

                                    // brute force paps
                                    foreach (var f2 in vfs.GetFolders(await vfs.AsFoldersResolved(unknownFolder))) {
                                        foreach (var f3 in vfs.GetFiles(f2)) {
                                            cts.Token.ThrowIfCancellationRequested();
                                            try {
                                                using var lookup = vfs.GetLookup(f3);
                                                if (!HasPapMagic(lookup))
                                                    continue;

                                                var papTask = lookup.AsFileResource<PapFile>(cts.Token);
                                                await this.Viewer.RunOnUiThreadAfter(
                                                    papTask,
                                                    r2 => {
                                                        if (!r2.IsCompletedSuccessfully ||
                                                            r2.Result.LoadException is not null)
                                                            return;
                                                        lock (listLock)
                                                            paps.Add(r2.Result);
                                                        Flush(false);
                                                    });
                                            } catch (Exception e) when (e is not OperationCanceledException) {
                                                // pass
                                            }
                                        }
                                    }
                                }
                            },
                            cts.Token));

                    Flush(true);
                }
            },
            cts.Token);
    }

    /// <summary>Checks whether the file starts with the magic of a pap file, without parsing it.</summary>
    private static bool HasPapMagic(IVirtualFileLookup lookup)
    {
        if (lookup.Type != FileType.Standard || lookup.Size < 4)
            return false;

        try {
            using var stream = lookup.CreateStream();
            Span<byte> magic = stackalloc byte[4];
            stream.ReadExactly(magic);
            return BitConverter.ToUInt32(magic) == PapFile.PapHeader.MagicValue;
        } catch (Exception) {
            return false;
        }
    }

    public void ShowRelativeTo(Control opener)
    {
        var rc = this.Viewer.GetViewportRectangleSuggestion(opener);
        var minimumSize = this.LogicalToDeviceUnits(new Size(MinimumDefaultWidth, MinimumDefaultHeight));
        if (rc.Width < minimumSize.Width) {
            rc.X -= (minimumSize.Width - rc.Width) / 2;
            rc.Width = minimumSize.Width;
        }

        if (rc.Height < minimumSize.Height) {
            rc.Y -= (minimumSize.Height - rc.Height) / 2;
            rc.Height = minimumSize.Height;
        }

        rc = Rectangle.Inflate(
            rc,
            (this.MainLeftSplitter.Panel1.Width + this.MainRightSplitter.Panel2.Width) / 2,
            0);

        this.SetBounds(rc.X, rc.Y, rc.Width, rc.Height);
        this.Show();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData) {
            case Keys.S | Keys.Control: {
                if (this.Viewer.ModelTask?.IsCompletedSuccessfully is not true)
                    return true;

                var model = PenumbraMdlFile.CreateModel(this.Viewer.ModelTask.Result);
                Debug.Assert(model.File is not null);

                using var sfd = new SaveFileDialog();
                sfd.OverwritePrompt = true;
                sfd.ClientGuid = ModelViewerSaveToGuid;
                sfd.Title = $"Save {Path.GetFileNameWithoutExtension(model.File.FilePath)}";
                sfd.AddExtension = true;
                sfd.FileName = $"{Path.GetFileNameWithoutExtension(model.File.FilePath)}.glb";
                sfd.OverwritePrompt = true;
                sfd.Filter = ".glb file|*.glb";
                if (sfd.ShowDialog() == DialogResult.OK) {
                    var fileName = sfd.FileName;
                    Task.Factory.StartNew(
                        async () => {
                            try {
                                var tuple = new GltfTuple();
                                int? skinIndexNullable = this.Viewer.SkeletonTask is { } skeletonTask
                                    ? tuple.AttachSkin(await skeletonTask)
                                    : null;

                                for (var i = 0; i < model.Materials.Length; i++) {
                                    var mtrlPath = model.File.Strings
                                        .AsSpan((int) model.File.MaterialNameOffsets[i])
                                        .ExtractCString();

                                    if (mtrlPath.StartsWith('/'))
                                        mtrlPath = Material.ResolveRelativeMaterialPath(
                                            mtrlPath,
                                            model.VariantId,
                                            strictSuffixValidation: false);

                                    if (mtrlPath is null)
                                        continue;

                                    var mtrlvf = await this.Viewer.Vfs!.LocateFile(this.Viewer.VfsRoot!, mtrlPath);
                                    if (mtrlvf is null)
                                        continue;

                                    using var lookup = this.Viewer.Vfs!.GetLookup(mtrlvf);
                                    model.Materials[i] = PenumbraMtrlFile.CreateMaterial(
                                        await lookup.AsFileResource<MtrlFile>());

                                    typeof(Material).GetProperty(nameof(Material.MaterialPath))!.SetValue(
                                        model.Materials[i],
                                        mtrlPath.Trim('/'));
                                    typeof(Material).GetProperty(nameof(Material.ResolvedPath))!.SetValue(
                                        model.Materials[i],
                                        mtrlPath.Trim('/'));
                                    typeof(Material).GetProperty(nameof(Material.Parent))!.SetValue(
                                        model.Materials[i],
                                        model);
                                }

                                foreach (var m in model.Materials) {
                                    await tuple.AttachMaterial(
                                        m,
                                        async texPath => {
                                            var mtrlvf = await this.Viewer.Vfs!.LocateFile(
                                                this.Viewer.VfsRoot!,
                                                texPath);
                                            if (mtrlvf is null)
                                                return null;

                                            using var lookup = this.Viewer.Vfs!.GetLookup(mtrlvf);
                                            return await lookup.AsFileResource<TexFile>();
                                        });
                                }

                                tuple.AddToScene(tuple.AttachMesh(model, skinIndexNullable), skinIndexNullable);

                                if (skinIndexNullable is { } skinIndex) {
                                    foreach (var anim in this._source)
                                        tuple.AttachAnimation(
                                            $"{anim.FullPath}:{anim.AnimationName}",
                                            await anim.AnimationTask,
                                            skinIndex);
                                }

                                await using var f = File.Open(fileName, FileMode.Create, FileAccess.Write);
                                tuple.Compile(f);
                            } catch (Exception e) {
                                MessageBox.Show(
                                    $"Failed to save.\n\n{e}",
                                    "Error",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                            }
                        });
                }

                return true;
            }
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this._closeToken.Cancel();
        this._loadCancelTokenSource?.Cancel();
        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (this.WindowState == FormWindowState.Normal && this.IsFullScreen) this.IsFullScreen = false;
    }

    private class AnimationListEntry {
        public readonly PapFile PapFile;
        public readonly Task<IAnimation> AnimationTask;

        public AnimationListEntry(PapFile papFile, int animationIndex)
        {
            this.PapFile = papFile;
            this.AnimationTask = Task.FromResult((IAnimation) papFile.AnimationBindings[animationIndex]);
            this.FileName = Path.GetFileName(this.PapFile.FilePath.Path);
            this.AnimationName = papFile.Animations[animationIndex].Name;
            this.AnimationIndex = animationIndex;
        }

        [UsedImplicitly] public string FileName { get; }
        [UsedImplicitly] public int AnimationIndex { get; }
        [UsedImplicitly] public string AnimationName { get; }
        [UsedImplicitly] public string FullPath => this.PapFile.FilePath.Path;
    }

    private class AnimationListDataSource : AbstractVirtualListDataSource, IReadOnlyList<AnimationListEntry> {
        private readonly List<AnimationListEntry> _objects = new();

        public AnimationListDataSource(VirtualObjectListView listView) : base(listView)
        { }

        public override object GetNthObject(int n) => this._objects[n];

        public override int GetObjectCount() => this._objects.Count;

        public override int GetObjectIndex(object model) =>
            model is AnimationListEntry e ? this._objects.IndexOf(e) : -1;

        public override int SearchText(string value, int first, int last, OLVColumn column)
            => DefaultSearchText(value, first, last, column, this);

        public override void Sort(OLVColumn column, SortOrder order)
        {
            var orderMultiplier = order == SortOrder.Descending ? -1 : 1;
            switch (column.AspectName) {
                case nameof(AnimationListEntry.FileName):
                    this._objects.Sort(
                        (x, y) => {
                            var a = MiscUtils.CompareNatural(x.FileName, y.FileName);
                            if (a != 0)
                                return a * orderMultiplier;
                            return x.AnimationIndex.CompareTo(y.AnimationIndex) * orderMultiplier;
                        });
                    break;
                case nameof(AnimationListEntry.AnimationIndex):
                    this._objects.Sort((x, y) => x.AnimationIndex.CompareTo(y.AnimationIndex) * orderMultiplier);
                    break;
                case nameof(AnimationListEntry.FullPath):
                    this._objects.Sort(
                        (x, y) => {
                            var a = MiscUtils.CompareNatural(x.FullPath, y.FullPath);
                            if (a != 0)
                                return a * orderMultiplier;
                            return x.AnimationIndex.CompareTo(y.AnimationIndex) * orderMultiplier;
                        });
                    break;
            }
        }

        public override void AddObjects(ICollection modelObjects) =>
            this._objects.AddRange(modelObjects.Cast<AnimationListEntry>());

        public override void InsertObjects(int index, ICollection modelObjects) =>
            this._objects.InsertRange(index, modelObjects.Cast<AnimationListEntry>());

        public override void RemoveObjects(ICollection modelObjects)
        {
            foreach (var o in modelObjects) {
                if (o is AnimationListEntry vo) {
                    var i = this._objects.IndexOf(vo);
                    if (i != -1) this._objects.RemoveAt(i);
                }
            }
        }

        public override void SetObjects(IEnumerable collection)
        {
            this._objects.Clear();
            this._objects.AddRange(collection.Cast<AnimationListEntry>());
        }

        public override void UpdateObject(int index, object modelObject) =>
            this._objects[index] = (AnimationListEntry) modelObject;

        public IEnumerator<AnimationListEntry> GetEnumerator() => this._objects.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this._objects.GetEnumerator();

        public int Count => this._objects.Count;

        public AnimationListEntry this[int index] => this._objects[index];
    }
}
