using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GTFS_Correction
{
    public class GTFSFileMerger
    {
        // ----------------------------------------------------------------
        // Data Structures
        // ----------------------------------------------------------------
        private readonly Dictionary<string, List<string[]>> dataFiles
            = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);

        // We log each feed's info here
        private readonly List<FeedMetadata> feedMetadatas = new List<FeedMetadata>();

        // For routes => skip duplicates by route_id
        private readonly HashSet<string> seenRouteIds = new HashSet<string>();

        // For stops => rename lat/lon if changed
        private readonly Dictionary<string, (string Lat, string Lon)> knownStops
            = new Dictionary<string, (string Lat, string Lon)>();

        // For shapes => shape_id => feed index introduced
        private readonly Dictionary<string, int> shapeIntroducedAt
            = new Dictionary<string, int>();

        // For service => service_id => feed index introduced
        // (We only rename if it was introduced in an earlier feed than the current.)
        private readonly Dictionary<string, int> serviceIntroducedAt
            = new Dictionary<string, int>();

        // For trips => track known trip_ids
        private readonly HashSet<string> knownTripIds = new HashSet<string>();

        // Temporary rename maps per feed
        private Dictionary<string, string> renamedStopIds;
        private Dictionary<string, string> renamedServiceIds;
        private Dictionary<string, string> renamedShapeIds;
        private Dictionary<string, string> renamedTripIds;

        // ----------------------------------------------------------------
        // LOAD
        // ----------------------------------------------------------------
        public void LoadGTFSFile(string zipFilePath, int fileIndex)
        {
            // re‑init rename maps for each feed
            renamedStopIds = new Dictionary<string, string>();
            renamedServiceIds = new Dictionary<string, string>();
            renamedShapeIds = new Dictionary<string, string>();
            renamedTripIds = new Dictionary<string, string>();

            // For logging
            var zipInfo = new FileInfo(zipFilePath);
            var feedMeta = new FeedMetadata
            {
                ZipFilePath = zipFilePath,
                ZipFileSize = zipInfo.Length,
                ZipCreated = zipInfo.CreationTime,
                ZipModified = zipInfo.LastWriteTime,
                InnerTxtFiles = new List<InnerTxtInfo>()
            };

            // unzip to temp
            string tempDir = Path.Combine(Path.GetTempPath(), "gtfs_merge_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(zipFilePath, tempDir);

            try
            {
                // sort => feed_info last
                var txtFiles = Directory.GetFiles(tempDir, "*.txt")
                    .OrderBy(CustomFileOrder)
                    .ThenBy(fn => fn, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var file in txtFiles)
                {
                    string fileName = Path.GetFileName(file);

                    // log the extracted file
                    var fi = new FileInfo(file);
                    feedMeta.InnerTxtFiles.Add(new InnerTxtInfo
                    {
                        FileName = fileName,
                        Size = fi.Length,
                        Created = fi.CreationTime,
                        Modified = fi.LastWriteTime
                    });

                    if (!dataFiles.ContainsKey(fileName))
                        dataFiles[fileName] = new List<string[]>();

                    var lines = File.ReadAllLines(file);
                    bool isFirstTime = (dataFiles[fileName].Count == 0);

                    if (fileName.Equals("agency.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        KeepFirstFeed(lines, fileName, isFirstTime);
                        continue;
                    }
                    if (fileName.Equals("feed_info.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        KeepFirstFeed(lines, fileName, isFirstTime);
                        continue;
                    }
                    if (fileName.Equals("stops.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStops(lines, fileName, isFirstTime, fileIndex);
                        continue;
                    }
                    if (fileName.Equals("calendar.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessCalendar(lines, fileName, isFirstTime, fileIndex);
                        continue;
                    }
                    if (fileName.Equals("calendar_dates.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessCalendarDates(lines, fileName, isFirstTime, fileIndex);
                        continue;
                    }
                    if (fileName.Equals("shapes.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessShapesPreserveOrder(lines, fileName, isFirstTime, fileIndex);
                        continue;
                    }
                    if (fileName.Equals("trips.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessTrips(lines, fileName, isFirstTime, fileIndex);
                        continue;
                    }
                    if (fileName.Equals("stop_times.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStopTimes(lines, fileName, isFirstTime);
                        continue;
                    }
                    if (fileName.Equals("routes.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessRoutes(lines, fileName, isFirstTime);
                        continue;
                    }

                    // fallback => skip repeated header
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i == 0 && !isFirstTime) continue;
                        dataFiles[fileName].Add(lines[i].Split(','));
                    }
                }
            }
            finally
            {
                Directory.Delete(tempDir, true);
                feedMetadatas.Add(feedMeta);
            }
        }

        // ----------------------------------------------------------------
        // WRITE
        // ----------------------------------------------------------------
        public void WriteMergedFiles(string outDir)
        {
            foreach (var kv in dataFiles)
            {
                string fileName = kv.Key;
                var rows = kv.Value;
                File.WriteAllLines(Path.Combine(outDir, fileName),
                                   rows.Select(r => string.Join(",", r)));
            }
        }

        // ----------------------------------------------------------------
        // CREATE A MERGE_LOG.TXT
        // ----------------------------------------------------------------
        public void CreateLogFile(string outDir)
        {
            string logPath = Path.Combine(outDir, "merge_log.txt");
            using (var w = new StreamWriter(logPath, false))
            {
                w.WriteLine("GTFS Merge Log");
                w.WriteLine($"Generated on {DateTime.Now}");
                w.WriteLine("================================================\n");

                foreach (var fm in feedMetadatas)
                {
                    w.WriteLine($"ZIP File: {fm.ZipFilePath}");
                    w.WriteLine($"   Size:    {fm.ZipFileSize} bytes");
                    w.WriteLine($"   Created: {fm.ZipCreated}");
                    w.WriteLine($"   Modified:{fm.ZipModified}");
                    w.WriteLine($"   Contains {fm.InnerTxtFiles.Count} file(s) unzipped:\n");

                    foreach (var info in fm.InnerTxtFiles)
                    {
                        w.WriteLine($"     - {info.FileName}");
                        w.WriteLine($"        Size:    {info.Size} bytes");
                        w.WriteLine($"        Created: {info.Created}");
                        w.WriteLine($"        Modified:{info.Modified}\n");
                    }
                    w.WriteLine("------------------------------------------------\n");
                }
            }
        }

        // ----------------------------------------------------------------
        // ZIP => skip merge_log.txt
        // ----------------------------------------------------------------
        public void ZipMergedFiles(string directoryPath, string zipFilePath)
        {
            string[] keep =
            {
                "agency.txt","calendar.txt","calendar_dates.txt","feed_info.txt",
                "routes.txt","shapes.txt","stop_times.txt","stops.txt","trips.txt"
            };

            string tempZip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempZip);

            foreach (var fn in keep)
            {
                string src = Path.Combine(directoryPath, fn);
                if (File.Exists(src))
                {
                    string dst = Path.Combine(tempZip, fn);
                    File.Copy(src, dst);
                }
            }

            ZipFile.CreateFromDirectory(tempZip, zipFilePath);
            Directory.Delete(tempZip, true);

            bool zipInside = Path.GetDirectoryName(zipFilePath)
                .Equals(directoryPath, StringComparison.OrdinalIgnoreCase);

            foreach (var file in Directory.GetFiles(directoryPath))
            {
                string name = Path.GetFileName(file);

                if (name.Equals("merge_log.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (zipInside && file.Equals(zipFilePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                File.Delete(file);
            }
        }

        // ----------------------------------------------------------------
        // Sorting => feed_info last
        // ----------------------------------------------------------------
        private static int CustomFileOrder(string path)
        {
            string f = Path.GetFileName(path).ToLowerInvariant();
            if (f == "stops.txt") return 0;
            if (f == "calendar.txt") return 1;
            if (f == "calendar_dates.txt") return 2;
            if (f == "shapes.txt") return 3;
            if (f == "trips.txt") return 4;
            if (f == "stop_times.txt") return 5;
            if (f == "routes.txt") return 6;
            if (f == "agency.txt") return 7;
            if (f == "feed_info.txt") return 999;
            return 1000;
        }


        // STOPS => rename lat/lon if changed
        private void ProcessStops(string[] lines, string fileName, bool first, int fileIndex)
        {
            int stopIdx = -1, latIdx = -1, lonIdx = -1;
            if (first && lines.Length > 0)
            {
                var h = lines[0].Split(',');
                dataFiles[fileName].Add(h);
                stopIdx = Array.IndexOf(h, "stop_id");
                latIdx = Array.IndexOf(h, "stop_lat");
                lonIdx = Array.IndexOf(h, "stop_lon");
            }
            else
            {
                var ex = dataFiles[fileName][0];
                stopIdx = Array.IndexOf(ex, "stop_id");
                latIdx = Array.IndexOf(ex, "stop_lat");
                lonIdx = Array.IndexOf(ex, "stop_lon");
            }

            for (int i = (first ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !first) continue;
                var p = lines[i].Split(',');

                if (stopIdx < 0 || stopIdx >= p.Length) { dataFiles[fileName].Add(p); continue; }
                string oldStop = p[stopIdx];
                if (string.IsNullOrEmpty(oldStop)) { dataFiles[fileName].Add(p); continue; }

                string lat = (latIdx >= 0 && latIdx < p.Length) ? p[latIdx] : "";
                string lon = (lonIdx >= 0 && lonIdx < p.Length) ? p[lonIdx] : "";

                if (!knownStops.ContainsKey(oldStop))
                {
                    knownStops[oldStop] = (lat, lon);
                    dataFiles[fileName].Add(p);
                }
                else if (fileIndex == 1)
                {
                    dataFiles[fileName].Add(p);
                }
                else
                {
                    var prev = knownStops[oldStop];
                    if (prev.Lat != lat || prev.Lon != lon)
                    {
                        string newStop = oldStop + "_Merged_" + fileIndex;
                        p[stopIdx] = newStop;
                        dataFiles[fileName].Add(p);

                        knownStops[newStop] = (lat, lon);
                        renamedStopIds[oldStop] = newStop;
                    }
                    // else skip
                }
            }
        }

        // CALENDAR => rename repeated service_id if introduced earlier
        // The fix: we track `serviceIntroducedAt[service_id] = feedIndex`.
        // rename only if introducedFeed < fileIndex
        private void ProcessCalendar(string[] lines, string fileName, bool first, int fileIndex)
        {
            int svcIdx = -1;
            if (first && lines.Length > 0)
            {
                var h = lines[0].Split(',');
                dataFiles[fileName].Add(h);
                svcIdx = Array.IndexOf(h, "service_id");
            }
            else
            {
                svcIdx = Array.IndexOf(dataFiles[fileName][0], "service_id");
            }

            for (int i = (first ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !first) continue;
                var parts = lines[i].Split(',');

                if (svcIdx < 0 || svcIdx >= parts.Length)
                {
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                string oldSvc = parts[svcIdx];
                if (string.IsNullOrEmpty(oldSvc))
                {
                    dataFiles[fileName].Add(parts);
                    continue;
                }

                // If we haven't introduced oldSvc yet, record it for this feed
                if (!serviceIntroducedAt.ContainsKey(oldSvc))
                {
                    serviceIntroducedAt[oldSvc] = fileIndex;
                    dataFiles[fileName].Add(parts);
                }
                else
                {
                    // we already have oldSvc from some feed
                    int introducedFeed = serviceIntroducedAt[oldSvc];
                    if (introducedFeed < fileIndex)
                    {
                        // rename only if it came from earlier feed
                        string newId = oldSvc + "_Merged_" + fileIndex;
                        parts[svcIdx] = newId;
                        dataFiles[fileName].Add(parts);

                        serviceIntroducedAt[newId] = fileIndex;
                        renamedServiceIds[oldSvc] = newId;
                    }
                    else
                    {
                        // same feed => do not rename
                        dataFiles[fileName].Add(parts);
                    }
                }
            }
        }

        // CALENDAR_DATES => unify rename similarly
        private void ProcessCalendarDates(string[] lines, string fileName, bool first, int fileIndex)
        {
            int svcIdx = -1;
            if (first && lines.Length > 0)
            {
                var h = lines[0].Split(',');
                dataFiles[fileName].Add(h);
                svcIdx = Array.IndexOf(h, "service_id");
            }
            else
            {
                svcIdx = Array.IndexOf(dataFiles[fileName][0], "service_id");
            }

            for (int i = (first ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !first) continue;
                var p = lines[i].Split(',');

                if (svcIdx < 0 || svcIdx >= p.Length)
                {
                    dataFiles[fileName].Add(p);
                    continue;
                }

                string oldSvc = p[svcIdx];
                if (string.IsNullOrEmpty(oldSvc))
                {
                    dataFiles[fileName].Add(p);
                    continue;
                }

                // if we already renamed oldSvc
                if (renamedServiceIds.ContainsKey(oldSvc))
                {
                    p[svcIdx] = renamedServiceIds[oldSvc];
                    dataFiles[fileName].Add(p);
                }
                else if (!serviceIntroducedAt.ContainsKey(oldSvc))
                {
                    // brand new => record for current feed
                    serviceIntroducedAt[oldSvc] = fileIndex;
                    dataFiles[fileName].Add(p);
                }
                else
                {
                    // we introduced oldSvc before
                    int introducedFeed = serviceIntroducedAt[oldSvc];
                    if (introducedFeed < fileIndex)
                    {
                        // rename only if it came from an earlier feed
                        string newId = oldSvc + "_Merged_" + fileIndex;
                        p[svcIdx] = newId;
                        dataFiles[fileName].Add(p);

                        serviceIntroducedAt[newId] = fileIndex;
                        renamedServiceIds[oldSvc] = newId;
                    }
                    else
                    {
                        // same feed => do not rename
                        dataFiles[fileName].Add(p);
                    }
                }
            }
        }

        // SHAPES => preserve line order, rename if introduced earlier
        private void ProcessShapesPreserveOrder(string[] lines, string fileName, bool first, int fileIndex)
        {
            int shpIdx = -1;
            if (first && lines.Length > 0)
            {
                var h = lines[0].Split(',');
                dataFiles[fileName].Add(h);
                shpIdx = Array.IndexOf(h, "shape_id");
            }
            else
            {
                shpIdx = Array.IndexOf(dataFiles[fileName][0], "shape_id");
            }

            for (int i = (first ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !first) continue;
                var p = lines[i].Split(',');

                if (shpIdx < 0 || shpIdx >= p.Length)
                {
                    dataFiles[fileName].Add(p);
                    continue;
                }

                string oldShape = p[shpIdx];
                if (string.IsNullOrEmpty(oldShape))
                {
                    dataFiles[fileName].Add(p);
                    continue;
                }

                if (!shapeIntroducedAt.ContainsKey(oldShape))
                {
                    shapeIntroducedAt[oldShape] = fileIndex;
                    dataFiles[fileName].Add(p);
                }
                else
                {
                    int introducedFeed = shapeIntroducedAt[oldShape];
                    if (introducedFeed < fileIndex)
                    {
                        string newSid = oldShape + "_Merged_" + fileIndex;
                        p[shpIdx] = newSid;
                        dataFiles[fileName].Add(p);

                        shapeIntroducedAt[newSid] = fileIndex;
                        renamedShapeIds[oldShape] = newSid;
                    }
                    else
                    {
                        dataFiles[fileName].Add(p);
                    }
                }
            }
        }

        // TRIPS => rename duplicates, unify shape/service
        private void ProcessTrips(string[] lines, string fileName, bool first, int fileIndex)
        {
            int tripIdx = -1, svcIdx = -1, shapeIdx = -1;
            if (first && lines.Length > 0)
            {
                var h = lines[0].Split(',');
                dataFiles[fileName].Add(h);
                tripIdx = Array.IndexOf(h, "trip_id");
                svcIdx = Array.IndexOf(h, "service_id");
                shapeIdx = Array.IndexOf(h, "shape_id");
            }
            else
            {
                var ex = dataFiles[fileName][0];
                tripIdx = Array.IndexOf(ex, "trip_id");
                svcIdx = Array.IndexOf(ex, "service_id");
                shapeIdx = Array.IndexOf(ex, "shape_id");
            }

            for (int i = (first ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !first) continue;
                var p = lines[i].Split(',');

                // rename trip if introduced earlier
                if (tripIdx >= 0 && tripIdx < p.Length)
                {
                    string oldTrip = p[tripIdx];
                    if (!string.IsNullOrEmpty(oldTrip))
                    {
                        if (!knownTripIds.Contains(oldTrip))
                        {
                            knownTripIds.Add(oldTrip);
                        }
                        else if (fileIndex > 1)
                        {
                            string newTrip = oldTrip + "_Merged_" + fileIndex;
                            p[tripIdx] = newTrip;
                            knownTripIds.Add(newTrip);

                            renamedTripIds[oldTrip] = newTrip;
                        }
                    }
                }

                // unify service rename
                if (svcIdx >= 0 && svcIdx < p.Length)
                {
                    string oldSvc = p[svcIdx];
                    if (renamedServiceIds.ContainsKey(oldSvc))
                    {
                        // we had renamed it
                        p[svcIdx] = renamedServiceIds[oldSvc];
                    }
                    else
                    {
                        // or if it belongs to an earlier feed => rename
                        if (serviceIntroducedAt.ContainsKey(oldSvc))
                        {
                            int introducedFeed = serviceIntroducedAt[oldSvc];
                            if (introducedFeed < fileIndex)
                            {
                                // do we rename here or skip?
                                // Usually we skip if we didn't rename it in calendar
                                // but to unify approach, we can do:
                                if (!renamedServiceIds.ContainsKey(oldSvc))
                                {
                                    // rename
                                    string newSvc = oldSvc + "_Merged_" + fileIndex;
                                    p[svcIdx] = newSvc;
                                    renamedServiceIds[oldSvc] = newSvc;
                                    serviceIntroducedAt[newSvc] = fileIndex;
                                }
                            }
                        }
                    }
                }

                // unify shape rename
                if (shapeIdx >= 0 && shapeIdx < p.Length)
                {
                    string oldSh = p[shapeIdx];
                    if (renamedShapeIds.ContainsKey(oldSh))
                    {
                        p[shapeIdx] = renamedShapeIds[oldSh];
                    }
                }

                dataFiles[fileName].Add(p);
            }
        }

        // STOP_TIMES => unify references to stops & trips
        private void ProcessStopTimes(string[] lines, string fileName, bool first)
        {
            int tripIdx = -1, stopIdx = -1;
            if (first && lines.Length > 0)
            {
                var h = lines[0].Split(',');
                dataFiles[fileName].Add(h);
                tripIdx = Array.IndexOf(h, "trip_id");
                stopIdx = Array.IndexOf(h, "stop_id");
            }
            else
            {
                var ex = dataFiles[fileName][0];
                tripIdx = Array.IndexOf(ex, "trip_id");
                stopIdx = Array.IndexOf(ex, "stop_id");
            }

            for (int i = (first ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !first) continue;
                var p = lines[i].Split(',');

                if (tripIdx >= 0 && tripIdx < p.Length)
                {
                    string oldTrip = p[tripIdx];
                    if (renamedTripIds.ContainsKey(oldTrip))
                    {
                        p[tripIdx] = renamedTripIds[oldTrip];
                    }
                }

                if (stopIdx >= 0 && stopIdx < p.Length)
                {
                    string oldStop = p[stopIdx];
                    if (renamedStopIds.ContainsKey(oldStop))
                    {
                        p[stopIdx] = renamedStopIds[oldStop];
                    }
                }

                dataFiles[fileName].Add(p);
            }
        }

        // ROUTES => skip duplicates
        private void ProcessRoutes(string[] lines, string fileName, bool first)
        {
            int routeIdx = -1;
            if (first && lines.Length > 0)
            {
                var h = lines[0].Split(',');
                dataFiles[fileName].Add(h);
                routeIdx = Array.IndexOf(h, "route_id");
            }
            else
            {
                routeIdx = Array.IndexOf(dataFiles[fileName][0], "route_id");
            }

            for (int i = (first ? 1 : 0); i < lines.Length; i++)
            {
                if (i == 0 && !first) continue;
                var p = lines[i].Split(',');
                if (routeIdx < 0 || routeIdx >= p.Length)
                {
                    dataFiles[fileName].Add(p);
                    continue;
                }

                string rid = p[routeIdx];
                if (!string.IsNullOrEmpty(rid) && !seenRouteIds.Contains(rid))
                {
                    seenRouteIds.Add(rid);
                    dataFiles[fileName].Add(p);
                }
                // else skip
            }
        }


        private void KeepFirstFeed(string[] lines, string fileName, bool isFirst)
        {
            if (!isFirst) return;
            if (lines.Length > 0) dataFiles[fileName].Add(lines[0].Split(','));
            if (lines.Length > 1) dataFiles[fileName].Add(lines[1].Split(','));
        }

        // ----------------------------------------------------------------
        // Inner classes for logging
        // ----------------------------------------------------------------
        private class FeedMetadata
        {
            public string ZipFilePath;
            public long ZipFileSize;
            public DateTime ZipCreated;
            public DateTime ZipModified;
            public List<InnerTxtInfo> InnerTxtFiles;
        }
        private class InnerTxtInfo
        {
            public string FileName;
            public long Size;
            public DateTime Created;
            public DateTime Modified;
        }
    }
}
