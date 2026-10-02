using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lumina.Data.Files;
using LuminaExplorer.Controls.FileResourceViewerControls.ScdPlayerControl;
using LuminaExplorer.Core.Audio;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window.FileViewers;

public class ScdPlayer : Form {
    private const int MinimumDefaultWidth = 480;
    private const int MinimumDefaultHeight = 280;

    private static readonly Guid ScdPlayerSaveToGuid = Guid.Parse("4c8f0f5e-2f7d-4a39-9d55-5f2b0c3c7a61");

    private readonly CancellationTokenSource _closeToken = new();
    private string _baseFileName = "sound";

    public ScdPlayer()
    {
        this.Text = "SCD Player";
        this.StartPosition = FormStartPosition.Manual;
        this.Controls.Add(
            this.Player = new() {
                Dock = DockStyle.Fill,
                AutoPlay = true,
            });
    }

    public ScdPlayerControl Player { get; }

    /// <summary>Shows the entries of the given SCD file and starts playing the first playable entry.</summary>
    public void SetFile(IVirtualFileSystem vfs, IVirtualFile file, ScdFile scdFile)
    {
        var fullPath = vfs.GetFullPath(file);
        this.Text = fullPath;
        this._baseFileName = Path.GetFileNameWithoutExtension(fullPath) is { Length: > 0 } name ? name : "sound";
        this.Player.SetFile(scdFile);
    }

    /// <summary>Shows the entries of the given SCD file and starts playing the first playable entry.</summary>
    public void SetFile(ScdFile scdFile)
    {
        var path = scdFile.FilePath?.Path ?? "";
        this.Text = path.Length > 0 ? path : "SCD Player";
        this._baseFileName = Path.GetFileNameWithoutExtension(path) is { Length: > 0 } name ? name : "sound";
        this.Player.SetFile(scdFile);
    }

    public void ShowRelativeTo(Control opener)
    {
        var rc = this.Player.GetViewportRectangleSuggestion(opener);
        var minimumSize = this.LogicalToDeviceUnits(new Size(MinimumDefaultWidth, MinimumDefaultHeight));
        if (rc.Width < minimumSize.Width) {
            rc.X -= (minimumSize.Width - rc.Width) / 2;
            rc.Width = minimumSize.Width;
        }

        if (rc.Height < minimumSize.Height) {
            rc.Y -= (minimumSize.Height - rc.Height) / 2;
            rc.Height = minimumSize.Height;
        }

        this.SetBounds(rc.X, rc.Y, rc.Width, rc.Height);
        this.Show();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData) {
            case Keys.Space:
                this.Player.TogglePlayPause();
                return true;
            case Keys.Enter:
                this.Player.PlaySelected();
                return true;
            case Keys.Up:
                this.Player.SelectRelative(-1);
                return true;
            case Keys.Down:
                this.Player.SelectRelative(1);
                return true;
            case Keys.Escape:
                this.Close();
                return true;
            case Keys.S | Keys.Control:
                this.SaveSelectedEntry();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this._closeToken.Cancel();
        this.Player.ClearFile();
        base.OnFormClosed(e);
    }

    private void SaveSelectedEntry()
    {
        if (this.Player.SelectedEntry is not { } entry)
            return;

        if (!entry.IsPlayable) {
            MessageBox.Show(
                this,
                $"Entry #{entry.Index} cannot be exported: {entry.UnsupportedReason}",
                this.Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var filters = new List<(string Filter, string Extension, bool Native)> {
            ("WAV, decoded 16-bit PCM (*.wav)|*.wav", ".wav", false),
        };
        switch (entry.Codec) {
            case ScdAudioCodec.OggVorbis:
                filters.Add(("Ogg Vorbis, original stream (*.ogg)|*.ogg", ".ogg", true));
                break;
            case ScdAudioCodec.MsAdpcm:
                filters.Add(("WAV, original MS-ADPCM stream (*.wav)|*.wav", ".wav", true));
                break;
        }

        using var sfd = new SaveFileDialog();
        sfd.ClientGuid = ScdPlayerSaveToGuid;
        sfd.Title = $"Save {this._baseFileName} #{entry.Index}";
        sfd.AddExtension = true;
        sfd.OverwritePrompt = true;
        sfd.Filter = string.Join('|', filters.ConvertAll(x => x.Filter));
        sfd.FilterIndex = 1;
        sfd.FileName = $"{this._baseFileName}_{entry.Index}.wav";
        if (sfd.ShowDialog(this) != DialogResult.OK)
            return;

        var fileName = sfd.FileName;
        var selected = filters[Math.Clamp(sfd.FilterIndex - 1, 0, filters.Count - 1)];
        if (!Path.HasExtension(fileName))
            fileName += selected.Extension;

        var token = this._closeToken.Token;
        Task.Run(
            () => {
                try {
                    using var f = File.Open(fileName, FileMode.Create, FileAccess.ReadWrite);
                    if (selected.Native)
                        entry.WriteNativeFile(f);
                    else
                        ScdAudioExport.WriteDecodedWav(entry, f, token);
                } catch (OperationCanceledException) {
                    // window closed
                } catch (Exception e) {
                    MessageBox.Show(
                        $"Failed to save.\n\n{e}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            },
            token);
    }
}
