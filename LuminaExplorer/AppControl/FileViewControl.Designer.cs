namespace LuminaExplorer.AppControl {
    partial class FileViewControl {
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            tabs = new TabControl();
            tabProperties = new TabPage();
            propertyGrid = new PropertyGrid();
            tabRaw = new TabPage();
            tabs.SuspendLayout();
            tabProperties.SuspendLayout();
            SuspendLayout();
            // 
            // tabs
            // 
            tabs.Controls.Add(tabProperties);
            tabs.Controls.Add(tabRaw);
            tabs.Dock = DockStyle.Fill;
            tabs.Location = new Point(0, 0);
            tabs.Name = "tabs";
            tabs.SelectedIndex = 0;
            tabs.Size = new Size(517, 478);
            tabs.TabIndex = 0;
            // 
            // tabProperties
            // 
            tabProperties.Controls.Add(propertyGrid);
            tabProperties.Location = new Point(4, 24);
            tabProperties.Name = "tabProperties";
            tabProperties.Padding = new Padding(3);
            tabProperties.Size = new Size(509, 450);
            tabProperties.TabIndex = 0;
            tabProperties.Text = "Properties";
            tabProperties.UseVisualStyleBackColor = true;
            // 
            // propertyGrid
            // 
            propertyGrid.Dock = DockStyle.Fill;
            propertyGrid.Location = new Point(3, 3);
            propertyGrid.Name = "propertyGrid";
            propertyGrid.PropertySort = PropertySort.Categorized;
            propertyGrid.Size = new Size(503, 444);
            propertyGrid.TabIndex = 0;
            propertyGrid.ToolbarVisible = false;
            // 
            // tabRaw
            // 
            tabRaw.Location = new Point(4, 24);
            tabRaw.Name = "tabRaw";
            tabRaw.Padding = new Padding(3);
            tabRaw.Size = new Size(509, 450);
            tabRaw.TabIndex = 1;
            tabRaw.Text = "Hex View";
            tabRaw.UseVisualStyleBackColor = true;
            // 
            // FileViewControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(tabs);
            Name = "FileViewControl";
            Size = new Size(517, 478);
            tabs.ResumeLayout(false);
            tabProperties.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private TabControl tabs;
        private TabPage tabProperties;
        private TabPage tabRaw;
        private PropertyGrid propertyGrid;
    }
}
