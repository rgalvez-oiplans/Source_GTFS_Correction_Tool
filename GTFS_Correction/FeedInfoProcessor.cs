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
        private string agencyName;
        private string agencyUrl;
        private string feedInfoFilePath;
        private string feedContactEmail;
        private string feedContactUrl;

        public FeedInfoProcessor(Action<string, bool> updateStatusAction)
        {
            this.updateStatusAction = updateStatusAction;
        }

        public void ProcessFeedInfo(string agencyFilePath, string calendarFilePath, string feedInfoFilePath, string configFilePath, string logFilePath)
        {
            this.feedInfoFilePath = feedInfoFilePath;
            LoadAgencyData(agencyFilePath);
            LoadConfigData(configFilePath);

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

        private void LoadAgencyData(string agencyFilePath)
        {
            using (var reader = new StreamReader(agencyFilePath))
            {
                var header = reader.ReadLine(); // Read header
                var columnIndices = GetColumnIndices(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    agencyName = parts[columnIndices["agency_name"]];
                    agencyUrl = parts[columnIndices["agency_url"]];

                    // Assuming single agency for simplicity; break after the first agency is loaded
                    break;
                }
            }
        }

        private void LoadConfigData(string configFilePath)
        {
            if (File.Exists(configFilePath))
            {
                var lines = File.ReadAllLines(configFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        if (parts[0].Trim() == "feed_contact_email")
                        {
                            feedContactEmail = parts[1].Trim();
                        }
                        else if (parts[0].Trim() == "feed_contact_url")
                        {
                            feedContactUrl = parts[1].Trim();
                        }
                    }
                }
            }
        }

        private Dictionary<string, int> GetColumnIndices(string header)
        {
            var columns = header.Split(',');
            var columnIndices = new Dictionary<string, int>();

            for (int i = 0; i < columns.Length; i++)
            {
                columnIndices[columns[i]] = i;
            }

            return columnIndices;
        }

        private void CreateFeedInfo(string calendarFilePath, string logFilePath)
        {
            var feedStartDate = DateTime.MaxValue;
            var feedEndDate = DateTime.MinValue;

            using (var reader = new StreamReader(calendarFilePath))
            {
                var header = reader.ReadLine(); // Read header
                var columnIndices = GetColumnIndices(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var startDate = DateTime.ParseExact(parts[columnIndices["start_date"]], "yyyyMMdd", CultureInfo.InvariantCulture);
                    var endDate = DateTime.ParseExact(parts[columnIndices["end_date"]], "yyyyMMdd", CultureInfo.InvariantCulture);

                    if (startDate < feedStartDate)
                    {
                        feedStartDate = startDate;
                    }

                    if (endDate > feedEndDate)
                    {
                        feedEndDate = endDate;
                    }
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

        private void CheckAndCleanFeedInfo(string logFilePath)
        {
            var requiredColumns = new List<string>
            {
                "feed_publisher_name", "feed_publisher_url", "feed_lang"
            };

            var recommendedColumns = new List<string>
            {
                "feed_start_date", "feed_end_date", "feed_version", "feed_contact_email", "feed_contact_url"
            };

            var feedInfoLines = File.ReadAllLines(feedInfoFilePath).ToList();
            var header = feedInfoLines[0];
            var columns = header.Split(',');

            var validColumns = columns.Intersect(requiredColumns.Concat(recommendedColumns)).ToList();
            var invalidColumns = columns.Except(requiredColumns.Concat(recommendedColumns)).ToList();

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

                var columnIndices = columns.Select((col, index) => new { col, index })
                                           .Where(x => validColumns.Contains(x.col))
                                           .ToDictionary(x => x.col, x => x.index);

                var cleanedLines = new List<string>
                {
                    string.Join(",", validColumns)
                };

                for (int i = 1; i < feedInfoLines.Count; i++)
                {
                    var parts = feedInfoLines[i].Split(',');
                    var cleanedParts = columnIndices.Values.Select(index => parts[index]).ToList();
                    cleanedLines.Add(string.Join(",", cleanedParts));
                }

                File.WriteAllLines(feedInfoFilePath, cleanedLines);
                updateStatusAction("Unrecognized columns removed from feed_info.txt.", false);
            }
            else
            {
                updateStatusAction("No unrecognized columns found in feed_info.txt.", false);
            }

            // Add missing required and recommended columns if necessary
            AddMissingColumns(feedInfoLines, requiredColumns, recommendedColumns);
        }

        private void AddMissingColumns(List<string> feedInfoLines, List<string> requiredColumns, List<string> recommendedColumns)
        {
            var header = feedInfoLines[0];
            var columns = header.Split(',').ToList();

            var allRequiredColumns = requiredColumns.Concat(recommendedColumns).ToList();
            foreach (var column in allRequiredColumns)
            {
                if (!columns.Contains(column))
                {
                    columns.Add(column);
                }
            }

            feedInfoLines[0] = string.Join(",", columns);

            for (int i = 1; i < feedInfoLines.Count; i++)
            {
                var parts = feedInfoLines[i].Split(',').ToList();
                while (parts.Count < columns.Count)
                {
                    parts.Add(string.Empty);
                }

                feedInfoLines[i] = string.Join(",", parts);
            }

            File.WriteAllLines(feedInfoFilePath, feedInfoLines);
            updateStatusAction("Missing required and recommended columns added to feed_info.txt.", false);
        }
    }
}
