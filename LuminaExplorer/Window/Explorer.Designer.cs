namespace LuminaExplorer.Window;

partial class Explorer {
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing) {
        if (disposing && (components != null)) {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent() {
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.quitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.splMain = new System.Windows.Forms.SplitContainer();
            this.txtFileFilter = new System.Windows.Forms.TextBox();
            this.tvwFiles = new System.Windows.Forms.TreeView();
            this.splSub = new System.Windows.Forms.SplitContainer();
            this.lvwFiles = new System.Windows.Forms.ListView();
            this.menuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splMain)).BeginInit();
            this.splMain.Panel1.SuspendLayout();
            this.splMain.Panel2.SuspendLayout();
            this.splMain.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splSub)).BeginInit();
            this.splSub.Panel1.SuspendLayout();
            this.splSub.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(1264, 24);
            this.menuStrip1.TabIndex = 0;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.quitToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem.Text = "&File";
            // 
            // quitToolStripMenuItem
            // 
            this.quitToolStripMenuItem.Name = "quitToolStripMenuItem";
            this.quitToolStripMenuItem.Size = new System.Drawing.Size(97, 22);
            this.quitToolStripMenuItem.Text = "&Quit";
            // 
            // splMain
            // 
            this.splMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splMain.Location = new System.Drawing.Point(0, 24);
            this.splMain.Name = "splMain";
            // 
            // splMain.Panel1
            // 
            this.splMain.Panel1.Controls.Add(this.txtFileFilter);
            this.splMain.Panel1.Controls.Add(this.tvwFiles);
            // 
            // splMain.Panel2
            // 
            this.splMain.Panel2.Controls.Add(this.splSub);
            this.splMain.Size = new System.Drawing.Size(1264, 737);
            this.splMain.SplitterDistance = 266;
            this.splMain.TabIndex = 1;
            // 
            // txtFileFilter
            // 
            this.txtFileFilter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.txtFileFilter.Location = new System.Drawing.Point(0, 714);
            this.txtFileFilter.Name = "txtFileFilter";
            this.txtFileFilter.PlaceholderText = "filter...";
            this.txtFileFilter.Size = new System.Drawing.Size(266, 23);
            this.txtFileFilter.TabIndex = 1;
            // 
            // tvwFiles
            // 
            this.tvwFiles.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tvwFiles.HideSelection = false;
            this.tvwFiles.Location = new System.Drawing.Point(3, 3);
            this.tvwFiles.Name = "tvwFiles";
            this.tvwFiles.PathSeparator = "/";
            this.tvwFiles.Size = new System.Drawing.Size(260, 708);
            this.tvwFiles.TabIndex = 0;
            this.tvwFiles.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.tvwFiles_BeforeExpand);
            this.tvwFiles.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.tvwFiles_AfterSelect);
            // 
            // splSub
            // 
            this.splSub.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splSub.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.splSub.Location = new System.Drawing.Point(0, 0);
            this.splSub.Name = "splSub";
            // 
            // splSub.Panel1
            // 
            this.splSub.Panel1.Controls.Add(this.lvwFiles);
            this.splSub.Size = new System.Drawing.Size(994, 737);
            this.splSub.SplitterDistance = 670;
            this.splSub.TabIndex = 0;
            // 
            // lvwFiles
            // 
            this.lvwFiles.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lvwFiles.Location = new System.Drawing.Point(0, 0);
            this.lvwFiles.Name = "lvwFiles";
            this.lvwFiles.Size = new System.Drawing.Size(670, 737);
            this.lvwFiles.TabIndex = 1;
            this.lvwFiles.UseCompatibleStateImageBehavior = false;
            this.lvwFiles.VirtualMode = true;
            this.lvwFiles.CacheVirtualItems += new System.Windows.Forms.CacheVirtualItemsEventHandler(this.lvwFiles_CacheVirtualItems);
            this.lvwFiles.ItemDrag += new System.Windows.Forms.ItemDragEventHandler(this.lvwFiles_ItemDrag);
            this.lvwFiles.RetrieveVirtualItem += new System.Windows.Forms.RetrieveVirtualItemEventHandler(this.lvwFiles_RetrieveVirtualItem);
            this.lvwFiles.SearchForVirtualItem += new System.Windows.Forms.SearchForVirtualItemEventHandler(this.lvwFiles_SearchForVirtualItem);
            this.lvwFiles.SelectedIndexChanged += new System.EventHandler(this.lvwFiles_SelectedIndexChanged);
            this.lvwFiles.DoubleClick += new System.EventHandler(this.lvwFiles_DoubleClick);
            // 
            // Explorer
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1264, 761);
            this.Controls.Add(this.splMain);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "Explorer";
            this.Text = "Form1";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.Explorer_FormClosed);
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.splMain.Panel1.ResumeLayout(false);
            this.splMain.Panel1.PerformLayout();
            this.splMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splMain)).EndInit();
            this.splMain.ResumeLayout(false);
            this.splSub.Panel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splSub)).EndInit();
            this.splSub.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

    }

    #endregion

    private MenuStrip menuStrip1;
    private ToolStripMenuItem fileToolStripMenuItem;
    private ToolStripMenuItem quitToolStripMenuItem;
    private SplitContainer splMain;
    private TextBox txtFileFilter;
    private TreeView tvwFiles;
    private SplitContainer splSub;
    private ListView lvwFiles;
}
