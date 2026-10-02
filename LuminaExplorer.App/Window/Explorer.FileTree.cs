using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.ExcelSheets;
using LuminaExplorer.Core.ExcelSheets.Index;
using LuminaExplorer.Core.GameDataNames;
using LuminaExplorer.Core.VirtualFileSystem;
using LuminaExplorer.Core.VirtualFileSystem.Sqpack;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class FileTreeHandler : IDisposable {
        // Matches are collected up to the search limit, ranked, and then truncated to the display limit.
        private const int FilterSearchLimit = 20000;
        private const int FilterDisplayLimit = 1000;

        // Parent chains are expanded only if there are not too many matches to look through.
        private const int FilterAutoExpandLimit = 100;

        // Cells of Excel sheets that mention the filter text.
        private const int SheetHitDisplayLimit = 500;
        private const int SheetHitAutoExpandLimit = 20;
        private const int SheetHitSnippetLength = 120;

        private readonly Explorer _explorer;
        private readonly TreeView _treeView;
        private readonly FileIconBadge _fileIconBadge;
        private readonly ContextMenuStrip _contextMenu;

        private IVirtualFileSystem? _vfs;

        // The regular tree, set aside while the tree shows filter results.
        private FolderTreeNode? _unfilteredRoot;
        private string? _filterText;
        private CancellationTokenSource? _filterCancel;

        // Shows the progress of the sheet index while the filter results are shown without it.
        private TreeNode? _sheetIndexStatusNode;

        // Whether the current filter was searched in the sheet index; if not, it is searched again once available.
        private bool _filterSearchedSheets;

        public FileTreeHandler(Explorer explorer)
        {
            this._explorer = explorer;
            this._treeView = explorer.tvwFiles;
            this._treeView.ImageList = new();
            this._treeView.ImageList.ColorDepth = ColorDepth.Depth32Bit;
            this._treeView.ImageList.ImageSize = this._treeView.LogicalToDeviceUnits(new Size(16, 16));
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, false)!)
                this._treeView.ImageList.Images.Add(icon);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, false)!)
                this._treeView.ImageList.Images.Add(icon);
            var fileImage = this._treeView.ImageList.Images[1];
            this._fileIconBadge = new(_ => fileImage);
            this._treeView.AfterExpand += this.AfterExpand;
            this._treeView.AfterSelect += this.AfterSelect;
            this._treeView.NodeMouseClick += this.NodeMouseClick;
            this._treeView.KeyDown += this.TreeViewKeyDown;
            this._treeView.ShowNodeToolTips = true;
            this._contextMenu = new();
            this._contextMenu.Opening += this.ContextMenuOpening;
            this._treeView.ContextMenuStrip = this._contextMenu;

            this._vfs = this._explorer._vfs;
            if (this._vfs is not null) {
                this._treeView.Nodes.Add(new FolderTreeNode(this._vfs));
                this._treeView.Nodes[0].Expand();
                this._treeView.SelectedNode = this._treeView.Nodes[0];

                this._vfs.FolderChanged += this.IVirtualFolderChanged;
            }
        }

        public void Dispose()
        {
            this._filterCancel?.Cancel();
            this.Vfs = null;
            this._fileIconBadge.Dispose();

            this._treeView.AfterExpand -= this.AfterExpand;
            this._treeView.AfterSelect -= this.AfterSelect;
            this._treeView.NodeMouseClick -= this.NodeMouseClick;
            this._treeView.KeyDown -= this.TreeViewKeyDown;
            this._treeView.ContextMenuStrip = null;
            this._contextMenu.Opening -= this.ContextMenuOpening;
            this._contextMenu.Dispose();
        }

        public IVirtualFileSystem? Vfs {
            get => this._vfs;
            set {
                if (this._vfs == value)
                    return;

                if (this._vfs is not null) {
                    this._vfs.FolderChanged -= this.IVirtualFolderChanged;
                    this._filterCancel?.Cancel();
                    this._filterText = null;
                    this._unfilteredRoot = null;
                    this._treeView.Nodes.Clear();
                }

                this._vfs = value;

                if (this._vfs is not null) {
                    this._treeView.Nodes.Add(new FolderTreeNode(this._vfs));
                    this._treeView.Nodes[0].Expand();
                    this._treeView.SelectedNode = this._treeView.Nodes[0];

                    this._vfs.FolderChanged += this.IVirtualFolderChanged;
                }
            }
        }

        private void IVirtualFolderChanged(
            IVirtualFolder changedFolder,
            IVirtualFolder[]? previousPathFromRoot)
        {
            if (this._vfs is not { } tree)
                return;

            if (previousPathFromRoot is null)
                return;

            if ((this._unfilteredRoot ?? this._treeView.Nodes[0]) is not FolderTreeNode node)
                return;

            foreach (var folder in previousPathFromRoot.Skip(1)) {
                if (node.TryFindChildNode(folder, out var node2) is not true)
                    return;
                node = node2;
            }

            if (!Equals(node.Folder, changedFolder))
                return;

            node.Remove();
            // TODO: does above work, or node.Parent.Nodes.Remove must be used?

            if ((this._unfilteredRoot ?? this._treeView.Nodes[0]) is not FolderTreeNode newParentNode)
                return;

            foreach (var folder in tree.GetTreeFromRoot(changedFolder).Skip(1)) {
                if (Equals(folder, changedFolder.Parent)) {
                    newParentNode.Nodes.Add(node);
                    break;
                }

                if (!newParentNode.TryFindChildNode(folder, out var parent2))
                    return;
                newParentNode = parent2;
            }
        }

        private void AfterExpand(object? sender, TreeViewEventArgs e)
        {
            switch (e.Node) {
                case FolderTreeNode ln:
                    this._treeView_PostProcessFolderTreeNodeExpansion(ln);
                    break;
                case FilterTreeNode { IsFolder: true, IsMatch: true } fn when fn.CallerMustPopulate():
                    this.PopulateMatchedFolder(fn);
                    break;
            }
        }

        /// <summary>Fills a folder that matched the filter with all of its contents.</summary>
        private void PopulateMatchedFolder(FilterTreeNode node)
        {
            if (this._vfs is not { } tree)
                return;

            tree.LocateFolder(tree.RootFolder, node.Path)
                .ContinueWith(
                    async r => {
                        if (r.Result is not { } folder)
                            return null;
                        await tree.AsFoldersResolved(folder);
                        await tree.AsFileNamesResolved(folder);
                        return folder;
                    },
                    TaskScheduler.Default)
                .Unwrap()
                .ContinueWith(
                    r => {
                        if (this._vfs != tree || node.TreeView is null)
                            return;

                        this._treeView.BeginUpdate();
                        try {
                            node.Nodes.Clear();
                            if (!r.IsCompletedSuccessfully || r.Result is not { } folder) {
                                node.Nodes.Add(new TreeNode("(failed to open)") { ForeColor = SystemColors.GrayText });
                                return;
                            }

                            node.Nodes.AddRange(
                                tree.GetFolders(folder)
                                    .Where(x => !Equals(x, folder.Parent))
                                    .OrderBy(x => x.Name.ToLowerInvariant())
                                    .Select(x => (TreeNode) new FolderTreeNode(tree, x, this._explorer))
                                    .Concat(
                                        tree.GetFiles(folder)
                                            .OrderBy(x => x.Name.ToLowerInvariant())
                                            .Select(
                                                x => {
                                                    var fileNode = new FilterTreeNode(node.Path + x.Name, false);
                                                    fileNode.UpdateText(this._explorer);
                                                    this.SetFileIcon(fileNode);
                                                    fileNode.ForeColor = Color.Empty;
                                                    return (TreeNode) fileNode;
                                                }))
                                    .ToArray());
                        } finally {
                            this._treeView.EndUpdate();
                        }
                    },
                    default,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void AfterSelect(object? sender, TreeViewEventArgs e)
        {
            switch (e.Node) {
                case FolderTreeNode node:
                    this._explorer._navigationHandler?.NavigateTo(node.Folder, true);
                    break;
                case FilterTreeNode { Path: { } path } node when this._explorer._navigationHandler is { } nav: {
                    var folderPath = node.IsFolder ? path : path[..(path.LastIndexOf('/') + 1)];
                    if (!node.IsFolder)
                        this._explorer._fileListHandler?.SelectFileAfterLoad(path[(path.LastIndexOf('/') + 1)..]);
                    _ = nav.NavigateTo(folderPath);
                    break;
                }
            }
        }

        // Cells found in sheets open a window, so they are opened on click or Enter rather than on selection.
        private void NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Node is SheetHitTreeNode node &&
                (this._treeView.HitTest(e.Location).Location & TreeViewHitTestLocations.PlusMinus) == 0)
                this.OpenSheetHit(node.Hit);
        }

        private void TreeViewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || this._treeView.SelectedNode is not SheetHitTreeNode node)
                return;

            e.Handled = e.SuppressKeyPress = true;
            this.OpenSheetHit(node.Hit);
        }

        private void ContextMenuOpening(object? sender, CancelEventArgs e)
        {
            ClearContextMenu(this._contextMenu);

            // The node under the cursor if opened with the mouse; the selected node otherwise.
            var pt = this._treeView.PointToClient(Cursor.Position);
            var node = this._treeView.ClientRectangle.Contains(pt)
                ? this._treeView.GetNodeAt(pt)
                : this._treeView.SelectedNode;
            if (this.CreateNodeTarget(node) is not { } target) {
                e.Cancel = true;
                return;
            }

            this._explorer.PopulateFileContextMenu(this._contextMenu.Items, target);

            // Opening is raised already cancelled if the menu had no items.
            e.Cancel = false;
        }

        /// <summary>Creates the target of file operations from a folder or a file node.</summary>
        private FileOperationTarget? CreateNodeTarget(TreeNode? node)
        {
            if (this._vfs is not { } tree)
                return null;

            switch (node) {
                case FolderTreeNode { IsRoot: false } folderNode:
                    return FileOperationTarget.FromItems(tree, [], [folderNode.Folder], null);

                case FilterTreeNode { IsFolder: true, Path: var path }: {
                    var name = path.TrimEnd('/')[(path.TrimEnd('/').LastIndexOf('/') + 1)..];
                    return new() {
                        FileCount = 0,
                        FolderCount = 1,
                        GetPaths = () => [path.TrimStart('/')],
                        GetNames = () => [name],
                        Resolve = async _ => (
                            [],
                            [
                                await tree.LocateFolder(tree.RootFolder, path)
                                ?? throw new DirectoryNotFoundException($"\"{path}\" could not be found."),
                            ]),
                    };
                }

                case FilterTreeNode { IsFolder: false, Path: var path }: {
                    var name = path[(path.LastIndexOf('/') + 1)..];
                    return new() {
                        FileCount = 1,
                        FolderCount = 0,
                        SingleFileName = name,
                        GetPaths = () => [path.TrimStart('/')],
                        GetNames = () => [name],
                        Resolve = async _ => ([await LocateFile(tree, path)], []),
                        Open = async () => {
                            try {
                                this._explorer.OpenFileInViewer(await LocateFile(tree, path));
                            } catch (Exception ex) {
                                MessageBox.Show(
                                    $"Failed to open \"{path}\".\n\n{ex.Message}",
                                    "Error",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                            }
                        },
                    };
                }

                default:
                    return null;
            }
        }

        private static async Task<IVirtualFile> LocateFile(IVirtualFileSystem tree, string path)
        {
            if (tree is SqpackFileSystem sqfs)
                await sqfs.SuggestFullPathAsync(path.TrimStart('/'));
            return await tree.LocateFile(tree.RootFolder, path)
                ?? throw new FileNotFoundException($"\"{path}\" could not be found.");
        }

        /// <summary>Opens the sheet of a found cell, scrolled to its row.</summary>
        private void OpenSheetHit(ExcelValueHit hit) =>
            this._explorer.OpenSheetCell(hit.SheetName, hit.RowId, hit.SubrowId, hit.ColumnIndex);

        /// <summary>Whether the tree is showing the results of a name filter.</summary>
        public bool IsFiltering => this._filterText is not null;

        /// <summary>
        /// Shows only the folders and files whose names contain all of the space-separated terms, along with their
        /// parent folders; or the regular tree if <paramref name="filterText"/> is null.
        /// </summary>
        public void SetFilter(string? filterText)
        {
            filterText = string.IsNullOrWhiteSpace(filterText) ? null : filterText.Trim();
            if (filterText == this._filterText)
                return;

            this._filterCancel?.Cancel();
            this._filterCancel = null;
            this._filterText = filterText;

            if (filterText is null) {
                this.RestoreUnfilteredTree();
                return;
            }

            if (this._vfs is not SqpackFileSystem sqfs)
                return;

            var cancel = this._filterCancel = new();
            var terms = filterText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var folderNames = this._explorer.FolderNames;

            this._explorer.LoadExcelValueIndex(true);
            var valueIndex = this._explorer.ExcelValueIndex;
            this._filterSearchedSheets = valueIndex is not null;

            // Wait for the typing to settle before searching.
            Task.Delay(200, cancel.Token)
                .ContinueWith(
                    _ => {
                        var results = sqfs.HashDatabase.FindPathsByName(terms, FilterSearchLimit, cancel.Token);
                        var truncated = results.Count >= FilterSearchLimit;

                        // Folders matched by the names of the models in them come first.
                        var byModelName = new List<string>();
                        var matchingNames = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
                        if (folderNames is not null) {
                            var seen = results.ToHashSet(StringComparer.OrdinalIgnoreCase);
                            foreach (var (path, names) in folderNames.FindFolders(terms)) {
                                matchingNames["/" + path] = names;
                                if (seen.Add(path))
                                    byModelName.Add(path);
                            }
                        }

                        var ranked = byModelName
                            .Concat(results.OrderBy(x => RankFilterMatch(x, terms)))
                            .ToList();
                        if (ranked.Count > FilterDisplayLimit) {
                            ranked.RemoveRange(FilterDisplayLimit, ranked.Count - FilterDisplayLimit);
                            truncated = true;
                        }

                        // Cells of sheets that mention the filter text, such as the file name in a BGM sheet.
                        var sheetHits = valueIndex?.Search(
                            terms,
                            SheetHitDisplayLimit,
                            cancellationToken: cancel.Token);

                        // Column names come from EXDSchema; look up the few sheets involved before showing hits.
                        if (sheetHits is { Hits.Count: > 0 }) {
                            this._explorer.PrepareExcelColumnNames(
                                    sheetHits.Hits.Select(x => x.SheetName).Distinct().ToArray(),
                                    cancel.Token)
                                .Wait(cancel.Token);
                        }

                        return (
                            Paths: ranked,
                            Truncated: truncated,
                            MatchingNames: matchingNames,
                            SheetHits: sheetHits);
                    },
                    cancel.Token,
                    TaskContinuationOptions.OnlyOnRanToCompletion,
                    TaskScheduler.Default)
                .ContinueWith(
                    r => {
                        if (r.IsCompletedSuccessfully && this._filterCancel == cancel) {
                            this.ShowFilterResults(
                                r.Result.Paths,
                                r.Result.Truncated,
                                r.Result.MatchingNames,
                                r.Result.SheetHits,
                                terms);
                        }
                    },
                    cancel.Token,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        public void FocusFilterResults()
        {
            if (!this.IsFiltering)
                return;

            this._treeView.Focus();
            this._treeView.SelectedNode ??= this.FindFirstMatch(this._treeView.Nodes);
        }

        private FilterTreeNode? FindFirstMatch(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes) {
                if (n is FilterTreeNode { IsMatch: true } match)
                    return match;
                if (this.FindFirstMatch(n.Nodes) is { } found)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Ranks a match: names equal to a term first, then names starting with a term, then shorter names.
        /// </summary>
        private static (int Kind, int Length, string Path) RankFilterMatch(string path, string[] terms)
        {
            var trimmed = path.TrimEnd('/');
            var name = trimmed[(trimmed.LastIndexOf('/') + 1)..];
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
            var kind = 2;
            foreach (var t in terms) {
                if (name.Equals(t, StringComparison.OrdinalIgnoreCase) ||
                    nameWithoutExtension.Equals(t, StringComparison.OrdinalIgnoreCase)) {
                    kind = 0;
                    break;
                }

                if (name.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                    kind = 1;
            }

            return (kind, name.Length, path);
        }

        private void ShowFilterResults(
            List<string> paths,
            bool truncated,
            Dictionary<string, IReadOnlyList<string>> matchingNames,
            ExcelValueSearchResult? sheetHits,
            string[] terms)
        {
            this._treeView.BeginUpdate();
            try {
                if (this._unfilteredRoot is null && this._treeView.Nodes.Count > 0)
                    this._unfilteredRoot = this._treeView.Nodes[0] as FolderTreeNode;
                this._treeView.Nodes.Clear();
                this._sheetIndexStatusNode = null;

                // Parents sort before their children, so a folder that matched is known before anything in it.
                var nodes = new Dictionary<string, FilterTreeNode>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase)) {
                    var isFolder = path.EndsWith('/');
                    var parts = path.TrimEnd('/').Split('/');
                    TreeNodeCollection parentNodes = this._treeView.Nodes;
                    var prefix = "/";
                    for (var i = 0; i < parts.Length; i++) {
                        var isLast = i == parts.Length - 1;
                        var isFolderPart = !isLast || isFolder;
                        prefix += parts[i] + (isFolderPart ? "/" : "");
                        if (!nodes.TryGetValue(prefix, out var node)) {
                            node = new(prefix, isFolderPart) { MatchingNames = matchingNames.GetValueOrDefault(prefix) };
                            node.UpdateText(this._explorer);
                            this.SetFileIcon(node);
                            nodes.Add(prefix, node);
                            parentNodes.Add(node);
                        }

                        // Everything in a folder that matched is shown when it is expanded.
                        if (node is { IsMatch: true, IsFolder: true })
                            break;

                        if (isLast)
                            node.MarkAsMatch();
                        parentNodes = node.Nodes;
                    }
                }

                if (paths.Count == 0)
                    this._treeView.Nodes.Add(new TreeNode("(no matching files)") { ForeColor = SystemColors.GrayText });
                else if (truncated)
                    this._treeView.Nodes.Insert(
                        0,
                        new TreeNode($"(showing the best {paths.Count} matches; type more to narrow down)") {
                            ForeColor = SystemColors.GrayText,
                        });

                // Expand the parent chains only; expanding a folder that matched would load all of its contents.
                if (paths.Count <= FilterAutoExpandLimit)
                    ExpandParentChains(this._treeView.Nodes);

                this.AddSheetHits(sheetHits, terms, paths.Count == 0);

                if (this._treeView.Nodes.Count > 0)
                    this._treeView.Nodes[0].EnsureVisible();
            } finally {
                this._treeView.EndUpdate();
            }
        }

        /// <summary>Adds the group of the cells in sheets that mention the filter text, at the top.</summary>
        private void AddSheetHits(ExcelValueSearchResult? sheetHits, string[] terms, bool noFileMatches)
        {
            if (sheetHits is null) {
                this._sheetIndexStatusNode = new(this._explorer.ExcelValueIndexStatusText) {
                    ForeColor = SystemColors.GrayText,
                };
                this._treeView.Nodes.Insert(0, this._sheetIndexStatusNode);
                return;
            }

            if (sheetHits.Hits.Count == 0) {
                this._treeView.Nodes.Insert(
                    0,
                    new TreeNode("(not mentioned in sheets)") { ForeColor = SystemColors.GrayText });
                return;
            }

            var total = $"{sheetHits.TotalCells}{(sheetHits.TotalIsLowerBound ? "+" : "")}";
            var group = new TreeNode($"Mentioned in sheets ({total})") {
                ToolTipText = "Cells of Excel sheets containing the filter text; click one to open the sheet.",
            };
            if (sheetHits.Hits.Count < sheetHits.TotalCells || sheetHits.TotalIsLowerBound) {
                group.Nodes.Add(
                    new TreeNode($"(showing the first {sheetHits.Hits.Count} of {total}; type more to narrow down)") {
                        ForeColor = SystemColors.GrayText,
                    });
            }

            var resolveColumnName = this._explorer.ExcelColumnNameResolver;
            var iconKey = this.GetFileIconKey("_" + ExcelSheetFileNames.HeaderExtension) ?? "";
            group.Nodes.AddRange(
                sheetHits.Hits
                    .Select(
                        x => (TreeNode) new SheetHitTreeNode(x, terms, resolveColumnName) {
                            ImageKey = iconKey,
                            SelectedImageKey = iconKey,
                        })
                    .ToArray());
            this._treeView.Nodes.Insert(0, group);

            if (noFileMatches || sheetHits.Hits.Count <= SheetHitAutoExpandLimit)
                group.Expand();
        }

        /// <summary>Updates the shown progress of the sheet index.</summary>
        public void UpdateExcelValueIndexStatus()
        {
            if (this._sheetIndexStatusNode is { TreeView: not null } node)
                node.Text = this._explorer.ExcelValueIndexStatusText;
        }

        /// <summary>Searches the sheets again, now that the index is available.</summary>
        public void OnExcelValueIndexReady()
        {
            if (this._filterText is not { } filterText || this._filterSearchedSheets)
                return;

            this._filterText = null;
            this.SetFilter(filterText);
        }

        /// <summary>Shows the extension of a file on its icon.</summary>
        private void SetFileIcon(FilterTreeNode node)
        {
            if (!node.IsFolder && this.GetFileIconKey(node.Path) is { } key)
                node.ImageKey = node.SelectedImageKey = key;
        }

        /// <summary>Gets the key of the file icon with the extension of the given path on it.</summary>
        private string? GetFileIconKey(string path)
        {
            if (FileIconBadge.GetBadgeText(path) is not { } badgeText)
                return null;

            var key = "ext:" + badgeText;
            var imageList = this._treeView.ImageList!;
            if (!imageList.Images.ContainsKey(key) &&
                this._fileIconBadge.Get(path, imageList.ImageSize.Width) is { } badgedIcon)
                imageList.Images.Add(key, badgedIcon);

            return key;
        }

        private static void ExpandParentChains(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes) {
                if (n is FilterTreeNode { IsMatch: false } && n.Nodes.Count > 0) {
                    n.Expand();
                    ExpandParentChains(n.Nodes);
                }
            }
        }

        private void RestoreUnfilteredTree()
        {
            if (this._unfilteredRoot is not { } root)
                return;

            this._unfilteredRoot = null;
            this._treeView.BeginUpdate();
            try {
                this._treeView.Nodes.Clear();
                this._treeView.Nodes.Add(root);
            } finally {
                this._treeView.EndUpdate();
            }

            if (this._explorer._navigationHandler?.CurrentFolder is { } currentFolder && this._vfs is { } tree)
                _ = this.ExpandTreeTo(tree.GetFullPath(currentFolder));
        }

        /// <summary>Updates the folder texts after the model names have been loaded.</summary>
        public void RefreshFolderDisplayNames()
        {
            if (this._vfs is not { } tree)
                return;

            this._treeView.BeginUpdate();
            try {
                if (this._unfilteredRoot is { } root)
                    Refresh(root);
                foreach (TreeNode n in this._treeView.Nodes)
                    Refresh(n);
            } finally {
                this._treeView.EndUpdate();
            }

            // Searching includes model names, which are now available.
            if (this._filterText is { } filterText) {
                this._filterText = null;
                this.SetFilter(filterText);
            }

            return;

            void Refresh(TreeNode node)
            {
                switch (node) {
                    case FolderTreeNode { IsRoot: false } f:
                        f.UpdateText(this._explorer, tree);
                        break;
                    case FilterTreeNode f:
                        f.UpdateText(this._explorer);
                        break;
                }

                foreach (TreeNode child in node.Nodes)
                    Refresh(child);
            }
        }

        public Task<FolderTreeNode> ExpandTreeTo(params string[] pathComponents)
        {
            if (this._vfs is null)
                throw new InvalidOperationException();
            return this.ExpandTreeToImpl(
                this._unfilteredRoot ?? (FolderTreeNode) this._treeView.Nodes[0],
                Path.Join(pathComponents).Replace('\\', '/').TrimStart('/').Split('/'),
                0);
        }

        private Task<FolderTreeNode> ExpandTreeToImpl(FolderTreeNode node, string[] parts, int partIndex)
        {
            for (; partIndex < parts.Length; partIndex++) {
                var name = parts[partIndex] + "/";
                if (name == "./")
                    continue;

                if (name == "../") {
                    node = node.Parent as FolderTreeNode ?? node;
                    continue;
                }

                node.Expand();
                return this._treeView_PostProcessFolderTreeNodeExpansion(node)
                    .ContinueWith(
                        _ => {
                            var i = 0;
                            for (; i < node.Nodes.Count; i++) {
                                if (node.Nodes[i] is FolderTreeNode subnode &&
                                    string.Compare(
                                        subnode.Folder.Name,
                                        name,
                                        StringComparison.InvariantCultureIgnoreCase)
                                    ==
                                    0) {
                                    return this.ExpandTreeToImpl(subnode, parts, partIndex + 1);
                                }
                            }

                            this._explorer._navigationHandler?.NavigateTo(node.Folder, true);
                            return Task.FromResult(node);
                        },
                        default,
                        TaskContinuationOptions.DenyChildAttach,
                        TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
            }

            this._explorer._navigationHandler?.NavigateTo(node.Folder, true);
            return Task.FromResult(node);
        }

        private Task _treeView_PostProcessFolderTreeNodeExpansion(FolderTreeNode ln)
        {
            if (this._vfs is not { } tree)
                return Task.CompletedTask;
            var resolvedFolder = tree.AsFoldersResolved(ln.Folder);

            if (ln.CallerMustPopulate()) {
                resolvedFolder = resolvedFolder
                    .ContinueWith(
                        f => {
                            if (this._vfs is not { } tree2)
                                return f.Result;
                            ln.Nodes.Clear();
                            ln.Nodes.AddRange(
                                tree2.GetFolders(ln.Folder)
                                    .OrderBy(x => x.Name.ToLowerInvariant())
                                    .Select(x => (TreeNode) new FolderTreeNode(tree2, x, this._explorer))
                                    .ToArray());

                            return f.Result;
                        },
                        default,
                        TaskContinuationOptions.DenyChildAttach,
                        TaskScheduler.FromCurrentSynchronizationContext());
            }

            return resolvedFolder
                .ContinueWith(
                    _ => {
                        if (tree.GetKnownFolderCount(ln.Folder) == 1) {
                            foreach (var n in ln.Nodes)
                                ((TreeNode) n).Expand();
                        }
                    },
                    default,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>A folder or a file in the results of a name filter.</summary>
        public sealed class FilterTreeNode : TreeNode {
            public FilterTreeNode(string path, bool isFolder)
            {
                this.Path = path;
                this.IsFolder = isFolder;
                this.SelectedImageIndex = this.ImageIndex = isFolder ? 0 : 1;
                this.ForeColor = SystemColors.GrayText;
            }

            /// <summary>Full path, starting with a slash; folders end with a slash.</summary>
            public string Path { get; }

            public bool IsFolder { get; }

            /// <summary>Names of the models in this folder that matched the filter, if matched by model names.</summary>
            public IReadOnlyList<string>? MatchingNames { get; init; }

            /// <summary>Whether this node matched the filter, as opposed to being a parent of a match.</summary>
            public bool IsMatch { get; private set; }

            private bool _populateTriggered;

            public void MarkAsMatch()
            {
                this.IsMatch = true;
                this.ForeColor = Color.Empty;

                if (this.IsFolder) {
                    // Matches inside are replaced by the full contents, loaded when expanded.
                    this.Nodes.Clear();
                    this.Nodes.Add(new TreeNode(@"Expanding..."));
                }
            }

            public bool CallerMustPopulate()
            {
                if (this._populateTriggered)
                    return false;

                this._populateTriggered = true;
                return true;
            }

            public void UpdateText(Explorer explorer)
            {
                var name = this.Path.TrimEnd('/');
                name = name[(name.LastIndexOf('/') + 1)..];
                if (this.IsFolder) {
                    if (explorer.TryGetFolderNames(this.Path, out var names)) {
                        this.Text = explorer.GetFolderDisplayName(name, this.Path);
                        this.ToolTipText = string.Join("\n", names);
                    } else if (this.MatchingNames is { Count: > 0 } matchingNames) {
                        this.Text = $"{name} ({CharaFolderNames.FormatShort(matchingNames)})";
                        this.ToolTipText = string.Join("\n", matchingNames);
                    } else {
                        this.Text = name;
                        this.ToolTipText = "";
                    }
                } else {
                    this.Text = name;
                }
            }
        }

        /// <summary>A cell of an Excel sheet that contains the filter text.</summary>
        public sealed class SheetHitTreeNode : TreeNode {
            public SheetHitTreeNode(
                ExcelValueHit hit,
                IReadOnlyList<string> terms,
                Func<string, int, string?>? resolveColumnName)
            {
                this.Hit = hit;
                this.Text = FormatHit(hit, resolveColumnName, terms);
                this.ToolTipText = hit.DerivedFrom is { } source
                    ? $"{source} → {hit.Value}\n(game path derived from the value of the cell)"
                    : hit.Value.Length > 1000 ? hit.Value[..1000] + "…" : hit.Value;
            }

            public ExcelValueHit Hit { get; }

            /// <summary>Formats a cell as <c>Sheet #row · Field: value</c>, or
            /// <c>Sheet #row · Field: value → path</c> for paths derived from numbers.</summary>
            public static string FormatHit(
                ExcelValueHit hit,
                Func<string, int, string?>? resolveColumnName,
                IReadOnlyList<string> terms)
            {
                var row = hit.SheetHasSubrows ? $"{hit.RowId}.{hit.SubrowId}" : $"{hit.RowId}";
                var column = resolveColumnName?.Invoke(hit.SheetName, hit.ColumnIndex) is { Length: > 0 } name
                    ? name
                    : $"column {hit.ColumnIndex}";
                return hit.DerivedFrom is { } source
                    ? $"{hit.SheetName} #{row} · {column}: {source} → {hit.Value}"
                    : $"{hit.SheetName} #{row} · {column}: {MakeSnippet(hit.Value, terms)}";
            }

            /// <summary>Shortens a long value to the part around the first term found in it.</summary>
            private static string MakeSnippet(string value, IReadOnlyList<string> terms)
            {
                if (value.Length <= SheetHitSnippetLength)
                    return value;

                var found = terms
                    .Select(t => value.IndexOf(t, StringComparison.OrdinalIgnoreCase))
                    .Where(x => x >= 0)
                    .DefaultIfEmpty(0)
                    .Min();
                var start = Math.Max(
                    0,
                    Math.Min(found - SheetHitSnippetLength / 3, value.Length - SheetHitSnippetLength));
                return (start > 0 ? "…" : "") +
                    value.Substring(start, SheetHitSnippetLength) +
                    (start + SheetHitSnippetLength < value.Length ? "…" : "");
            }
        }

        public class FolderTreeNode : TreeNode {
            public readonly IVirtualFolder Folder;
            public readonly bool IsRoot;

            private bool _populateTriggered;

            public FolderTreeNode(IVirtualFileSystem tree) : this(tree.RootFolder, @"(root)", true)
            {
                this.IsRoot = true;
            }

            public FolderTreeNode(IVirtualFileSystem tree, IVirtualFolder folder, Explorer explorer)
                : this(folder, folder.Name.Trim('/'), !tree.HasNoSubfolder(folder))
            {
                this.UpdateText(explorer, tree);
            }

            public void UpdateText(Explorer explorer, IVirtualFileSystem tree)
            {
                var fullPath = tree.GetFullPath(this.Folder);
                this.Text = explorer.GetFolderDisplayName(this.Folder.Name.Trim('/'), fullPath);
                this.ToolTipText = explorer.TryGetFolderNames(fullPath, out var names) ? string.Join("\n", names) : "";
            }

            private FolderTreeNode(IVirtualFolder folder, string displayName, bool mayHaveChildren)
            {
                this.Text = displayName;
                this.Folder = folder;
                this.SelectedImageIndex = this.ImageIndex = 0;
                if (mayHaveChildren) this.Nodes.Add(new TreeNode(@"Expanding..."));
            }

            public bool TryFindChildNode(IVirtualFolder folder, [MaybeNullWhen(false)] out FolderTreeNode childNode)
            {
                foreach (var node in this.Nodes) {
                    if (node is not FolderTreeNode n)
                        continue;

                    if (Equals(n.Folder, folder)) {
                        childNode = n;
                        return true;
                    }
                }

                childNode = null!;
                return false;
            }

            public bool CallerMustPopulate()
            {
                if (this._populateTriggered)
                    return false;

                this._populateTriggered = true;
                return true;
            }
        }
    }
}
