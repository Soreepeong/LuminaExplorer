﻿using System.Drawing;
using System.Windows.Forms;
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
            Viewer = new ModelViewerControl();
            PropertyPanel = new Panel();
            PropertyPanelGrid = new PropertyGrid();
            PropertyPanel.SuspendLayout();
            SuspendLayout();
            // 
            // TexViewer
            // 
            Viewer.Dock = DockStyle.Fill;
            Viewer.Location = new Point(0, 0);
            Viewer.Margin = new Padding(0);
            Viewer.Name = "Viewer";
            Viewer.Padding = new Padding(16);
            Viewer.Size = new Size(831, 693);
            Viewer.TabIndex = 0;
            // 
            // PropertyPanel
            // 
            PropertyPanel.Anchor =  AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            PropertyPanel.Controls.Add(PropertyPanelGrid);
            PropertyPanel.Cursor = Cursors.SizeWE;
            PropertyPanel.Location = new Point(514, 0);
            PropertyPanel.Margin = new Padding(0);
            PropertyPanel.Name = "PropertyPanel";
            PropertyPanel.Padding = new Padding(4, 0, 0, 0);
            PropertyPanel.Size = new Size(317, 693);
            PropertyPanel.TabIndex = 1;
            // 
            // PropertyPanelGrid
            // 
            PropertyPanelGrid.Dock = DockStyle.Fill;
            PropertyPanelGrid.Location = new Point(4, 0);
            PropertyPanelGrid.Name = "PropertyPanelGrid";
            PropertyPanelGrid.PropertySort = PropertySort.Categorized;
            PropertyPanelGrid.Size = new Size(313, 693);
            PropertyPanelGrid.TabIndex = 0;
            PropertyPanelGrid.ToolbarVisible = false;
            // 
            // ModelViewer
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(831, 693);
            Controls.Add(PropertyPanel);
            Controls.Add(Viewer);
            Name = "ModelViewer";
            Text = "ModelViewer";
            PropertyPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private ModelViewerControl Viewer;
        private Panel PropertyPanel;
        private PropertyGrid PropertyPanelGrid;
    }
}