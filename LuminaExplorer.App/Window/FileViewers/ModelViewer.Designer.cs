using System.Drawing;
using System.Windows.Forms;
using LuminaExplorer.Controls;
using LuminaExplorer.Controls.FileResourceViewerControls.ModelViewerControl;
using LuminaExplorer.Controls.FileResourceViewerControls.MultiBitmapViewerControl;

namespace LuminaExplorer.App.Window.FileViewers {
    partial class ModelViewer {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            MainLeftSplitter = new SplitContainer();
            ModelConfigSplitter = new SplitContainer();
            AnimationEnabledCheckbox = new CheckBox();
            AnimationSpeedTrackBar = new TrackBar();
            AnimationSpeedLabel = new Label();
            ModelConfigTabControl = new TabControl();
            tabPage1 = new TabPage();
            AnimationListView = new CoreVirtualObjectListView();
            MainRightSplitter = new SplitContainer();
            Viewer = new ModelViewerControl();
            PropertyPanelGrid = new PropertyGrid();
            AnimationListViewColumnFileName = new BrightIdeasSoftware.OLVColumn();
            AnimationListViewColumnAnimationName = new BrightIdeasSoftware.OLVColumn();
            AnimationListViewColumnFullPath = new BrightIdeasSoftware.OLVColumn();
            ((System.ComponentModel.ISupportInitialize) MainLeftSplitter).BeginInit();
            MainLeftSplitter.Panel1.SuspendLayout();
            MainLeftSplitter.Panel2.SuspendLayout();
            MainLeftSplitter.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) ModelConfigSplitter).BeginInit();
            ModelConfigSplitter.Panel1.SuspendLayout();
            ModelConfigSplitter.Panel2.SuspendLayout();
            ModelConfigSplitter.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) AnimationSpeedTrackBar).BeginInit();
            ModelConfigTabControl.SuspendLayout();
            tabPage1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) MainRightSplitter).BeginInit();
            MainRightSplitter.Panel1.SuspendLayout();
            MainRightSplitter.Panel2.SuspendLayout();
            MainRightSplitter.SuspendLayout();
            SuspendLayout();
            // 
            // MainLeftSplitter
            // 
            MainLeftSplitter.Dock = DockStyle.Fill;
            MainLeftSplitter.FixedPanel = FixedPanel.Panel1;
            MainLeftSplitter.Location = new Point(0, 0);
            MainLeftSplitter.Name = "MainLeftSplitter";
            // 
            // MainLeftSplitter.Panel1
            // 
            MainLeftSplitter.Panel1.Controls.Add(ModelConfigSplitter);
            // 
            // MainLeftSplitter.Panel2
            // 
            MainLeftSplitter.Panel2.Controls.Add(MainRightSplitter);
            MainLeftSplitter.Size = new Size(1264, 681);
            MainLeftSplitter.SplitterDistance = 277;
            MainLeftSplitter.TabIndex = 2;
            // 
            // ModelConfigSplitter
            // 
            ModelConfigSplitter.Dock = DockStyle.Fill;
            ModelConfigSplitter.FixedPanel = FixedPanel.Panel1;
            ModelConfigSplitter.Location = new Point(0, 0);
            ModelConfigSplitter.Name = "ModelConfigSplitter";
            ModelConfigSplitter.Orientation = Orientation.Horizontal;
            // 
            // ModelConfigSplitter.Panel1
            // 
            ModelConfigSplitter.Panel1.AutoScroll = true;
            ModelConfigSplitter.Panel1.Controls.Add(AnimationEnabledCheckbox);
            ModelConfigSplitter.Panel1.Controls.Add(AnimationSpeedTrackBar);
            ModelConfigSplitter.Panel1.Controls.Add(AnimationSpeedLabel);
            // 
            // ModelConfigSplitter.Panel2
            // 
            ModelConfigSplitter.Panel2.Controls.Add(ModelConfigTabControl);
            ModelConfigSplitter.Size = new Size(277, 681);
            ModelConfigSplitter.SplitterDistance = 219;
            ModelConfigSplitter.TabIndex = 0;
            // 
            // AnimationEnabledCheckbox
            // 
            AnimationEnabledCheckbox.Anchor =  AnchorStyles.Top | AnchorStyles.Right;
            AnimationEnabledCheckbox.AutoSize = true;
            AnimationEnabledCheckbox.Checked = true;
            AnimationEnabledCheckbox.CheckState = CheckState.Checked;
            AnimationEnabledCheckbox.Location = new Point(222, 12);
            AnimationEnabledCheckbox.Name = "AnimationEnabledCheckbox";
            AnimationEnabledCheckbox.Size = new Size(48, 19);
            AnimationEnabledCheckbox.TabIndex = 2;
            AnimationEnabledCheckbox.Text = "Play";
            AnimationEnabledCheckbox.UseVisualStyleBackColor = true;
            // 
            // AnimationSpeedTrackBar
            // 
            AnimationSpeedTrackBar.Anchor =  AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            AnimationSpeedTrackBar.LargeChange = 25;
            AnimationSpeedTrackBar.Location = new Point(12, 37);
            AnimationSpeedTrackBar.Maximum = 400;
            AnimationSpeedTrackBar.Name = "AnimationSpeedTrackBar";
            AnimationSpeedTrackBar.Size = new Size(258, 45);
            AnimationSpeedTrackBar.SmallChange = 10;
            AnimationSpeedTrackBar.TabIndex = 1;
            AnimationSpeedTrackBar.TickFrequency = 10;
            AnimationSpeedTrackBar.Value = 100;
            // 
            // AnimationSpeedLabel
            // 
            AnimationSpeedLabel.AutoSize = true;
            AnimationSpeedLabel.Location = new Point(12, 13);
            AnimationSpeedLabel.Name = "AnimationSpeedLabel";
            AnimationSpeedLabel.Size = new Size(116, 15);
            AnimationSpeedLabel.TabIndex = 0;
            AnimationSpeedLabel.Text = "Animation Speed: 1x";
            // 
            // ModelConfigTabControl
            // 
            ModelConfigTabControl.Controls.Add(tabPage1);
            ModelConfigTabControl.Dock = DockStyle.Fill;
            ModelConfigTabControl.Location = new Point(0, 0);
            ModelConfigTabControl.Name = "ModelConfigTabControl";
            ModelConfigTabControl.SelectedIndex = 0;
            ModelConfigTabControl.Size = new Size(277, 458);
            ModelConfigTabControl.TabIndex = 0;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(AnimationListView);
            tabPage1.Location = new Point(4, 24);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(269, 430);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "Animations";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // AnimationListView
            // 
            AnimationListView.Columns.AddRange(new ColumnHeader[] { AnimationListViewColumnFileName, AnimationListViewColumnAnimationName, AnimationListViewColumnFullPath });
            AnimationListView.Dock = DockStyle.Fill;
            AnimationListView.FullRowSelect = true;
            AnimationListView.Location = new Point(3, 3);
            AnimationListView.Name = "AnimationListView";
            AnimationListView.Size = new Size(263, 424);
            AnimationListView.TabIndex = 0;
            AnimationListView.UseCompatibleStateImageBehavior = false;
            AnimationListView.View = View.Details;
            // 
            // MainRightSplitter
            // 
            MainRightSplitter.Dock = DockStyle.Fill;
            MainRightSplitter.FixedPanel = FixedPanel.Panel2;
            MainRightSplitter.Location = new Point(0, 0);
            MainRightSplitter.Name = "MainRightSplitter";
            // 
            // MainRightSplitter.Panel1
            // 
            MainRightSplitter.Panel1.Controls.Add(Viewer);
            // 
            // MainRightSplitter.Panel2
            // 
            MainRightSplitter.Panel2.Controls.Add(PropertyPanelGrid);
            MainRightSplitter.Size = new Size(983, 681);
            MainRightSplitter.SplitterDistance = 715;
            MainRightSplitter.TabIndex = 0;
            // 
            // Viewer
            // 
            Viewer.Animations = null;
            Viewer.AnimationPlaying = true;
            Viewer.AnimationSpeed = 1F;
            Viewer.BackColor = SystemColors.Control;
            Viewer.Dock = DockStyle.Fill;
            Viewer.Location = new Point(0, 0);
            Viewer.Margin = new Padding(0);
            Viewer.ModelInfoResolverTask = null;
            Viewer.Name = "Viewer";
            Viewer.Padding = new Padding(16);
            Viewer.Size = new Size(715, 681);
            Viewer.TabIndex = 1;
            // 
            // PropertyPanelGrid
            // 
            PropertyPanelGrid.Dock = DockStyle.Fill;
            PropertyPanelGrid.Location = new Point(0, 0);
            PropertyPanelGrid.Name = "PropertyPanelGrid";
            PropertyPanelGrid.PropertySort = PropertySort.Categorized;
            PropertyPanelGrid.Size = new Size(264, 681);
            PropertyPanelGrid.TabIndex = 1;
            PropertyPanelGrid.ToolbarVisible = false;
            // 
            // AnimationListViewColumnFileName
            // 
            AnimationListViewColumnFileName.AspectName = "FileName";
            AnimationListViewColumnFileName.IsEditable = false;
            AnimationListViewColumnFileName.Text = "File Name";
            AnimationListViewColumnFileName.Width = 80;
            // 
            // AnimationListViewColumnAnimationName
            // 
            AnimationListViewColumnAnimationName.AspectName = "AnimationName";
            AnimationListViewColumnAnimationName.IsEditable = false;
            AnimationListViewColumnAnimationName.Text = "Animation Name";
            AnimationListViewColumnAnimationName.Width = 80;
            // 
            // AnimationListViewColumnFullPath
            // 
            AnimationListViewColumnFullPath.AspectName = "FullPath";
            AnimationListViewColumnFullPath.IsEditable = false;
            AnimationListViewColumnFullPath.Text = "Full Path";
            AnimationListViewColumnFullPath.Width = 320;
            // 
            // ModelViewer
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1264, 681);
            Controls.Add(MainLeftSplitter);
            Name = "ModelViewer";
            Text = "ModelViewer";
            MainLeftSplitter.Panel1.ResumeLayout(false);
            MainLeftSplitter.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) MainLeftSplitter).EndInit();
            MainLeftSplitter.ResumeLayout(false);
            ModelConfigSplitter.Panel1.ResumeLayout(false);
            ModelConfigSplitter.Panel1.PerformLayout();
            ModelConfigSplitter.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) ModelConfigSplitter).EndInit();
            ModelConfigSplitter.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) AnimationSpeedTrackBar).EndInit();
            ModelConfigTabControl.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            MainRightSplitter.Panel1.ResumeLayout(false);
            MainRightSplitter.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) MainRightSplitter).EndInit();
            MainRightSplitter.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private SplitContainer MainLeftSplitter;
        private SplitContainer ModelConfigSplitter;
        private SplitContainer MainRightSplitter;
        private ModelViewerControl Viewer;
        private PropertyGrid PropertyPanelGrid;
        private TabControl ModelConfigTabControl;
        private TabPage tabPage1;
        private CheckBox AnimationEnabledCheckbox;
        private TrackBar AnimationSpeedTrackBar;
        private Label AnimationSpeedLabel;
        private Controls.CoreVirtualObjectListView AnimationListView;
        private BrightIdeasSoftware.OLVColumn AnimationListViewColumnFileName;
        private BrightIdeasSoftware.OLVColumn AnimationListViewColumnAnimationName;
        private BrightIdeasSoftware.OLVColumn AnimationListViewColumnFullPath;
    }
}