using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;
using LuminaExplorer.Core.Util;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace LuminaExplorer.App.Window.FileViewers;

public class TabbedTextViewer : Form {
    private const int MinimumDefaultWidth = 320;
    private const int MinimumDefaultHeight = 240;

    private readonly TabbedTextViewerControl _viewerControl;
    private CancellationTokenSource _cancellationTokenSource = new();

    public TabbedTextViewer()
    {
        this.Controls.Add(
            this._viewerControl = new() {
                Dock = DockStyle.Fill,
            });
    }

    protected override void Dispose(bool disposing)
    {
        this._cancellationTokenSource.Cancel();
        base.Dispose(disposing);
    }

    public void ShowShader(ShcdFile shcdFile, Control? opener)
    {
        this._cancellationTokenSource.Cancel();
        var cts = this._cancellationTokenSource = new();
        this.Text = shcdFile.FilePath.Path;
        this._viewerControl.Clear();
        Task.Run(
                () => {
                    return this.DisassembleCsoData(shcdFile.ByteCode, out var d, out var e)
                        ? [d]
                        : new[] { e.ToString() };
                },
                cts.Token)
            .ContinueWith(
                r => {
                    if (cts.IsCancellationRequested || cts != this._cancellationTokenSource ||
                        !r.IsCompletedSuccessfully)
                        return;

                    this._viewerControl.SetTexts([shcdFile.FileHeader.ShaderType.ToString()], r.Result);
                    this.ShowWithParent(opener);
                },
                this._cancellationTokenSource.Token,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void ShowShader(ShpkFile shpkFile, Control? opener)
    {
        this._cancellationTokenSource.Cancel();
        var cts = this._cancellationTokenSource = new();
        this.Text = shpkFile.FilePath.Path;
        this._viewerControl.Clear();
        var showed = false;
        var context = TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(
            async () => {
                const int updateFrequency = 1000;
                var nextUpdate = Environment.TickCount64 + updateFrequency;
                var names = new List<string>();
                var disd = new List<string>();

                async Task UpdateResults()
                {
                    if (!names.Any())
                        return;

                    await Task.Factory.StartNew(
                        () => {
                            if (cts.IsCancellationRequested || cts != this._cancellationTokenSource)
                                return;

                            this._viewerControl.AppendTexts(names, disd);
                            if (!showed) {
                                this.ShowWithParent(opener);
                                showed = true;
                            }
                        },
                        this._cancellationTokenSource.Token,
                        TaskCreationOptions.None,
                        context);
                    nextUpdate = Environment.TickCount64 + updateFrequency;
                    names.Clear();
                    disd.Clear();
                }

                foreach (var (prefix, entries) in new[] {
                             ("VS", shpkFile.VertexShaderEntries),
                             ("PS", shpkFile.PixelShaderEntries),
                             ("HS", shpkFile.HullShaderEntries),
                             ("DS", shpkFile.DomainShaderEntries),
                             ("GS", shpkFile.GeometryShaderEntries),
                         }) {
                    for (var i = 0; i < entries.Length; i++) {
                        if (cts.IsCancellationRequested || cts != this._cancellationTokenSource)
                            break;

                        names.Add($"{prefix}#{i}");
                        disd.Add(
                            this.DisassembleCsoData(entries[i].ByteCode, out var d, out var e)
                                ? d
                                : e.ToString());

                        if (Environment.TickCount64 >= nextUpdate)
                            await UpdateResults();
                    }
                }

                await UpdateResults();
            },
            cts.Token);
    }

    public void ShowWithParent(Control? opener)
    {
        var rc = this._viewerControl.GetViewportRectangleSuggestion(opener);
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

    private unsafe bool DisassembleCsoData(
        ReadOnlySpan<byte> data,
        [MaybeNullWhen(false)] out string disassembled,
        [MaybeNullWhen(true)] out Exception exception)
    {
        using var blob = new ComPtr<ID3DBlob>();
        try {
            var i = 0;
            while (i + 3 < data.Length && !data[i..].StartsWith("DXBC"u8))
                i++;
            fixed (void* pData = data)
                DirectX.D3DDisassemble((byte*) pData + i, (nuint) (data.Length - i), 0, null, blob.GetAddressOf())
                    .Ensure();

            var slice = new Span<byte>(blob.Get()->GetBufferPointer(), checked((int) blob.Get()->GetBufferSize()));
            while (!slice.IsEmpty && slice[^1] == 0)
                slice = slice[..^1];
            disassembled = System.Text.Encoding.UTF8.GetString(slice);
            exception = null;
            return true;
        } catch (Exception e) {
            disassembled = null;
            exception = e;
            return false;
        }
    }
}
