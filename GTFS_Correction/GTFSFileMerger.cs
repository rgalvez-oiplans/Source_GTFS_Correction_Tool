using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GTFS_Correction
{
    public class GTFSFileMerger
    {
        // Merged data: key=filename, value=list of rows
        private readonly Dictionary<string, List<string[]>> dataFiles
            = new Dictionary<string, List<string[]>>();

        private readonly HashSet<string> seenRouteIds = new HashSet<string>();
        private readonly Dictionary<string, (string Lat, string Lon)> knownStops
            = new Dictionary<string, (string Lat, string Lon)>();
        private readonly HashSet<string> knownServiceIds = new HashSet<string>();

        // For each feed, track rename maps
        private Dictionary<string, string> renamedStopIds;
        private Dictionary<string, string> renamedServiceIds;

        // For logging feed metadata (zip file + unzipped .txt info)
        private readonly List<FeedMetadata> feedMetadatas = new List<FeedMetadata>();

        // Load
        public void LoadGTFSFile(string zipFilePath, int fileIndex)
        {
            renamedStopIds = new Dictionary<string, string>();
            renamedServiceIds = new Dictionary<string, string>();

            // Gather .zip file metadata
            var zipInfo = new FileInfo(zipFilePath);
            var feedMeta = new FeedMetadata
            {
                ZipFilePath = zipFilePath,
                ZipFileSize = zipInfo.Length,
                ZipCreated = zipInfo.CreationTime,
                ZipModified = zipInfo.LastWriteTime,
                InnerTxtFiles = new List<InnerTxtInfo>()
            };

            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);
                ZipFile.ExtractToDirectory(zipFilePath, tempDir);

                var files = Directory.GetFiles(tempDir, "*.txt")
                    .OrderBy(fn => CustomFileOrder(fn))
                    .ThenBy(fn => fn, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var filePath in files)
                {
                    string fileName = Path.GetFileName(filePath);

                    // Log metadata about this unzipped file
                    var fi = new FileInfo(filePath);
                    feedMeta.InnerTxtFiles.Add(new InnerTxtInfo
                    {
                        FileName = fileName,
                        Size = fi.Length,
                        Created = fi.CreationTime,
                        Modified = fi.LastWriteTime
                    });

                    if (!dataFiles.ContainsKey(fileName))
                    {
                        dataFiles[fileName] = new List<string[]>();
                    }

                    var lines = File.ReadAllLines(filePath);
                    bool isFirstTime = (dataFiles[fileName].Count == 0);

                    // 1) agency.txt => only keep first feed’s row
                    if (fileName.Equals("agency.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isFirstTime)
                        {
                            if (lines.Length > 0)
                                dataFiles[fileName].Add(lines[0].Split(','));
                            if (lines.Length > 1)
                                dataFiles[fileName].Add(lines[1].Split(','));
                        }
                        continue;
                    }

                    // 2) feed_info.txt => only keep first feed’s row
                    if (fileName.Equals("feed_info.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isFirstTime)
                        {
                            if (lines.Length > 0)
                                dataFiles[fileName].Add(lines[0].Split(','));
                            if (lines.Length > 1)
                                dataFiles[fileName].Add(lines[1].Split(','));
                        }
                        continue;
                    }

                    // 3) stops.txt => rename lat/lon if changed
                    if (fileName.Equals("stops.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStops(lines, fileName, isFirstTime, fileIndex);
                        continue;
                    }

                    // 4) calendar.txt => rename service_id if repeated
                    if (fileName.Equals("calendar.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessCalendar(lines, fileName, isFirstTime, fileIndex);
                        continue;
                    }

                    // 5) calendar_dates.txt => rename service_id if repeated
                    if (fileName.Equals("calendar_dates.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessCalendarDates(lines, fileName, isFirstTime, fileIndex);
                        continue;
                    }

                    // 6) routes.txt => skip duplicates by route_id
                    if (fileName.Equals("routes.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessRoutes(lines, fileName, isFirstTime);
                        continue;
                    }

                    // 7) stop_times.txt => update references if stops renamed
                    if (fileName.Equals("stop_times.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStopTimes(lines, fileName, isFirstTime);
                        continue;
                    }

                    // 8) trips.txt => update references if service_id renamed
                    if (fileName.Equals("trips.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessTrips(lines, fileName, isFirstTime);
                        continue;
                    }

                    // 9) everything else => fallback
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i == 0 && !isFirstTime)
                            continue;
                        dataFiles[fileName].Add(lines[i].Split(','));
                    }
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Error loading GTFS from {zipFilePath}: {ex.Message}");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                renamedStopIds = null;
                renamedServiceIds = null;
            }

            // store this feed's metadata
            feedMetadatas.Add(feedMeta);
        }

        // Sorter
        private static int CustomFileOrder(string path)
        {
            string f = Path.GetFileName(path).ToLowerInvariant();
            if (f == "stops.txt") return 0;
            if (f == "calendar.txt") return 1;
            if (f == "calendar_dates.txt") return 2;
            if (f == "stop_times.txt") return 3;
            if (f == "trips.txt") return 4;
            return 5;
        }

        // STOPS => rename if lat/lon differ
        private void ProcessStops(string[] lines, string fileName, bool isFirstFile, int fileIndex)
        {
            int stopIdIndex = -1, latIndex = -1, lonIndex = -1;

            if (isFirstFile && lines.Length > 0)
            {
                var header = lines[0].Split(',');
                dataFiles[fileName].Add(header);
                stopIdIndex = Array.IndexOf(header, "stop_id");
                latIndex = Array.IndexOf(header, "stop_lat");
                lonIndex = Array.IndexOf(header, "stop_lon");
            }
            else
            {
                var existingHeader = dataFiles[fileName][0];
                stopIdIndex = Array.IndexOf(existingHeader, "stop_id");
                latIndex = Array.IndexOf(existingHeader, "stop_lat");
                lonIndex = Array.IndexOf(existingHeader, "stop_lon");
            }

            for (int i = (isFirstFile ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !isFirstFile)
                    continue;

                var parts = lines[i].Split(',');
                if (stopIdIndex < 0 || stopIdIndex >= parts.Length)
                {
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                string stopId = parts[stopIdIndex];
                if (string.IsNullOrEmpty(stopId))
                {
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                // lat/lon
                string lat = (latIndex >= 0 && latIndex < parts.Length) ? parts[latIndex] : "";
                string lon = (lonIndex >= 0 && lonIndex < parts.Length) ? parts[lonIndex] : "";

                if (!knownStops.ContainsKey(stopId))
                {
                    knownStops[stopId] = (lat, lon);
                    dataFiles[fileName].Add(parts);
                }
                else
                {
                    if (fileIndex == 1)
                    {
                        dataFiles[fileName].Add(parts);
                    }
                    else
                    {
                        var oldVal = knownStops[stopId];
                        bool changed = (oldVal.Lat != lat || oldVal.Lon != lon);
                        if (changed)
                        {
                            string newId = stopId + "_Merged_" + fileIndex;
                            parts[stopIdIndex] = newId;
                            dataFiles[fileName].Add(parts);

                            knownStops[newId] = (lat, lon);
                            renamedStopIds[stopId] = newId;
                            Console.WriteLine($"[stops.txt] Renaming {stopId} => {newId}");
                        }
                        else
                        {
                            dataFiles[fileName].Add(parts);
                        }
                    }
                }
            }
        }

        // CALENDAR => rename if we see duplicate service_id in feed#2+
        private void ProcessCalendar(string[] lines, string fileName, bool isFirstFile, int fileIndex)
        {
            int svcIdx = -1;
            if (isFirstFile && lines.Length > 0)
            {
                var header = lines[0].Split(',');
                dataFiles[fileName].Add(header);
                svcIdx = Array.IndexOf(header, "service_id");
            }
            else
            {
                var existingHeader = dataFiles[fileName][0];
                svcIdx = Array.IndexOf(existingHeader, "service_id");
            }

            for (int i = (isFirstFile ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !isFirstFile)
                    continue;

                var parts = lines[i].Split(',');
                if (svcIdx < 0 || svcIdx >= parts.Length)
                {
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                string svcId = parts[svcIdx];
                if (string.IsNullOrEmpty(svcId))
                {
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                if (!knownServiceIds.Contains(svcId))
                {
                    knownServiceIds.Add(svcId);
                    dataFiles[fileName].Add(parts);
                }
                else
                {
                    if (fileIndex == 1)
                    {
                        dataFiles[fileName].Add(parts);
                    }
                    else
                    {
                        string newId = svcId + "_Merged_" + fileIndex;
                        parts[svcIdx] = newId;
                        dataFiles[fileName].Add(parts);

                        knownServiceIds.Add(newId);
                        renamedServiceIds[svcId] = newId;
                        Console.WriteLine($"[calendar.txt] Renaming service_id='{svcId}' => '{newId}'");
                    }
                }
            }
        }

        // CALENDAR_DATES => rename if repeated service_id
        private void ProcessCalendarDates(string[] lines, string fileName, bool isFirstFile, int fileIndex)
        {
            int svcIdx = -1;
            if (isFirstFile && lines.Length > 0)
            {
                var header = lines[0].Split(',');
                dataFiles[fileName].Add(header);
                svcIdx = Array.IndexOf(header, "service_id");
            }
            else
            {
                var existingHeader = dataFiles[fileName][0];
                svcIdx = Array.IndexOf(existingHeader, "service_id");
            }

            for (int i = (isFirstFile ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !isFirstFile)
                    continue;

                var parts = lines[i].Split(',');
                if (svcIdx < 0 || svcIdx >= parts.Length)
                {
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                string oldId = parts[svcIdx];
                if (string.IsNullOrEmpty(oldId))
                {
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                if (renamedServiceIds.ContainsKey(oldId))
                {
                    // update
                    string renameTo = renamedServiceIds[oldId];
                    parts[svcIdx] = renameTo;
                    dataFiles[fileName].Add(parts);
                    Console.WriteLine($"[calendar_dates.txt] updating {oldId} => {renameTo}");
                    continue;
                }

                if (!knownServiceIds.Contains(oldId))
                {
                    knownServiceIds.Add(oldId);
                    dataFiles[fileName].Add(parts);
                }
                else
                {
                    if (fileIndex == 1)
                    {
                        dataFiles[fileName].Add(parts);
                    }
                    else
                    {
                        string newId = oldId + "_Merged_" + fileIndex;
                        parts[svcIdx] = newId;
                        dataFiles[fileName].Add(parts);

                        knownServiceIds.Add(newId);
                        renamedServiceIds[oldId] = newId;
                        Console.WriteLine($"[calendar_dates.txt] Renaming {oldId} => {newId}");
                    }
                }
            }
        }

        // ROUTES => skip duplicates by route_id
        private void ProcessRoutes(string[] lines, string fileName, bool isFirstFile)
        {
            int routeIdIndex = -1;
            if (isFirstFile && lines.Length > 0)
            {
                var header = lines[0].Split(',');
                dataFiles[fileName].Add(header);
                routeIdIndex = Array.IndexOf(header, "route_id");
            }
            else
            {
                var existingHeader = dataFiles[fileName][0];
                routeIdIndex = Array.IndexOf(existingHeader, "route_id");
            }

            for (int i = (isFirstFile ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !isFirstFile)
                    continue;

                var parts = lines[i].Split(',');
                if (routeIdIndex >= 0 && routeIdIndex < parts.Length)
                {
                    string rid = parts[routeIdIndex];
                    if (!string.IsNullOrEmpty(rid) && !seenRouteIds.Contains(rid))
                    {
                        dataFiles[fileName].Add(parts);
                        seenRouteIds.Add(rid);
                    }
                }
                else
                {
                    dataFiles[fileName].Add(parts);
                }
            }
        }

        // STOP_TIMES => update references if stops renamed
        private void ProcessStopTimes(string[] lines, string fileName, bool isFirstFile)
        {
            int stopIdx = -1;
            if (isFirstFile && lines.Length > 0)
            {
                var header = lines[0].Split(',');
                dataFiles[fileName].Add(header);
                stopIdx = Array.IndexOf(header, "stop_id");
            }
            else
            {
                var existingHeader = dataFiles[fileName][0];
                stopIdx = Array.IndexOf(existingHeader, "stop_id");
            }

            for (int i = (isFirstFile ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !isFirstFile)
                    continue;

                var parts = lines[i].Split(',');
                if (stopIdx >= 0 && stopIdx < parts.Length)
                {
                    string oldStopId = parts[stopIdx];
                    if (!string.IsNullOrEmpty(oldStopId) && renamedStopIds.ContainsKey(oldStopId))
                    {
                        string newId = renamedStopIds[oldStopId];
                        parts[stopIdx] = newId;
                        Console.WriteLine($"[stop_times.txt] {oldStopId} => {newId}");
                    }
                }
                dataFiles[fileName].Add(parts);
            }
        }

        // TRIPS => update references if service_id renamed
        private void ProcessTrips(string[] lines, string fileName, bool isFirstFile)
        {
            int svcIdx = -1;
            if (isFirstFile && lines.Length > 0)
            {
                var header = lines[0].Split(',');
                dataFiles[fileName].Add(header);
                svcIdx = Array.IndexOf(header, "service_id");
            }
            else
            {
                var existingHeader = dataFiles[fileName][0];
                svcIdx = Array.IndexOf(existingHeader, "service_id");
            }

            for (int i = (isFirstFile ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !isFirstFile)
                    continue;

                var parts = lines[i].Split(',');
                if (svcIdx >= 0 && svcIdx < parts.Length)
                {
                    string oldId = parts[svcIdx];
                    if (!string.IsNullOrEmpty(oldId) && renamedServiceIds.ContainsKey(oldId))
                    {
                        string newId = renamedServiceIds[oldId];
                        parts[svcIdx] = newId;
                        Console.WriteLine($"[trips.txt] {oldId} => {newId}");
                    }
                }
                dataFiles[fileName].Add(parts);
            }
        }

        // Write out .txt
        public void WriteMergedFiles(string outDir)
        {
            foreach (var kvp in dataFiles)
            {
                string fn = kvp.Key;
                var rows = kvp.Value;
                string path = Path.Combine(outDir, fn);
                File.WriteAllLines(path, rows.Select(r => string.Join(",", r)));
            }

            // remove discrepancy
            string disc = Path.Combine(outDir, "discrepancy.txt");
            if (File.Exists(disc))
            {
                File.Delete(disc);
            }
        }

        // We store feed metadata in feedMetadatas. Now we create a log that matches your format
        public void CreateLogFile(string directoryPath)
        {
            string logFile = Path.Combine(directoryPath, "merge_log.txt");
            using (var writer = new StreamWriter(logFile, false))
            {
                writer.WriteLine("GTFS Merge Log");
                writer.WriteLine($"Generated on {DateTime.Now}");
                writer.WriteLine("================================================");
                writer.WriteLine();

                // For each feed => Print zip info, then unzipped .txt files
                foreach (var feed in feedMetadatas)
                {
                    writer.WriteLine($"ZIP File: {feed.ZipFilePath}");
                    writer.WriteLine($"   Size:    {feed.ZipFileSize} bytes");
                    writer.WriteLine($"   Created: {feed.ZipCreated}");
                    writer.WriteLine($"   Modified:{feed.ZipModified}");
                    writer.WriteLine($"   Contains {feed.InnerTxtFiles.Count} file(s) unzipped:");
                    writer.WriteLine();

                    foreach (var txtFileInfo in feed.InnerTxtFiles)
                    {
                        writer.WriteLine($"     - {txtFileInfo.FileName}");
                        writer.WriteLine($"        Size:    {txtFileInfo.Size} bytes");
                        writer.WriteLine($"        Created: {txtFileInfo.Created}");
                        writer.WriteLine($"        Modified:{txtFileInfo.Modified}");
                        writer.WriteLine();
                    }
                    writer.WriteLine("------------------------------------------------");
                    writer.WriteLine();
                }
            }
        }

        // Zip + Cleanup
        public void ZipMergedFiles(string directoryPath, string zipFilePath)
        {
            try
            {
                string[] requiredFiles = {
                    "agency.txt", "calendar.txt", "calendar_dates.txt", "feed_info.txt",
                    "routes.txt", "shapes.txt", "stop_times.txt", "stops.txt", "trips.txt"
                };

                // 1) create a temp
                string tempZipDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempZipDir);

                // 2) copy .txt except merge_log
                foreach (var fn in requiredFiles)
                {
                    string src = Path.Combine(directoryPath, fn);
                    if (File.Exists(src))
                    {
                        string dst = Path.Combine(tempZipDir, fn);
                        File.Copy(src, dst);
                    }
                }

                // 3) zip => zipFilePath
                ZipFile.CreateFromDirectory(tempZipDir, zipFilePath);

                // 4) cleanup
                Directory.Delete(tempZipDir, true);

                // 5) remove all .txt from directory, keep .zip & merge_log
                bool zipInSameFolder =
                    Path.GetDirectoryName(zipFilePath).Equals(directoryPath, StringComparison.OrdinalIgnoreCase);

                foreach (var file in Directory.GetFiles(directoryPath))
                {
                    string fName = Path.GetFileName(file);

                    if (fName.Equals("merge_log.txt", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (zipInSameFolder && file.Equals(zipFilePath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Error zipping merged files: {ex.Message}");
            }
        }

        // Basic feed metadata for logging
        private class FeedMetadata
        {
            public string ZipFilePath { get; set; }
            public long ZipFileSize { get; set; }
            public DateTime ZipCreated { get; set; }
            public DateTime ZipModified { get; set; }
            public List<InnerTxtInfo> InnerTxtFiles { get; set; }
        }

        private class InnerTxtInfo
        {
            public string FileName { get; set; }
            public long Size { get; set; }
            public DateTime Created { get; set; }
            public DateTime Modified { get; set; }
        }
    }
}

