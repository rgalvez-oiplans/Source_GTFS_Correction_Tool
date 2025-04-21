using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace GTFS_Correction
{
    public class GTFSFileMerger
    {
        // ------------------------------------------------------
        //  Data fields for merges
        // ------------------------------------------------------

        // In-memory data: each filename → list of CSV rows
        private readonly Dictionary<string, List<string[]>> dataFiles
            = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);

        // known route_id duplicates
        private readonly HashSet<string> seenRouteIds = new HashSet<string>();

        // known stops: stop_id → (lat, lon)
        // (this still does the lat/lon rename if changed)
        private readonly Dictionary<string, (string Lat, string Lon)> knownStops
            = new Dictionary<string, (string Lat, string Lon)>();

        // known service_id
        private readonly HashSet<string> knownServiceIds = new HashSet<string>();

        // known shapes: shape_id → feed index that introduced it
        // (We only rename if shape was introduced in a strictly earlier feed.)
        private readonly Dictionary<string, int> shapeIntroducedAt
            = new Dictionary<string, int>();

        // known trip_ids
        private readonly HashSet<string> knownTripIds = new HashSet<string>();

        // rename maps for stops, service, shapes, trips
        private Dictionary<string, string> renamedStopIds;
        private Dictionary<string, string> renamedServiceIds;
        private Dictionary<string, string> renamedShapeIds;
        private Dictionary<string, string> renamedTripIds;

        // For logging
        private readonly List<FeedMetadata> feedMetadatas = new List<FeedMetadata>();

        // ------------------------------------------------------
        //  PUBLIC methods
        // ------------------------------------------------------

        /// <summary>
        /// Loads one GTFS zip feed, merges it into memory, renaming duplicates.
        /// Now shapes logic always preserves line order.
        /// If shape_id was introduced in an earlier feed => rename; if new => no rename.
        /// </summary>
        public void LoadGTFSFile(string zipFilePath, int fileIndex)
        {
            // Re-init rename dictionaries for each feed
            renamedStopIds = new Dictionary<string, string>();
            renamedServiceIds = new Dictionary<string, string>();
            renamedShapeIds = new Dictionary<string, string>();
            renamedTripIds = new Dictionary<string, string>();

            // Logging info
            var zipInfo = new FileInfo(zipFilePath);
            var feedMeta = new FeedMetadata
            {
                ZipFilePath = zipFilePath,
                ZipFileSize = zipInfo.Length,
                ZipCreated = zipInfo.CreationTime,
                ZipModified = zipInfo.LastWriteTime,
                InnerTxtFiles = new List<InnerTxtInfo>()
            };

            // Unzip to temp folder
            string tempDir = Path.Combine(Path.GetTempPath(), "gtfs_merge_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(zipFilePath, tempDir);

            try
            {
                var txtFiles = Directory.GetFiles(tempDir, "*.txt")
                    .OrderBy(CustomFileOrder)
                    .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var filePath in txtFiles)
                {
                    string fileName = Path.GetFileName(filePath);

                    // record each txt file for logging
                    var fi = new FileInfo(filePath);
                    feedMeta.InnerTxtFiles.Add(new InnerTxtInfo
                    {
                        FileName = fileName,
                        Size = fi.Length,
                        Created = fi.CreationTime,
                        Modified = fi.LastWriteTime
                    });

                    if (!dataFiles.ContainsKey(fileName))
                        dataFiles[fileName] = new List<string[]>();

                    var lines = File.ReadAllLines(filePath);
                    bool isFirstTime = (dataFiles[fileName].Count == 0);

                    // dispatch
                    if (fileName.Equals("agency.txt", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Equals("feed_info.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        KeepFirstFeed(lines, fileName, isFirstTime);
                        continue;
                    }
                    if (fileName.Equals("stops.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessStops(lines, fileName, isFirstTime, fileIndex);  // lat/lon rename remains
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
                        // *** Key update: preserve line order
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

                    // fallback => skip 2nd+ header
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i == 0 && !isFirstTime) continue;
                        dataFiles[fileName].Add(lines[i].Split(','));
                    }
                }
            }
            finally
            {
                // clean up temp
                Directory.Delete(tempDir, true);
                feedMetadatas.Add(feedMeta);
            }
        }

        /// <summary>Write out all in-memory .txt to the specified folder.</summary>
        public void WriteMergedFiles(string outDir)
        {
            foreach (var kv in dataFiles)
            {
                string path = Path.Combine(outDir, kv.Key);
                var rows = kv.Value;
                File.WriteAllLines(path, rows.Select(r => string.Join(",", r)));
            }
        }

        /// <summary>Create a merge_log.txt listing feed metadata.</summary>
        public void CreateLogFile(string outDir)
        {
            string logPath = Path.Combine(outDir, "merge_log.txt");
            using (var w = new StreamWriter(logPath, false))
            {
                w.WriteLine("GTFS Merge Log");
                w.WriteLine("Generated on " + DateTime.Now);
                w.WriteLine("================================================\n");

                foreach (var fm in feedMetadatas)
                {
                    w.WriteLine("ZIP File: " + fm.ZipFilePath);
                    w.WriteLine("   Size:    " + fm.ZipFileSize + " bytes");
                    w.WriteLine("   Created: " + fm.ZipCreated);
                    w.WriteLine("   Modified:" + fm.ZipModified);
                    w.WriteLine("   Contains " + fm.InnerTxtFiles.Count + " file(s) unzipped:\n");

                    foreach (var t in fm.InnerTxtFiles)
                    {
                        w.WriteLine("     - " + t.FileName);
                        w.WriteLine("        Size:    " + t.Size + " bytes");
                        w.WriteLine("        Created: " + t.Created);
                        w.WriteLine("        Modified:" + t.Modified + "\n");
                    }
                    w.WriteLine("------------------------------------------------\n");
                }
            }
        }

        /// <summary>Zip the standard GTFS text files; remove them from the output folder except merge_log.txt.</summary>
        public void ZipMergedFiles(string directoryPath, string zipFilePath)
        {
            string[] keep =
            {
                "agency.txt","calendar.txt","calendar_dates.txt","feed_info.txt",
                "routes.txt","shapes.txt","stop_times.txt","stops.txt","trips.txt"
            };

            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dir");
            Directory.CreateDirectory(tmp);

            foreach (string fn in keep)
            {
                string src = Path.Combine(directoryPath, fn);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(tmp, fn));
            }

            ZipFile.CreateFromDirectory(tmp, zipFilePath);
            Directory.Delete(tmp, true);

            bool zipInside = Path.GetDirectoryName(zipFilePath)
                .Equals(directoryPath, StringComparison.OrdinalIgnoreCase);

            foreach (string f in Directory.GetFiles(directoryPath))
            {
                string name = Path.GetFileName(f);
                if (name.Equals("merge_log.txt", StringComparison.OrdinalIgnoreCase)) continue;
                if (zipInside && f.Equals(zipFilePath, StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(f);
            }
        }

        // ------------------------------------------------------
        //  ORDER => shapes BEFORE trips
        // ------------------------------------------------------
        private static int CustomFileOrder(string path)
        {
            string f = Path.GetFileName(path).ToLowerInvariant();
            if (f == "stops.txt") return 0;
            if (f == "calendar.txt") return 1;
            if (f == "calendar_dates.txt") return 2;
            if (f == "shapes.txt") return 3;  // shapes before trips
            if (f == "trips.txt") return 4;  // trips before stop_times
            if (f == "stop_times.txt") return 5;
            if (f == "routes.txt") return 6;
            return 7;
        }

        // Keep only first feed's row for agency/feed_info
        private void KeepFirstFeed(string[] lines, string fileName, bool isFirstTime)
        {
            if (!isFirstTime) return;
            if (lines.Length > 0)
                dataFiles[fileName].Add(lines[0].Split(','));
            if (lines.Length > 1)
                dataFiles[fileName].Add(lines[1].Split(','));
        }

        // ------------------------------------------------------------------
        // STOPS => rename if lat/lon changed in feed2+ (unchanged logic)
        // ------------------------------------------------------------------
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

                // lat/lon
                string lat = (latIdx >= 0 && latIdx < p.Length) ? p[latIdx] : "";
                string lon = (lonIdx >= 0 && lonIdx < p.Length) ? p[lonIdx] : "";

                if (!knownStops.ContainsKey(oldStop))
                {
                    knownStops[oldStop] = (lat, lon);
                    dataFiles[fileName].Add(p);
                }
                else if (fileIndex == 1)
                {
                    // feed #1 => keep duplicates
                    dataFiles[fileName].Add(p);
                }
                else
                {
                    // feed #2+ => rename if lat/lon differ
                    var prev = knownStops[oldStop];
                    if (prev.Lat != lat || prev.Lon != lon)
                    {
                        string newStop = oldStop + "_Merged_" + fileIndex;
                        p[stopIdx] = newStop;
                        dataFiles[fileName].Add(p);

                        knownStops[newStop] = (lat, lon);
                        renamedStopIds[oldStop] = newStop;
                        Console.WriteLine($"[stops] rename {oldStop} => {newStop}");
                    }
                    // else identical => skip
                }
            }
        }

        // ------------------------------------------------------------------
        //  CALENDAR => rename repeated service_id
        // ------------------------------------------------------------------
        private void ProcessCalendar(string[] lines, string fileName, bool first, int fileIndex)
        {
            ProcessService(lines, fileName, first, fileIndex, false);
        }

        private void ProcessCalendarDates(string[] lines, string fileName, bool first, int fileIndex)
        {
            ProcessService(lines, fileName, first, fileIndex, true);
        }

        private void ProcessService(string[] lines, string fileName,
                                    bool first, int fileIndex, bool isCalendarDates)
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

                if (svcIdx < 0 || svcIdx >= p.Length) { dataFiles[fileName].Add(p); continue; }
                string oldSvc = p[svcIdx];
                if (string.IsNullOrEmpty(oldSvc))
                {
                    dataFiles[fileName].Add(p);
                    continue;
                }

                // if feed2+ modifies calendar_dates referencing an already renamed service
                if (isCalendarDates && renamedServiceIds.ContainsKey(oldSvc))
                {
                    p[svcIdx] = renamedServiceIds[oldSvc];
                    dataFiles[fileName].Add(p);
                    Console.WriteLine($"[calendar_dates] updating {oldSvc} => {renamedServiceIds[oldSvc]}");
                    continue;
                }

                if (!knownServiceIds.Contains(oldSvc))
                {
                    knownServiceIds.Add(oldSvc);
                    dataFiles[fileName].Add(p);
                }
                else if (fileIndex == 1)
                {
                    dataFiles[fileName].Add(p);
                }
                else
                {
                    string newId = oldSvc + "_Merged_" + fileIndex;
                    p[svcIdx] = newId;
                    dataFiles[fileName].Add(p);

                    knownServiceIds.Add(newId);
                    renamedServiceIds[oldSvc] = newId;
                    Console.WriteLine($"[{fileName}] rename service_id {oldSvc} => {newId}");
                }
            }
        }

        // ------------------------------------------------------------------
        //  SHAPES => preserve line order, rename if introduced in earlier feed
        // ------------------------------------------------------------------
        private void ProcessShapesPreserveOrder(string[] lines, string fileName,
                                                bool first, int fileIndex)
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

            // always append lines => preserve shapes order
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

                // brand new shape => record feed index, no rename
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
                        // shape_id was in an earlier feed => rename
                        string newSid = oldShape + "_Merged_" + fileIndex;
                        p[shpIdx] = newSid;
                        dataFiles[fileName].Add(p);

                        shapeIntroducedAt[newSid] = fileIndex;
                        renamedShapeIds[oldShape] = newSid;

                        Console.WriteLine($"[shapes] rename {oldShape} => {newSid} (introducedIn={introducedFeed} < {fileIndex})");
                    }
                    else
                    {
                        // same feed => do not rename => just add line
                        dataFiles[fileName].Add(p);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        //  TRIPS => rename duplicates, unify shape references
        // ------------------------------------------------------------------
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

                // rename trip if introduced in earlier feed
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
                            Console.WriteLine($"[trips] rename {oldTrip} => {newTrip}");
                        }
                    }
                }

                // unify service_id rename
                if (svcIdx >= 0 && svcIdx < p.Length)
                {
                    string oldSvc = p[svcIdx];
                    if (!string.IsNullOrEmpty(oldSvc) &&
                        renamedServiceIds.ContainsKey(oldSvc))
                    {
                        p[svcIdx] = renamedServiceIds[oldSvc];
                    }
                }

                // unify shape_id rename
                if (shapeIdx >= 0 && shapeIdx < p.Length)
                {
                    string oldSh = p[shapeIdx];
                    if (!string.IsNullOrEmpty(oldSh) &&
                        renamedShapeIds.ContainsKey(oldSh))
                    {
                        p[shapeIdx] = renamedShapeIds[oldSh];
                    }
                }

                dataFiles[fileName].Add(p);
            }
        }

        // ------------------------------------------------------------------
        //  STOP_TIMES => unify references for stops, trips
        // ------------------------------------------------------------------
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

                // unify trip
                if (tripIdx >= 0 && tripIdx < p.Length)
                {
                    string oldTrip = p[tripIdx];
                    if (renamedTripIds.ContainsKey(oldTrip))
                    {
                        p[tripIdx] = renamedTripIds[oldTrip];
                    }
                }

                // unify stop
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

        // ------------------------------------------------------------------
        //  ROUTES => skip duplicates
        // ------------------------------------------------------------------
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

        // ------------------------------------------------------
        //  SUPPORT CLASSES
        // ------------------------------------------------------
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
