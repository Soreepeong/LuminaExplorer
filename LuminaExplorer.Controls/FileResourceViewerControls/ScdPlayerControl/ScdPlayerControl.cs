using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data.Files;
using LuminaExplorer.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Timer = System.Windows.Forms.Timer;

namespace LuminaExplorer.Controls.FileResourceViewerControls.ScdPlayerControl;

/// <summary>
/// Lists the audio entries of a SCD file and plays them.
/// </summary>
public class ScdPlayerControl : AbstractFileResourceViewerControl {
    private static float _lastVolume = 0.5f;

    private readonly ListView _listView;
    private readonly TrackBar _seekBar;
    private readonly Button _playPauseButton;
    private readonly Button _stopButton;
    private readonly Label _timeLabel;
    private readonly CheckBox _loopCheckBox;
    private readonly ComboBox _layerComboBox;
    private readonly TrackBar _volumeBar;
    private readonly Label _statusLabel;
    private readonly Timer _timer;

    private CancellationTokenSource? _loadCancellationTokenSource;
    private CancellationTokenSource? _prepareCancellationTokenSource;
    private List<ScdAudioEntry> _entries = [];

    private WaveOut? _waveOut;
    private LoopingSampleProvider? _looping;
    private StereoLayerSampleProvider? _layer;
    private VolumeSampleProvider? _volume;
    private ScdAudioEntry? _currentEntry;
    private bool _seekDragging;
    private bool _suppressUiEvents;

    public ScdPlayerControl()
    {
        this.SuspendLayout();

        this._listView = new() {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            GridLines = true,
        };
        this._listView.Columns.AddRange(
        [
            new() { Text = "#", TextAlign = HorizontalAlignment.Right, Tag = 40 },
            new() { Text = "Codec", Tag = 90 },
            new() { Text = "Ch", TextAlign = HorizontalAlignment.Right, Tag = 36 },
            new() { Text = "Rate", TextAlign = HorizontalAlignment.Right, Tag = 60 },
            new() { Text = "Duration", TextAlign = HorizontalAlignment.Right, Tag = 80 },
            new() { Text = "Loop", Tag = 150 },
            new() { Text = "Size", TextAlign = HorizontalAlignment.Right, Tag = 80 },
            new() { Text = "Note", Tag = 200 },
        ]);

        this._seekBar = new() {
            Dock = DockStyle.Fill,
            TickStyle = TickStyle.None,
            Minimum = 0,
            Maximum = 1,
            SmallChange = 1000,
            LargeChange = 5000,
            AutoSize = false,
        };

        this._playPauseButton = new() { Text = "Play", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        this._stopButton = new() { Text = "Stop", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        this._timeLabel = new() {
            Text = "0:00.000 / 0:00.000",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new(0, 6, 0, 0),
        };
        this._loopCheckBox = new() { Text = "Loop", AutoSize = true, Checked = true, Padding = new(0, 3, 0, 0) };
        this._layerComboBox = new() {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
        };
        var volumeLabel = new Label { Text = "Volume", AutoSize = true, Padding = new(0, 6, 0, 0) };
        this._volumeBar = new() {
            Minimum = 0,
            Maximum = 100,
            TickStyle = TickStyle.None,
            SmallChange = 5,
            LargeChange = 10,
            Value = (int) Math.Round(_lastVolume * 100),
            AutoSize = false,
        };
        this._statusLabel = new() {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
        };
        buttons.Controls.AddRange(
        [
            this._playPauseButton,
            this._stopButton,
            this._timeLabel,
            this._loopCheckBox,
            this._layerComboBox,
            volumeLabel,
            this._volumeBar,
        ]);

        var bottom = new TableLayoutPanel {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
        };
        bottom.ColumnStyles.Add(new(SizeType.Percent, 100));
        bottom.RowStyles.Add(new(SizeType.AutoSize));
        bottom.RowStyles.Add(new(SizeType.AutoSize));
        bottom.RowStyles.Add(new(SizeType.AutoSize));
        bottom.Controls.Add(this._seekBar, 0, 0);
        bottom.Controls.Add(buttons, 0, 1);
        bottom.Controls.Add(this._statusLabel, 0, 2);

        this.Controls.Add(this._listView);
        this.Controls.Add(bottom);

        this.ApplyDpiSizes();

        this._listView.ItemActivate += (_, _) => this.PlaySelected();
        this._listView.SelectedIndexChanged += this.ListViewOnSelectedIndexChanged;
        this._playPauseButton.Click += (_, _) => this.TogglePlayPause();
        this._stopButton.Click += (_, _) => this.Stop();
        this._seekBar.MouseDown += (_, _) => this._seekDragging = true;
        this._seekBar.MouseUp += (_, _) => {
            this._seekDragging = false;
            this.SeekToSeekBarValue();
        };
        this._seekBar.Scroll += (_, _) => this.SeekToSeekBarValue();
        this._loopCheckBox.CheckedChanged += (_, _) => {
            if (this._looping is { } looping)
                looping.LoopEnabled = this._loopCheckBox.Checked;
        };
        this._layerComboBox.SelectedIndexChanged += (_, _) => {
            if (this._layer is { } layer)
                layer.SelectedLayer = this._layerComboBox.SelectedIndex - 1;
        };
        this._volumeBar.ValueChanged += (_, _) => {
            _lastVolume = this._volumeBar.Value / 100f;
            if (this._volume is { } volume)
                volume.Volume = _lastVolume;
        };

        this._timer = new() { Interval = 50 };
        this._timer.Tick += (_, _) => this.UpdatePlaybackUi();
        this._timer.Start();

        this.ResumeLayout(true);
        this.UpdatePlaybackUi();
    }

    /// <summary>Whether to start playing the first playable entry once a file has been loaded.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AutoPlay { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool LoopEnabled {
        get => this._loopCheckBox.Checked;
        set => this._loopCheckBox.Checked = value;
    }

    /// <summary>Volume, between 0 and 1.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Volume {
        get => this._volumeBar.Value / 100f;
        set => this._volumeBar.Value = (int) Math.Round(Math.Clamp(value, 0, 1) * 100);
    }

    public IReadOnlyList<ScdAudioEntry> Entries => this._entries;

    public ScdAudioEntry? SelectedEntry =>
        this._listView.SelectedItems.Count == 0 ? null : this._listView.SelectedItems[0].Tag as ScdAudioEntry;

    /// <summary>Entry currently loaded into the output device (playing, paused, or stopped).</summary>
    public ScdAudioEntry? CurrentEntry => this._currentEntry;

    public PlaybackState PlaybackState => this._waveOut?.PlaybackState ?? PlaybackState.Stopped;

    /// <summary>Raised on the UI thread after the entries of the file passed to <see cref="SetFile"/> are read.
    /// </summary>
    public event EventHandler? EntriesLoaded;

    public override Size GetPreferredSize(Size proposedSize) => this.LogicalToDeviceUnits(new Size(760, 360));

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this._timer.Dispose();
            this._loadCancellationTokenSource?.Cancel();
            this._prepareCancellationTokenSource?.Cancel();
            this.ReleaseOutput();
        }

        base.Dispose(disposing);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        this.ApplyDpiSizes();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        this.ApplyDpiSizes();
    }

    private void ApplyDpiSizes()
    {
        foreach (ColumnHeader column in this._listView.Columns) {
            if (column.Tag is int width)
                column.Width = this.LogicalToDeviceUnits(width);
        }

        this._seekBar.Height = this.LogicalToDeviceUnits(28);
        this._volumeBar.Size = this.LogicalToDeviceUnits(new Size(120, 28));
        this._layerComboBox.Width = this.LogicalToDeviceUnits(140);
        this._statusLabel.Height = this.LogicalToDeviceUnits(20);
    }

    /// <summary>Stops playback and shows the entries of the given file.</summary>
    /// <remarks>The file is read in a background thread from a private copy; <paramref name="scd"/> is not
    /// modified.</remarks>
    public void SetFile(ScdFile? scd)
    {
        this.ClearFile();
        if (scd is null)
            return;

        var cts = this._loadCancellationTokenSource = new();
        this._statusLabel.Text = "Loading...";
        Task.Run(() => ScdAudioEntry.ReadAll(scd, cts.Token), cts.Token)
            .ContinueWith(
                r => {
                    if (cts.IsCancellationRequested || this.IsDisposed)
                        return;

                    if (!r.IsCompletedSuccessfully) {
                        this._statusLabel.Text =
                            $"Failed to read: {r.Exception?.InnerException?.Message ?? r.Exception?.Message}";
                        return;
                    }

                    this.SetEntries(r.Result);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                this.UiTaskScheduler);
    }

    /// <summary>Stops playback, releases the audio device, and clears the list.</summary>
    public void ClearFile()
    {
        this._loadCancellationTokenSource?.Cancel();
        this._loadCancellationTokenSource = null;
        this.ReleaseOutput();
        this._entries = [];
        this._listView.Items.Clear();
        this._statusLabel.Text = "";
        this.UpdatePlaybackUi();
    }

    private void SetEntries(List<ScdAudioEntry> entries)
    {
        this._entries = entries;
        this._listView.BeginUpdate();
        try {
            this._listView.Items.Clear();
            foreach (var entry in entries) {
                var item = new ListViewItem(entry.Index.ToString(CultureInfo.InvariantCulture)) { Tag = entry };
                item.SubItems.Add(entry.CodecName);
                item.SubItems.Add(entry.Channels.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(entry.SampleRate.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(entry.TotalFrames < 0 ? "?" : FormatTime(entry.Duration));
                item.SubItems.Add(
                    entry.HasLoop
                        ? $"{FormatTime(entry.FramesToTime(entry.LoopStartFrame!.Value))} - " +
                        $"{FormatTime(entry.FramesToTime(entry.LoopEndFrame!.Value))}"
                        : "-");
                item.SubItems.Add(FormatSize(entry.StreamSize));
                item.SubItems.Add(entry.IsPlayable ? "" : entry.UnsupportedReason ?? "Unsupported");
                if (!entry.IsPlayable)
                    item.ForeColor = SystemColors.GrayText;
                this._listView.Items.Add(item);
            }
        } finally {
            this._listView.EndUpdate();
        }

        this._statusLabel.Text = entries.Count switch {
            0 => "No audio entries.",
            1 => "1 audio entry.",
            _ => $"{entries.Count} audio entries.",
        };

        var first = entries.FindIndex(x => x.IsPlayable);
        if (this._listView.Items.Count > 0) {
            var item = this._listView.Items[Math.Max(0, first)];
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
        }

        this.EntriesLoaded?.Invoke(this, EventArgs.Empty);

        if (this.AutoPlay && first >= 0)
            this.PlaySelected();
    }

    /// <summary>Moves the selection by the given number of entries.</summary>
    public void SelectRelative(int delta)
    {
        if (this._listView.Items.Count == 0)
            return;

        var index = this._listView.SelectedIndices.Count == 0
            ? delta > 0 ? 0 : this._listView.Items.Count - 1
            : Math.Clamp(this._listView.SelectedIndices[0] + delta, 0, this._listView.Items.Count - 1);
        var item = this._listView.Items[index];
        item.Selected = true;
        item.Focused = true;
        item.EnsureVisible();
    }

    /// <summary>Starts playing the selected entry from the beginning.</summary>
    public void PlaySelected()
    {
        if (this.SelectedEntry is { } entry)
            this.PlayEntry(entry);
    }

    /// <summary>Resumes playback, or starts playing the selected entry if nothing has been loaded.</summary>
    public void Play()
    {
        if (this._waveOut is null || this._looping is null) {
            this.PlaySelected();
            return;
        }

        if (this._waveOut.PlaybackState == PlaybackState.Playing)
            return;

        if (this._waveOut.PlaybackState == PlaybackState.Stopped &&
            !this._looping.LoopEnabled &&
            this._looping.PositionFrames >= this._looping.LengthFrames)
            this._looping.PositionFrames = 0;

        try {
            this._waveOut.Play();
        } catch (Exception e) {
            this._statusLabel.Text = $"Audio device error: {e.Message}";
            this.ReleaseOutput();
        }

        this.UpdatePlaybackUi();
    }

    public void Pause()
    {
        if (this._waveOut?.PlaybackState == PlaybackState.Playing)
            this._waveOut.Pause();
        this.UpdatePlaybackUi();
    }

    public void TogglePlayPause()
    {
        var selected = this.SelectedEntry;
        if (selected is not null && selected != this._currentEntry) {
            this.PlayEntry(selected);
            return;
        }

        if (this.PlaybackState == PlaybackState.Playing)
            this.Pause();
        else
            this.Play();
    }

    /// <summary>Stops playback and rewinds to the beginning. The audio device is kept open.</summary>
    public void Stop()
    {
        this._prepareCancellationTokenSource?.Cancel();
        this._prepareCancellationTokenSource = null;
        try {
            this._waveOut?.Stop();
        } catch (Exception e) {
            this._statusLabel.Text = $"Audio device error: {e.Message}";
        }

        if (this._looping is not null)
            this._looping.PositionFrames = 0;
        this.UpdatePlaybackUi();
    }

    /// <summary>Seeks within the current entry.</summary>
    public void Seek(TimeSpan position)
    {
        if (this._looping is not { } looping || this._currentEntry is not { } entry)
            return;
        looping.PositionFrames = (long) Math.Round(position.TotalSeconds * entry.SampleRate);
        this.UpdatePlaybackUi();
    }

    private void PlayEntry(ScdAudioEntry entry)
    {
        this.ReleaseOutput();
        this._currentEntry = entry;

        if (!entry.IsPlayable) {
            this._statusLabel.Text = $"#{entry.Index}: {entry.UnsupportedReason ?? "Unsupported"}";
            this.UpdatePlaybackUi();
            return;
        }

        var cts = this._prepareCancellationTokenSource = new();
        this._statusLabel.Text = $"#{entry.Index}: Decoding...";
        Task.Run(() => ScdSampleSource.Create(entry), cts.Token)
            .ContinueWith(
                r => {
                    if (cts.IsCancellationRequested || this.IsDisposed || this._currentEntry != entry) {
                        if (r.IsCompletedSuccessfully)
                            r.Result.Dispose();
                        return;
                    }

                    if (!r.IsCompletedSuccessfully) {
                        this._statusLabel.Text =
                            $"#{entry.Index}: Failed to decode: " +
                            $"{r.Exception?.InnerException?.Message ?? r.Exception?.Message}";
                        return;
                    }

                    this.StartOutput(entry, r.Result);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                this.UiTaskScheduler);
        this.UpdatePlaybackUi();
    }

    private void StartOutput(ScdAudioEntry entry, ScdSampleSource source)
    {
        var looping = new LoopingSampleProvider(source, entry.LoopStartFrame, entry.LoopEndFrame) {
            LoopEnabled = this._loopCheckBox.Checked,
        };

        ISampleProvider chain = looping;
        StereoLayerSampleProvider? layer = null;
        if (source.WaveFormat.Channels > 2) {
            chain = layer = new(chain);
            this._suppressUiEvents = true;
            try {
                this._layerComboBox.Items.Clear();
                this._layerComboBox.Items.Add("All layers (mixed)");
                for (var i = 0; i < layer.LayerCount; i++) {
                    this._layerComboBox.Items.Add(
                        i * 2 + 1 < source.WaveFormat.Channels
                            ? $"Layer {i + 1} (ch {i * 2 + 1}-{i * 2 + 2})"
                            : $"Layer {i + 1} (ch {i * 2 + 1})");
                }

                this._layerComboBox.SelectedIndex = 0;
            } finally {
                this._suppressUiEvents = false;
            }
        }

        this._layerComboBox.Visible = layer is not null;

        var volume = new VolumeSampleProvider(chain) { Volume = this._volumeBar.Value / 100f };
        var waveOut = new WaveOut { BufferMilliseconds = 60, NumberOfBuffers = 3 };
        waveOut.PlaybackStopped += this.WaveOutOnPlaybackStopped;
        try {
            waveOut.Init(volume);
            waveOut.Play();
        } catch (Exception e) {
            waveOut.PlaybackStopped -= this.WaveOutOnPlaybackStopped;
            waveOut.Dispose();
            looping.Dispose();
            this._layerComboBox.Visible = false;
            this._statusLabel.Text = $"#{entry.Index}: Audio device error: {e.Message}";
            this.UpdatePlaybackUi();
            return;
        }

        this._waveOut = waveOut;
        this._looping = looping;
        this._layer = layer;
        this._volume = volume;

        this._statusLabel.Text =
            $"#{entry.Index}: {entry.CodecName}, {entry.Channels}ch, {entry.SampleRate}Hz" +
            (looping.HasLoop
                ? $", loop {FormatTime(entry.FramesToTime(looping.LoopStartFrame!.Value))} - " +
                $"{FormatTime(entry.FramesToTime(looping.LoopEndFrame!.Value))}"
                : ", no loop points");
        this.UpdatePlaybackUi();
    }

    private void WaveOutOnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (this.InvokeRequired) {
            try {
                this.BeginInvoke(() => this.WaveOutOnPlaybackStopped(sender, e));
            } catch (InvalidOperationException) {
                // handle destroyed
            }

            return;
        }

        if (this.IsDisposed || sender != this._waveOut || this._waveOut is not { } waveOut)
            return;

        if (e.Exception is not null)
            this._statusLabel.Text = $"Playback stopped: {e.Exception.Message}";

        if (waveOut.PlaybackState == PlaybackState.Stopped &&
            this._looping is { } looping &&
            looping.PositionFrames >= looping.LengthFrames)
            looping.PositionFrames = 0;

        this.UpdatePlaybackUi();
    }

    private void ReleaseOutput()
    {
        this._prepareCancellationTokenSource?.Cancel();
        this._prepareCancellationTokenSource = null;

        if (this._waveOut is { } waveOut) {
            this._waveOut = null;
            waveOut.PlaybackStopped -= this.WaveOutOnPlaybackStopped;
            try {
                waveOut.Stop();
            } catch (Exception) {
                // ignore; disposing anyway
            }

            waveOut.Dispose();
        }

        this._looping?.Dispose();
        this._looping = null;
        this._layer = null;
        this._volume = null;
        this._currentEntry = null;
        if (!this.IsDisposed)
            this._layerComboBox.Visible = false;
    }

    private void SeekToSeekBarValue()
    {
        if (this._suppressUiEvents)
            return;
        this.Seek(TimeSpan.FromMilliseconds(this._seekBar.Value));
    }

    private void ListViewOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (this.SelectedEntry is { IsPlayable: false } entry)
            this._statusLabel.Text = $"#{entry.Index}: {entry.UnsupportedReason ?? "Unsupported"}";
    }

    private void UpdatePlaybackUi()
    {
        if (this.IsDisposed)
            return;

        var state = this.PlaybackState;
        var playText = state == PlaybackState.Playing ? "Pause" : "Play";
        if (this._playPauseButton.Text != playText)
            this._playPauseButton.Text = playText;
        this._stopButton.Enabled = this._waveOut is not null;
        this._seekBar.Enabled = this._looping is not null;

        if (this._looping is not { } looping || this._currentEntry is not { } entry || entry.SampleRate <= 0) {
            this.SetTimeLabel("0:00.000 / 0:00.000");
            this._suppressUiEvents = true;
            try {
                this._seekBar.Value = 0;
            } finally {
                this._suppressUiEvents = false;
            }

            return;
        }

        var position = entry.FramesToTime(looping.PositionFrames);
        var length = entry.FramesToTime(looping.LengthFrames);
        this.SetTimeLabel($"{FormatTime(position)} / {FormatTime(length)}");

        if (this._seekDragging)
            return;

        this._suppressUiEvents = true;
        try {
            var max = Math.Max(1, (int) Math.Min(int.MaxValue, length.TotalMilliseconds));
            if (this._seekBar.Maximum != max) {
                this._seekBar.Maximum = max;
                this._seekBar.SmallChange = Math.Max(1, Math.Min(1000, max / 100));
                this._seekBar.LargeChange = Math.Max(1, Math.Min(5000, max / 20));
            }

            this._seekBar.Value = Math.Clamp((int) position.TotalMilliseconds, 0, max);
        } finally {
            this._suppressUiEvents = false;
        }
    }

    private void SetTimeLabel(string text)
    {
        if (this._timeLabel.Text != text)
            this._timeLabel.Text = text;
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);

    private static string FormatSize(long size) => size switch {
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024.0:0.0} KiB",
        _ => $"{size / 1024.0 / 1024.0:0.00} MiB",
    };

    /// <summary>Gets entries that can be exported or played.</summary>
    public IEnumerable<ScdAudioEntry> PlayableEntries => this._entries.Where(x => x.IsPlayable);
}
