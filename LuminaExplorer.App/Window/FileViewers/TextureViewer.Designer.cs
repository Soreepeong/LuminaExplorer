namespace LuminaExplorer.App.Window.FileViewers {
    partial class TextureViewer {
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
            TexViewer = new Controls.FileResourceViewerControls.TexFileViewerControl();
            PropertyPanel = new Panel();
            PropertyPanelGrid = new PropertyGrid();
            PropertyPanel.SuspendLayout();
            SuspendLayout();
            // 
            // TexViewer
            // 
            TexViewer.ContentBorderColor = Color.LightGray;
            TexViewer.Dock = DockStyle.Fill;
            TexViewer.Location = new Point(0, 0);
            TexViewer.Margin = new Padding(0);
            TexViewer.Name = "TexViewer";
            TexViewer.Padding = new Padding(16);
            TexViewer.Size = new Size(831, 693);
            TexViewer.TabIndex = 0;
            TexViewer.Text = "TexViewer";
            TexViewer.TransparencyCellSize = 8;
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
            // TextureViewer
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(831, 693);
            Controls.Add(PropertyPanel);
            Controls.Add(TexViewer);
            Name = "TextureViewer";
            Text = "TextureViewer";
            PropertyPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private Controls.FileResourceViewerControls.TexFileViewerControl TexViewer;
        private Panel PropertyPanel;
        private PropertyGrid PropertyPanelGrid;
    }
}