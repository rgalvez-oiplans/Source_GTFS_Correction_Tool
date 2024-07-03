using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GTFS_Correction
{
    public class RoutesProcessor
    {
        private readonly Action<string, bool> updateStatusAction;
        private readonly Dictionary<string, string> agencyIdMap;
        private readonly string defaultAgencyId;

        public RoutesProcessor(Action<string, bool> updateStatusAction, Dictionary<string, string> agencyIdMap)
        {
            this.updateStatusAction = updateStatusAction;
            this.agencyIdMap = agencyIdMap;
            this.defaultAgencyId = agencyIdMap.Keys.FirstOrDefault() ?? "UNKNOWN";
        }

        public void ProcessRoutes(string routesFilePath, string logFilePath)
        {
            var routes = LoadRoutes(routesFilePath, logFilePath);
            SaveRoutes(routesFilePath, routes);
        }

        private List<string> LoadRoutes(string filePath, string logFilePath)
        {
            var routes = new List<string>();
            updateStatusAction("Loading routes data...", false);

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine();
                var columnIndices = GetColumnIndices(header);

                // Define required and recommended fields according to GTFS standard
                string[] requiredFields = { "route_id", "agency_id", "route_short_name", "route_long_name", "route_type" };
                string[] recommendedFields = { "route_desc", "route_url", "route_color", "route_text_color", "route_sort_order" };
                var validFields = new HashSet<string>(requiredFields.Concat(recommendedFields));

                var headersList = header.Split(',').ToList();
                var filteredHeaders = headersList.Where(field => validFields.Contains(field)).ToList();

                if (!filteredHeaders.Contains("agency_id"))
                {
                    filteredHeaders.Add("agency_id");
                }

                var removedFields = headersList.Except(filteredHeaders).ToList();

                if (removedFields.Any())
                {
                    updateStatusAction("Removing unrecognized columns from Routes.txt.", false);
                    using (var logWriter = new StreamWriter(logFilePath, true))
                    {
                        logWriter.WriteLine($"Removed columns from routes.txt: {string.Join(", ", removedFields)}");
                    }
                }

                header = string.Join(",", filteredHeaders);
                routes.Add(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var routeDict = headersList.Zip(parts, (key, value) => new { key, value })
                        .ToDictionary(x => x.key, x => x.value);

                    var agencyId = routeDict.ContainsKey("agency_id") ? routeDict["agency_id"].Trim() : string.Empty;

                    if (string.IsNullOrEmpty(agencyId))
                    {
                        // Assign the correct agency_id from the agencyIdMap
                        agencyId = defaultAgencyId;

                        routeDict["agency_id"] = agencyId;
                    }

                    // Ensure route_long_name is in proper case
                    if (columnIndices.ContainsKey("route_long_name") && routeDict.ContainsKey("route_long_name"))
                    {
                        routeDict["route_long_name"] = ConvertToProperCase(routeDict["route_long_name"]);
                    }

                    // Ensure all required and recommended fields are present
                    var filteredParts = filteredHeaders.Select(field => routeDict.ContainsKey(field) ? routeDict[field] : string.Empty).ToList();
                    routes.Add(string.Join(",", filteredParts));
                }
            }

            updateStatusAction("Routes data loaded.", false);
            return routes;
        }

        private void SaveRoutes(string filePath, List<string> routes)
        {
            updateStatusAction("Saving updated routes data...", false);

            using (var writer = new StreamWriter(filePath))
            {
                foreach (var line in routes)
                {
                    writer.WriteLine(line);
                }
            }

            updateStatusAction("Updated routes data saved.", false);
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

        private string ConvertToProperCase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 1)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
                else
                {
                    words[i] = words[i].ToUpper();
                }
            }
            return string.Join(" ", words);
        }
    }
}


