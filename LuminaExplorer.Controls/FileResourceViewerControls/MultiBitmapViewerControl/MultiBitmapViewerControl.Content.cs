using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lumina.Data;
using Lumina.Data.Files;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl.BitmapSource;
using LuminaExplorer.Core.ExtraFormats.DirectDrawSurface;
using LuminaExplorer.Core.Util;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl {
    public IBitmapSource? PreviousBitmapSource =>
        this._bitmapSourceTaskPrevious?.IsCompletedSuccessfully is true
            ? this._bitmapSourceTaskPrevious.Result
            : null;

    public IBitmapSource? CurrentBitmapSource =>
        this._bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
            ? this._bitmapSourceTaskCurrent.Result
            : null;

    public IBitmapSource? BitmapSource => this.CurrentBitmapSource ?? this.PreviousBitmapSource;

    public override Size GetPreferredSize(Size proposedSize) =>
        Size.Add(
            this._bitmapSourceTaskCurrent?.IsCompletedSuccessfully is true
                ? this._bitmapSourceTaskCurrent.Result.Layout.GridSize
                : base.GetPreferredSize(proposedSize),
            new(this.Margin.Horizontal, this.Margin.Vertical));

    public override async Task<Size> GetPreferredSizeAsync(Size proposedSize)
    {
        Size? size = null;
        if (this._bitmapSourceTaskCurrent is not null) {
            try {
                size = (await this._bitmapSourceTaskCurrent.ConfigureAwait(false)).Layout.GridSize;
            } catch (Exception) {
                // pass
            }
        }

        size ??= await base.GetPreferredSizeAsync(proposedSize);

        return Size.Add(size.Value, new(this.Margin.Horizontal, this.Margin.Vertical));
    }

    public void ChangeDisplayedMipmap(int imageIndex, int mipmap, bool force = false)
    {
        if (this._bitmapSourceTaskCurrent is not { } bitmapSource)
            return;

        if (!force && this._currentMipmap == mipmap && this._currentImageIndex == imageIndex)
            return;

        this._currentMipmap = mipmap;
        this._currentImageIndex = imageIndex;
        this._loadStartTicks = Environment.TickCount64;
        this._timer.Enabled = true;
        this._timer.Interval = 1;
        this.MouseActivity.Enabled = false;

        this.ClearDisplayInformationCache();
        bitmapSource.Task.ContinueWith(
            result => {
                if (!result.IsCompletedSuccessfully ||
                    bitmapSource != this._bitmapSourceTaskCurrent || this._currentMipmap != mipmap ||
                    this._currentImageIndex != imageIndex) {
                    if (this._bitmapSourceTaskPrevious is not null) {
                        if (this.TryGetRenderers(out var renderers))
                            foreach (var r in renderers)
                                r.PreviousSourceTask = null;
                        SafeDispose.OneAsync(ref this._bitmapSourceTaskPrevious);
                    }

                    return;
                }

                result.Result.UpdateSelection(imageIndex, mipmap);
            },
            this.UiTaskScheduler);

        this.Invalidate();
    }

    public void SetFile(FileInfo fileInfo)
    {
        switch (fileInfo.Extension.ToLowerInvariant()) {
            case ".dds":
                this.SetFile(new DdsFile(fileInfo.Name, fileInfo.OpenRead()));
                break;
            case ".tex":
            case ".atex":
                this.SetFile(TexBitmapSource.FromFile(fileInfo));
                break;
            default:
                this.SetFile(fileInfo.FullName, fileInfo.Length, fileInfo.OpenRead());
                break;
        }
    }

    public void SetFile(DdsFile fileResource) => this.SetFile(new DdsBitmapSource(fileResource));

    public void SetFile(TexFile fileResource) => this.SetFile(new TexBitmapSource(fileResource));

    public void SetFile(string name, long size, Stream stream) => this.SetFile(
        Task.Run(() => (IBitmapSource) new PlainBitmapSource(name, size, stream, this._sliceSpacing)));

    public void SetFile(string name, byte[] rawData) =>
        this.SetFile(name, rawData.Length, new MemoryStream(rawData, false));

    public void SetFile(FileResource fileResource)
    {
        if (fileResource is TexFile texFile) {
            this.SetFile(texFile);
            return;
        }

        switch (Path.GetExtension(fileResource.FilePath.Path).ToLowerInvariant()) {
            case ".dds":
                this.SetFile(new DdsBitmapSource(new(fileResource.FilePath.Path, fileResource.Data)));
                break;
            default:
                this.SetFile(fileResource.FilePath.Path, fileResource.Data);
                break;
        }
    }

    public void SetFile(IBitmapSource bitmapSource)
    {
        bitmapSource.SliceSpacing = this._sliceSpacing;
        this.SetFile(Task.FromResult(bitmapSource));
    }

    public void SetFile(Task<IBitmapSource> sourceTask)
    {
        this.ClearFileImpl();

        if (this._bitmapSourceTaskCurrent is not null) {
            if (this.IsCurrentBitmapSourceReadyOnRenderer()) {
                if (this.TryGetRenderers(out var renderers))
                    renderers.FirstOrDefault()?.UpdateBitmapSource(this._bitmapSourceTaskCurrent?.Task, null);

                SafeDispose.OneAsync(ref this._bitmapSourceTaskPrevious);
                this._bitmapSourceTaskPrevious = this._bitmapSourceTaskCurrent;
                this._bitmapSourceTaskCurrent = null;
            } else {
                if (this.TryGetRenderers(out var renderers))
                    renderers.FirstOrDefault()?.UpdateBitmapSource(this._bitmapSourceTaskPrevious?.Task, null);
                SafeDispose.OneAsync(ref this._bitmapSourceTaskCurrent);
            }
        }

        var sourceTaskCurrent = this._bitmapSourceTaskCurrent = new(sourceTask);

        {
            if (this.TryGetRenderers(out var renderers, true)) {
                renderers.FirstOrDefault()?.UpdateBitmapSource(
                    this._bitmapSourceTaskPrevious?.Task,
                    this._bitmapSourceTaskCurrent?.Task);
            } else {
                this._renderers!.ContinueWith(
                    result => {
                        if (sourceTaskCurrent != this._bitmapSourceTaskCurrent || !result.IsCompletedSuccessfully)
                            return;

                        result.Result.FirstOrDefault()?.UpdateBitmapSource(
                            this._bitmapSourceTaskPrevious?.Task,
                            this._bitmapSourceTaskCurrent?.Task);
                    },
                    this.UiTaskScheduler);
            }
        }

        this.ChangeDisplayedMipmap(0, 0);
    }

    public void ClearFile(bool keepContentsDisplayed = false)
    {
        this.ClearFileImpl();

        if (keepContentsDisplayed) {
            if (this._bitmapSourceTaskCurrent is not null) {
                if (this.IsCurrentBitmapSourceReadyOnRenderer()) {
                    if (this.TryGetRenderers(out var renderers))
                        renderers.FirstOrDefault()?.UpdateBitmapSource(this._bitmapSourceTaskCurrent?.Task, null);

                    SafeDispose.OneAsync(ref this._bitmapSourceTaskPrevious);
                    this._bitmapSourceTaskPrevious = this._bitmapSourceTaskCurrent;
                    this._bitmapSourceTaskCurrent = null;
                } else {
                    if (this.TryGetRenderers(out var renderers))
                        renderers.FirstOrDefault()?.UpdateBitmapSource(this._bitmapSourceTaskPrevious?.Task, null);
                    SafeDispose.OneAsync(ref this._bitmapSourceTaskCurrent);
                }
            }
        } else {
            if (this.TryGetRenderers(out var renderers))
                renderers.FirstOrDefault()?.UpdateBitmapSource(null, null);

            SafeDispose.OneAsync(ref this._bitmapSourceTaskPrevious);
            SafeDispose.OneAsync(ref this._bitmapSourceTaskCurrent);
            this.Viewport.Reset(Size.Empty, 0f);
        }
    }

    private void ClearDisplayInformationCache()
    {
        this._autoDescriptionCached = null;
        if (this.TryGetRenderers(out var renderers))
            foreach (var r in renderers)
                r.AutoDescriptionRectangle = null;
    }

    private void ClearFileImpl()
    {
        this.MouseActivity.Enabled = false;
        this._loadStartTicks = long.MaxValue;
        this.ClearDisplayInformationCache();
        this._currentMipmap = -1;
    }

    private bool IsCurrentBitmapSourceReadyOnRenderer() =>
        this._bitmapSourceTaskCurrent is { IsCompletedSuccessfully: true } sourceTask &&
        this.TryGetRenderers(out var renderers) &&
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
        { } y => ImagingExtensions.ThumbnailSupportedExtensions.Contains(y),
    };
}
