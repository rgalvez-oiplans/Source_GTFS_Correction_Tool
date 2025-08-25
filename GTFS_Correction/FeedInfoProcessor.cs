using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GTFS_Correction
{
    public class FeedInfoProcessor
    {
        private readonly Action<string, bool> updateStatusAction;

        // Fields loaded from agency + config
        private string agencyName;
        private string agencyUrl;
        private string feedInfoFilePath;
        private string feedContactEmail;
        private string feedContactUrl;

        // ----------------------------------------------------------------
        // Constructor
        // ----------------------------------------------------------------
        public FeedInfoProcessor(Action<string, bool> updateStatusAction)
        {
            this.updateStatusAction = updateStatusAction;
        }

        // ----------------------------------------------------------------
        // 1) Existing 5-parameter method (backwards compatibility)
        // ----------------------------------------------------------------
        public void ProcessFeedInfo(
            string agencyFilePath,
            string calendarFilePath,
            string feedInfoFilePath,
            string configFilePath,
            string logFilePath)
        {
            // store path so we can refer to it
            this.feedInfoFilePath = feedInfoFilePath;

            // 1) load agency + config => sets agencyName, agencyUrl, etc.
            LoadAgencyData(agencyFilePath);
            LoadConfigData(configFilePath);

            // 2) create feed_info if it doesn’t exist
            if (!File.Exists(feedInfoFilePath))
            {
                updateStatusAction("feed_info.txt does not exist. Creating feed_info.txt...", false);
                CreateFeedInfo(calendarFilePath, logFilePath);
            }
            else
            {
                updateStatusAction("feed_info.txt exists. Checking for unrecognized columns...", false);
                CheckAndCleanFeedInfo(logFilePath);
            }
        }

        // ----------------------------------------------------------------
        // 2) New Method: 
        //    UpdateOrCreateFeedInfoFromMergedCalendar(...) 
        //    (for final merged calendar logic)
        // ----------------------------------------------------------------
        public void UpdateOrCreateFeedInfoFromMergedCalendar(
            string mergedCalendarPath,
            string mergedFeedInfoPath)
        {
            // parse earliest+latest from merged calendar
            var (feedStart, feedEnd) = GetMinMaxDateFromCalendar(mergedCalendarPath);
            if (feedStart == DateTime.MaxValue || feedEnd == DateTime.MinValue)
            {
                updateStatusAction("No valid start/end date in merged calendar.txt. feed_info not updated.", false);
                return;
            }

            if (!File.Exists(mergedFeedInfoPath))
            {
                // feed_info does not exist => create brand-new
                updateStatusAction("Merged feed_info.txt not found => creating new...", false);
                CreateFeedInfoInternal(mergedFeedInfoPath, feedStart, feedEnd);
            }
            else
            {
                // feed_info does exist => update feed_start_date / feed_end_date
                updateStatusAction("Merged feed_info.txt found => updating start/end date...", false);
                UpdateExistingFeedInfo(mergedFeedInfoPath, feedStart, feedEnd);
            }
        }

        // ----------------------------------------------------------------
        // Creates new feed_info from the original (non-merged) calendar
        // ----------------------------------------------------------------
        private void CreateFeedInfo(string calendarFilePath, string logFilePath)
        {
            DateTime feedStartDate = DateTime.MaxValue;
            DateTime feedEndDate = DateTime.MinValue;

            // parse earliest+latest from the local (non-merged) calendar
            using (var reader = new StreamReader(calendarFilePath))
            {
                var header = reader.ReadLine();
                var colMap = GetColumnIndices(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var startDate = DateTime.ParseExact(parts[colMap["start_date"]], "yyyyMMdd", CultureInfo.InvariantCulture);
                    var endDate = DateTime.ParseExact(parts[colMap["end_date"]], "yyyyMMdd", CultureInfo.InvariantCulture);

                    if (startDate < feedStartDate) feedStartDate = startDate;
                    if (endDate > feedEndDate) feedEndDate = endDate;
                }
            }

            var feedVersion = $"S{feedStartDate:yyyyMMdd}_E{feedEndDate:yyyyMMdd}_TS{DateTime.Now:yyyyMMddHHmmss}";
            var feedInfoLines = new List<string>
            {
                "feed_publisher_name,feed_publisher_url,feed_lang,feed_start_date,feed_end_date,feed_version,feed_contact_email,feed_contact_url",
                $"{agencyName},{agencyUrl},en,{feedStartDate:yyyyMMdd},{feedEndDate:yyyyMMdd},{feedVersion},{feedContactEmail},{feedContactUrl}"
            };

            File.WriteAllLines(feedInfoFilePath, feedInfoLines);
            updateStatusAction("feed_info.txt created successfully.", false);

            using (var logWriter = new StreamWriter(logFilePath, true))
            {
                logWriter.WriteLine("feed_info.txt created with the following columns:");
                logWriter.WriteLine("feed_publisher_name,feed_publisher_url,feed_lang,feed_start_date,feed_end_date,feed_version,feed_contact_email,feed_contact_url");
            }
        }

        // ----------------------------------------------------------------
        // Check feed_info columns => remove invalid, ensure required
        // ----------------------------------------------------------------
        private void CheckAndCleanFeedInfo(string logFilePath)
        {
            var requiredColumns = new List<string>
            {
                "feed_publisher_name",
                "feed_publisher_url",
                "feed_lang"
            };
            var recommendedColumns = new List<string>
            {
                "feed_start_date",
                "feed_end_date",
                "feed_version",
                "feed_contact_email",
                "feed_contact_url"
            };

            var feedInfoLines = File.ReadAllLines(feedInfoFilePath).ToList();
            if (feedInfoLines.Count == 0) return;

            var header = feedInfoLines[0];
            var columns = header.Split(',');

            var validColumns = columns
                .Intersect(requiredColumns.Concat(recommendedColumns))
                .ToList();
            var invalidColumns = columns
                .Except(requiredColumns.Concat(recommendedColumns))
                .ToList();

            // remove invalid
            if (invalidColumns.Any())
            {
                updateStatusAction("Removing unrecognized columns from feed_info.txt...", false);
                using (var logWriter = new StreamWriter(logFilePath, true))
                {
                    logWriter.WriteLine("Removing unrecognized columns from feed_info.txt:");
                    foreach (var invalidColumn in invalidColumns)
                    {
                        logWriter.WriteLine($"- {invalidColumn}");
                    }
                }

                var columnIndices = columns
                    .Select((col, i) => new { col, i })
                    .Where(x => validColumns.Contains(x.col))
                    .ToDictionary(x => x.col, x => x.i);

                var cleanedLines = new List<string>
                {
                    string.Join(",", validColumns)
                };

                for (int i = 1; i < feedInfoLines.Count; i++)
                {
                    var parts = feedInfoLines[i].Split(',');
                    var cleanedParts = columnIndices.Values
                                                    .Select(idx => (idx < parts.Length) ? parts[idx] : "")
                                                    .ToList();
                    cleanedLines.Add(string.Join(",", cleanedParts));
                }

                File.WriteAllLines(feedInfoFilePath, cleanedLines);
                updateStatusAction("Unrecognized columns removed from feed_info.txt.", false);
            }
            else
            {
                updateStatusAction("No unrecognized columns found in feed_info.txt.", false);
            }

            // add missing required or recommended columns
            feedInfoLines = File.ReadAllLines(feedInfoFilePath).ToList();
            header = feedInfoLines[0];
            columns = header.Split(',');

            var allNeeded = requiredColumns.Concat(recommendedColumns).ToList();
            var newCols = columns.ToList();
            foreach (var col in allNeeded)
            {
                if (!newCols.Contains(col))
                {
                    newCols.Add(col);
                }
            }

            if (newCols.Count > columns.Length)
            {
                feedInfoLines[0] = string.Join(",", newCols);

                for (int i = 1; i < feedInfoLines.Count; i++)
                {
                    var parts = feedInfoLines[i].Split(',').ToList();
                    while (parts.Count < newCols.Count)
                    {
                        parts.Add("");
                    }
                    feedInfoLines[i] = string.Join(",", parts);
                }
                File.WriteAllLines(feedInfoFilePath, feedInfoLines);
                updateStatusAction("Missing required/recommended columns were added to feed_info.txt.", false);
            }
        }

        // ----------------------------------------------------------------
        // parse earliest+latest from a final merged calendar
        // ----------------------------------------------------------------
        private (DateTime, DateTime) GetMinMaxDateFromCalendar(string calendarPath)
        {
            DateTime earliest = DateTime.MaxValue;
            DateTime latest = DateTime.MinValue;

            if (!File.Exists(calendarPath))
                return (earliest, latest);

            using (var reader = new StreamReader(calendarPath))
            {
                var header = reader.ReadLine();
                if (string.IsNullOrEmpty(header))
                    return (earliest, latest);

                var colMap = GetColumnIndices(header);
                if (!colMap.ContainsKey("start_date") || !colMap.ContainsKey("end_date"))
                    return (earliest, latest);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');
                    if (parts.Length <= colMap["end_date"]) continue;

                    if (DateTime.TryParseExact(parts[colMap["start_date"]], "yyyyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime sdt) &&
                        DateTime.TryParseExact(parts[colMap["end_date"]], "yyyyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime edt))
                    {
                        if (sdt < earliest) earliest = sdt;
                        if (edt > latest) latest = edt;
                    }
                }
            }
            return (earliest, latest);
        }

        // ----------------------------------------------------------------
        // create brand-new feed_info from merged start/end date
        // ----------------------------------------------------------------
        private void CreateFeedInfoInternal(
            string feedInfoPath,
            DateTime feedStartDate,
            DateTime feedEndDate)
        {
            var feedVersion = $"S{feedStartDate:yyyyMMdd}_E{feedEndDate:yyyyMMdd}_TS{DateTime.Now:yyyyMMddHHmmss}";
            var lines = new List<string>
            {
                "feed_publisher_name,feed_publisher_url,feed_lang,feed_start_date,feed_end_date,feed_version,feed_contact_email,feed_contact_url",
                $"{agencyName},{agencyUrl},en,{feedStartDate:yyyyMMdd},{feedEndDate:yyyyMMdd},{feedVersion},{feedContactEmail},{feedContactUrl}"
            };

            File.WriteAllLines(feedInfoPath, lines);
            updateStatusAction("feed_info.txt created from merged calendar data.", false);
        }

        // ----------------------------------------------------------------
        // update existing feed_info => feed_start/end date columns
        // ----------------------------------------------------------------
        private void UpdateExistingFeedInfo(
            string feedInfoPath,
            DateTime startDate,
            DateTime endDate)
        {
            var lines = File.ReadAllLines(feedInfoPath).ToList();
            if (lines.Count < 2)
            {
                // only header => treat as new
                updateStatusAction("feed_info.txt has no data row => overwriting with new feed_info", false);
                CreateFeedInfoInternal(feedInfoPath, startDate, endDate);
                return;
            }

            var header = lines[0].Split(',');
            var data = lines[1].Split(',');

            // build colMap
            var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                colMap[header[i]] = i;
            }

            // build new feed_version same as create method
            var feedVersion = $"S{startDate:yyyyMMdd}_E{endDate:yyyyMMdd}_TS{DateTime.Now:yyyyMMddHHmmss}";


            // if these columns exist => update them
            if (colMap.ContainsKey("feed_start_date") && colMap["feed_start_date"] < data.Length)
            {
                data[colMap["feed_start_date"]] = startDate.ToString("yyyyMMdd");
            }
            if (colMap.ContainsKey("feed_end_date") && colMap["feed_end_date"] < data.Length)
            {
                data[colMap["feed_end_date"]] = endDate.ToString("yyyyMMdd");
            }
            if (colMap.ContainsKey("feed_version") && colMap["feed_version"] < data.Length)
            {
                data[colMap["feed_version"]] = feedVersion;
            }

                lines[1] = string.Join(",", data);
            File.WriteAllLines(feedInfoPath, lines);

            updateStatusAction("feed_info.txt updated with merged start/end date.", false);

            // optionally re-check columns => pass a logFile if desired
            // CheckAndCleanFeedInfo("feedinfoLog.txt"); 
        }

        // ----------------------------------------------------------------
        // load agency => sets agencyName, agencyUrl
        // ----------------------------------------------------------------
        private void LoadAgencyData(string agencyFilePath)
        {
            if (!File.Exists(agencyFilePath))
                return;

            using (var reader = new StreamReader(agencyFilePath))
            {
                var header = reader.ReadLine();
                var colMap = GetColumnIndices(header);

                // only read first data row
                if (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');
                    if (colMap.ContainsKey("agency_name"))
                        agencyName = parts[colMap["agency_name"]];
                    if (colMap.ContainsKey("agency_url"))
                        agencyUrl = parts[colMap["agency_url"]];
                }
            }
        }

        // load config => feed_contact
        private void LoadConfigData(string configFilePath)
        {
            if (!File.Exists(configFilePath)) return;

            var lines = File.ReadAllLines(configFilePath);
            foreach (var ln in lines)
            {
                var eq = ln.Split('=');
                if (eq.Length == 2)
                {
                    var k = eq[0].Trim();
                    var v = eq[1].Trim();

                    if (k == "feed_contact_email") feedContactEmail = v;
                    else if (k == "feed_contact_url") feedContactUrl = v;
                }
            }
        }

        // ----------------------------------------------------------------
        // parse CSV header => dictionary colName -> index
        // ----------------------------------------------------------------
        private Dictionary<string, int> GetColumnIndices(string header)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var cols = header.Split(',');
            for (int i = 0; i < cols.Length; i++)
            {
                dict[cols[i]] = i;
            }
            return dict;
        }
    }
}

