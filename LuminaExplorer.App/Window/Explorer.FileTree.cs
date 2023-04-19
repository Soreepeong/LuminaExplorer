using System.Diagnostics.CodeAnalysis;
using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.App.Window;

public partial class Explorer {
    private sealed class FileTreeHandler : IDisposable {
        private readonly Explorer _explorer;
        private readonly TreeView _treeView;

        private VirtualSqPackTree? _tree;

        public FileTreeHandler(Explorer explorer) {
            _explorer = explorer;
            _treeView = explorer.tvwFiles;
            _treeView.ImageList = new();
            _treeView.ImageList.ColorDepth = ColorDepth.Depth32Bit;
            _treeView.ImageList.ImageSize = new(16, 16);
            using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, false)!)
                _treeView.ImageList.Images.Add(icon);
            _treeView.AfterExpand += AfterExpand;
            _treeView.AfterSelect += AfterSelect;

            _tree = _explorer._tree;
            if (_tree is not null) {
                _treeView.Nodes.Add(new FolderTreeNode(_tree));
                _treeView.Nodes[0].Expand();
                _treeView.SelectedNode = _treeView.Nodes[0];

                _tree.FolderChanged += VirtualFolderChanged;
            }
        }

        public void Dispose() {
            Tree = null;

            _treeView.AfterExpand -= AfterExpand;
            _treeView.AfterSelect -= AfterSelect;
        }

        public VirtualSqPackTree? Tree {
            get => _tree;
            set {
                if (_tree == value)
                    return;

                if (_tree is not null) {
                    _tree.FolderChanged -= VirtualFolderChanged;
                    _treeView.Nodes.Clear();
                }

                _tree = value;

                if (_tree is not null) {
                    _treeView.Nodes.Add(new FolderTreeNode(_tree));
                    _treeView.Nodes[0].Expand();
                    _treeView.SelectedNode = _treeView.Nodes[0];

                    _tree.FolderChanged += VirtualFolderChanged;
                }
            }
        }

        private void VirtualFolderChanged(VirtualFolder changedFolder,
            VirtualFolder[]? previousPathFromRoot) {
            if (_tree is not { } tree)
                return;

            if (previousPathFromRoot is null)
                return;

            if (_treeView.Nodes[0] is not FolderTreeNode node)
                return;

            foreach (var folder in previousPathFromRoot.Skip(1)) {
                if (node.TryFindChildNode(folder, out var node2) is not true)
                    return;
                node = node2;
            }

            if (node.Folder != changedFolder)
                return;

            node.Remove();
            // TODO: does above work, or node.Parent.Nodes.Remove must be used?

            if (_treeView.Nodes[0] is not FolderTreeNode newParentNode)
                return;

            foreach (var folder in tree.GetTreeFromRoot(changedFolder).Skip(1)) {
                if (folder == changedFolder.Parent) {
                    newParentNode.Nodes.Add(node);
                    break;
                }

                if (!newParentNode.TryFindChildNode(folder, out var parent2))
                    return;
                newParentNode = parent2;
            }
        }

        private void AfterExpand(object? sender, TreeViewEventArgs e) {
            if (e.Node is FolderTreeNode ln)
                _treeView_PostProcessFolderTreeNodeExpansion(ln);
        }

        private void AfterSelect(object? sender, TreeViewEventArgs e) {
            if (e.Node is FolderTreeNode node)
                _explorer._navigationHandler?.NavigateTo(node.Folder, true);
        }

        public Task<FolderTreeNode> ExpandTreeTo(params string[] pathComponents) {
            if (_tree is null)
                throw new InvalidOperationException();
            return ExpandTreeToImpl(
                (FolderTreeNode) _treeView.Nodes[0],
                Path.Join(pathComponents).Replace('\\', '/').TrimStart('/').Split('/'),
                0);
        }

        private Task<FolderTreeNode> ExpandTreeToImpl(FolderTreeNode node, string[] parts, int partIndex) {
            for (; partIndex < parts.Length; partIndex++) {
                var name = parts[partIndex] + "/";
                if (name == "./")
                    continue;

                if (name == VirtualFolder.UpFolderKey) {
                    node = node.Parent as FolderTreeNode ?? node;
                    continue;
                }

                node.Expand();
                return _treeView_PostProcessFolderTreeNodeExpansion(node)
                    .ContinueWith(_ => {
                        var i = 0;
                        for (; i < node.Nodes.Count; i++) {
                            if (node.Nodes[i] is FolderTreeNode subnode &&
                                string.Compare(subnode.Folder.Name, name, StringComparison.InvariantCultureIgnoreCase)
                                ==
                                0) {
                                return ExpandTreeToImpl(subnode, parts, partIndex + 1);
                            }
                        }

                        _explorer._navigationHandler?.NavigateTo(node.Folder, true);
                        return Task.FromResult(node);
                    }, default,
                    TaskContinuationOptions.DenyChildAttach,
                    TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
            }

            _explorer._navigationHandler?.NavigateTo(node.Folder, true);
            return Task.FromResult(node);
        }

        private Task _treeView_PostProcessFolderTreeNodeExpansion(FolderTreeNode ln) {
            if (_tree is not { } tree)
                return Task.CompletedTask;
            var resolvedFolder = tree.AsFoldersResolved(ln.Folder);

            if (ln.CallerMustPopulate()) {
                resolvedFolder = resolvedFolder
                    .ContinueWith(f => {
                            if (_tree is not { } tree2)
                                return f.Result;
                            ln.Nodes.Clear();
                            ln.Nodes.AddRange(tree2.GetFolders(ln.Folder)
                                .OrderBy(x => x.Name.ToLowerInvariant())
                                .Select(x => (TreeNode) new FolderTreeNode(tree2, x))
                                .ToArray());

                            return f.Result;
                        }, default,
                        TaskContinuationOptions.DenyChildAttach,
                        TaskScheduler.FromCurrentSynchronizationContext());
            }

            return resolvedFolder
                .ContinueWith(_ => {
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
            public readonly VirtualFolder Folder;

            private bool _populateTriggered;

            public FolderTreeNode(VirtualSqPackTree tree) : this(tree.RootFolder, @"(root)", true) { }

            public FolderTreeNode(VirtualSqPackTree tree, VirtualFolder folder)
                : this(folder, folder.Name.Trim('/'), !tree.WillFolderNeverHaveSubfolders(folder)) { }

            private FolderTreeNode(VirtualFolder folder, string displayName, bool mayHaveChildren) {
                Text = displayName;
                Folder = folder;
                SelectedImageIndex = ImageIndex = 0;
                if (mayHaveChildren)
                    Nodes.Add(new TreeNode(@"Expanding..."));
            }

            public bool TryFindChildNode(VirtualFolder folder, [MaybeNullWhen(false)] out FolderTreeNode childNode) {
                foreach (var node in Nodes) {
                    if (node is not FolderTreeNode n)
                        continue;

                    if (n.Folder == folder) {
                        childNode = n;
                        return true;
                    }
                }

                childNode = null!;
                return false;
            }

            public bool CallerMustPopulate() {
                if (_populateTriggered)
                    return false;

                _populateTriggered = true;
                return true;
            }
        }
    }
}
