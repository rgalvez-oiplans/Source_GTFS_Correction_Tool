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
            this.SuspendLayout();
            // 
            // btnUploadGTFS
            // 
            this.btnUploadGTFS.Location = new System.Drawing.Point(12, 12);
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
            this.btnUploadShapefile.Location = new System.Drawing.Point(12, 41);
            this.btnUploadShapefile.Name = "btnUploadShapefile";
            this.btnUploadShapefile.Size = new System.Drawing.Size(100, 23);
            this.btnUploadShapefile.TabIndex = 1;
            this.btnUploadShapefile.Text = "Upload Shapefile";
            this.btnUploadShapefile.UseVisualStyleBackColor = true;
            this.btnUploadShapefile.Click += new System.EventHandler(this.btnUploadShapefile_Click);
            // 
            // btnValidate
            // 
            this.btnValidate.Location = new System.Drawing.Point(12, 70);
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
            this.lstStatus.Location = new System.Drawing.Point(12, 99);
            this.lstStatus.Name = "lstStatus";
            this.lstStatus.Size = new System.Drawing.Size(776, 184);
            this.lstStatus.TabIndex = 3;
            // 
            // txtLog
            // 
            this.txtLog.Location = new System.Drawing.Point(12, 289);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(776, 134);
            this.txtLog.TabIndex = 4;
            // 
            // btnMergeGTFS
            // 
            this.btnMergeGTFS.Location = new System.Drawing.Point(132, 12);
            this.btnMergeGTFS.Name = "btnMergeGTFS";
            this.btnMergeGTFS.Size = new System.Drawing.Size(96, 23);
            this.btnMergeGTFS.TabIndex = 5;
            this.btnMergeGTFS.Text = "Merge GTFS";
            this.btnMergeGTFS.UseVisualStyleBackColor = true;
            this.btnMergeGTFS.Click += new System.EventHandler(this.btnMergeGTFS_Click);
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(800, 435);
            this.Controls.Add(this.btnMergeGTFS);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.lstStatus);
            this.Controls.Add(this.btnValidate);
            this.Controls.Add(this.btnUploadShapefile);
            this.Controls.Add(this.btnUploadGTFS);
            this.Name = "MainForm";
            this.Text = "GTFS Correction Tool";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.Button btnUploadGTFS;
        private System.Windows.Forms.Button btnUploadShapefile;
        private System.Windows.Forms.Button btnValidate;
        private System.Windows.Forms.ListBox lstStatus;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Button btnMergeGTFS;
    }
}

