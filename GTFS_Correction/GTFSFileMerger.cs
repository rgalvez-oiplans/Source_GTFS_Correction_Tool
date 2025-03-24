using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GTFS_Correction
{
    public class GTFSFileMerger
    {
        // Stores all GTFS data in-memory, keyed by filename
        private readonly Dictionary<string, List<string[]>> dataFiles
            = new Dictionary<string, List<string[]>>();

        // For avoiding duplicates in routes.txt
        private readonly HashSet<string> seenRouteIds = new HashSet<string>();

        // Tracks each unique final stop_id => (lat, lon).
        // If a new row has same stop_id but different lat/lon, we rename it.
        private readonly Dictionary<string, (string Lat, string Lon)> knownStops
            = new Dictionary<string, (string Lat, string Lon)>();

        // During each LoadGTFSFile call, we’ll build a mapping of oldStopId->newStopId 
        // for that feed, so we can update stop_times references accordingly.
        private Dictionary<string, string> renamedStopIds;

        /// <summary>
        /// Loads a GTFS zip file, unzips it, merges data into dataFiles.
        /// Row order is preserved for new records. If an existing stop’s lat/lon changed, 
        /// we suffix its stop_id with "_Merged_{FileIndex}" and update references in stop_times.
        /// </summary>
        /// <param name="zipFilePath">Path to the GTFS zip file.</param>
        /// <param name="FileIndex">
        /// A unique index for this feed (1,2,3...). Used for "_Merged_{FileIndex}" suffixing.
        /// </param>
        public void LoadGTFSFile(string zipFilePath, int FileIndex)
        {
            // A fresh mapping for each feed: oldStopId => newStopId
            renamedStopIds = new Dictionary<string, string>();

            string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            try
            {
                Directory.CreateDirectory(tempDirectory);
                ZipFile.ExtractToDirectory(zipFilePath, tempDirectory);

                // Get all .txt files from the extracted folder
                // but ensure "stops.txt" comes before "stop_times.txt" in the sort order
                var filesToLoad = Directory.GetFiles(tempDirectory, "*.txt")
                    .OrderBy(fn => OrderForStopsFirst(fn))
                    .ThenBy(fn => fn, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var file in filesToLoad)
                {
                    string fileName = Path.GetFileName(file);

                    // Ensure we have a place in dataFiles for this filename
                    if (!dataFiles.ContainsKey(fileName))
                    {
                        dataFiles[fileName] = new List<string[]>();
                    }

                    var lines = File.ReadAllLines(file);
                    bool isFirstTimeForThisFile = (dataFiles[fileName].Count == 0);

                    // -------------------------------------------------------------------
                    // 1) Special handling for agency.txt:
                    //    - Only keep the first feed’s header + first data row
                    // -------------------------------------------------------------------
                    if (fileName.Equals("agency.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isFirstTimeForThisFile)
                        {
                            // Add header if it exists
                            if (lines.Length > 0)
                            {
                                dataFiles[fileName].Add(lines[0].Split(','));
                            }
                            // Add first data row if it exists
                            if (lines.Length > 1)
                            {
                                dataFiles[fileName].Add(lines[1].Split(','));
                            }
                        }
                        // Ignore subsequent agency.txt files entirely
                        continue;
                    }

                    // -------------------------------------------------------------------
                    // 2) Special handling for feed_info.txt:
                    //    - Only keep the first feed’s header + first data row
                    // -------------------------------------------------------------------
                    if (fileName.Equals("feed_info.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isFirstTimeForThisFile)
                        {
                            if (lines.Length > 0)
                            {
                                dataFiles[fileName].Add(lines[0].Split(',')); // header
                            }
                            if (lines.Length > 1)
                            {
                                dataFiles[fileName].Add(lines[1].Split(',')); // first data row
                            }
                        }
                        // Ignore subsequent feed_info.txt files
                        continue;
                    }

                    // -------------------------------------------------------------------
                    // 3) stops.txt logic: 
                    //    - If same stop_id but lat/lon changed => rename new ID with "_Merged_{FileIndex}".
                    //    - Keep row order, skip duplicates if lat/lon is identical.
                    // -------------------------------------------------------------------
                    if (fileName.Equals("stops.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStopsFile(lines, fileName, isFirstTimeForThisFile, FileIndex);
                        continue; // done with stops.txt
                    }

                    // -------------------------------------------------------------------
                    // 4) routes.txt logic (avoid duplicates based on route_id).
                    // -------------------------------------------------------------------
                    if (fileName.Equals("routes.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessRoutesFile(lines, fileName, isFirstTimeForThisFile);
                        continue; // done with routes.txt
                    }

                    // -------------------------------------------------------------------
                    // 5) stop_times.txt logic:
                    //    - If we renamed some stops, we update their stop_id references.
                    // -------------------------------------------------------------------
                    if (fileName.Equals("stop_times.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStopTimesFile(lines, fileName, isFirstTimeForThisFile);
                        continue;
                    }

                    // -------------------------------------------------------------------
                    // 6) For all other files: 
                    //    - We simply append data, skipping the header on subsequent files
                    // -------------------------------------------------------------------
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // If we already have a header, skip line[0] from subsequent files
                        if (i == 0 && !isFirstTimeForThisFile)
                            continue;

                        var parts = lines[i].Split(',');
                        dataFiles[fileName].Add(parts);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Error loading GTFS files from {zipFilePath}: {ex.Message}");
            }
            finally
            {
                // Clean up the temporary directory
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }

                // Clear renamedStopIds since it's specific to this feed
                renamedStopIds = null;
            }
        }

        /// <summary>
        /// Defines a custom ordering so that "stops.txt" sorts before "stop_times.txt",
        /// ensuring we process stops first (which sets renameStopIds) and then stop_times after.
        /// This fixes the issue where alphabetical order would put "stop_times.txt" before "stops.txt".
        /// </summary>
        private static int OrderForStopsFirst(string filePath)
        {
            string name = Path.GetFileName(filePath).ToLowerInvariant();
            if (name == "stops.txt") return 0;
            if (name == "stop_times.txt") return 1;
            // everything else can be 2
            return 2;
        }

        /// <summary>
        /// If the same stop_id appears with different lat/lon, we rename the new row’s ID 
        /// to oldStopId_Merged_{FileIndex}. We also store that rename in renamedStopIds so that 
        /// stop_times can reference the new ID.
        /// </summary>
        private void ProcessStopsFile(string[] lines, string fileName, bool isFirstFile, int fileIndex)
        {
            int stopIdColIndex = -1;
            int latColIndex = -1;
            int lonColIndex = -1;

            // If first time, add header
            if (isFirstFile && lines.Length > 0)
            {
                var headerParts = lines[0].Split(',');
                dataFiles[fileName].Add(headerParts);

                stopIdColIndex = Array.IndexOf(headerParts, "stop_id");
                latColIndex = Array.IndexOf(headerParts, "stop_lat");
                lonColIndex = Array.IndexOf(headerParts, "stop_lon");
            }
            else
            {
                // Already have a header, find indexes
                var existingHeader = dataFiles[fileName][0];
                stopIdColIndex = Array.IndexOf(existingHeader, "stop_id");
                latColIndex = Array.IndexOf(existingHeader, "stop_lat");
                lonColIndex = Array.IndexOf(existingHeader, "stop_lon");
            }

            // Read data rows
            for (int i = (isFirstFile ? 1 : 0); i < lines.Length; i++)
            {
                // If not first-time, skip the header row from subsequent files
                if (i == 0 && !isFirstFile)
                    continue;

                var parts = lines[i].Split(',');

                // If we can't find stop_id col, just add row
                if (stopIdColIndex < 0 || stopIdColIndex >= parts.Length)
                {
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                string stopId = parts[stopIdColIndex];
                if (string.IsNullOrEmpty(stopId))
                {
                    // No ID => just add
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                // Attempt to find lat/lon
                string lat = (latColIndex >= 0 && latColIndex < parts.Length)
                             ? parts[latColIndex] : "";
                string lon = (lonColIndex >= 0 && lonColIndex < parts.Length)
                             ? parts[lonColIndex] : "";

                // If we've never seen this stop_id before:
                if (!knownStops.ContainsKey(stopId))
                {
                    // record it
                    knownStops[stopId] = (lat, lon);
                    dataFiles[fileName].Add(parts);
                }
                else
                {
                    // We have seen stopId. Compare lat/lon:
                    var oldLatLon = knownStops[stopId];
                    bool latChanged = !string.IsNullOrEmpty(lat) && lat != oldLatLon.Lat;
                    bool lonChanged = !string.IsNullOrEmpty(lon) && lon != oldLatLon.Lon;

                    if (latChanged || lonChanged)
                    {
                        // We have to rename this new row’s stop_id
                        string newStopId = stopId + "_Merged_" + fileIndex;

                        // update stop_id in 'parts'
                        parts[stopIdColIndex] = newStopId;
                        dataFiles[fileName].Add(parts);

                        // record the new lat/lon under the new ID
                        knownStops[newStopId] = (lat, lon);

                        // Also store that this feed's oldStopId references newStopId
                        renamedStopIds[stopId] = newStopId;
                    }
                    else
                    {
                        // same lat/lon => skip the duplicate row
                    }
                }
            }
        }

        /// <summary>
        /// routes.txt logic: if we see a route_id again, skip it (duplicate).
        /// </summary>
        private void ProcessRoutesFile(string[] lines, string fileName, bool isFirstFile)
        {
            int routeIdColIndex = -1;
            if (isFirstFile && lines.Length > 0)
            {
                var headerParts = lines[0].Split(',');
                dataFiles[fileName].Add(headerParts);
                routeIdColIndex = Array.IndexOf(headerParts, "route_id");
            }
            else
            {
                var existingHeader = dataFiles[fileName][0];
                routeIdColIndex = Array.IndexOf(existingHeader, "route_id");
            }

            for (int i = (isFirstFile ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !isFirstFile)
                    continue;

                var parts = lines[i].Split(',');
                if (routeIdColIndex >= 0 && routeIdColIndex < parts.Length)
                {
                    string routeId = parts[routeIdColIndex];
                    if (!string.IsNullOrEmpty(routeId) && !seenRouteIds.Contains(routeId))
                    {
                        dataFiles[fileName].Add(parts);
                        seenRouteIds.Add(routeId);
                    }
                    // else duplicate => skip
                }
                else
                {
                    // If route_id column not found, just add row
                    dataFiles[fileName].Add(parts);
                }
            }
        }

        /// <summary>
        /// stop_times.txt logic: if a stop_id was renamed in this feed, we update the reference.
        /// e.g., if oldStopId -> newStopId, we replace it in each row.
        /// </summary>
        private void ProcessStopTimesFile(string[] lines, string fileName, bool isFirstFile)
        {
            int stopIdColIndex = -1;

            if (isFirstFile && lines.Length > 0)
            {
                var headerParts = lines[0].Split(',');
                dataFiles[fileName].Add(headerParts);
                stopIdColIndex = Array.IndexOf(headerParts, "stop_id");
            }
            else
            {
                var existingHeader = dataFiles[fileName][0];
                stopIdColIndex = Array.IndexOf(existingHeader, "stop_id");
            }

            for (int i = (isFirstFile ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !isFirstFile)
                    continue;

                var parts = lines[i].Split(',');

                // If we have a stop_id column:
                if (stopIdColIndex >= 0 && stopIdColIndex < parts.Length)
                {
                    string oldStopId = parts[stopIdColIndex];

                    // Check if we have a renamed ID for this feed
                    if (!string.IsNullOrEmpty(oldStopId) && renamedStopIds.ContainsKey(oldStopId))
                    {
                        parts[stopIdColIndex] = renamedStopIds[oldStopId];
                    }
                }

                dataFiles[fileName].Add(parts);
            }
        }

        /// <summary>
        /// Writes the merged data to .txt files in the specified output directory.
        /// Removes discrepancy.txt if it exists in that directory.
        /// </summary>
        /// <param name="outputDirectory">Path to the folder where .txt files are written.</param>
        public void WriteMergedFiles(string outputDirectory)
        {
            foreach (var kvp in dataFiles)
            {
                string fileName = kvp.Key;
                List<string[]> rows = kvp.Value;

                string outputFilePath = Path.Combine(outputDirectory, fileName);
                File.WriteAllLines(outputFilePath, rows.Select(line => string.Join(",", line)));
            }

            // Remove discrepancy.txt if it exists
            string discrepancyFilePath = Path.Combine(outputDirectory, "discrepancy.txt");
            if (File.Exists(discrepancyFilePath))
            {
                File.Delete(discrepancyFilePath);
            }
        }

        /// <summary>
        /// Zips only the required GTFS files from the given directory into a new zip.
        /// </summary>
        /// <param name="directoryPath">Path to folder containing the merged .txt files.</param>
        /// <param name="zipFilePath">Where to create the final zip.</param>
        public void ZipMergedFiles(string directoryPath, string zipFilePath)
        {
            try
            {
                // List the standard GTFS files we expect to zip
                string[] requiredFiles = new string[]
                {
                    "agency.txt", "calendar.txt", "calendar_dates.txt", "feed_info.txt",
                    "routes.txt", "shapes.txt", "stop_times.txt", "stops.txt", "trips.txt"
                };

                // Create a temporary directory to store the zip
                string tempZipDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempZipDirectory);

                // Copy only required files into the temp directory
                foreach (var fileName in requiredFiles)
                {
                    string sourceFilePath = Path.Combine(directoryPath, fileName);
                    if (File.Exists(sourceFilePath))
                    {
                        string destPath = Path.Combine(tempZipDirectory, fileName);
                        File.Copy(sourceFilePath, destPath);
                    }
                }

                // Create the zip from temp directory
                ZipFile.CreateFromDirectory(tempZipDirectory, zipFilePath);

                // Clean up
                Directory.Delete(tempZipDirectory, true);
            }
            catch (Exception ex)
            {
                throw new IOException($"Error zipping merged files: {ex.Message}");
            }
        }
    }
}

