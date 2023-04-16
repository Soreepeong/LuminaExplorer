﻿namespace LuminaExplorer.App.Window;

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
        System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Explorer));
        menuStrip1 = new MenuStrip();
        fileToolStripMenuItem = new ToolStripMenuItem();
        quitToolStripMenuItem = new ToolStripMenuItem();
        splMain = new SplitContainer();
        tvwFiles = new TreeView();
        splSub = new SplitContainer();
        lvwFiles = new Controls.CoreVirtualObjectListView();
        colFilesName = new BrightIdeasSoftware.OLVColumn();
        colFilesPackType = new BrightIdeasSoftware.OLVColumn();
        colFilesHash1 = new BrightIdeasSoftware.OLVColumn();
        colFilesHash2 = new BrightIdeasSoftware.OLVColumn();
        tspNavigation = new ToolStrip();
        btnNavBack = new ToolStripButton();
        btnNavForward = new ToolStripButton();
        btnsHistory = new ToolStripDropDownButton();
        btnNavUp = new ToolStripButton();
        txtPath = new Controls.ToolStripSpringComboBox();
        btnSearch = new ToolStripButton();
        txtSearch = new ToolStripTextBox();
        toolStripContainer1 = new ToolStripContainer();
        tspActions = new ToolStrip();
        cboView = new ToolStripComboBox();
        splPreview = new SplitContainer();
        picPreview = new PictureBox();
        menuStrip1.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splMain).BeginInit();
        splMain.Panel1.SuspendLayout();
        splMain.Panel2.SuspendLayout();
        splMain.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splSub).BeginInit();
        splSub.Panel1.SuspendLayout();
        splSub.Panel2.SuspendLayout();
        splSub.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)lvwFiles).BeginInit();
        tspNavigation.SuspendLayout();
        toolStripContainer1.ContentPanel.SuspendLayout();
        toolStripContainer1.TopToolStripPanel.SuspendLayout();
        toolStripContainer1.SuspendLayout();
        tspActions.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splPreview).BeginInit();
        splPreview.Panel2.SuspendLayout();
        splPreview.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)picPreview).BeginInit();
        SuspendLayout();
        // 
        // menuStrip1
        // 
        menuStrip1.Dock = DockStyle.None;
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
        splMain.Location = new Point(0, 0);
        splMain.Name = "splMain";
        // 
        // splMain.Panel1
        // 
        splMain.Panel1.Controls.Add(tvwFiles);
        // 
        // splMain.Panel2
        // 
        splMain.Panel2.Controls.Add(splSub);
        splMain.Size = new Size(1264, 687);
        splMain.SplitterDistance = 266;
        splMain.TabIndex = 1;
        splMain.TabStop = false;
        // 
        // tvwFiles
        // 
        tvwFiles.Dock = DockStyle.Fill;
        tvwFiles.HideSelection = false;
        tvwFiles.Location = new Point(0, 0);
        tvwFiles.Name = "tvwFiles";
        tvwFiles.PathSeparator = "/";
        tvwFiles.Size = new Size(266, 687);
        tvwFiles.TabIndex = 1;
        tvwFiles.BeforeExpand += tvwFiles_BeforeExpand;
        tvwFiles.AfterExpand += tvwFiles_AfterExpand;
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
        // 
        // splSub.Panel2
        // 
        splSub.Panel2.Controls.Add(splPreview);
        splSub.Size = new Size(994, 687);
        splSub.SplitterDistance = 670;
        splSub.TabIndex = 0;
        splSub.TabStop = false;
        // 
        // lvwFiles
        // 
        lvwFiles.Columns.AddRange(new ColumnHeader[] { colFilesName, colFilesPackType, colFilesHash1, colFilesHash2 });
        lvwFiles.Dock = DockStyle.Fill;
        lvwFiles.FullRowSelect = true;
        lvwFiles.Location = new Point(0, 0);
        lvwFiles.Name = "lvwFiles";
        lvwFiles.OwnerDraw = false;
        lvwFiles.ShowGroups = false;
        lvwFiles.Size = new Size(670, 687);
        lvwFiles.TabIndex = 0;
        lvwFiles.UseExplorerTheme = true;
        lvwFiles.UseTranslucentSelection = true;
        lvwFiles.View = View.Details;
        lvwFiles.VirtualMode = true;
        lvwFiles.ItemDrag += lvwFiles_ItemDrag;
        lvwFiles.SelectedIndexChanged += lvwFiles_SelectedIndexChanged;
        lvwFiles.DoubleClick += lvwFiles_DoubleClick;
        lvwFiles.KeyPress += lvwFiles_KeyPress;
        lvwFiles.KeyUp += lvwFiles_KeyUp;
        lvwFiles.MouseUp += lvwFiles_MouseUp;
        // 
        // colFilesName
        // 
        colFilesName.AspectName = "Name";
        colFilesName.HeaderCheckBox = true;
        colFilesName.ImageAspectName = "Image";
        colFilesName.IsEditable = false;
        colFilesName.Text = "Name";
        colFilesName.Width = 240;
        // 
        // colFilesPackType
        // 
        colFilesPackType.AspectName = "PackTypeString";
        colFilesPackType.IsEditable = false;
        colFilesPackType.Text = "Pack Type";
        colFilesPackType.Width = 80;
        // 
        // colFilesHash1
        // 
        colFilesHash1.AspectName = "Hash1";
        colFilesHash1.IsEditable = false;
        colFilesHash1.Text = "Hash 1";
        colFilesHash1.Width = 80;
        // 
        // colFilesHash2
        // 
        colFilesHash2.AspectName = "Hash2";
        colFilesHash2.IsEditable = false;
        colFilesHash2.Text = "Hash 2";
        colFilesHash2.Width = 80;
        // 
        // tspNavigation
        // 
        tspNavigation.AutoSize = false;
        tspNavigation.Dock = DockStyle.None;
        tspNavigation.GripStyle = ToolStripGripStyle.Hidden;
        tspNavigation.Items.AddRange(new ToolStripItem[] { btnNavBack, btnNavForward, btnsHistory, btnNavUp, txtPath, btnSearch, txtSearch });
        tspNavigation.Location = new Point(0, 24);
        tspNavigation.Name = "tspNavigation";
        tspNavigation.Size = new Size(1264, 25);
        tspNavigation.Stretch = true;
        tspNavigation.TabIndex = 2;
        tspNavigation.Text = "tspNavigation";
        // 
        // btnNavBack
        // 
        btnNavBack.DisplayStyle = ToolStripItemDisplayStyle.Image;
        btnNavBack.Image = (Image)resources.GetObject("btnNavBack.Image");
        btnNavBack.ImageTransparentColor = Color.Magenta;
        btnNavBack.Name = "btnNavBack";
        btnNavBack.Size = new Size(23, 22);
        btnNavBack.Text = "toolStripButton1";
        btnNavBack.Click += btnNavBack_Click;
        // 
        // btnNavForward
        // 
        btnNavForward.DisplayStyle = ToolStripItemDisplayStyle.Image;
        btnNavForward.Image = (Image)resources.GetObject("btnNavForward.Image");
        btnNavForward.ImageTransparentColor = Color.Magenta;
        btnNavForward.Name = "btnNavForward";
        btnNavForward.Size = new Size(23, 22);
        btnNavForward.Text = "toolStripButton3";
        btnNavForward.Click += btnNavForward_Click;
        // 
        // btnsHistory
        // 
        btnsHistory.DisplayStyle = ToolStripItemDisplayStyle.Image;
        btnsHistory.ImageTransparentColor = Color.Magenta;
        btnsHistory.Name = "btnsHistory";
        btnsHistory.Size = new Size(13, 22);
        btnsHistory.Text = "toolStripDropDownButton1";
        btnsHistory.DropDownOpening += btnsHistory_DropDownOpening;
        btnsHistory.DropDownItemClicked += btnsHistory_DropDownItemClicked;
        // 
        // btnNavUp
        // 
        btnNavUp.DisplayStyle = ToolStripItemDisplayStyle.Image;
        btnNavUp.Image = (Image)resources.GetObject("btnNavUp.Image");
        btnNavUp.ImageTransparentColor = Color.Magenta;
        btnNavUp.Name = "btnNavUp";
        btnNavUp.Size = new Size(23, 22);
        btnNavUp.Text = "toolStripButton2";
        btnNavUp.Click += btnNavUp_Click;
        // 
        // txtPath
        // 
        txtPath.FlatStyle = FlatStyle.System;
        txtPath.Name = "txtPath";
        txtPath.Size = new Size(963, 25);
        txtPath.KeyDown += txtPath_KeyDown;
        txtPath.KeyUp += txtPath_KeyUp;
        // 
        // btnSearch
        // 
        btnSearch.Alignment = ToolStripItemAlignment.Right;
        btnSearch.DisplayStyle = ToolStripItemDisplayStyle.Image;
        btnSearch.Image = (Image)resources.GetObject("btnSearch.Image");
        btnSearch.ImageTransparentColor = Color.Magenta;
        btnSearch.Name = "btnSearch";
        btnSearch.Size = new Size(23, 22);
        btnSearch.Text = "toolStripButton1";
        // 
        // txtSearch
        // 
        txtSearch.Alignment = ToolStripItemAlignment.Right;
        txtSearch.Name = "txtSearch";
        txtSearch.Size = new Size(160, 25);
        // 
        // toolStripContainer1
        // 
        // 
        // toolStripContainer1.ContentPanel
        // 
        toolStripContainer1.ContentPanel.AutoScroll = true;
        toolStripContainer1.ContentPanel.Controls.Add(splMain);
        toolStripContainer1.ContentPanel.Size = new Size(1264, 687);
        toolStripContainer1.Dock = DockStyle.Fill;
        toolStripContainer1.Location = new Point(0, 0);
        toolStripContainer1.Name = "toolStripContainer1";
        toolStripContainer1.Size = new Size(1264, 761);
        toolStripContainer1.TabIndex = 3;
        toolStripContainer1.Text = "toolStripContainer1";
        // 
        // toolStripContainer1.TopToolStripPanel
        // 
        toolStripContainer1.TopToolStripPanel.Controls.Add(menuStrip1);
        toolStripContainer1.TopToolStripPanel.Controls.Add(tspNavigation);
        toolStripContainer1.TopToolStripPanel.Controls.Add(tspActions);
        // 
        // tspActions
        // 
        tspActions.Dock = DockStyle.None;
        tspActions.GripStyle = ToolStripGripStyle.Hidden;
        tspActions.Items.AddRange(new ToolStripItem[] { cboView });
        tspActions.Location = new Point(0, 49);
        tspActions.Name = "tspActions";
        tspActions.Size = new Size(1264, 25);
        tspActions.Stretch = true;
        tspActions.TabIndex = 3;
        // 
        // cboView
        // 
        cboView.Alignment = ToolStripItemAlignment.Right;
        cboView.DropDownStyle = ComboBoxStyle.DropDownList;
        cboView.FlatStyle = FlatStyle.System;
        cboView.Items.AddRange(new object[] { "Extra large icons", "Large icons", "Medium icons", "Small icons", "List", "Details" });
        cboView.Name = "cboView";
        cboView.Size = new Size(160, 25);
        cboView.SelectedIndexChanged += cboView_SelectedIndexChanged;
        // 
        // splPreview
        // 
        splPreview.Dock = DockStyle.Fill;
        splPreview.Location = new Point(0, 0);
        splPreview.Name = "splPreview";
        splPreview.Orientation = Orientation.Horizontal;
        // 
        // splPreview.Panel2
        // 
        splPreview.Panel2.Controls.Add(picPreview);
        splPreview.Size = new Size(320, 687);
        splPreview.SplitterDistance = 486;
        splPreview.TabIndex = 0;
        // 
        // picPreview
        // 
        picPreview.Dock = DockStyle.Fill;
        picPreview.Location = new Point(0, 0);
        picPreview.Name = "picPreview";
        picPreview.Size = new Size(320, 197);
        picPreview.TabIndex = 0;
        picPreview.TabStop = false;
        // 
        // Explorer
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1264, 761);
        Controls.Add(toolStripContainer1);
        KeyPreview = true;
        MainMenuStrip = menuStrip1;
        Name = "Explorer";
        Text = "Form1";
        FormClosed += Explorer_FormClosed;
        menuStrip1.ResumeLayout(false);
        menuStrip1.PerformLayout();
        splMain.Panel1.ResumeLayout(false);
        splMain.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splMain).EndInit();
        splMain.ResumeLayout(false);
        splSub.Panel1.ResumeLayout(false);
        splSub.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splSub).EndInit();
        splSub.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)lvwFiles).EndInit();
        tspNavigation.ResumeLayout(false);
        tspNavigation.PerformLayout();
        toolStripContainer1.ContentPanel.ResumeLayout(false);
        toolStripContainer1.TopToolStripPanel.ResumeLayout(false);
        toolStripContainer1.TopToolStripPanel.PerformLayout();
        toolStripContainer1.ResumeLayout(false);
        toolStripContainer1.PerformLayout();
        tspActions.ResumeLayout(false);
        tspActions.PerformLayout();
        splPreview.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splPreview).EndInit();
        splPreview.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)picPreview).EndInit();
        ResumeLayout(false);
    }

    #endregion

    private MenuStrip menuStrip1;
    private ToolStripMenuItem fileToolStripMenuItem;
    private ToolStripMenuItem quitToolStripMenuItem;
    private SplitContainer splMain;
    private TreeView tvwFiles;
    private SplitContainer splSub;
    private Controls.CoreVirtualObjectListView lvwFiles;
    private BrightIdeasSoftware.OLVColumn colFilesName;
    private BrightIdeasSoftware.OLVColumn colFilesPackType;
    private BrightIdeasSoftware.OLVColumn colFilesHash1;
    private BrightIdeasSoftware.OLVColumn colFilesHash2;
    private ToolStrip tspNavigation;
    private ToolStripButton btnNavBack;
    private ToolStripButton btnNavForward;
    private ToolStripButton btnNavUp;
    private ToolStripContainer toolStripContainer1;
    private Controls.ToolStripSpringComboBox txtPath;
    private ToolStripTextBox txtSearch;
    private ToolStripButton btnSearch;
    private ToolStripDropDownButton btnsHistory;
    private ToolStrip tspActions;
    private ToolStripComboBox cboView;
    private SplitContainer splPreview;
    private PictureBox picPreview;
}