using System;
using System.Text;
using LuminaExplorer.Controls.DirectXStuff.Shaders;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl {
    private long _autoDescriptionShowUntilTicks;
    private bool _autoDescriptionBeingHovered;
    private string? _autoDescriptionCached;

    private string? _overlayCustomString;
    private long _overlayShowUntilTicks;

    private long _loadStartTicks = long.MaxValue;

    public TimeSpan OverlayShortDuration = TimeSpan.FromSeconds(0.5);

    public TimeSpan OverlayLongDuration = TimeSpan.FromSeconds(1);

    public string? LoadingFileNameWhenEmpty {
        get => this._loadingFileNameWhenEmpty;
        set {
            if (this._loadingFileNameWhenEmpty == value)
                return;
            this._loadingFileNameWhenEmpty = value;
            this.Invalidate();
        }
    }

    public string AutoDescription {
        get {
            if (this.BitmapSource is not { } source)
                return "";

            if (this._autoDescriptionCached is not null)
                return this._autoDescriptionCached;

            var sb = new StringBuilder();
            sb.Append(source.FileName);
            sb.Append($" ({this.Viewport.EffectiveZoom * 100:0.00}%");
            switch (this.ChannelFilter) {
                case DirectXTexRendererShader.VisibleColorChannelTypes.Red:
                    sb.Append("; red");
                    goto case DirectXTexRendererShader.VisibleColorChannelTypes.All;
                case DirectXTexRendererShader.VisibleColorChannelTypes.Green:
                    sb.Append("; green");
                    goto case DirectXTexRendererShader.VisibleColorChannelTypes.All;
                case DirectXTexRendererShader.VisibleColorChannelTypes.Blue:
                    sb.Append("; blue");
                    goto case DirectXTexRendererShader.VisibleColorChannelTypes.All;
                case DirectXTexRendererShader.VisibleColorChannelTypes.Alpha:
                    sb.Append("; alpha");
                    break;
                case DirectXTexRendererShader.VisibleColorChannelTypes.All:
                    if (!this.UseAlphaChannel)
                        sb.Append("; alpha channel hidden");
                    break;
            }

            if (this.Rotation != 0)
                sb.Append($"; cw {MathF.Round((360 + 180 * this.Rotation / MathF.PI) % 360)} degrees");

            sb.AppendLine(")");

            source.DescribeImage(sb);

            return this._autoDescriptionCached = sb.ToString();
        }
    }

    internal bool TryGetEffectiveOverlayInformation(
        out string s,
        out float foreOpacity,
        out float backOpacity,
        out bool hideIfNotLoading)
    {
        hideIfNotLoading = false;
        var now = Environment.TickCount64;
        var customOverlayVisible =
            !string.IsNullOrWhiteSpace(this._overlayCustomString) &&
            this._overlayShowUntilTicks > Environment.TickCount64;
        var hasLoadingText = this.BitmapSource?.FileName is not null || this._loadingFileNameWhenEmpty is not null;

        if (customOverlayVisible) {
            var remaining = this._overlayShowUntilTicks - now;
            if (remaining >= FadeOutDurationMs / 2 || !hasLoadingText) {
                s = this._overlayCustomString!;
                foreOpacity = remaining >= FadeOutDurationMs ? 1f : 1f * remaining / FadeOutDurationMs;
                backOpacity = this._overlayBackgroundOpacity * foreOpacity;
                return true;
            }
        }

        if (hasLoadingText) {
            s = string.IsNullOrWhiteSpace(this.BitmapSource?.FileName ?? this._loadingFileNameWhenEmpty)
                ? "Loading..."
                : $"Loading {this.BitmapSource?.FileName ?? this._loadingFileNameWhenEmpty}...";
            foreOpacity = 1f;
            backOpacity = this._overlayBackgroundOpacity * foreOpacity;
            hideIfNotLoading = true;
            return true;
        }

        s = "";
        foreOpacity = backOpacity = 0f;
        return false;
    }

    public void ExtendDescriptionMandatoryDisplay(TimeSpan duration)
    {
        var now = Environment.TickCount64;
        this._autoDescriptionShowUntilTicks = Math.Max(
            this._autoDescriptionShowUntilTicks,
            now + (long) duration.TotalMilliseconds);

        if (this._autoDescriptionShowUntilTicks <= now)
            return;

        this._timer.Enabled = true;
        this._timer.Interval = 1;
        this.Invalidate(this.AutoDescriptionRectangle);
    }

    public void ClearOverlayString()
    {
        this._overlayCustomString = null;
        this._overlayShowUntilTicks = 0;
        this.Invalidate();
    }

    public void ShowOverlayString(string? overlayString, TimeSpan overlayTextMessageDuration)
    {
        var now = Environment.TickCount64;
        this._overlayCustomString = overlayString;
        this._overlayShowUntilTicks = now + (int) overlayTextMessageDuration.TotalMilliseconds;

        if (this._overlayShowUntilTicks > now) {
            this._timer.Enabled = true;
            this._timer.Interval = 1;
        }

        this.Invalidate();
    }

    public void ShowOverlayStringShort(string? overlayString) =>
        this.ShowOverlayString(overlayString, this.OverlayShortDuration);

    public void ShowOverlayStringLong(string? overlayString) =>
        this.ShowOverlayString(overlayString, this.OverlayLongDuration);
}
