using LuminaExplorer.App.Utils;
using LuminaExplorer.Core.LazySqPackTree;

namespace LuminaExplorer.App.Window; 

public partial class Explorer {
    private void Constructor_FileTree() {
        tvwFiles.ImageList = new();
        tvwFiles.ImageList.ColorDepth = ColorDepth.Depth32Bit;
        tvwFiles.ImageList.ImageSize = new(16, 16);
        using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 0, false)!)
            tvwFiles.ImageList.Images.Add(icon);
        using (var icon = UiUtils.ExtractPeIcon("shell32.dll", 4, false)!)
            tvwFiles.ImageList.Images.Add(icon);
        tvwFiles.Nodes.Add(new FolderTreeNode(_vsp));
        tvwFiles.Nodes[0].Expand();
        tvwFiles.SelectedNode = tvwFiles.Nodes[0];

        _vsp.FolderChanged += SqPackTree_FolderChanged_FileTree;
    }

    private void Dispose_FileTree() {
        _vsp.FolderChanged -= SqPackTree_FolderChanged_FileTree;
        
        tvwFiles.Nodes.Clear();
    }

    private void SqPackTree_FolderChanged_FileTree(VirtualFolder changedFolder, VirtualFolder[]? previousPathFromRoot) {
        if (previousPathFromRoot is null)
            return;

        if (tvwFiles.Nodes[0] is not FolderTreeNode node)
            return;

        foreach (var folder in previousPathFromRoot.Skip(1)) {
            if (node.TryFindChildNode(folder, out node) is not true)
                break;
        }

        if (node.Folder != changedFolder)
            return;

        node.Remove();
        // TODO: does above work, or node.Parent.Nodes.Remove must be used?

        if (tvwFiles.Nodes[0] is not FolderTreeNode newParentNode)
            return;

        foreach (var folder in _vsp.GetTreeFromRoot(changedFolder).Skip(1)) {
            if (folder == changedFolder.Parent) {
                newParentNode.Nodes.Add(node);
                break;
            }

            if (newParentNode.TryFindChildNode(folder, out newParentNode))
                break;
        }
    }

    private void tvwFiles_AfterExpand(object sender, TreeViewEventArgs e) {
        if (e.Node is FolderTreeNode ln)
            tvwFiles_PostProcessFolderTreeNodeExpansion(ln);
    }

    private void tvwFiles_AfterSelect(object sender, TreeViewEventArgs e) {
        if (e.Node is FolderTreeNode node)
            NavigateTo(node.Folder, true);
    }

    private void lvwFiles_MouseWheel(object? sender, MouseEventArgs e) {
        if (ModifierKeys == Keys.Control) {
            cboView.SelectedIndex = e.Delta switch {
                > 0 => Math.Max(0, cboView.SelectedIndex - 1),
                < 0 => Math.Min(cboView.Items.Count - 1, cboView.SelectedIndex + 1),
                _ => cboView.SelectedIndex
            };
        }
    }
    
    private Task<FolderTreeNode> ExpandTreeTo(params string[] pathComponents) =>
        ExpandTreeToImpl(
            (FolderTreeNode) tvwFiles.Nodes[0],
            Path.Join(pathComponents).Replace('\\', '/').TrimStart('/').Split('/'),
            0);

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
            return tvwFiles_PostProcessFolderTreeNodeExpansion(node).ContinueWith(_ => {
                var i = 0;
                for (; i < node.Nodes.Count; i++) {
                    if (node.Nodes[i] is FolderTreeNode subnode &&
                        string.Compare(subnode.Folder.Name, name, StringComparison.InvariantCultureIgnoreCase) == 0) {
                        return ExpandTreeToImpl(subnode, parts, partIndex + 1);
                    }
                }

                NavigateTo(node.Folder, true);
                return Task.FromResult(node);
            }, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
        }

        NavigateTo(node.Folder, true);
        return Task.FromResult(node);
    }

    private Task tvwFiles_PostProcessFolderTreeNodeExpansion(FolderTreeNode ln) {
        var resolvedFolder = _vsp.AsFoldersResolved(ln.Folder);

        if (ln.CallerMustPopulate()) {
            resolvedFolder = resolvedFolder
                .ContinueWith(f => {
                    ln.Nodes.Clear();
                    ln.Nodes.AddRange(_vsp.GetFolders(ln.Folder)
                        .OrderBy(x => x.Name.ToLowerInvariant())
                        .Select(x => (TreeNode) new FolderTreeNode(_vsp, x))
                        .ToArray());

                    return f.Result;
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        return resolvedFolder.ContinueWith(_ => {
            if (_vsp.GetKnownFolderCount(ln.Folder) == 1) {
                foreach (var n in ln.Nodes)
                    ((TreeNode) n).Expand();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private class FolderTreeNode : TreeNode {
        public readonly VirtualFolder Folder;

        private bool _populateTriggered;

        public FolderTreeNode(VirtualSqPackTree tree) : this(tree.RootFolder, @"(root)", true) { }

        public FolderTreeNode(VirtualSqPackTree tree, VirtualFolder folder)
            : this(folder, folder.Name.Trim('/'), !tree.WillFolderNeverHaveSubfolders(folder)) { }

        private FolderTreeNode(VirtualFolder folder, string displayName, bool mayHaveChildren) {
            Text = displayName;
            Folder = folder;
            SelectedImageIndex = ImageIndex = ImageListIndexFolder;
            if (mayHaveChildren)
                Nodes.Add(new TreeNode(@"Expanding..."));
        }

        public bool TryFindChildNode(VirtualFolder folder, out FolderTreeNode childNode) {
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
