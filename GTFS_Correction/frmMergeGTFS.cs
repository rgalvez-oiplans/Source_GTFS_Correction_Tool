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
            gtfsFiles = new List<string>();  // Initialize the list
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
                        gtfsFiles.Add(fileName);  // Add to list
                        lstGTFSFiles.Items.Add(fileName);  // Add to list box in UI
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
                    string outputDirectory = folderDialog.SelectedPath;

                    // Validate the directory name
                    if (string.IsNullOrEmpty(outputDirectory) || !Directory.Exists(outputDirectory))
                    {
                        MessageBox.Show("The selected directory is invalid. Please choose a valid directory.", "Invalid Directory", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    try
                    {
                        GTFSFileMerger fileMerger = new GTFSFileMerger();

                        // Load all GTFS files into memory with their respective indices
                        for (int i = 0; i < gtfsFiles.Count; i++)
                        {
                            string filePath = gtfsFiles[i];
                            // Ensure the file path is valid and log it for debugging
                            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                            {
                                MessageBox.Show($"The file path '{filePath}' is invalid. Please check the input files.", "Invalid File Path", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                return;
                            }

                            // Log or Debug output path for validation
                            Console.WriteLine($"Loading GTFS file: {filePath}");

                            fileMerger.LoadGTFSFile(filePath, i + 1); // Provide both filePath and fileIndex
                        }

                        // Write the merged files to the output directory
                        fileMerger.WriteMergedFiles(outputDirectory);

                        // Zip the merged files in the output directory
                        string zipFileName = $"BCT_GTFS_Merged_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                        string zipFilePath = Path.Combine(outputDirectory, zipFileName);

                        fileMerger.ZipMergedFiles(outputDirectory, zipFilePath);

                        MessageBox.Show("GTFS files merged and zipped successfully.", "Merge Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error merging GTFS files: {ex.Message}", "Merge Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show("No directory was selected. Please select a valid directory.", "No Directory Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void MergeGTFSFiles(List<string> gtfsFiles, string outputDirectory)
        {
            var fileMerger = new GTFSFileMerger();  // Assuming you have a GTFSFileMerger class to handle the merging logic

            // Load all GTFS files into memory
            for (int i = 0; i < gtfsFiles.Count; i++)
            {
                string filePath = gtfsFiles[i];
                fileMerger.LoadGTFSFile(filePath, i + 1); // Pass fileIndex as i+1 to ensure unique indexing starts from 1
            }

            // Perform the merging
           // fileMerger.MergeFiles();

            // Write the merged files to the output directory
            fileMerger.WriteMergedFiles(outputDirectory);
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