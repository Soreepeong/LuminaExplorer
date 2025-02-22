using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.VirtualFileSystem;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class FileTreeHandler : IDisposable {
        private readonly Explorer _explorer;
        private readonly TreeView _treeView;

        private IVirtualFileSystem? _vfs;

        public FileTreeHandler(Explorer explorer)
        {
            this._explorer = explorer;
            this._treeView = explorer.tvwFiles;
            this._treeView.ImageList = new();
            this._treeView.ImageList.ColorDepth = ColorDepth.Depth32Bit;
            this._treeView.ImageList.ImageSize = new(16, 16);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, false)!)
                this._treeView.ImageList.Images.Add(icon);
            this._treeView.AfterExpand += this.AfterExpand;
            this._treeView.AfterSelect += this.AfterSelect;

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
            this.Vfs = null;

            this._treeView.AfterExpand -= this.AfterExpand;
            this._treeView.AfterSelect -= this.AfterSelect;
        }

        public IVirtualFileSystem? Vfs {
            get => this._vfs;
            set {
                if (this._vfs == value)
                    return;

                if (this._vfs is not null) {
                    this._vfs.FolderChanged -= this.IVirtualFolderChanged;
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

            if (this._treeView.Nodes[0] is not FolderTreeNode node)
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

            if (this._treeView.Nodes[0] is not FolderTreeNode newParentNode)
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
            if (e.Node is FolderTreeNode ln) this._treeView_PostProcessFolderTreeNodeExpansion(ln);
        }

        private void AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (e.Node is FolderTreeNode node) this._explorer._navigationHandler?.NavigateTo(node.Folder, true);
        }

        public Task<FolderTreeNode> ExpandTreeTo(params string[] pathComponents)
        {
            if (this._vfs is null)
                throw new InvalidOperationException();
            return this.ExpandTreeToImpl(
                (FolderTreeNode) this._treeView.Nodes[0],
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
                                    .Select(x => (TreeNode) new FolderTreeNode(tree2, x))
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

        public class FolderTreeNode : TreeNode {
            public readonly IVirtualFolder Folder;

            private bool _populateTriggered;

            public FolderTreeNode(IVirtualFileSystem tree) : this(tree.RootFolder, @"(root)", true)
            { }

            public FolderTreeNode(IVirtualFileSystem tree, IVirtualFolder folder)
                : this(folder, folder.Name.Trim('/'), !tree.HasNoSubfolder(folder))
            { }

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
