namespace GTFS_Correction
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.btnUploadGTFS = new System.Windows.Forms.Button();
            this.btnUploadShapefile = new System.Windows.Forms.Button();
            this.btnValidate = new System.Windows.Forms.Button();
            this.lstStatus = new System.Windows.Forms.ListBox();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.btnMergeGTFS = new System.Windows.Forms.Button();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.aboutToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // btnUploadGTFS
            // 
            this.btnUploadGTFS.Location = new System.Drawing.Point(12, 36);
            this.btnUploadGTFS.Name = "btnUploadGTFS";
            this.btnUploadGTFS.Size = new System.Drawing.Size(100, 23);
            this.btnUploadGTFS.TabIndex = 0;
            this.btnUploadGTFS.Text = "Upload GTFS";
            this.btnUploadGTFS.UseVisualStyleBackColor = true;
            this.btnUploadGTFS.Click += new System.EventHandler(this.btnUploadGTFS_Click);
            // 
            // btnUploadShapefile
            // 
            this.btnUploadShapefile.Enabled = false;
            this.btnUploadShapefile.Location = new System.Drawing.Point(12, 65);
            this.btnUploadShapefile.Name = "btnUploadShapefile";
            this.btnUploadShapefile.Size = new System.Drawing.Size(100, 23);
            this.btnUploadShapefile.TabIndex = 1;
            this.btnUploadShapefile.Text = "Upload Shapefile";
            this.btnUploadShapefile.UseVisualStyleBackColor = true;
            this.btnUploadShapefile.Click += new System.EventHandler(this.btnUploadShapefile_Click);
            // 
            // btnValidate
            // 
            this.btnValidate.Location = new System.Drawing.Point(12, 103);
            this.btnValidate.Name = "btnValidate";
            this.btnValidate.Size = new System.Drawing.Size(100, 23);
            this.btnValidate.TabIndex = 2;
            this.btnValidate.Text = "Validate";
            this.btnValidate.UseVisualStyleBackColor = true;
            this.btnValidate.Visible = false;
            this.btnValidate.Click += new System.EventHandler(this.btnValidate_Click);
            // 
            // lstStatus
            // 
            this.lstStatus.FormattingEnabled = true;
            this.lstStatus.ItemHeight = 20;
            this.lstStatus.Location = new System.Drawing.Point(12, 132);
            this.lstStatus.Name = "lstStatus";
            this.lstStatus.Size = new System.Drawing.Size(776, 184);
            this.lstStatus.TabIndex = 3;
            // 
            // txtLog
            // 
            this.txtLog.Location = new System.Drawing.Point(12, 322);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(776, 134);
            this.txtLog.TabIndex = 4;
            // 
            // btnMergeGTFS
            // 
            this.btnMergeGTFS.Location = new System.Drawing.Point(132, 36);
            this.btnMergeGTFS.Name = "btnMergeGTFS";
            this.btnMergeGTFS.Size = new System.Drawing.Size(96, 23);
            this.btnMergeGTFS.TabIndex = 5;
            this.btnMergeGTFS.Text = "Merge GTFS";
            this.btnMergeGTFS.UseVisualStyleBackColor = true;
            this.btnMergeGTFS.Click += new System.EventHandler(this.btnMergeGTFS_Click);
            // 
            // menuStrip1
            // 
            this.menuStrip1.GripMargin = new System.Windows.Forms.Padding(2, 2, 0, 2);
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(24, 24);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.aboutToolStripMenuItem,
            this.aboutToolStripMenuItem1});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(800, 33);
            this.menuStrip1.TabIndex = 6;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // aboutToolStripMenuItem
            // 
            this.aboutToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.exitToolStripMenuItem});
            this.aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            this.aboutToolStripMenuItem.Size = new System.Drawing.Size(54, 29);
            this.aboutToolStripMenuItem.Text = "File";
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(270, 34);
            this.exitToolStripMenuItem.Text = "Exit";
            this.exitToolStripMenuItem.Click += new System.EventHandler(this.exitToolStripMenuItem_Click);
            // 
            // aboutToolStripMenuItem1
            // 
            this.aboutToolStripMenuItem1.Name = "aboutToolStripMenuItem1";
            this.aboutToolStripMenuItem1.Size = new System.Drawing.Size(78, 29);
            this.aboutToolStripMenuItem1.Text = "About";
            this.aboutToolStripMenuItem1.Click += new System.EventHandler(this.aboutToolStripMenuItem1_Click);
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(800, 464);
            this.Controls.Add(this.btnMergeGTFS);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.lstStatus);
            this.Controls.Add(this.btnValidate);
            this.Controls.Add(this.btnUploadShapefile);
            this.Controls.Add(this.btnUploadGTFS);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "MainForm";
            this.Text = "GTFS Correction Tool";
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.Button btnUploadGTFS;
        private System.Windows.Forms.Button btnUploadShapefile;
        private System.Windows.Forms.Button btnValidate;
        private System.Windows.Forms.ListBox lstStatus;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Button btnMergeGTFS;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutToolStripMenuItem1;
    }
}

