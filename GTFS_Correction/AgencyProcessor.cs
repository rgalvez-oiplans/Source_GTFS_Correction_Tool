using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GTFS_Correction
{
    public class AgencyProcessor
    {
        private readonly Action<string, bool> updateStatusAction;

        public AgencyProcessor(Action<string, bool> updateStatusAction)
        {
            this.updateStatusAction = updateStatusAction;
        }

        public void ProcessAgencies(string agencyFilePath, string logFilePath)
        {
            var agencies = LoadAgencies(agencyFilePath, logFilePath);
            SaveAgencies(agencyFilePath, agencies);
        }

        private List<string> LoadAgencies(string filePath, string logFilePath)
        {
            var agencies = new List<string>();
            updateStatusAction("Loading agency data...", false);

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine();
                var columnIndices = GetColumnIndices(header);

                // Define required and recommended fields according to GTFS standard
                string[] requiredFields = { "agency_name", "agency_url", "agency_timezone" };
                string[] recommendedFields = { "agency_id", "agency_lang", "agency_phone", "agency_fare_url", "agency_email" };
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
                    updateStatusAction("Removing unrecognized columns from agency.txt.", false);
                    using (var logWriter = new StreamWriter(logFilePath, true))
                    {
                        logWriter.WriteLine($"Removed columns from agency.txt: {string.Join(", ", removedFields)}");
                    }
                }

                header = string.Join(",", filteredHeaders);
                agencies.Add(header);

                var existingIds = new HashSet<string>();

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var agencyDict = headersList.Zip(parts, (key, value) => new { key, value })
                        .ToDictionary(x => x.key, x => x.value);

                    string agencyId = agencyDict.ContainsKey("agency_id") ? agencyDict["agency_id"].Trim() : string.Empty;
                    var agencyName = agencyDict["agency_name"].Trim();

                    if (string.IsNullOrEmpty(agencyId))
                    {
                        agencyId = GenerateUniqueAgencyId(agencyName, existingIds);
                        agencyDict["agency_id"] = agencyId;
                    }

                    existingIds.Add(agencyId);

                    // Ensure all required and recommended fields are present
                    var filteredParts = filteredHeaders.Select(field => agencyDict.ContainsKey(field) ? agencyDict[field] : string.Empty).ToList();
                    agencies.Add(string.Join(",", filteredParts));
                }
            }

            updateStatusAction("Agency data loaded.", false);
            return agencies;
        }

        public Dictionary<string, string> LoadAgencyIdMap(string agencyFilePath)
        {
            var agencyIdMap = new Dictionary<string, string>();

            using (var reader = new StreamReader(agencyFilePath))
            {
                var header = reader.ReadLine();
                var columnIndices = GetColumnIndices(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var agencyId = parts[columnIndices["agency_id"]].Trim();
                    var agencyName = parts[columnIndices["agency_name"]].Trim();

                    if (!agencyIdMap.ContainsKey(agencyId))
                    {
                        agencyIdMap[agencyId] = agencyName;
                    }
                }
            }

            return agencyIdMap;
        }

        private void SaveAgencies(string filePath, List<string> agencies)
        {
            updateStatusAction("Saving updated agency data...", false);

            using (var writer = new StreamWriter(filePath))
            {
                foreach (var line in agencies)
                {
                    writer.WriteLine(line);
                }
            }

            updateStatusAction("Updated agency data saved.", false);
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

        private string GenerateUniqueAgencyId(string agencyName, HashSet<string> existingIds)
        {
            var baseAcronym = string.Concat(agencyName.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(word => word[0])).ToUpper();
            var acronym = baseAcronym;
            int suffix = 1;

            while (existingIds.Contains(acronym))
            {
                acronym = $"{baseAcronym}{suffix++}";
            }

            return acronym;
        }
    }
}

