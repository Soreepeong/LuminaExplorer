using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;
using LuminaExplorer.Controls.Util;
using LuminaExplorer.Core.Util;
using LuminaExplorer.Core.Util.DdsStructs;
using WicNet;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl {
    public IBitmapSource? PreviousBitmapSource => _bitmapSourceTaskPrevious?.IsCompletedSuccessfully is true
        ? _bitmapSourceTaskPrevious.Result
        : null;

    public IBitmapSource? CurrentBitmapSource => _bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
        ? _bitmapSourceTaskCurrent.Result
        : null;

    public IBitmapSource? BitmapSource => CurrentBitmapSource ?? PreviousBitmapSource;

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
        _timer.Enabled = true;
        _timer.Interval = 1;
        MouseActivity.Enabled = false;

        ClearDisplayInformationCache();
        bitmapSource.Task.ContinueWith(result => {
            if (!result.IsCompletedSuccessfully ||
                bitmapSource != _bitmapSourceTaskCurrent ||
                _currentMipmap != mipmap ||
                _currentImageIndex != imageIndex) {
                if (_bitmapSourceTaskPrevious is not null) {
                    if (TryGetRenderers(out var renderers))
                        foreach (var r in renderers)
                            r.PreviousSourceTask = null;
                    SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
                }

                return;
            }

            result.Result.UpdateSelection(imageIndex, mipmap);
        }, UiTaskScheduler);

        Invalidate();
    }

    public void SetFile(FileInfo fileInfo) {
        switch (fileInfo.Extension.ToLowerInvariant()) {
            case ".dds":
                SetFile(new DdsFile(fileInfo.Name, fileInfo.OpenRead()));
                break;
            case ".tex":
            case ".atex":
                SetFile(TexBitmapSource.FromFile(fileInfo));
                break;
            default:
                SetFile(fileInfo.FullName, fileInfo.Length, fileInfo.OpenRead());
                break;
        }
    }

    public void SetFile(DdsFile fileResource) => SetFile(new DdsBitmapSource(fileResource));

    public void SetFile(TexFile fileResource) => SetFile(new TexBitmapSource(fileResource));

    public void SetFile(string name, long size, Stream stream) => 
        SetFile(Task.Run(() => (IBitmapSource) new PlainBitmapSource(name, size, stream, _sliceSpacing)));

    public void SetFile(string name, byte[] rawData) => SetFile(name, rawData.Length, new MemoryStream(rawData, false));

    public void SetFile(FileResource fileResource) {
        if (fileResource is TexFile texFile) {
            SetFile(texFile);
            return;
        }

        switch (Path.GetExtension(fileResource.FilePath.Path).ToLowerInvariant()) {
            case ".dds":
                SetFile(new DdsBitmapSource(new(fileResource.FilePath.Path, fileResource.Data)));
                break;
            default:
                SetFile(fileResource.FilePath.Path, fileResource.Data);
                break;
        }
    }

    public void SetFile(IBitmapSource bitmapSource) {
        bitmapSource.SliceSpacing = _sliceSpacing;
        SetFile(Task.FromResult(bitmapSource));
    }

    public void SetFile(Task<IBitmapSource> sourceTask) {
        ClearFileImpl();

        if (_bitmapSourceTaskCurrent is not null) {
            if (IsCurrentBitmapSourceReadyOnRenderer()) {
                if (TryGetRenderers(out var renderers))
                    renderers.FirstOrDefault()?.UpdateBitmapSource(_bitmapSourceTaskCurrent?.Task, null);

                SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
                _bitmapSourceTaskPrevious = _bitmapSourceTaskCurrent;
                _bitmapSourceTaskCurrent = null;
            } else {
                if (TryGetRenderers(out var renderers))
                    renderers.FirstOrDefault()?.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, null);
                SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
            }
        }

        var sourceTaskCurrent = _bitmapSourceTaskCurrent = new(sourceTask);

        {
            if (TryGetRenderers(out var renderers, true)) {
                renderers.FirstOrDefault()?.UpdateBitmapSource(
                    _bitmapSourceTaskPrevious?.Task, _bitmapSourceTaskCurrent?.Task);
            } else {
                _renderers!.ContinueWith(result => {
                    if (sourceTaskCurrent != _bitmapSourceTaskCurrent || !result.IsCompletedSuccessfully)
                        return;

                    result.Result.FirstOrDefault()?.UpdateBitmapSource(
                        _bitmapSourceTaskPrevious?.Task, _bitmapSourceTaskCurrent?.Task);
                }, UiTaskScheduler);
            }
        }

        ChangeDisplayedMipmap(0, 0);
    }

    public void ClearFile(bool keepContentsDisplayed = false) {
        ClearFileImpl();

        if (keepContentsDisplayed) {
            if (_bitmapSourceTaskCurrent is not null) {
                if (IsCurrentBitmapSourceReadyOnRenderer()) {
                    if (TryGetRenderers(out var renderers))
                        renderers.FirstOrDefault()?.UpdateBitmapSource(_bitmapSourceTaskCurrent?.Task, null);

                    SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
                    _bitmapSourceTaskPrevious = _bitmapSourceTaskCurrent;
                    _bitmapSourceTaskCurrent = null;
                } else {
                    if (TryGetRenderers(out var renderers))
                        renderers.FirstOrDefault()?.UpdateBitmapSource(_bitmapSourceTaskPrevious?.Task, null);
                    SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
                }
            }
        } else {
            if (TryGetRenderers(out var renderers))
                renderers.FirstOrDefault()?.UpdateBitmapSource(null, null);

            SafeDispose.OneAsync(ref _bitmapSourceTaskPrevious);
            SafeDispose.OneAsync(ref _bitmapSourceTaskCurrent);
            Viewport.Reset(Size.Empty);
        }
    }

    private void ClearDisplayInformationCache() {
        _autoDescriptionCached = null;
        _autoDescriptionSourceZoom = float.NaN;
        if (TryGetRenderers(out var renderers))
            foreach (var r in renderers)
                r.AutoDescriptionRectangle = null;
    }

    private void ClearFileImpl() {
        MouseActivity.Enabled = false;
        _loadStartTicks = long.MaxValue;
        ClearDisplayInformationCache();
        _currentMipmap = -1;
    }

    private bool IsCurrentBitmapSourceReadyOnRenderer() =>
        _bitmapSourceTaskCurrent is {IsCompletedSuccessfully: true} sourceTask &&
        TryGetRenderers(out var renderers) &&
        renderers.Any(r => r.LastException is null && r.IsAnyVisibleSliceReadyForDrawing(sourceTask.Task));

    public static bool MaySupportFileResource(FileResource fileResource) =>
        fileResource is TexFile ||
        MaySupportFileName(fileResource.FilePath.Path);

    public static bool MaySupportFileName(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch {
        // TexBitmapSource
        ".tex" => true,
        ".atex" => true,

        // DdsBitmapSource
        ".dds" => true,

        // PlainBitmapSource
        { } y => WicImagingComponent.DecoderFileExtensions.Any(x => x.ToLowerInvariant() == y),
    };
}
