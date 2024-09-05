using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace GTFS_Correction
{
    public class GTFSFileMerger
    {
        private Dictionary<string, List<string[]>> dataFiles = new Dictionary<string, List<string[]>>();
        private Dictionary<string, string[]> keyColumns = new Dictionary<string, string[]>
        {
            { "agency.txt", new string[] { "agency_id" } },
            { "calendar.txt", new string[] { "service_id" } },
            { "routes.txt", new string[] { "route_id", "agency_id" } },  // Primary + Foreign Key
            { "stops.txt", new string[] { "stop_id", "parent_station" } },
            { "trips.txt", new string[] { "trip_id", "route_id", "service_id", "shape_id","block_id" } },  // Primary + Foreign Keys
            { "stop_times.txt", new string[] { "trip_id", "stop_id" } },  // Foreign Keys
            { "calendar_dates.txt", new string[] { "service_id" } },  // Foreign Key
            { "shapes.txt", new string[] { "shape_id" } }  // Foreign Key
        };

        public void LoadGTFSFile(string zipFilePath, int fileIndex)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                Directory.CreateDirectory(tempDirectory);
                System.IO.Compression.ZipFile.ExtractToDirectory(zipFilePath, tempDirectory);
                var filesToLoad = Directory.GetFiles(tempDirectory, "*.txt");

                foreach (var file in filesToLoad)
                {
                    string fileName = Path.GetFileName(file);

                    if (!dataFiles.ContainsKey(fileName))
                    {
                        dataFiles[fileName] = new List<string[]>();
                    }

                    var lines = File.ReadAllLines(file);

                    // Handle feed_info.txt separately
                    if (fileName.Equals("feed_info.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (dataFiles[fileName].Count == 0)
                        {
                            dataFiles[fileName].Add(lines[0].Split(','));
                            dataFiles[fileName].Add(lines[1].Split(','));
                        }
                        continue;
                    }

                    bool isFirstFile = dataFiles[fileName].Count == 0;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i == 0 && !isFirstFile) continue;
                        var parts = lines[i].Split(',');
                        dataFiles[fileName].Add(parts);
                    }
                }

                UpdateKeys(fileIndex);
            }
            catch (Exception ex)
            {
                throw new IOException($"Error loading GTFS files from {zipFilePath}: {ex.Message}");
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
        }

        private void UpdateKeys(int fileIndex)
        {
            foreach (var fileName in dataFiles.Keys)
            {
                if (fileName.Equals("feed_info.txt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (keyColumns.ContainsKey(fileName))
                {
                    foreach (var keyColumn in keyColumns[fileName])
                    {
                        int keyIndex = Array.IndexOf(dataFiles[fileName][0], keyColumn);

                        if (keyIndex >= 0)
                        {
                            for (int i = 1; i < dataFiles[fileName].Count; i++)
                            {
                                string currentId = dataFiles[fileName][i][keyIndex];

                                // Only append suffix if the ID has a non-empty value
                                if (!string.IsNullOrEmpty(currentId))
                                {
                                    dataFiles[fileName][i][keyIndex] = $"{currentId}_FM{fileIndex}";
                                }
                            }
                        }
                    }
                }
            }
        }

        public void WriteMergedFiles(string outputDirectory)
        {
            foreach (var kvp in dataFiles)
            {
                string fileName = kvp.Key;
                var data = kvp.Value;

                string outputFilePath = Path.Combine(outputDirectory, fileName);
                File.WriteAllLines(outputFilePath, data.Select(line => string.Join(",", line)));
            }

            // Remove discrepancy.txt if it exists
            string discrepancyFilePath = Path.Combine(outputDirectory, "discrepancy.txt");
            if (File.Exists(discrepancyFilePath))
            {
                File.Delete(discrepancyFilePath);
            }
        }

        public void ZipMergedFiles(string directoryPath, string zipFilePath)
        {
            try
            {
                // Define the list of files that need to be zipped
                string[] requiredFiles = new string[]
                {
            "agency.txt", "calendar.txt", "calendar_dates.txt", "feed_info.txt",
            "routes.txt", "shapes.txt", "stop_times.txt", "stops.txt", "trips.txt"
                };

                // Create a temporary directory to store the zip file
                string tempZipDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

                if (!Directory.Exists(tempZipDirectory))
                {
                    Directory.CreateDirectory(tempZipDirectory);
                }

                // Copy only the required files to the temporary directory
                foreach (var fileName in requiredFiles)
                {
                    string sourceFilePath = Path.Combine(directoryPath, fileName);
                    if (File.Exists(sourceFilePath))
                    {
                        string destPath = Path.Combine(tempZipDirectory, fileName);
                        File.Copy(sourceFilePath, destPath);
                    }
                }

                // Create the zip file from the temporary directory
                ZipFile.CreateFromDirectory(tempZipDirectory, zipFilePath);

                // Clean up the temporary directory
                Directory.Delete(tempZipDirectory, true);
            }
            catch (Exception ex)
            {
                throw new IOException($"Error zipping merged files: {ex.Message}");
            }
        }

    }
}

