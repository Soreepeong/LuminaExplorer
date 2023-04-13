namespace LuminaExplorer;

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
        menuStrip1 = new MenuStrip();
        fileToolStripMenuItem = new ToolStripMenuItem();
        quitToolStripMenuItem = new ToolStripMenuItem();
        splMain = new SplitContainer();
        txtFileFilter = new TextBox();
        tvwFiles = new TreeView();
        splSub = new SplitContainer();
        lvwFiles = new ListView();
        menuStrip1.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splMain).BeginInit();
        splMain.Panel1.SuspendLayout();
        splMain.Panel2.SuspendLayout();
        splMain.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splSub).BeginInit();
        splSub.Panel1.SuspendLayout();
        splSub.SuspendLayout();
        SuspendLayout();
        // 
        // menuStrip1
        // 
        menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
        menuStrip1.Location = new Point(0, 0);
        menuStrip1.Name = "menuStrip1";
        menuStrip1.Size = new Size(1264, 24);
        menuStrip1.TabIndex = 0;
        menuStrip1.Text = "menuStrip1";
        // 
        // fileToolStripMenuItem
        // 
        fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { quitToolStripMenuItem });
        fileToolStripMenuItem.Name = "fileToolStripMenuItem";
        fileToolStripMenuItem.Size = new Size(37, 20);
        fileToolStripMenuItem.Text = "&File";
        // 
        // quitToolStripMenuItem
        // 
        quitToolStripMenuItem.Name = "quitToolStripMenuItem";
        quitToolStripMenuItem.Size = new Size(97, 22);
        quitToolStripMenuItem.Text = "&Quit";
        // 
        // splMain
        // 
        splMain.Dock = DockStyle.Fill;
        splMain.FixedPanel = FixedPanel.Panel1;
        splMain.Location = new Point(0, 24);
        splMain.Name = "splMain";
        // 
        // splMain.Panel1
        // 
        splMain.Panel1.Controls.Add(txtFileFilter);
        splMain.Panel1.Controls.Add(tvwFiles);
        // 
        // splMain.Panel2
        // 
        splMain.Panel2.Controls.Add(splSub);
        splMain.Size = new Size(1264, 737);
        splMain.SplitterDistance = 266;
        splMain.TabIndex = 1;
        // 
        // txtFileFilter
        // 
        txtFileFilter.Dock = DockStyle.Bottom;
        txtFileFilter.Location = new Point(0, 714);
        txtFileFilter.Name = "txtFileFilter";
        txtFileFilter.PlaceholderText = "filter...";
        txtFileFilter.Size = new Size(266, 23);
        txtFileFilter.TabIndex = 1;
        // 
        // tvwFiles
        // 
        tvwFiles.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        tvwFiles.HideSelection = false;
        tvwFiles.Location = new Point(3, 3);
        tvwFiles.Name = "tvwFiles";
        tvwFiles.PathSeparator = "/";
        tvwFiles.Size = new Size(260, 708);
        tvwFiles.TabIndex = 0;
        tvwFiles.BeforeExpand += tvwFiles_BeforeExpand;
        tvwFiles.AfterSelect += tvwFiles_AfterSelect;
        // 
        // splSub
        // 
        splSub.Dock = DockStyle.Fill;
        splSub.FixedPanel = FixedPanel.Panel2;
        splSub.Location = new Point(0, 0);
        splSub.Name = "splSub";
        // 
        // splSub.Panel1
        // 
        splSub.Panel1.Controls.Add(lvwFiles);
        splSub.Size = new Size(994, 737);
        splSub.SplitterDistance = 670;
        splSub.TabIndex = 0;
        // 
        // lvwFiles
        // 
        lvwFiles.Dock = DockStyle.Fill;
        lvwFiles.Location = new Point(0, 0);
        lvwFiles.Name = "lvwFiles";
        lvwFiles.Size = new Size(670, 737);
        lvwFiles.TabIndex = 1;
        lvwFiles.UseCompatibleStateImageBehavior = false;
        lvwFiles.ItemDrag += lvwFiles_ItemDrag;
        lvwFiles.SelectedIndexChanged += lvwFiles_SelectedIndexChanged;
        lvwFiles.DoubleClick += lvwFiles_DoubleClick;
        // 
        // Explorer
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1264, 761);
        Controls.Add(splMain);
        Controls.Add(menuStrip1);
        MainMenuStrip = menuStrip1;
        Name = "Explorer";
        Text = "Form1";
        FormClosed += Explorer_FormClosed;
        menuStrip1.ResumeLayout(false);
        menuStrip1.PerformLayout();
        splMain.Panel1.ResumeLayout(false);
        splMain.Panel1.PerformLayout();
        splMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splMain).EndInit();
        splMain.ResumeLayout(false);
        splSub.Panel1.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splSub).EndInit();
        splSub.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
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
