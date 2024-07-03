using ExcelDataReader;
using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GTFS_Correction
{
    public class StopProcessor
    {
        private readonly SQLiteHelper sqliteHelper;
        private readonly Action<string, bool> updateStatusAction;

        public StopProcessor(string databasePath, Action<string, bool> updateStatusAction)
        {
            this.sqliteHelper = new SQLiteHelper(databasePath);
            this.updateStatusAction = updateStatusAction;
        }

        public void ProcessStops(string inputFilePath, string logFilePath,string inputStopsNamePath)
        {
            ImportStopDescriptions(inputStopsNamePath);

            var stopPoints = LoadStopData(inputFilePath, logFilePath);
            UpdateStopNames(stopPoints, logFilePath);
            SaveStopData(inputFilePath, stopPoints);
        }
        public void ImportStopDescriptions(string excelFilePath)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using (var stream = File.Open(excelFilePath, FileMode.Open, FileAccess.Read))
            {
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    var result = reader.AsDataSet();

                    if (result.Tables.Count > 0)
                    {
                        var table = result.Tables[0];
                        for (int i = 1; i < table.Rows.Count; i++)
                        {
                            var row = table.Rows[i];
                            string stopName = row[0].ToString();
                            string stopNameDescription = row[1].ToString();
                            string stopNameKey = NormalizeStopName(stopName);

                            try
                            {
                                string existingDescription = sqliteHelper.GetStopNameDescription(stopNameKey);
                                if (existingDescription != null)
                                {
                                    if (!existingDescription.Equals(stopNameDescription, StringComparison.OrdinalIgnoreCase))
                                    {
                                        sqliteHelper.AddOrUpdateStopName(stopName, stopNameDescription);
                                    }
                                }
                                else
                                {
                                    sqliteHelper.AddOrUpdateStopName(stopName, stopNameDescription);
                                }
                            }
                            catch (Exception ex)
                            {
                                updateStatusAction($"Error importing row {i}: {ex.Message}", false);
                            }
                        }
                    }
                }
            }

            updateStatusAction("Import completed.", false);
        }
        public void ExportStopNamesToXlsx(string stopsFilePath, string logFilePath)
        {
            try
            {
                // Load stops data
                var stopPoints = LoadStopData(stopsFilePath, logFilePath);

                // Perform left join with the stopnamelookup table in the database
                var enrichedStops = EnrichStopsWithDescriptions(stopPoints);

                // Export to XLSX
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Stop Names");
                    worksheet.Cells[1, 1].Value = "Stop Name";
                    worksheet.Cells[1, 2].Value = "Stop Description";

                    for (int i = 0; i < enrichedStops.Count; i++)
                    {
                        worksheet.Cells[i + 2, 1].Value = enrichedStops[i].StopName;
                        worksheet.Cells[i + 2, 2].Value = enrichedStops[i].StopDescription;
                    }

                    string xlsxPath = Path.Combine(Path.GetDirectoryName(logFilePath), "StopNames.xlsx");
                    package.SaveAs(new FileInfo(xlsxPath));

                    updateStatusAction("Stop names exported to XLSX.", false);
                }
            }
            catch (Exception ex)
            {
                updateStatusAction($"Error: {ex.Message}", false);
            }
        }
        private List<StopPoint> LoadStopData(string filePath, string logFilePath)
        {
            var stopPoints = new List<StopPoint>();

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine(); // Read header
                var columnIndices = GetColumnIndices(header);

                // Define required and recommended fields according to GTFS standard
                string[] requiredFields = { "stop_id", "stop_name", "stop_lat", "stop_lon" };
                string[] recommendedFields = { "stop_code", "stop_desc", "zone_id", "stop_url", "location_type", "parent_station", "wheelchair_boarding" };
                var validFields = new HashSet<string>(requiredFields.Concat(recommendedFields));

                var headersList = header.Split(',').ToList();
                var filteredHeaders = headersList.Where(field => validFields.Contains(field)).ToList();

                var removedFields = headersList.Except(filteredHeaders).ToList();

                if (removedFields.Any())
                {
                    updateStatusAction("Removing unrecognized columns from stops.txt.", false);
                    using (var logWriter = new StreamWriter(logFilePath, true))
                    {
                        logWriter.WriteLine($"Removed columns from stops.txt: {string.Join(", ", removedFields)}");
                    }
                }

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var stopPointDict = headersList.Zip(parts, (key, value) => new { key, value })
                        .ToDictionary(x => x.key, x => x.value);

                    var stopPoint = new StopPoint
                    {
                        StopId = stopPointDict["stop_id"],
                        StopName = stopPointDict["stop_name"].Trim('"'), // Remove quotes
                        StopLat = double.Parse(stopPointDict["stop_lat"], CultureInfo.InvariantCulture),
                        StopLon = double.Parse(stopPointDict["stop_lon"], CultureInfo.InvariantCulture),
                        StopCode = stopPointDict.ContainsKey("stop_code") ? stopPointDict["stop_code"] : string.Empty,
                        StopDesc = stopPointDict.ContainsKey("stop_desc") ? stopPointDict["stop_desc"] : string.Empty,
                        ZoneId = stopPointDict.ContainsKey("zone_id") ? stopPointDict["zone_id"] : string.Empty,
                        StopUrl = stopPointDict.ContainsKey("stop_url") ? stopPointDict["stop_url"] : string.Empty,
                        LocationType = stopPointDict.ContainsKey("location_type") ? stopPointDict["location_type"] : string.Empty,
                        ParentStation = stopPointDict.ContainsKey("parent_station") ? stopPointDict["parent_station"] : string.Empty,
                        WheelchairBoarding = stopPointDict.ContainsKey("wheelchair_boarding") ? stopPointDict["wheelchair_boarding"] : string.Empty,
                       // AvlTriggerRadius = stopPointDict.ContainsKey("avl_trigger_radius") ? stopPointDict["avl_trigger_radius"] : string.Empty
                    };
                    stopPoints.Add(stopPoint);
                }
            }

            return stopPoints.OrderBy(p => p.StopId).ToList();
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

        private void UpdateStopNames(List<StopPoint> stopPoints, string logFilePath)
        {
            using (var logWriter = new StreamWriter(logFilePath, true))
            {
                int totalStops = stopPoints.Count;
                for (int i = 0; i < totalStops; i++)
                {
                    var stop = stopPoints[i];
                    var originalStopName = stop.StopName;
                    var stopNameKey = NormalizeStopName(stop.StopName);
                    var stopNameDescription = sqliteHelper.GetStopNameDescription(stopNameKey);

                    if (!string.IsNullOrEmpty(stopNameDescription))
                    {
                        stop.StopName = $"\"{stopNameDescription}\"";
                    }
                    else
                    {
                        sqliteHelper.AddOrUpdateStopName(originalStopName, null);
                        logWriter.WriteLine($"Stop name not found in database: {originalStopName}");
                        stop.StopName = $"\"{stop.StopName}\""; // Add quotes if not found
                    }

                    // Update status with percentage
                    int currentStop = i + 1;
                    int percentage = (int)((currentStop / (double)totalStops) * 100);
                    updateStatusAction($"Processing stops... {currentStop}/{totalStops} ({percentage}%)", true);
                }
            }
        }

        private string NormalizeStopName(string stopName)
        {
            return stopName.Replace(" ", string.Empty).ToUpperInvariant();
        }

        private void SaveStopData(string filePath, List<StopPoint> stopPoints)
        {
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine("stop_id,stop_code,stop_name,stop_desc,stop_lat,stop_lon,zone_id,stop_url,location_type,parent_station,wheelchair_boarding");
                foreach (var stop in stopPoints)
                {
                    writer.WriteLine($"{stop.StopId},{stop.StopCode},{stop.StopName},{stop.StopDesc},{stop.StopLat.ToString(CultureInfo.InvariantCulture)},{stop.StopLon.ToString(CultureInfo.InvariantCulture)},{stop.ZoneId},{stop.StopUrl},{stop.LocationType},{stop.ParentStation},{stop.WheelchairBoarding}");
                }
            }
        }
        private List<StopPoint> EnrichStopsWithDescriptions(List<StopPoint> stopPoints)
        {
            var enrichedStops = new List<StopPoint>();

            foreach (var stop in stopPoints)
            {
                var stopDescription = sqliteHelper.GetStopNameDescription(NormalizeStopName(stop.StopName));

                enrichedStops.Add(new StopPoint
                {
                    StopId = stop.StopId,
                    StopName = stop.StopName,
                    StopDescription = stopDescription
                });
            }

            return enrichedStops;
        }

        private class StopPoint
        {
            public string StopId { get; set; }
            public string StopCode { get; set; }
            public string StopName { get; set; }
            public string StopDesc { get; set; }
            public double StopLat { get; set; }
            public double StopLon { get; set; }
            public string ZoneId { get; set; }
            public string StopUrl { get; set; }
            public string LocationType { get; set; }
            public string ParentStation { get; set; }
            public string WheelchairBoarding { get; set; }
            public string StopDescription { get; set; }
        }
    }
}
