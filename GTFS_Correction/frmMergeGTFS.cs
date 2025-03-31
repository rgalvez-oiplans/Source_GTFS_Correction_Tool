using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GTFS_Correction
{
    public partial class frmMergeGTFS : Form
    {
        private List<string> gtfsFiles;  // List to store paths of GTFS files

        public frmMergeGTFS()
        {
            InitializeComponent();
            gtfsFiles = new List<string>();
        }

        private void btnAddGTFS_Click(object sender, EventArgs e)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "GTFS ZIP files (*.zip)|*.zip";
                openFileDialog.Multiselect = true;  // Allow multiple selection

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    foreach (var fileName in openFileDialog.FileNames)
                    {
                        gtfsFiles.Add(fileName);
                        lstGTFSFiles.Items.Add(fileName);
                    }
                }
            }
        }

        private void BtnMergeGTFS_Click(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string baseOutputDirectory = folderDialog.SelectedPath;

                    if (string.IsNullOrEmpty(baseOutputDirectory) || !Directory.Exists(baseOutputDirectory))
                    {
                        MessageBox.Show("The selected directory is invalid. Please choose a valid directory.",
                                        "Invalid Directory", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    try
                    {
                        GTFSFileMerger fileMerger = new GTFSFileMerger();

                        // 1) Load all GTFS files
                        for (int i = 0; i < gtfsFiles.Count; i++)
                        {
                            string filePath = gtfsFiles[i];
                            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                            {
                                MessageBox.Show($"The file '{filePath}' is invalid.", "Invalid File",
                                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return;
                            }

                            // Provide the path to the merger so it can log
                            fileMerger.LoadGTFSFile(filePath, i + 1);
                        }

                        // 2) Create subfolder name from the .zip
                        string zipFileName = $"BCT_GTFS_Merged_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                        string baseName = Path.GetFileNameWithoutExtension(zipFileName);
                        if (string.IsNullOrEmpty(baseName)) baseName = "MergedFeed";

                        string finalFolder = Path.Combine(baseOutputDirectory, baseName);
                        int suffix = 1;
                        while (Directory.Exists(finalFolder))
                        {
                            finalFolder = Path.Combine(baseOutputDirectory, $"{baseName}_{suffix}");
                            suffix++;
                        }
                        Directory.CreateDirectory(finalFolder);

                        // 3) Write merged .txt into finalFolder
                        fileMerger.WriteMergedFiles(finalFolder);

                        // 4) Create a merge_log.txt listing the final .txt details
                        //    PLUS the raw lines from each feed
                        fileMerger.CreateLogFile(finalFolder);

                        // 5) Zip that folder in-place
                        string zipFilePath = Path.Combine(finalFolder, zipFileName);
                        fileMerger.ZipMergedFiles(finalFolder, zipFilePath);

                        MessageBox.Show("GTFS files merged, log created, and zipped successfully.",
                                        "Merge Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error merging GTFS files: {ex.Message}",
                                        "Merge Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show("No directory was selected. Please select a valid directory.",
                                    "No Directory Selected",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                }
            }
        }

        private void BtnRemoveSelected_Click(object sender, EventArgs e)
        {
            var selectedItems = lstGTFSFiles.SelectedItems.Cast<string>().ToList();
            foreach (var selectedItem in selectedItems)
            {
                lstGTFSFiles.Items.Remove(selectedItem);
                gtfsFiles.Remove(selectedItem);
            }
        }

        private void BtnClearAll_Click(object sender, EventArgs e)
        {
            lstGTFSFiles.Items.Clear();
            gtfsFiles.Clear();
        }
    }
}
