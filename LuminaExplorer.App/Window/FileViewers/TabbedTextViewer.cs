using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Controls.FileResourceViewerControls;
using LuminaExplorer.Core.ExtraFormats.FileResourceImplementors.ShaderFiles;

namespace LuminaExplorer.App.Window.FileViewers;

public class TabbedTextViewer : Form {
    private const int MinimumDefaultWidth = 320;
    private const int MinimumDefaultHeight = 240;

    private readonly TabbedTextViewerControl _viewerControl;
    private CancellationTokenSource _cancellationTokenSource = new();

    public TabbedTextViewer() {
        Controls.Add(_viewerControl = new() {
            Dock = DockStyle.Fill,
        });
    }

    protected override void Dispose(bool disposing) {
        _cancellationTokenSource.Cancel();
        base.Dispose(disposing);
    }

    public void ShowShader(ShcdFile shcdFile, Control? opener) {
        _cancellationTokenSource.Cancel();
        var cts = _cancellationTokenSource = new();
        Text = shcdFile.FilePath.Path;
        _viewerControl.Clear();
        Task.Run(
                () => {
                    return DisassembleCsoData(shcdFile.ByteCode, out var d, out var e)
                        ? new[] {d}
                        : new[] {e.ToString()};
                }, cts.Token)
            .ContinueWith(
                r => {
                    if (cts.IsCancellationRequested || cts != _cancellationTokenSource || !r.IsCompletedSuccessfully)
                        return;

                    _viewerControl.SetTexts(new[] {shcdFile.FileHeader.ShaderType.ToString()}, r.Result);
                    ShowWithParent(opener);
                }, _cancellationTokenSource.Token, TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void ShowShader(ShpkFile shpkFile, Control? opener) {
        _cancellationTokenSource.Cancel();
        var cts = _cancellationTokenSource = new();
        Text = shpkFile.FilePath.Path;
        _viewerControl.Clear();
        var showed = false;
        var context = TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(async () => {
            const int updateFrequency = 1000;
            var nextUpdate = Environment.TickCount64 + updateFrequency;
            var names = new List<string>();
            var disd = new List<string>();

            async Task UpdateResults() {
                if (!names.Any())
                    return;

                await Task.Factory.StartNew(() => {
                    if (cts.IsCancellationRequested || cts != _cancellationTokenSource)
                        return;

                    _viewerControl.AppendTexts(names, disd);
                    if (!showed) {
                        ShowWithParent(opener);
                        showed = true;
                    }
                }, _cancellationTokenSource.Token, TaskCreationOptions.None, context);
                nextUpdate = Environment.TickCount64 + updateFrequency;
                names.Clear();
                disd.Clear();
            }

            for (var i = 0; i < shpkFile.ShaderEntries.Length; i++) {
                if (cts.IsCancellationRequested || cts != _cancellationTokenSource)
                    break;

                names.Add(i < shpkFile.Header.NumVertexShaders
                    ? $"VS#{i}"
                    : $"PS#{i - shpkFile.Header.NumVertexShaders}");
                disd.Add(DisassembleCsoData(shpkFile.ShaderEntries[i].ByteCode, out var d, out var e)
                    ? d
                    : e.ToString());

                if (Environment.TickCount64 >= nextUpdate)
                    await UpdateResults();
            }

            await UpdateResults();
        }, cts.Token);
    }

    public void ShowWithParent(Control? opener) {
        var rc = _viewerControl.GetViewportRectangleSuggestion(opener);
        if (rc.Width < MinimumDefaultWidth) {
            rc.X -= (MinimumDefaultWidth - rc.Width) / 2;
            rc.Width = MinimumDefaultWidth;
        }

        if (rc.Height < MinimumDefaultHeight) {
            rc.X -= (MinimumDefaultHeight - rc.Height) / 2;
            rc.Height = MinimumDefaultHeight;
        }

        SetBounds(rc.X, rc.Y, rc.Width, rc.Height);
        Show();
    }

    private unsafe bool DisassembleCsoData(
        ReadOnlySpan<byte> data,
        [MaybeNullWhen(false)] out string disassembled,
        [MaybeNullWhen(true)] out Exception exception) {
        var api = Silk.NET.Direct3D.Compilers.D3DCompiler.GetApi();
        Silk.NET.Core.Native.ID3D10Blob* pBlob = null;
        try {
            var i = 0;
            while (i + 3 < data.Length && (
                       data[i] != 'D' ||
                       data[i + 1] != 'X' ||
                       data[i + 2] != 'B' ||
                       data[i + 3] != 'C'))
                i++;
            fixed (void* pData = data)
                Marshal.ThrowExceptionForHR(
                    api.Disassemble((byte*) pData + i, (nuint) (data.Length - i), 0, (byte*) null, &pBlob));

            var slice = pBlob->Buffer;
            while (!slice.IsEmpty && slice[^1] == 0)
                slice = slice[..^1];
            disassembled = System.Text.Encoding.UTF8.GetString(slice);
            exception = null;
            return true;
        } catch (Exception e) {
            disassembled = null;
            exception = e;
            return false;
        } finally {
            if (pBlob is not null)
                pBlob->Release();
        }
    }
}
