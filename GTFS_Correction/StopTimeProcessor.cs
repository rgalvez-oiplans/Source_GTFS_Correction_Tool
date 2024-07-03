using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GTFS_Correction
{
    public class StopTimeProcessor
    {
        private readonly Action<string, bool> updateStatusAction;
        private Dictionary<string, string> stopDescriptions;

        public StopTimeProcessor(Action<string, bool> updateStatusAction)
        {
            this.updateStatusAction = updateStatusAction;
        }

        public void ProcessStopTimes(string stopTimesFilePath, string stopsFilePath, string logFilePath)
        {
            LoadStopDescriptions(stopsFilePath);
            var stopTimes = LoadStopTimeData(stopTimesFilePath, logFilePath);
            UpdateStopDescriptions(stopTimes);
            UpdateShapeDistTraveledAndLog(stopTimes, logFilePath);
            SaveStopTimeData(stopTimesFilePath, stopTimes, logFilePath);
        }

        private void LoadStopDescriptions(string filePath)
        {
            stopDescriptions = new Dictionary<string, string>();

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine(); // Read header
                var columnIndices = GetColumnIndices(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var stopId = parts[columnIndices["stop_id"]];
                    var stopName = parts[columnIndices["stop_name"]];

                    stopDescriptions[stopId] = stopName;
                }
            }
        }

        private List<StopTime> LoadStopTimeData(string filePath, string logFilePath)
        {
            var stopTimes = new List<StopTime>();
            updateStatusAction("Loading stop times data...", false);

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine(); // Read header
                var columnIndices = GetColumnIndices(header);

                // Define required and recommended fields according to GTFS standard
                string[] requiredFields = { "trip_id", "arrival_time", "departure_time", "stop_id", "stop_sequence" };
                string[] recommendedFields = { "stop_headsign", "pickup_type", "drop_off_type", "shape_dist_traveled", "timepoint" };
                var validFields = new HashSet<string>(requiredFields.Concat(recommendedFields));

                var headersList = header.Split(',').ToList();
                var filteredHeaders = headersList.Where(field => validFields.Contains(field)).ToList();

                var removedFields = headersList.Except(filteredHeaders).ToList();

                if (removedFields.Any())
                {
                    updateStatusAction("Removing unrecognized columns from stoptimes.txt.", false);
                    using (var logWriter = new StreamWriter(logFilePath, true))
                    {
                        logWriter.WriteLine($"Removed columns from stoptimes.txt: {string.Join(", ", removedFields)}");
                    }
                }

                int lineNumber = 1;

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var stopTimeDict = headersList.Zip(parts, (key, value) => new { key, value })
                        .ToDictionary(x => x.key, x => x.value);

                    var stopTime = new StopTime
                    {
                        TripId = stopTimeDict["trip_id"],
                        ArrivalTime = stopTimeDict["arrival_time"],
                        DepartureTime = stopTimeDict["departure_time"],
                        StopId = stopTimeDict["stop_id"],
                        StopSequence = int.Parse(stopTimeDict["stop_sequence"]),
                        StopHeadsign = stopTimeDict.ContainsKey("stop_headsign") ? stopTimeDict["stop_headsign"] : string.Empty,
                        PickupType = stopTimeDict.ContainsKey("pickup_type") ? stopTimeDict["pickup_type"] : string.Empty,
                        DropOffType = stopTimeDict.ContainsKey("drop_off_type") ? stopTimeDict["drop_off_type"] : string.Empty,
                        ShapeDistTraveled = stopTimeDict.ContainsKey("shape_dist_traveled") ? stopTimeDict["shape_dist_traveled"] : string.Empty,
                        Timepoint = stopTimeDict.ContainsKey("timepoint") ? stopTimeDict["timepoint"] : string.Empty,
                        LineNumber = lineNumber
                    };
                    stopTimes.Add(stopTime);
                    lineNumber++;
                }
            }

            updateStatusAction("Stop times data loaded.", false);
            return stopTimes;
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

        private void UpdateStopDescriptions(List<StopTime> stopTimes)
        {
            foreach (var stopTime in stopTimes)
            {
                if (stopDescriptions.TryGetValue(stopTime.StopId, out var stopName))
                {
                    stopTime.StopDescription = $"{stopTime.StopId}_{stopName}";
                }
            }
        }

        private void SaveStopTimeData(string filePath, List<StopTime> stopTimes, string logFilePath)
        {
            using (var writer = new StreamWriter(filePath))
            {
                var headers = new List<string> { "trip_id", "arrival_time", "departure_time", "stop_id", "stop_sequence", "stop_headsign", "pickup_type", "drop_off_type", "shape_dist_traveled" };
                if (stopTimes.Any(st => !string.IsNullOrEmpty(st.Timepoint)))
                {
                    headers.Add("timepoint");
                }
                else
                {
                    updateStatusAction("Removing 'timepoint' column as it is empty.", false);
                    using (var logWriter = new StreamWriter(logFilePath, true))
                    {
                        logWriter.WriteLine("Removed 'timepoint' column from stoptimes.txt as it is empty.");
                    }
                }

                writer.WriteLine(string.Join(",", headers));

                foreach (var stopTime in stopTimes.OrderBy(st => st.LineNumber))
                {
                    var values = new List<string>
                    {
                        stopTime.TripId,
                        stopTime.ArrivalTime,
                        stopTime.DepartureTime,
                        stopTime.StopId,
                        stopTime.StopSequence.ToString(),
                        stopTime.StopHeadsign,
                        stopTime.PickupType,
                        stopTime.DropOffType,
                        stopTime.ShapeDistTraveled
                    };

                    if (headers.Contains("timepoint"))
                    {
                        values.Add(stopTime.Timepoint);
                    }

                    writer.WriteLine(string.Join(",", values));
                }
            }
        }

        private void UpdateShapeDistTraveledAndLog(List<StopTime> stopTimes, string logFilePath)
        {
            int totalPoints = stopTimes.Count;
            int lastReportedProgress = 0;

            using (var logWriter = new StreamWriter(logFilePath, true))
            {
                logWriter.WriteLine("TripId,StopSequence,StopId,OldShapeDistTraveled,NewShapeDistTraveled");

                for (int i = 1; i < stopTimes.Count; i++)
                {
                    if (stopTimes[i].TripId == stopTimes[i - 1].TripId)
                    {
                        double.TryParse(stopTimes[i].ShapeDistTraveled, NumberStyles.Float, CultureInfo.InvariantCulture, out double currentShapeDistTraveled);
                        double.TryParse(stopTimes[i - 1].ShapeDistTraveled, NumberStyles.Float, CultureInfo.InvariantCulture, out double previousShapeDistTraveled);

                        // If shape distance traveled is the same, and trip_id is the same
                        if (currentShapeDistTraveled == previousShapeDistTraveled)
                        {
                            // Update shape distance traveled by adding 1.12 meters (0.000696221 miles) to avoid precision issues
                            double newDistance = previousShapeDistTraveled + 0.000696221;

                            stopTimes[i].ShapeDistTraveled = Math.Round(newDistance, 9).ToString("F6", CultureInfo.InvariantCulture);
                            logWriter.WriteLine($"{stopTimes[i].TripId},{stopTimes[i].StopSequence},{stopTimes[i].StopId},{previousShapeDistTraveled},{stopTimes[i].ShapeDistTraveled}");
                        }

                        double percentProcessed = ((double)i / totalPoints) * 100;
                        int roundedProgress = (int)Math.Round(percentProcessed);

                        if (roundedProgress >= lastReportedProgress + 5)
                        {
                            updateStatusAction($"Processed {i + 1}/{stopTimes.Count} points ({roundedProgress}%)", true);
                            lastReportedProgress = roundedProgress;
                        }
                    }
                }
            }
        }

        private class StopTime
        {
            public string TripId { get; set; }
            public string ArrivalTime { get; set; }
            public string DepartureTime { get; set; }
            public string StopId { get; set; }
            public int StopSequence { get; set; }
            public string StopHeadsign { get; set; }
            public string PickupType { get; set; }
            public string DropOffType { get; set; }
            public string ShapeDistTraveled { get; set; }
            public string Timepoint { get; set; }
            public string StopDescription { get; set; }
            public int LineNumber { get; set; }
        }
    }
}
