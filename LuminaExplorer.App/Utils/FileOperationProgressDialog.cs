using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace LuminaExplorer.App.Utils;

/// <summary>Shows the progress of a background file operation, with a button to cancel it.</summary>
/// <remarks>The dialog is shown only if the operation takes a while, so that quick operations do not flash it.
/// </remarks>
public sealed class FileOperationProgressDialog : Form {
    private readonly CancellationTokenSource _cancel = new();
    private readonly FileExportProgress _progress;
    private readonly Label _label;
    private readonly ProgressBar _progressBar;
    private readonly Button _cancelButton;
    private readonly System.Windows.Forms.Timer _showTimer;
    private readonly System.Windows.Forms.Timer _updateTimer;
    private IWin32Window? _owner;
    private bool _completed;

    public FileOperationProgressDialog(string title, FileExportProgress progress)
    {
        this._progress = progress;

        this.SuspendLayout();
        this.Text = title;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = this.MinimizeBox = false;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.AutoScaleDimensions = new SizeF(96F, 96F);
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.ClientSize = new(420, 110);

        this._label = new() {
            AutoEllipsis = true,
            Location = new(12, 12),
            Size = new(396, 34),
            Text = "Preparing...",
            UseMnemonic = false,
        };
        this._progressBar = new() {
            Location = new(12, 50),
            Size = new(396, 20),
            Style = ProgressBarStyle.Marquee,
        };
        this._cancelButton = new() {
            Text = "Cancel",
            Location = new(333, 78),
            Size = new(75, 24),
            DialogResult = DialogResult.Cancel,
        };
        this._cancelButton.Click += (_, _) => this.Cancel();
        this.CancelButton = this._cancelButton;
        this.Controls.AddRange([this._label, this._progressBar, this._cancelButton]);
        this.ResumeLayout(false);

        this._showTimer = new() { Interval = 400 };
        this._showTimer.Tick += (_, _) => {
            this._showTimer.Stop();
            if (!this._completed && !this.IsDisposed)
                this.Show(this._owner);
        };

        this._updateTimer = new() { Interval = 100 };
        this._updateTimer.Tick += (_, _) => this.UpdateProgress();
    }

    /// <summary>Gets the token that is cancelled when the user cancels the operation.</summary>
    public CancellationToken Token => this._cancel.Token;

    /// <summary>Starts showing the progress; the dialog appears if the operation is not complete soon.</summary>
    public void Start(IWin32Window? owner)
    {
        this._owner = owner;
        this._showTimer.Start();
        this._updateTimer.Start();
    }

    /// <summary>Closes the dialog.</summary>
    public void Complete()
    {
        this._completed = true;
        this._showTimer.Stop();
        this._updateTimer.Stop();
        if (this.Visible)
            this.Hide();
        this.Dispose();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!this._completed && e.CloseReason == CloseReason.UserClosing) {
            e.Cancel = true;
            this.Cancel();
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            this._showTimer.Dispose();
            this._updateTimer.Dispose();
            this._cancel.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Cancel()
    {
        if (this._cancel.IsCancellationRequested)
            return;
        this._cancel.Cancel();
        this._cancelButton.Enabled = false;
        this._label.Text = "Cancelling...";
    }

    private void UpdateProgress()
    {
        if (this._cancel.IsCancellationRequested)
            return;

        var total = this._progress.Total;
        var done = this._progress.Done;
        if (total < 0) {
            this._progressBar.Style = ProgressBarStyle.Marquee;
            this._label.Text = this._progress.Status;
            return;
        }

        this._progressBar.Style = ProgressBarStyle.Continuous;
        this._progressBar.Maximum = Math.Max(1, total);
        this._progressBar.Value = Math.Clamp(done, 0, this._progressBar.Maximum);
        this._label.Text = $"{done:N0} of {total:N0}: {this._progress.Status}";
    }
}
