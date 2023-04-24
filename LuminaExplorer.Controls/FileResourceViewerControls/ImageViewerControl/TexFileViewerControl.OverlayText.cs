using System;
using System.Drawing;
using System.Text;
using Lumina.Data.Files;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ImageViewerControl;

public partial class TexFileViewerControl {
    private long _autoDescriptionShowUntilTicks;
    private bool _autoDescriptionBeingHovered;
    private string? _autoDescriptionCached;
    private float _autoDescriptionSourceZoom = float.NaN;

    private string? _overlayCustomString;
    private long _overlayShowUntilTicks;

    private long _loadStartTicks = long.MaxValue;

    public string? FileName { get; private set; }
    
    public TimeSpan OverlayShortDuration = TimeSpan.FromSeconds(0.5);

    public TimeSpan OverlayLongDuration = TimeSpan.FromSeconds(1);

    public string? LoadingFileNameWhenEmpty {
        get => _loadingFileNameWhenEmpty;
        set {
            if (_loadingFileNameWhenEmpty == value)
                return;
            _loadingFileNameWhenEmpty = value;
            Invalidate();
        }
    }

    public string AutoDescription {
        get {
            var effectiveZoom = Viewport.EffectiveZoom;
            if (_autoDescriptionCached is not null && Equals(effectiveZoom, _autoDescriptionSourceZoom))
                return _autoDescriptionCached;

            var sb = new StringBuilder();
            _autoDescriptionSourceZoom = effectiveZoom;
            sb.AppendLine(FileName);

            if (PhysicalFile is { } physicalFile) {
                // TODO
            } else if (FileResourceTyped is { } texFile) {
                sb.Append(texFile.Header.Format).Append("; ")
                    .Append($"{texFile.Data.Length:##,###} Bytes");
                if (texFile.Header.MipLevels > 1)
                    sb.Append("; ").Append(texFile.Header.MipLevels).Append(" mipmaps");
                sb.AppendLine();

                if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
                    sb.Append("1D: ").Append(texFile.Header.Width);
                if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
                    sb.Append("2D: ").Append(texFile.Header.Width)
                        .Append(" x ").Append(texFile.Header.Height);
                if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType3D))
                    sb.Append("3D: ").Append(texFile.Header.Width)
                        .Append(" x ").Append(texFile.Header.Height)
                        .Append(" x ").Append(texFile.Header.Depth);
                if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
                    sb.Append("Cube: ").Append(texFile.Header.Width)
                        .Append(" x ").Append(texFile.Header.Height);
                if (_currentMipmap > 0) {
                    sb.AppendLine().Append("Mipmap #").Append(_currentMipmap + 1).Append(": ");
                    if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType1D))
                        sb.Append(texFile.TextureBuffer.WidthOfMipmap(_currentMipmap));
                    if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType2D))
                        sb.Append(texFile.TextureBuffer.WidthOfMipmap(_currentMipmap))
                            .Append(" x ").Append(texFile.TextureBuffer.HeightOfMipmap(_currentMipmap));
                    if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureType3D))
                        sb.Append(texFile.TextureBuffer.WidthOfMipmap(_currentMipmap))
                            .Append(" x ").Append(texFile.TextureBuffer.HeightOfMipmap(_currentMipmap))
                            .Append(" x ").Append(texFile.TextureBuffer.DepthOfMipmap(_currentMipmap));
                    if (texFile.Header.Type.HasFlag(TexFile.Attribute.TextureTypeCube))
                        sb.Append(texFile.TextureBuffer.WidthOfMipmap(_currentMipmap))
                            .Append(" x ").Append(texFile.TextureBuffer.HeightOfMipmap(_currentMipmap));
                }

                if (!Equals(effectiveZoom, 1f))
                    sb.Append($" ({effectiveZoom * 100:0.00}%)");
                sb.AppendLine();
                foreach (var f in new[] {
                             TexFile.Attribute.DiscardPerFrame,
                             TexFile.Attribute.DiscardPerMap,
                             TexFile.Attribute.Managed,
                             TexFile.Attribute.UserManaged,
                             TexFile.Attribute.CpuRead,
                             TexFile.Attribute.LocationMain,
                             TexFile.Attribute.NoGpuRead,
                             TexFile.Attribute.AlignedSize,
                             TexFile.Attribute.EdgeCulling,
                             TexFile.Attribute.LocationOnion,
                             TexFile.Attribute.ReadWrite,
                             TexFile.Attribute.Immutable,
                             TexFile.Attribute.TextureRenderTarget,
                             TexFile.Attribute.TextureDepthStencil,
                             TexFile.Attribute.TextureSwizzle,
                             TexFile.Attribute.TextureNoTiled,
                             TexFile.Attribute.TextureNoSwizzle
                         })
                    if (texFile.Header.Type.HasFlag(f))
                        sb.Append("+ ").AppendLine(f.ToString());
            }

            return _autoDescriptionCached = sb.ToString();
        }
    }

    internal bool TryGetEffectiveOverlayInformation(
        out string s,
        out float foreOpacity,
        out float backOpacity,
        out bool hideIfNotLoading) {
        hideIfNotLoading = false;
        var now = Environment.TickCount64;
        var customOverlayVisible =
            !string.IsNullOrWhiteSpace(_overlayCustomString) &&
            _overlayShowUntilTicks > Environment.TickCount64;
        var hasLoadingText =
            FileName is not null || _loadingFileNameWhenEmpty is not null;

        if (customOverlayVisible) {
            var remaining = _overlayShowUntilTicks - now;
            if (remaining >= FadeOutDurationMs / 2 || !hasLoadingText) {
                s = _overlayCustomString!;
                foreOpacity = remaining >= FadeOutDurationMs ? 1f : 1f * remaining / FadeOutDurationMs;
                backOpacity = _overlayBackgroundOpacity * foreOpacity;
                return true;
            }
        }

        if (hasLoadingText) {
            s = string.IsNullOrWhiteSpace(FileName ?? _loadingFileNameWhenEmpty)
                ? "Loading..."
                : $"Loading {FileName ?? _loadingFileNameWhenEmpty}...";
            foreOpacity = 1f;
            backOpacity = _overlayBackgroundOpacity * foreOpacity;
            hideIfNotLoading = true;
            return true;
        }

        s = "";
        foreOpacity = backOpacity = 0f;
        return false;
    }

    public void ExtendDescriptionMandatoryDisplay(TimeSpan duration) {
        var now = Environment.TickCount64;
        _autoDescriptionShowUntilTicks = Math.Max(
            _autoDescriptionShowUntilTicks,
            now + (long) duration.TotalMilliseconds);

        if (_autoDescriptionShowUntilTicks <= now)
            return;

        _timer.Enabled = true;
        _timer.Interval = 1;
        Invalidate(AutoDescriptionRectangle);
    }

    public void ClearOverlayString() {
        _overlayCustomString = null;
        _overlayShowUntilTicks = 0;
        Invalidate();
    }

    public void ShowOverlayString(string? overlayString, TimeSpan overlayTextMessageDuration) {
        var now = Environment.TickCount64;
        _overlayCustomString = overlayString;
        _overlayShowUntilTicks = now + (int) overlayTextMessageDuration.TotalMilliseconds;

        if (_overlayShowUntilTicks > now) {
            _timer.Enabled = true;
            _timer.Interval = 1;
        }

        Invalidate();
    }

    public void ShowOverlayStringShort(string? overlayString) => ShowOverlayString(overlayString, OverlayShortDuration);
    
    public void ShowOverlayStringLong(string? overlayString) => ShowOverlayString(overlayString, OverlayLongDuration);
}
