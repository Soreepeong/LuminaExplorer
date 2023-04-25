using System;
using System.Text;

namespace LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

public partial class MultiBitmapViewerControl {
    private long _autoDescriptionShowUntilTicks;
    private bool _autoDescriptionBeingHovered;
    private string? _autoDescriptionCached;
    private float _autoDescriptionSourceZoom = float.NaN;

    private string? _overlayCustomString;
    private long _overlayShowUntilTicks;

    private long _loadStartTicks = long.MaxValue;

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
            if (BitmapSource is not { } source)
                return "";

            var effectiveZoom = Viewport.EffectiveZoom;
            if (_autoDescriptionCached is not null &&
                Equals(effectiveZoom, _autoDescriptionSourceZoom))
                return _autoDescriptionCached;

            var sb = new StringBuilder();
            _autoDescriptionSourceZoom = effectiveZoom;
            sb.Append(source.FileName);
            if (!Equals(effectiveZoom, 1f))
                sb.Append($" ({effectiveZoom * 100:0.00}%)");
            sb.AppendLine();

            source.DescribeImage(sb);

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
            BitmapSource?.FileName is not null || _loadingFileNameWhenEmpty is not null;

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
            s = string.IsNullOrWhiteSpace(BitmapSource?.FileName ?? _loadingFileNameWhenEmpty)
                ? "Loading..."
                : $"Loading {BitmapSource?.FileName ?? _loadingFileNameWhenEmpty}...";
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
