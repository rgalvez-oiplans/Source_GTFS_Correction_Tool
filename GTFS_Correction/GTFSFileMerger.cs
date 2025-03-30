using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GTFS_Correction
{
    public class GTFSFileMerger
    {
        // Stores all merged rows, keyed by filename
        private readonly Dictionary<string, List<string[]>> dataFiles
            = new Dictionary<string, List<string[]>>();

        // For routes.txt duplicates
        private readonly HashSet<string> seenRouteIds = new HashSet<string>();

        // Known stops across all feeds:
        //   Key = stop_id, Value = (lat, lon)
        //   We never rename in feedIndex=1. If feedIndex>1 and lat/lon differ => rename.
        private readonly Dictionary<string, (string Lat, string Lon)> knownStops
            = new Dictionary<string, (string Lat, string Lon)>();

        // Known services across all feeds (for calendar & calendar_dates)
        //   Key = service_id, Value = fingerprint of row data (excluding the ID).
        //   If feedIndex>1 and row differs => rename.
        private readonly Dictionary<string, string> knownServiceFingerprints
            = new Dictionary<string, string>();

        // For each feed, track old->new renames (stops, service IDs)
        private Dictionary<string, string> renamedStopIds;
        private Dictionary<string, string> renamedServiceIds;

        /// <summary>
        /// Loads GTFS from a zip file. If fileIndex=1 (first feed), no IDs get renamed.
        /// For subsequent feeds, if an ID conflicts with known data, we rename to ID_Merged_{fileIndex}.
        /// </summary>
        public void LoadGTFSFile(string zipFilePath, int fileIndex)
        {
            renamedStopIds = new Dictionary<string, string>();
            renamedServiceIds = new Dictionary<string, string>();

            string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                Directory.CreateDirectory(tempDir);
                ZipFile.ExtractToDirectory(zipFilePath, tempDir);

                // Custom order: stops.txt < calendar.txt < calendar_dates.txt < stop_times.txt < trips.txt
                var files = Directory.GetFiles(tempDir, "*.txt")
                    .OrderBy(fn => CustomFileOrder(fn))
                    .ThenBy(fn => fn, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var filePath in files)
                {
                    string fileName = Path.GetFileName(filePath);
                    if (!dataFiles.ContainsKey(fileName))
                    {
                        dataFiles[fileName] = new List<string[]>();
                    }

                    var lines = File.ReadAllLines(filePath);
                    bool isFirstTimeForThisFile = (dataFiles[fileName].Count == 0);

                    if (fileName.Equals("agency.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        // Only keep first feed’s row
                        if (isFirstTimeForThisFile)
                        {
                            if (lines.Length > 0) dataFiles[fileName].Add(lines[0].Split(','));
                            if (lines.Length > 1) dataFiles[fileName].Add(lines[1].Split(','));
                        }
                        continue;
                    }

                    if (fileName.Equals("feed_info.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isFirstTimeForThisFile)
                        {
                            if (lines.Length > 0) dataFiles[fileName].Add(lines[0].Split(','));
                            if (lines.Length > 1) dataFiles[fileName].Add(lines[1].Split(','));
                        }
                        continue;
                    }

                    if (fileName.Equals("stops.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStops(lines, fileName, isFirstTimeForThisFile, fileIndex);
                        continue;
                    }

                    if (fileName.Equals("calendar.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessCalendar(lines, fileName, isFirstTimeForThisFile, fileIndex);
                        continue;
                    }

                    if (fileName.Equals("calendar_dates.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessCalendarDates(lines, fileName, isFirstTimeForThisFile, fileIndex);
                        continue;
                    }

                    if (fileName.Equals("routes.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessRoutes(lines, fileName, isFirstTimeForThisFile);
                        continue;
                    }

                    if (fileName.Equals("stop_times.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStopTimes(lines, fileName, isFirstTimeForThisFile);
                        continue;
                    }

                    if (fileName.Equals("trips.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessTrips(lines, fileName, isFirstTimeForThisFile);
                        continue;
                    }

                    // everything else => just merge
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i == 0 && !isFirstTimeForThisFile)
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
        }

        // Custom sort
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

        // ----------------------------------------------------------------
        // STOPS => if fileIndex=1 => no rename, else rename if lat/lon differs
        // ----------------------------------------------------------------
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

                string lat = (latIndex >= 0 && latIndex < parts.Length) ? parts[latIndex] : "";
                string lon = (lonIndex >= 0 && lonIndex < parts.Length) ? parts[lonIndex] : "";

                if (!knownStops.ContainsKey(stopId))
                {
                    knownStops[stopId] = (lat, lon);
                    dataFiles[fileName].Add(parts);
                }
                else
                {
                    // If first feed => no rename
                    if (fileIndex == 1)
                    {
                        dataFiles[fileName].Add(parts);
                    }
                    else
                    {
                        // subsequent feed => rename if lat/lon differs from known
                        var oldVal = knownStops[stopId];
                        if (oldVal.Lat != lat || oldVal.Lon != lon)
                        {
                            string newId = stopId + "_Merged_" + fileIndex;
                            parts[stopIdIndex] = newId;
                            dataFiles[fileName].Add(parts);

                            knownStops[newId] = (lat, lon);
                            renamedStopIds[stopId] = newId;
                            Console.WriteLine($"[stops.txt] Renaming {stopId} => {newId} (feed {fileIndex}, lat/lon changed)");
                        }
                        else
                        {
                            // same lat/lon => keep
                            dataFiles[fileName].Add(parts);
                        }
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // CALENDAR => if fileIndex=1 => no rename, else rename if row changed
        // ----------------------------------------------------------------
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

                // Build a fingerprint from the row minus service_id column
                string fp = BuildFingerprint(parts, svcIdx);

                if (!knownServiceFingerprints.ContainsKey(svcId))
                {
                    knownServiceFingerprints[svcId] = fp;
                    dataFiles[fileName].Add(parts);
                }
                else
                {
                    // first feed => keep
                    if (fileIndex == 1)
                    {
                        dataFiles[fileName].Add(parts);
                    }
                    else
                    {
                        // subsequent feed => rename if data changed
                        var oldFp = knownServiceFingerprints[svcId];
                        if (fp != oldFp)
                        {
                            string newId = svcId + "_Merged_" + fileIndex;
                            parts[svcIdx] = newId;
                            dataFiles[fileName].Add(parts);

                            knownServiceFingerprints[newId] = fp;
                            renamedServiceIds[svcId] = newId;
                            Console.WriteLine($"[calendar.txt] {svcId} => {newId} (feed {fileIndex}, row changed)");
                        }
                        else
                        {
                            dataFiles[fileName].Add(parts);
                        }
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // CALENDAR_DATES => if fileIndex=1 => no rename, else rename if row changed
        // Also update references if service_id was renamed in calendar
        // ----------------------------------------------------------------
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

                // If calendar renamed it, we update
                if (renamedServiceIds.ContainsKey(oldId))
                {
                    string renameTo = renamedServiceIds[oldId];
                    parts[svcIdx] = renameTo;
                    dataFiles[fileName].Add(parts);
                    Console.WriteLine($"[calendar_dates.txt] updating {oldId} => {renameTo} (due to prior rename in calendar)");
                    continue;
                }

                // Build fingerprint ignoring svcIdx
                string fp = BuildFingerprint(parts, svcIdx);

                if (!knownServiceFingerprints.ContainsKey(oldId))
                {
                    knownServiceFingerprints[oldId] = fp;
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
                        var oldFp = knownServiceFingerprints[oldId];
                        if (fp != oldFp)
                        {
                            string newId = oldId + "_Merged_" + fileIndex;
                            parts[svcIdx] = newId;
                            dataFiles[fileName].Add(parts);

                            knownServiceFingerprints[newId] = fp;
                            renamedServiceIds[oldId] = newId;
                            Console.WriteLine($"[calendar_dates.txt] {oldId} => {newId} (feed {fileIndex}, row changed)");
                        }
                        else
                        {
                            dataFiles[fileName].Add(parts);
                        }
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // ROUTES => skip duplicates by route_id
        // ----------------------------------------------------------------
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
                    else
                    {
                        // skip dup
                    }
                }
                else
                {
                    dataFiles[fileName].Add(parts);
                }
            }
        }

        // ----------------------------------------------------------------
        // STOP_TIMES => update references if stop was renamed
        // ----------------------------------------------------------------
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
                var existing = dataFiles[fileName][0];
                stopIdx = Array.IndexOf(existing, "stop_id");
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

        // ----------------------------------------------------------------
        // TRIPS => update references if service_id was renamed
        // ----------------------------------------------------------------
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
                var existing = dataFiles[fileName][0];
                svcIdx = Array.IndexOf(existing, "service_id");
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

        // ----------------------------------------------------------------
        // BuildFingerprint => skip the ID column, join the rest
        // ----------------------------------------------------------------
        private string BuildFingerprint(string[] row, int ignoreIndex)
        {
            return string.Join("|", row.Where((_, i) => i != ignoreIndex));
        }

        // ----------------------------------------------------------------
        // Write & Zip
        // ----------------------------------------------------------------
        public void WriteMergedFiles(string outDir)
        {
            foreach (var kvp in dataFiles)
            {
                string fn = kvp.Key;
                var rows = kvp.Value;
                string path = Path.Combine(outDir, fn);
                File.WriteAllLines(path, rows.Select(r => string.Join(",", r)));
            }

            string disc = Path.Combine(outDir, "discrepancy.txt");
            if (File.Exists(disc)) File.Delete(disc);
        }

        public void ZipMergedFiles(string directoryPath, string zipFilePath)
        {
            try
            {
                string[] requiredFiles = {
                    "agency.txt","calendar.txt","calendar_dates.txt","feed_info.txt",
                    "routes.txt","shapes.txt","stop_times.txt","stops.txt","trips.txt"
                };

                string tempZipDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempZipDir);

                foreach (var fn in requiredFiles)
                {
                    string src = Path.Combine(directoryPath, fn);
                    if (File.Exists(src))
                    {
                        string dst = Path.Combine(tempZipDir, fn);
                        File.Copy(src, dst);
                    }
                }
                ZipFile.CreateFromDirectory(tempZipDir, zipFilePath);
                Directory.Delete(tempZipDir, true);
            }
            catch (Exception ex)
            {
                throw new IOException($"Error zipping merged files: {ex.Message}");
            }
        }
    }
}

