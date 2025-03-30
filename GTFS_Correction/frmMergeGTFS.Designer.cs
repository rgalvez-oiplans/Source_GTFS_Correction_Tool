namespace GTFS_Correction
{
    partial class frmMergeGTFS
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lstGTFSFiles = new System.Windows.Forms.ListBox();
            this.btnAddGTFS = new System.Windows.Forms.Button();
            this.btnMergeGTFS = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // lstGTFSFiles
            // 
            this.lstGTFSFiles.FormattingEnabled = true;
            this.lstGTFSFiles.ItemHeight = 20;
            this.lstGTFSFiles.Location = new System.Drawing.Point(23, 55);
            this.lstGTFSFiles.Name = "lstGTFSFiles";
            this.lstGTFSFiles.Size = new System.Drawing.Size(877, 204);
            this.lstGTFSFiles.TabIndex = 0;
            // 
            // btnAddGTFS
            // 
            this.btnAddGTFS.Location = new System.Drawing.Point(785, 265);
            this.btnAddGTFS.Name = "btnAddGTFS";
            this.btnAddGTFS.Size = new System.Drawing.Size(115, 33);
            this.btnAddGTFS.TabIndex = 1;
            this.btnAddGTFS.Text = "Add GTFS";
            this.btnAddGTFS.UseVisualStyleBackColor = true;
            this.btnAddGTFS.Click += new System.EventHandler(this.btnAddGTFS_Click);
            // 
            // btnMergeGTFS
            // 
            this.btnMergeGTFS.Location = new System.Drawing.Point(785, 304);
            this.btnMergeGTFS.Name = "btnMergeGTFS";
            this.btnMergeGTFS.Size = new System.Drawing.Size(115, 33);
            this.btnMergeGTFS.TabIndex = 2;
            this.btnMergeGTFS.Text = "Merge All";
            this.btnMergeGTFS.UseVisualStyleBackColor = true;
            this.btnMergeGTFS.Click += new System.EventHandler(this.BtnMergeGTFS_Click);
            // 
            // frmMergeGTFS
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(918, 343);
            this.Controls.Add(this.btnMergeGTFS);
            this.Controls.Add(this.btnAddGTFS);
            this.Controls.Add(this.lstGTFSFiles);
            this.Name = "frmMergeGTFS";
            this.Text = "Merge GTFS";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ListBox lstGTFSFiles;
        private System.Windows.Forms.Button btnAddGTFS;
        private System.Windows.Forms.Button btnMergeGTFS;
    }
}