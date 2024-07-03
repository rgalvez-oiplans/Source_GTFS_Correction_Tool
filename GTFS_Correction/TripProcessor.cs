using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GTFS_Correction
{
    public class TripProcessor
    {
        private readonly Action<string, bool> updateStatusAction;

        public TripProcessor(Action<string, bool> updateStatusAction)
        {
            this.updateStatusAction = updateStatusAction;
        }

        public void ProcessTrips(string inputFilePath, string logFilePath)
        {
            var tripPoints = LoadTripData(inputFilePath);
            UpdateTripHeadsigns(tripPoints, logFilePath);
            SaveTripData(inputFilePath, tripPoints);
        }

        private List<TripPoint> LoadTripData(string filePath)
        {
            var tripPoints = new List<TripPoint>();

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine(); // Read header
                var columnIndices = GetColumnIndices(header);
                int lineNumber = 1;

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var tripPoint = new TripPoint
                    {
                        TripId = parts[columnIndices["trip_id"]],
                        RouteId = parts[columnIndices["route_id"]],
                        ServiceId = parts[columnIndices["service_id"]],
                        TripHeadsign = parts[columnIndices["trip_headsign"]],
                        DirectionId = parts[columnIndices["direction_id"]],
                        BlockId = parts[columnIndices["block_id"]],
                        ShapeId = parts[columnIndices["shape_id"]],
                        LineNumber = lineNumber
                    };
                    tripPoints.Add(tripPoint);
                    lineNumber++;
                }
            }

            return tripPoints;
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

        private void UpdateTripHeadsigns(List<TripPoint> tripPoints, string logFilePath)
        {
            using (var logWriter = new StreamWriter(logFilePath, true))
            {
                foreach (var trip in tripPoints)
                {
                    var originalTripHeadsign = trip.TripHeadsign;
                    var properCaseTripHeadsign = ConvertToProperCase(trip.TripHeadsign);

                    if (!originalTripHeadsign.Equals(properCaseTripHeadsign))
                    {
                        trip.TripHeadsign = properCaseTripHeadsign;
                        logWriter.WriteLine($"Trip headsign updated: {originalTripHeadsign} -> {properCaseTripHeadsign}");
                    }
                }
            }
        }

        private string ConvertToProperCase(string text)
        {
            TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;
            return textInfo.ToTitleCase(text.ToLower());
        }

        private void SaveTripData(string filePath, List<TripPoint> tripPoints)
        {
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine("trip_id,route_id,service_id,trip_headsign,direction_id,block_id,shape_id");
                foreach (var trip in tripPoints.OrderBy(p => p.LineNumber))
                {
                    writer.WriteLine($"{trip.TripId},{trip.RouteId},{trip.ServiceId},{trip.TripHeadsign},{trip.DirectionId},{trip.BlockId},{trip.ShapeId}");
                }
            }
        }

        private class TripPoint
        {
            public string TripId { get; set; }
            public string RouteId { get; set; }
            public string ServiceId { get; set; }
            public string TripHeadsign { get; set; }
            public string DirectionId { get; set; }
            public string BlockId { get; set; }
            public string ShapeId { get; set; }
            public int LineNumber { get; set; }
        }
    }
}
