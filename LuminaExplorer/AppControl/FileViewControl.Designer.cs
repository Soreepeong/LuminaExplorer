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
            if (disposing && (components != null))
            {
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
            this.tabs = new System.Windows.Forms.TabControl();
            this.tabProperties = new System.Windows.Forms.TabPage();
            this.tabRaw = new System.Windows.Forms.TabPage();
            this.propertyGrid = new System.Windows.Forms.PropertyGrid();
            this.tabs.SuspendLayout();
            this.tabProperties.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabs
            // 
            this.tabs.Controls.Add(this.tabProperties);
            this.tabs.Controls.Add(this.tabRaw);
            this.tabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabs.Location = new System.Drawing.Point(0, 0);
            this.tabs.Name = "tabs";
            this.tabs.SelectedIndex = 0;
            this.tabs.Size = new System.Drawing.Size(517, 478);
            this.tabs.TabIndex = 0;
            // 
            // tabProperties
            // 
            this.tabProperties.Controls.Add(this.propertyGrid);
            this.tabProperties.Location = new System.Drawing.Point(4, 24);
            this.tabProperties.Name = "tabProperties";
            this.tabProperties.Padding = new System.Windows.Forms.Padding(3);
            this.tabProperties.Size = new System.Drawing.Size(509, 450);
            this.tabProperties.TabIndex = 0;
            this.tabProperties.Text = "Properties";
            this.tabProperties.UseVisualStyleBackColor = true;
            // 
            // tabRaw
            // 
            this.tabRaw.Location = new System.Drawing.Point(4, 24);
            this.tabRaw.Name = "tabRaw";
            this.tabRaw.Padding = new System.Windows.Forms.Padding(3);
            this.tabRaw.Size = new System.Drawing.Size(509, 450);
            this.tabRaw.TabIndex = 1;
            this.tabRaw.Text = "Hex View";
            this.tabRaw.UseVisualStyleBackColor = true;
            // 
            // propertyGrid
            // 
            this.propertyGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.propertyGrid.Location = new System.Drawing.Point(3, 3);
            this.propertyGrid.Name = "propertyGrid";
            this.propertyGrid.Size = new System.Drawing.Size(503, 444);
            this.propertyGrid.TabIndex = 0;
            // 
            // FileViewControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tabs);
            this.Name = "FileViewControl";
            this.Size = new System.Drawing.Size(517, 478);
            this.tabs.ResumeLayout(false);
            this.tabProperties.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private TabControl tabs;
        private TabPage tabProperties;
        private TabPage tabRaw;
        private PropertyGrid propertyGrid;
    }
}
