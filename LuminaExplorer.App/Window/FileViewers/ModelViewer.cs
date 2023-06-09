﻿using System;
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
using Lumina.Models.Materials;
using Lumina.Models.Models;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors;
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

    public ModelViewer() {
        InitializeComponent();

        MainLeftSplitter.Panel1Collapsed = true;
        MainRightSplitter.Panel2Collapsed = true;

        PropertyPanelGrid.PreviewKeyDown += PropertyPanelGridOnPreviewKeyDown;

        Viewer.MouseActivity.MiddleClick += MouseActivityOnMiddleClick;
        Viewer.PreviewKeyDown += ViewerOnPreviewKeyDown;
        Viewer.MouseDown += ViewerOnMouseDown;
        Viewer.AnimationSpeedChanged += ViewerOnAnimationSpeedChanged;
        Viewer.AnimationPlayingChanged += ViewerOnAnimationPlayingChanged;

        AnimationListView.SelectedIndexChanged += AnimationListViewOnSelectedIndexChanged;
        AnimationListView.VirtualListDataSource = _source = new(AnimationListView);
        AnimationListView.Sorting = SortOrder.Ascending;
        AnimationListView.PrimarySortColumn = AnimationListViewColumnFileName;

        AnimationEnabledCheckbox.CheckedChanged += AnimationEnabledCheckboxOnCheckedChanged;
        AnimationSpeedTrackBar.ValueChanged += AnimationSpeedTrackBarOnValueChanged;
    }

    public bool IsFullScreen {
        get => _isFullScreen;
        set {
            if (value == _isFullScreen)
                return;

            using var redrawLock = new ControlExtensions.ScopedDisableRedraw(this);

            if (!value) {
                ControlBox = _nonFullScreenControlBox;
                FormBorderStyle = _nonFullScreenBorderStyle;
                WindowState = _nonFullScreenWindowState;
                Size = _nonFullScreenSize;
            } else {
                _nonFullScreenSize = Size;

                _nonFullScreenControlBox = ControlBox;
                ControlBox = false;

                _nonFullScreenBorderStyle = FormBorderStyle;
                FormBorderStyle = FormBorderStyle.None;

                _nonFullScreenWindowState = WindowState;
                // Setting to normal then maximized is required to enter fullscreen, covering Windows task bar.
                WindowState = FormWindowState.Normal;
                WindowState = FormWindowState.Maximized;
            }

            _isFullScreen = value;
        }
    }

    private void AnimationEnabledCheckboxOnCheckedChanged(object? sender, EventArgs e) =>
        Viewer.AnimationPlaying = AnimationEnabledCheckbox.Checked;

    private void AnimationSpeedTrackBarOnValueChanged(object? sender, EventArgs e) =>
        Viewer.AnimationSpeed = AnimationSpeedTrackBar.Value / 100f;

    private void AnimationListViewOnSelectedIndexChanged(object? sender, EventArgs e) =>
        Viewer.Animations = AnimationListView.SelectedObjects.Cast<AnimationListEntry>().Select(x => x.AnimationTask)
            .ToArray();

    private void MouseActivityOnMiddleClick(Point cursor) => IsFullScreen = !IsFullScreen;

    private void ViewerOnAnimationPlayingChanged(object? sender, EventArgs e) =>
        AnimationEnabledCheckbox.Checked = Viewer.AnimationPlaying;

    private void ViewerOnAnimationSpeedChanged(object? sender, EventArgs e) {
        AnimationSpeedTrackBar.Value = (int) Math.Round(Viewer.AnimationSpeed * 100);
        AnimationSpeedLabel.Text = $"Animation Speed: {Viewer.AnimationSpeed:0.00}x";
    }

    private void ViewerOnMouseDown(object? sender, MouseEventArgs e) {
        Viewer.Focus();
    }

    private void ViewerOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e) {
        switch (e.KeyCode) {
            case Keys.PageUp:
            case Keys.PageDown:
                if (!_source.Any())
                    break;
                var direction = e.KeyCode == Keys.PageDown ? 1 : -1;
                int index;
                if (AnimationListView.SelectedIndices.Count == 0)
                    index = 0;
                else {
                    index = AnimationListView.SelectedIndices[0];
                    AnimationListView.Items[index].Selected = false;
                    index += direction;
                }

                AnimationListView.Items[index].Selected = true;
                break;
            case Keys.Space:
                AnimationEnabledCheckbox.Checked = !AnimationEnabledCheckbox.Checked;
                break;
            case Keys.Tab:
                MainLeftSplitter.Panel1Collapsed = !MainLeftSplitter.Panel1Collapsed;
                MainRightSplitter.Panel2Collapsed = !MainRightSplitter.Panel2Collapsed;
                e.IsInputKey = true;
                break;
            case Keys.Enter:
                IsFullScreen = !IsFullScreen;
                break;
            case Keys.Escape:
                if (IsFullScreen)
                    IsFullScreen = false;
                else if (!MainLeftSplitter.Panel1Collapsed) {
                    MainLeftSplitter.Panel1Collapsed = false;
                    MainRightSplitter.Panel2Collapsed = false;
                } else
                    Close();

                break;
        }
    }

    private void PropertyPanelGridOnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e) {
        switch (e.KeyCode) {
            case Keys.Tab:
                MainLeftSplitter.Panel1Collapsed = !MainLeftSplitter.Panel1Collapsed;
                MainRightSplitter.Panel2Collapsed = !MainRightSplitter.Panel2Collapsed;
                break;
        }
    }

    public void SetFile(IVirtualFileSystem vfs, IVirtualFolder root, IVirtualFile file, MdlFile? mdlFile) {
        Text = vfs.GetFullPath(file);

        _loadCancelTokenSource?.Cancel();
        _loadCancelTokenSource = null;

        Task<MdlFile> task;
        var cts = _loadCancelTokenSource = new();
        if (mdlFile is not null) {
            task = Task.FromResult(mdlFile);
        } else {
            task = Task.Factory.StartNew(async () => {
                using var lookup = vfs.GetLookup(file);
                return await lookup.AsFileResource<MdlFile>(cts.Token);
            }, cts.Token).Unwrap();
        }

        Viewer.SetModel(vfs, root, task);
        task.ContinueWith(
            r => {
                if (!r.IsCompletedSuccessfully)
                    return;
                PropertyPanelGrid.SelectedObject = new WrapperTypeConverter().ConvertFrom(r.Result);
            },
            _loadCancelTokenSource.Token,
            TaskContinuationOptions.None,
            TaskScheduler.FromCurrentSynchronizationContext());

        _source.SetObjects(Array.Empty<AnimationListEntry>());
        AnimationListView.Items.Clear();
        Task.WhenAll(Viewer.ModelInfoResolverTask!, task).ContinueWith(async r => {
            if (!r.IsCompletedSuccessfully)
                return;

            mdlFile = task.Result;

            if (Viewer.ModelInfoResolverTask!.Result.FindSklbPath(mdlFile!.FilePath.Path).FirstOrDefault() is not {} sklbPath)
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
                void Flush(bool force) {
                    var entries = new List<AnimationListEntry>();
                    lock (listLock) {
                        if (sw.ElapsedMilliseconds < 500 && !force)
                            return;

                        entries.AddRange(paps.Where(p => 
                            p.Header.ModelClassification == sklb.VersionedHeader.ModelClassification &&
                            p.Header.ModelId == sklb.VersionedHeader.ModelId)
                            .SelectMany(x => x.Animations.Select((_, i) => new AnimationListEntry(x, i))));
                        paps.Clear();
                    }
                    
                    sw.Reset();

                    Viewer.RunOnUiThread(() => {
                        AnimationListView.AddObjects(entries);
                        
                        sw.Start();
                    });
                }

                void OnFileFound(IVirtualFile papFile) {
                    using var lookup = vfs.GetLookup(papFile);
                    lookup
                        .AsFileResource<PapFile>(cts.Token)
                        .ContinueWith(r2 => {
                            lock (listLock)
                                paps.Add(r2.Result);
                            Flush(false);
                        }, cts.Token);
                }

                await Task.WhenAll(
                    vfs.Search(animationFolder, "*.pap", null, null, OnFileFound, cancellationToken: cts.Token),
                    Viewer.ModelInfoResolverTask!.ContinueWith(async ra => {
                        if (!ra.IsCompletedSuccessfully)
                            return;

                        var charaFolder = await vfs.LocateFolder(root, "chara/");
                        if (charaFolder is null)
                            return;

                        foreach (var f in vfs.GetFolders(await vfs.AsFoldersResolved(charaFolder))) {
                            if (f is not SqpackFolder {IsUnknownContainer: true} unknownFolder)
                                continue;

                            // brute force paps
                            foreach (var f2 in vfs.GetFolders(await vfs.AsFoldersResolved(unknownFolder))) {
                                foreach (var f3 in vfs.GetFiles(f2)) {
                                    cts.Token.ThrowIfCancellationRequested();
                                    try {
                                        using var lookup = vfs.GetLookup(f3);
                                        var papTask = lookup.AsFileResource<PapFile>(cts.Token);
                                        await Viewer.RunOnUiThreadAfter(papTask, r2 => {
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
                    }, cts.Token));

                Flush(true);
            }
        }, cts.Token);
    }

    public void ShowRelativeTo(Control opener) {
        var rc = Viewer.GetViewportRectangleSuggestion(opener);
        if (rc.Width < MinimumDefaultWidth) {
            rc.X -= (MinimumDefaultWidth - rc.Width) / 2;
            rc.Width = MinimumDefaultWidth;
        }

        if (rc.Height < MinimumDefaultHeight) {
            rc.X -= (MinimumDefaultHeight - rc.Height) / 2;
            rc.Height = MinimumDefaultHeight;
        }

        rc = Rectangle.Inflate(
            rc,
            (MainLeftSplitter.Panel1.Width + MainRightSplitter.Panel2.Width) / 2,
            0);

        SetBounds(rc.X, rc.Y, rc.Width, rc.Height);
        Show();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) {
        switch (keyData) {
            case Keys.S | Keys.Control: {
                if (Viewer.ModelTask?.IsCompletedSuccessfully is not true)
                    return true;
                
                var model = new Model(Viewer.ModelTask.Result);
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
                    Task.Factory.StartNew(async () => {
                        try {
                            var tuple = new GltfTuple();
                            int? skinIndexNullable = Viewer.SkeletonTask is { } skeletonTask
                                ? tuple.AttachSkin(await skeletonTask)
                                : null;

                            for (var i = 0; i < model.Materials.Length; i++) {
                                var mtrlPath = model.File.Strings
                                    .AsSpan((int) model.File.MaterialNameOffsets[i])
                                    .ExtractCString();

                                if (mtrlPath.StartsWith('/'))
                                    mtrlPath = Material.ResolveRelativeMaterialPath(mtrlPath, model.VariantId);

                                if (mtrlPath is null)
                                    continue;

                                var mtrlvf = await Viewer.Vfs!.LocateFile(Viewer.VfsRoot!, mtrlPath);
                                if (mtrlvf is null)
                                    continue;

                                using var lookup = Viewer.Vfs!.GetLookup(mtrlvf);
                                model.Materials[i] = new(await lookup.AsFileResource<MtrlFile>());
                                
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
                                await tuple.AttachMaterial(m, async texPath => {
                                    var mtrlvf = await Viewer.Vfs!.LocateFile(Viewer.VfsRoot!, texPath);
                                    if (mtrlvf is null)
                                        return null;

                                    using var lookup = Viewer.Vfs!.GetLookup(mtrlvf);
                                    return await lookup.AsFileResource<TexFile>();
                                });
                            }

                            tuple.AddToScene(tuple.AttachMesh(model, skinIndexNullable), skinIndexNullable);

                            if (skinIndexNullable is { } skinIndex) {
                                foreach (var anim in _source)
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

    protected override void OnFormClosed(FormClosedEventArgs e) {
        _closeToken.Cancel();
        base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        if (WindowState == FormWindowState.Normal && IsFullScreen)
            IsFullScreen = false;
    }

    private class AnimationListEntry {
        public readonly PapFile PapFile;
        public readonly Task<IAnimation> AnimationTask;

        public AnimationListEntry(PapFile papFile, int animationIndex) {
            PapFile = papFile;
            AnimationTask = Task.FromResult((IAnimation) papFile.AnimationBindings[animationIndex]);
            FileName = Path.GetFileName(PapFile.FilePath.Path);
            AnimationName = papFile.Animations[animationIndex].Name;
            AnimationIndex = animationIndex;
        }

        [UsedImplicitly] public string FileName { get; }
        [UsedImplicitly] public int AnimationIndex { get; }
        [UsedImplicitly] public string AnimationName { get; }
        [UsedImplicitly] public string FullPath => PapFile.FilePath.Path;
    }

    private class AnimationListDataSource : AbstractVirtualListDataSource, IReadOnlyList<AnimationListEntry> {
        private readonly List<AnimationListEntry> _objects = new();

        public AnimationListDataSource(VirtualObjectListView listView) : base(listView) { }

        public override object GetNthObject(int n) => _objects[n];

        public override int GetObjectCount() => _objects.Count;

        public override int GetObjectIndex(object model) =>
            model is AnimationListEntry e ? _objects.IndexOf(e) : -1;

        public override int SearchText(string value, int first, int last, OLVColumn column)
            => DefaultSearchText(value, first, last, column, this);

        public override void Sort(OLVColumn column, SortOrder order) {
            var orderMultiplier = order == SortOrder.Descending ? -1 : 1;
            switch (column.AspectName) {
                case nameof(AnimationListEntry.FileName):
                    _objects.Sort((x, y) => {
                        var a = MiscUtils.CompareNatural(x.FileName, y.FileName);
                        if (a != 0)
                            return a * orderMultiplier;
                        return x.AnimationIndex.CompareTo(y.AnimationIndex) * orderMultiplier;
                    });
                    break;
                case nameof(AnimationListEntry.AnimationIndex):
                    _objects.Sort((x, y) => x.AnimationIndex.CompareTo(y.AnimationIndex) * orderMultiplier);
                    break;
                case nameof(AnimationListEntry.FullPath):
                    _objects.Sort((x, y) => {
                        var a = MiscUtils.CompareNatural(x.FullPath, y.FullPath);
                        if (a != 0)
                            return a * orderMultiplier;
                        return x.AnimationIndex.CompareTo(y.AnimationIndex) * orderMultiplier;
                    });
                    break;
            }
        }

        public override void AddObjects(ICollection modelObjects) =>
            _objects.AddRange(modelObjects.Cast<AnimationListEntry>());

        public override void InsertObjects(int index, ICollection modelObjects) =>
            _objects.InsertRange(index, modelObjects.Cast<AnimationListEntry>());

        public override void RemoveObjects(ICollection modelObjects) {
            foreach (var o in modelObjects) {
                if (o is AnimationListEntry vo) {
                    var i = _objects.IndexOf(vo);
                    if (i != -1)
                        _objects.RemoveAt(i);
                }
            }
        }

        public override void SetObjects(IEnumerable collection) {
            _objects.Clear();
            _objects.AddRange(collection.Cast<AnimationListEntry>());
        }

        public override void UpdateObject(int index, object modelObject) =>
            _objects[index] = (AnimationListEntry) modelObject;

        public IEnumerator<AnimationListEntry> GetEnumerator() => _objects.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _objects.GetEnumerator();

        public int Count => _objects.Count;

        public AnimationListEntry this[int index] => _objects[index];
    }
}
