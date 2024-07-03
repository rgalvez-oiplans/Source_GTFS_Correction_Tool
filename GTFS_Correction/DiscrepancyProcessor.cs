using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DotSpatial.Topology;

namespace GTFS_Correction
{
    public class DiscrepancyProcessor
    {
        private readonly Action<string, bool> updateStatusAction;

        public DiscrepancyProcessor(Action<string, bool> updateStatusAction)
        {
            this.updateStatusAction = updateStatusAction;
        }

        public void ProcessDiscrepancies(string tripsFilePath, string stopTimesFilePath, string stopsFilePath, string shapesFilePath, string outputFilePath)
        {
            var stops = LoadStops(stopsFilePath);
            var stopTimes = LoadStopTimes(stopTimesFilePath, stops);
            var shapes = LoadShapeData(shapesFilePath);
            var trips = LoadTrips(tripsFilePath);

            var discrepancies = new List<Discrepancy>();

            foreach (var trip in trips)
            {
                var tripStopTimes = stopTimes.Where(st => st.TripId == trip.TripId).OrderBy(st => st.StopSequence).ToList();
                var tripShapes = shapes.Where(s => s.ShapeId == trip.ShapeId).OrderBy(s => s.Sequence).ToList();

                if (tripStopTimes.Any() && tripShapes.Any())
                {
                    var maxTripDistanceTraveled = tripStopTimes.Last().ShapeDistTraveled;
                    var maxShapeDistanceTraveled = tripShapes.Last().ShapeDistTraveled;
                    var geoDistanceToShape = CalculateGeoDistance(tripStopTimes.Last(), tripShapes.Last());

                    //if (geoDistanceToShape >= 11.1)
                    //{
                    //    discrepancies.Add(new Discrepancy
                    //    {
                    //        TripId = trip.TripId,
                    //        ShapeId = trip.ShapeId,
                    //        MaxTripDistanceTraveled = maxTripDistanceTraveled,
                    //        MaxShapeDistanceTraveled = maxShapeDistanceTraveled,
                    //        GeoDistanceToShape = geoDistanceToShape,
                    //        StopLat = tripStopTimes.Last().Latitude,
                    //        StopLon = tripStopTimes.Last().Longitude,
                    //        ShapeLat = tripShapes.Last().Latitude,
                    //        ShapeLon = tripShapes.Last().Longitude
                    //    });
                    //}
                    if (maxTripDistanceTraveled > maxShapeDistanceTraveled)
                    {
                        discrepancies.Add(new Discrepancy
                        {
                            TripId = trip.TripId,
                            ShapeId = trip.ShapeId,
                            MaxTripDistanceTraveled = maxTripDistanceTraveled,
                            MaxShapeDistanceTraveled = maxShapeDistanceTraveled,
                            GeoDistanceToShape = geoDistanceToShape,
                            StopLat = tripStopTimes.Last().Latitude,
                            StopLon = tripStopTimes.Last().Longitude,
                            ShapeLat = tripShapes.Last().Latitude,
                            ShapeLon = tripShapes.Last().Longitude
                        });
                    }
                }
            }

            SaveDiscrepancies(outputFilePath, discrepancies);
        }

        private Dictionary<string, Stop> LoadStops(string filePath)
        {
            var stops = new Dictionary<string, Stop>();

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine(); // Read header
                var columnIndices = GetColumnIndices(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var stop = new Stop
                    {
                        StopId = parts[columnIndices["stop_id"]],
                        StopLat = double.Parse(parts[columnIndices["stop_lat"]], CultureInfo.InvariantCulture),
                        StopLon = double.Parse(parts[columnIndices["stop_lon"]], CultureInfo.InvariantCulture)
                    };
                    stops[stop.StopId] = stop;
                }
            }

            return stops;
        }

        private List<StopTime> LoadStopTimes(string stopTimesFilePath, Dictionary<string, Stop> stops)
        {
            var stopTimes = new List<StopTime>();

            using (var reader = new StreamReader(stopTimesFilePath))
            {
                var header = reader.ReadLine();
                var columnIndices = GetColumnIndices(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var stopId = parts[columnIndices["stop_id"]];
                    var stopTime = new StopTime
                    {
                        TripId = parts[columnIndices["trip_id"]],
                        StopId = stopId,
                        StopSequence = int.Parse(parts[columnIndices["stop_sequence"]]),
                        ShapeDistTraveled = string.IsNullOrEmpty(parts[columnIndices["shape_dist_traveled"]])
                            ? 0
                            : double.Parse(parts[columnIndices["shape_dist_traveled"]], CultureInfo.InvariantCulture)
                    };

                    if (stops.TryGetValue(stopId, out var stop))
                    {
                        stopTime.Latitude = stop.StopLat;
                        stopTime.Longitude = stop.StopLon;
                    }

                    stopTimes.Add(stopTime);
                }
            }

            return stopTimes;
        }


        private List<ShapePoint> LoadShapeData(string filePath)
        {
            var shapePoints = new List<ShapePoint>();

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine();
                var columnIndices = GetColumnIndices(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var shapePoint = new ShapePoint
                    {
                        ShapeId = parts[columnIndices["shape_id"]],
                        Latitude = double.Parse(parts[columnIndices["shape_pt_lat"]], CultureInfo.InvariantCulture),
                        Longitude = double.Parse(parts[columnIndices["shape_pt_lon"]], CultureInfo.InvariantCulture),
                        Sequence = int.Parse(parts[columnIndices["shape_pt_sequence"]]),
                        ShapeDistTraveled = double.Parse(parts[columnIndices["shape_dist_traveled"]], CultureInfo.InvariantCulture)
                    };
                    shapePoints.Add(shapePoint);
                }
            }

            return shapePoints;
        }

        private List<Trip> LoadTrips(string filePath)
        {
            var trips = new List<Trip>();

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine(); // Read header
                var columnIndices = GetColumnIndices(header);

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var trip = new Trip
                    {
                        TripId = parts[columnIndices["trip_id"]],
                        ShapeId = parts[columnIndices["shape_id"]]
                    };
                    trips.Add(trip);
                }
            }

            return trips;
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

        private double CalculateGeoDistance(StopTime stopTime, ShapePoint shapePoint)
        {
            var R = 6371e3; // Radius of the Earth in meters
            var lat1 = stopTime.Latitude * Math.PI / 180;
            var lat2 = shapePoint.Latitude * Math.PI / 180;
            var deltaLat = (shapePoint.Latitude - stopTime.Latitude) * Math.PI / 180;
            var deltaLon = (shapePoint.Longitude - stopTime.Longitude) * Math.PI / 180;

            var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                    Math.Cos(lat1) * Math.Cos(lat2) *
                    Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            var distance = R * c;
            return distance;
        }


        private void SaveDiscrepancies(string outputFilePath, List<Discrepancy> discrepancies)
        {
            using (var writer = new StreamWriter(outputFilePath))
            {
                writer.WriteLine("trip_id,shape_id,max_trip_distance_traveled,max_shape_distance_traveled,geo_distance_to_shape,stop_lat,stop_lon,shape_lat,shape_lon");

                foreach (var discrepancy in discrepancies)
                {
                    writer.WriteLine($"{discrepancy.TripId},{discrepancy.ShapeId},{discrepancy.MaxTripDistanceTraveled},{discrepancy.MaxShapeDistanceTraveled},{discrepancy.GeoDistanceToShape},{discrepancy.StopLat},{discrepancy.StopLon},{discrepancy.ShapeLat},{discrepancy.ShapeLon}");
                }
            }
        }


        private class Stop
        {
            public string StopId { get; set; }
            public double StopLat { get; set; }
            public double StopLon { get; set; }
        }


        private class StopTime
        {
            public string TripId { get; set; }
            public string StopId { get; set; }
            public int StopSequence { get; set; }
            public double ShapeDistTraveled { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
        }

        private class ShapePoint
        {
            public string ShapeId { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
            public int Sequence { get; set; }
            public double ShapeDistTraveled { get; set; }
        }

        private class Trip
        {
            public string TripId { get; set; }
            public string ShapeId { get; set; }
        }

        public class Discrepancy
        {
            public string TripId { get; set; }
            public string ShapeId { get; set; }
            public double MaxTripDistanceTraveled { get; set; }
            public double MaxShapeDistanceTraveled { get; set; }
            public double GeoDistanceToShape { get; set; }
            public double StopLat { get; set; }
            public double StopLon { get; set; }
            public double ShapeLat { get; set; }
            public double ShapeLon { get; set; }
        }

    }
}
