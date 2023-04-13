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
        splMainSplitter = new SplitContainer();
        txtFileFilter = new TextBox();
        tvwFiles = new TreeView();
        lvwFiles = new ListView();
        menuStrip1.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splMainSplitter).BeginInit();
        splMainSplitter.Panel1.SuspendLayout();
        splMainSplitter.Panel2.SuspendLayout();
        splMainSplitter.SuspendLayout();
        SuspendLayout();
        // 
        // menuStrip1
        // 
        menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
        menuStrip1.Location = new Point(0, 0);
        menuStrip1.Name = "menuStrip1";
        menuStrip1.Size = new Size(800, 24);
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
        // splMainSplitter
        // 
        splMainSplitter.Dock = DockStyle.Fill;
        splMainSplitter.Location = new Point(0, 24);
        splMainSplitter.Name = "splMainSplitter";
        // 
        // splMainSplitter.Panel1
        // 
        splMainSplitter.Panel1.Controls.Add(txtFileFilter);
        splMainSplitter.Panel1.Controls.Add(tvwFiles);
        // 
        // splMainSplitter.Panel2
        // 
        splMainSplitter.Panel2.Controls.Add(lvwFiles);
        splMainSplitter.Size = new Size(800, 426);
        splMainSplitter.SplitterDistance = 266;
        splMainSplitter.TabIndex = 1;
        // 
        // txtFileFilter
        // 
        txtFileFilter.Dock = DockStyle.Bottom;
        txtFileFilter.Location = new Point(0, 403);
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
        tvwFiles.Size = new Size(260, 397);
        tvwFiles.TabIndex = 0;
        tvwFiles.BeforeExpand += tvwFiles_BeforeExpand;
        tvwFiles.AfterSelect += tvwFiles_AfterSelect;
        // 
        // lvwFiles
        // 
        lvwFiles.Dock = DockStyle.Fill;
        lvwFiles.Location = new Point(0, 0);
        lvwFiles.Name = "lvwFiles";
        lvwFiles.Size = new Size(530, 426);
        lvwFiles.TabIndex = 0;
        lvwFiles.UseCompatibleStateImageBehavior = false;
        lvwFiles.ItemDrag += lvwFiles_ItemDrag;
        lvwFiles.DragDrop += lvwFiles_DragDrop;
        lvwFiles.DoubleClick += lvwFiles_DoubleClick;
        // 
        // Explorer
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(800, 450);
        Controls.Add(splMainSplitter);
        Controls.Add(menuStrip1);
        MainMenuStrip = menuStrip1;
        Name = "Explorer";
        Text = "Form1";
        FormClosed += Explorer_FormClosed;
        menuStrip1.ResumeLayout(false);
        menuStrip1.PerformLayout();
        splMainSplitter.Panel1.ResumeLayout(false);
        splMainSplitter.Panel1.PerformLayout();
        splMainSplitter.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splMainSplitter).EndInit();
        splMainSplitter.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private MenuStrip menuStrip1;
    private ToolStripMenuItem fileToolStripMenuItem;
    private ToolStripMenuItem quitToolStripMenuItem;
    private SplitContainer splMainSplitter;
    private TextBox txtFileFilter;
    private TreeView tvwFiles;
    private ListView lvwFiles;
}
