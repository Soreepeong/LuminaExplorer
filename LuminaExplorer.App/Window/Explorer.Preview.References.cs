using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.Core.ExcelSheets.Index;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    /// <summary>The "Referenced by" tab, listing the cells of sheets that refer to the selected file or folder.</summary>
    private sealed partial class PreviewHandler {
        private const int ReferenceDisplayLimit = 500;

        // Icons are referred to by their base path; their high resolution and language specific versions are the same.
        private static readonly Regex IconVariantRegex = new(
            @"^ui/icon/(\d{6})/(?:hq/|[a-z]{2}/)?(\d{6})(?:_hr1)?\.tex$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private TabPage _referencesTab = null!;
        private ListView _referencesList = null!;
        private LinkLabel _referencesStatus = null!;
        private CancellationTokenSource? _referencesCancel;
        private int _referencesItemWidth;

        private (string Path, bool IsFolder)? _referencesTarget;
        private (string Path, bool IsFolder)? _referencesShown;

        private void InitializeReferences()
        {
            this._referencesStatus = new() {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = this._explorer.LogicalToDeviceUnits(36),
                Padding = new(3),
                TextAlign = ContentAlignment.MiddleLeft,
                LinkArea = new(0, 0),
            };
            this._referencesStatus.LinkClicked += (_, _) => {
                this._explorer.BuildExcelValueIndex();
                this.UpdateExcelValueIndexStatus();
            };

            this._referencesList = new() {
                Dock = DockStyle.Fill,
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                ShowItemToolTips = true,
            };
            this._referencesList.Columns.Add("Cell");
            this._referencesList.Resize += (_, _) => this.FitReferencesColumn();
            this._referencesList.ItemActivate += (_, _) => this.OpenSelectedReference();

            this._referencesTab = new("Referenced by") {
                Name = "tabPreviewReferencedBy",
                Padding = new(3),
                UseVisualStyleBackColor = true,
            };
            this._referencesTab.Controls.Add(this._referencesList);
            this._referencesTab.Controls.Add(this._referencesStatus);
            this._explorer.tabPreview.TabPages.Add(this._referencesTab);
            this._explorer.tabPreview.SelectedIndexChanged += this.TabPreviewOnSelectedIndexChanged;
        }

        private void DisposeReferences()
        {
            this._referencesCancel?.Cancel();
            this._referencesCancel = null;
            this._explorer.tabPreview.SelectedIndexChanged -= this.TabPreviewOnSelectedIndexChanged;
        }

        private bool IsReferencesTabVisible => this._explorer.tabPreview.SelectedTab == this._referencesTab;

        /// <summary>Sets the file or folder to list the references to; they are looked up when the tab is shown.
        /// </summary>
        /// <param name="path">Full path; <c>null</c> if nothing is selected.</param>
        /// <param name="isFolder">Whether <paramref name="path"/> is a folder.</param>
        public void SetReferencesTarget(string? path, bool isFolder)
        {
            var target = path is null ? ((string, bool)?) null : (path.Trim('/'), isFolder);
            if (this._referencesTarget == target)
                return;

            this._referencesTarget = target;
            this._referencesCancel?.Cancel();
            this._referencesCancel = null;
            this._referencesShown = null;
            if (this.IsReferencesTabVisible)
                this.RefreshReferences();
        }

        /// <summary>Looks up the references again, now that the index is available.</summary>
        public void OnExcelValueIndexReady()
        {
            this._referencesShown = null;
            if (this.IsReferencesTabVisible)
                this.RefreshReferences();
        }

        /// <summary>Updates the shown state of the index, while it is not available.</summary>
        public void UpdateExcelValueIndexStatus()
        {
            if (this._explorer.ExcelValueIndex is null && this._referencesTarget is not null)
                this.ShowIndexStatus();
        }

        private void TabPreviewOnSelectedIndexChanged(object? sender, EventArgs e)
        {
            if (this.IsReferencesTabVisible && this._referencesShown != this._referencesTarget)
                this.RefreshReferences();
        }

        private void ShowIndexStatus()
        {
            var text = this._explorer.ExcelValueIndexStatusText;
            if (this._explorer.IsExcelValueIndexNotBuilt) {
                const string link = "Build index";
                this._referencesStatus.Text = $"{text} {link}";
                this._referencesStatus.LinkArea = new(text.Length + 1, link.Length);
            } else {
                this._referencesStatus.Text = text;
                this._referencesStatus.LinkArea = new(0, 0);
            }
        }

        private void SetStatus(string text)
        {
            this._referencesStatus.Text = text;
            this._referencesStatus.LinkArea = new(0, 0);
        }

        private void RefreshReferences()
        {
            this._referencesCancel?.Cancel();
            this._referencesCancel = null;
            this._referencesList.Items.Clear();
            this._referencesItemWidth = 0;

            if (this._referencesTarget is not { } target) {
                this._referencesShown = null;
                this.SetStatus("(nothing selected)");
                return;
            }

            if (this._explorer.ExcelValueIndex is not { } index) {
                this._referencesShown = null;
                this.ShowIndexStatus();
                return;
            }

            this._referencesShown = target;
            this.SetStatus("Looking up...");
            var cancel = this._referencesCancel = new();
            var explorer = this._explorer;
            Task.Run(
                    async () => {
                        var result = FindReferences(index, target.Path, target.IsFolder, cancel.Token);
                        if (result.Hits.Count > 0) {
                            await explorer.PrepareExcelColumnNames(
                                result.Hits.Select(x => x.SheetName).Distinct().ToArray(),
                                cancel.Token);
                        }

                        return result;
                    },
                    cancel.Token)
                .ContinueWith(
                    r => {
                        if (this._referencesCancel != cancel || explorer.IsDisposed)
                            return;

                        if (!r.IsCompletedSuccessfully) {
                            this.SetStatus($"Failed: {r.Exception?.InnerException?.Message}");
                            return;
                        }

                        try {
                            this.ShowReferences(target, r.Result);
                        } catch (Exception e) {
                            this.SetStatus($"Failed: {e.Message}");
                        }
                    },
                    cancel.Token,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Finds the references to a path, and to the other forms of the same icon.</summary>
        private static ExcelValueSearchResult FindReferences(
            ExcelValueIndex index,
            string path,
            bool isFolder,
            CancellationToken cancellationToken)
        {
            var paths = new List<string> { path };
            if (!isFolder && IconVariantRegex.Match(path) is { Success: true } m) {
                paths.Add($"ui/icon/{m.Groups[1].Value}/{m.Groups[2].Value}.tex");
                paths.Add($"ui/icon/{m.Groups[1].Value}/en/{m.Groups[2].Value}.tex");
            }

            var hits = new List<ExcelValueHit>();
            var seen = new HashSet<(string, uint, ushort, int)>();
            var total = 0;
            foreach (var p in paths.Distinct(StringComparer.OrdinalIgnoreCase)) {
                var result = index.FindReferences(p, isFolder, ReferenceDisplayLimit, cancellationToken);
                total += result.TotalCells;
                foreach (var hit in result.Hits) {
                    if (seen.Add((hit.SheetName, hit.RowId, hit.SubrowId, hit.ColumnIndex)))
                        hits.Add(hit);
                    else
                        total--;
                }
            }

            if (hits.Count > ReferenceDisplayLimit)
                hits.RemoveRange(ReferenceDisplayLimit, hits.Count - ReferenceDisplayLimit);
            return new(hits, total, false);
        }

        private void ShowReferences((string Path, bool IsFolder) target, ExcelValueSearchResult result)
        {
            var what = target.IsFolder ? "this folder or its contents" : "this file";
            this.SetStatus(
                result.Hits.Count == 0 ? $"No sheet cells refer to {what}."
                : result.Hits.Count < result.TotalCells
                    ? $"{result.TotalCells} cells refer to {what}; showing the first {result.Hits.Count}. " +
                    "Double-click to open."
                    : $"{result.TotalCells} cell(s) refer to {what}. Double-click to open.");

            var resolveColumnName = this._explorer.ExcelColumnNameResolver;
            var items = result.Hits
                .Select(
                    x => new ListViewItem(FileTreeHandler.SheetHitTreeNode.FormatHit(x, resolveColumnName, [])) {
                        Tag = x,
                        ToolTipText = x.DerivedFrom is { } source ? $"{source} → {x.Value}" : x.Value,
                    })
                .ToArray();

            // Wide enough for the longest item, so that it can be scrolled to.
            this._referencesItemWidth = items.Length == 0
                ? 0
                : items.Max(x => TextRenderer.MeasureText(x.Text, this._referencesList.Font).Width) +
                this._explorer.LogicalToDeviceUnits(8);

            this._referencesList.BeginUpdate();
            try {
                this._referencesList.Items.Clear();
                this._referencesList.Items.AddRange(items);
            } finally {
                this._referencesList.EndUpdate();
            }

            this.FitReferencesColumn();
        }

        private void FitReferencesColumn()
        {
            if (this._referencesList.Columns.Count != 0) {
                this._referencesList.Columns[0].Width =
                    Math.Max(this._referencesList.ClientSize.Width, this._referencesItemWidth);
            }
        }

        private void OpenSelectedReference()
        {
            if (this._referencesList.SelectedItems.Count == 0 ||
                this._referencesList.SelectedItems[0].Tag is not ExcelValueHit hit)
                return;

            this._explorer.OpenSheetCell(hit.SheetName, hit.RowId, hit.SubrowId, hit.ColumnIndex);
        }

        /// <summary>Gets the items shown in the tab, for testing.</summary>
        public IReadOnlyList<string> GetShownReferences()
        {
            var result = new List<string> { this._referencesStatus.Text };
            for (var i = 0; i < this._referencesList.Items.Count; i++)
                result.Add(this._referencesList.Items[i]?.Text ?? "(null)");
            return result;
        }
    }
}
