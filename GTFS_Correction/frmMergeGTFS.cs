using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GTFS_Correction
{
    public partial class frmMergeGTFS : Form
    {
        private List<string> gtfsFiles; // store GTFS zip file paths

        public frmMergeGTFS()
        {
            InitializeComponent();
            gtfsFiles = new List<string>();
        }

        private void btnAddGTFS_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "GTFS ZIP files (*.zip)|*.zip";
                dlg.Multiselect = true;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    foreach (var fn in dlg.FileNames)
                    {
                        gtfsFiles.Add(fn);
                        lstGTFSFiles.Items.Add(fn);
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
                    string baseOutputDir = folderDialog.SelectedPath;
                    if (string.IsNullOrEmpty(baseOutputDir) ||
                        !Directory.Exists(baseOutputDir))
                    {
                        MessageBox.Show("The selected directory is invalid.",
                            "Invalid Directory",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                        return;
                    }

                    try
                    {
                        // 1) Create merger
                        var fileMerger = new GTFSFileMerger();

                        // 2) Load all GTFS zip files
                        for (int i = 0; i < gtfsFiles.Count; i++)
                        {
                            string filePath = gtfsFiles[i];
                            if (!File.Exists(filePath))
                            {
                                MessageBox.Show($"File not found: {filePath}",
                                    "File Error",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                                return;
                            }

                            fileMerger.LoadGTFSFile(filePath, i + 1);
                        }

                        // 3) Determine subfolder
                        string zipFileName = $"BCT_GTFS_Merged_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                        string baseName = Path.GetFileNameWithoutExtension(zipFileName);
                        if (string.IsNullOrEmpty(baseName)) baseName = "MergedFeed";

                        string finalFolder = Path.Combine(baseOutputDir, baseName);
                        int suffix = 1;
                        while (Directory.Exists(finalFolder))
                        {
                            finalFolder = Path.Combine(baseOutputDir, $"{baseName}_{suffix}");
                            suffix++;
                        }
                        Directory.CreateDirectory(finalFolder);

                        // 4) Write merged .txt => finalFolder
                        fileMerger.WriteMergedFiles(finalFolder);

                        // 5) create the log in finalFolder
                        fileMerger.CreateLogFile(finalFolder);

                        // 6) use FeedInfoProcessor => 
                        //    update/create feed_info from final "calendar.txt"
                        var feedProc = new FeedInfoProcessor((msg, isErr) =>
                        {
                            // for demonstration, show in console or ignore
                            Console.WriteLine($"[FeedInfo] {msg}");
                        });

                        // If you want to load agency or config, call feedProc's 
                        // existing approach OR skip if you only want final start/end date
                        // e.g. feedProc.ProcessFeedInfo("agency.txt","calendar.txt","feed_info.txt","someConfig.txt");

                        // new approach:
                        feedProc.UpdateOrCreateFeedInfoFromMergedCalendar(
                            mergedCalendarPath: Path.Combine(finalFolder, "calendar.txt"),
                            mergedFeedInfoPath: Path.Combine(finalFolder, "feed_info.txt")
                        );

                        // 6) Zip finalFolder => zipFilePath
                        string zipFilePath = Path.Combine(finalFolder, zipFileName);
                        fileMerger.ZipMergedFiles(finalFolder, zipFilePath);

                        MessageBox.Show("GTFS merged & feed_info updated (no merge_log), folder zipped successfully.",
                            "Merge Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error merging GTFS: {ex.Message}",
                            "Merge Error",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show("No directory selected.",
                        "No Directory",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private void BtnRemoveSelected_Click(object sender, EventArgs e)
        {
            var selectedItems = lstGTFSFiles.SelectedItems.Cast<string>().ToList();
            foreach (var s in selectedItems)
            {
                lstGTFSFiles.Items.Remove(s);
                gtfsFiles.Remove(s);
            }
        }

        private void BtnClearAll_Click(object sender, EventArgs e)
        {
            lstGTFSFiles.Items.Clear();
            gtfsFiles.Clear();
        }
    }
}
