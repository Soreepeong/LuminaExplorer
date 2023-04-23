using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Data;
using LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl.BitmapSource;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.LazySqPackTree;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl;

public partial class TexFileViewerControl {
    private PanZoomTracker Viewport { get; }

    public PointF Pan {
        get => Viewport.Pan;
        set {
            if (Viewport.Pan != value)
                return;
            Viewport.Pan = value;
            Invalidate();
        }
    }

    public RectangleF ImageRect => Viewport.EffectiveRect;

    public float EffectiveZoom => Viewport.EffectiveZoom;

    public override Size GetPreferredSize(Size proposedSize) =>
        Size.Add(
            _bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
                ? _bitmapSourceTaskCurrent.Result.Layout.GridSize
                : base.GetPreferredSize(proposedSize),
            new(Margin.Horizontal, Margin.Vertical));

    public override async Task<Size> GetPreferredSizeAsync(Size proposedSize) {
        Size? size = null;
        if (_bitmapSourceTaskCurrent is not null) {
            try {
                size = (await _bitmapSourceTaskCurrent.ConfigureAwait(false)).Layout.GridSize;
            } catch (Exception) {
                // pass
            }
        }

        size ??= await base.GetPreferredSizeAsync(proposedSize);

        return Size.Add(size.Value, new(Margin.Horizontal, Margin.Vertical));
    }

    public void ChangeDisplayedMipmap(int imageIndex, int mipmap, bool force = false) {
        if (_bitmapSourceTaskCurrent is not { } bitmapSource)
            return;

        if (!force && _currentMipmap == mipmap && _currentImageIndex == imageIndex)
            return;

        _currentMipmap = mipmap;
        _currentImageIndex = imageIndex;
        _loadStartTicks = Environment.TickCount64;
        _fadeTimer.Enabled = true;
        _fadeTimer.Interval = 1;
        MouseActivity.Enabled = false;

        ClearDisplayInformationCache();
        bitmapSource.Task.ContinueWith(r => {
            if (!r.IsCompletedSuccessfully ||
                bitmapSource != _bitmapSourceTaskCurrent ||
                _currentMipmap != mipmap ||
                _currentImageIndex != imageIndex)
                return;

            r.Result.UpdateSelection(imageIndex, mipmap);
        }, UiTaskScheduler);

        Invalidate();
    }

    public void SetFile(FileInfo fileInfo) {
        ClearFileImpl();
        PhysicalFile = fileInfo;

        throw new NotImplementedException();
        SetFileImpl(fileInfo.Name, Task.FromResult((IBitmapSource) new TexBitmapSource(FileResourceTyped!)));
    }

    public override void SetFile(VirtualSqPackTree tree, VirtualFile file, FileResource fileResource) {
        base.SetFile(tree, file, fileResource);
        ClearFileImpl();

        SetFileImpl(file.Name, Task.FromResult((IBitmapSource) new TexBitmapSource(FileResourceTyped!)));
    }

    private void SetFileImpl(string fileName, Task<IBitmapSource> sourceTask) {
        FileName = fileName;

        if (_bitmapSourceTaskCurrent is not null) {
            if (IsCurrentBitmapSourceReadyOnRenderer()) {
                if (TryGetRenderers(out var renderers))
                    foreach (var renderer in renderers)
                        renderer.UpdateBitmapSource(_bitmapSourceTaskCurrent?.Task, null);

                SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
                _bitmapSourceTaskPrevious = _bitmapSourceTaskCurrent;
                _bitmapSourceTaskCurrent = null;
            } else {
                if (TryGetRenderers(out var renderers))
                    foreach (var renderer in renderers)
                        renderer.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, null);
                SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
            }
        }

        var sourceTaskCurrent = _bitmapSourceTaskCurrent = new(sourceTask);

        {
            if (TryGetRenderers(out var renderers, true)) {
                foreach (var renderer in renderers)
                    renderer.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, _bitmapSourceTaskCurrent?.Task);
            } else {
                _renderers!.ContinueWith(result => {
                    if (sourceTaskCurrent != _bitmapSourceTaskCurrent || !result.IsCompletedSuccessfully)
                        return;

                    foreach (var renderer in result.Result)
                        renderer.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, _bitmapSourceTaskCurrent?.Task);
                }, UiTaskScheduler);
            }
        }

        ChangeDisplayedMipmap(0, 0);
    }

    public override void ClearFile(bool keepContentsDisplayed = false) {
        ClearFileImpl();

        if (keepContentsDisplayed) {
            if (_bitmapSourceTaskCurrent is not null) {
                if (IsCurrentBitmapSourceReadyOnRenderer()) {
                    if (TryGetRenderers(out var renderers))
                        foreach (var r in renderers)
                            r.UpdateBitmapSource(_bitmapSourceTaskCurrent?.Task, null);

                    SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
                    _bitmapSourceTaskPrevious = _bitmapSourceTaskCurrent;
                    _bitmapSourceTaskCurrent = null;
                } else {
                    if (TryGetRenderers(out var renderers))
                        foreach (var r in renderers)
                            r.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, null);
                    SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
                }
            }
        } else {
            if (TryGetRenderers(out var renderers))
                foreach (var r in renderers)
                    r.UpdateBitmapSource(null, null);

            SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
            SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
            Viewport.Reset(Size.Empty);
        }

        base.ClearFile(keepContentsDisplayed);
    }

    private void ClearDisplayInformationCache() {
        _autoDescriptionCached = null;
        _autoDescriptionSourceZoom = float.NaN;
        _autoDescriptionRectangle = null;
    }

    private void ClearFileImpl() {
        MouseActivity.Enabled = false;
        _loadStartTicks = long.MaxValue;
        FileName = null;
        ClearDisplayInformationCache();
        _currentMipmap = -1;
    }

    private bool IsCurrentBitmapSourceReadyOnRenderer() =>
        _bitmapSourceTaskCurrent is {IsCompletedSuccessfully: true} sourceTask &&
        TryGetRenderers(out var renderers) &&
        renderers.Any(r => r.LastException is null && r.HasBitmapSourceReadyForDrawing(sourceTask.Task));
}
