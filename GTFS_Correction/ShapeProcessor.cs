using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DotSpatial.Data;
using DotSpatial.Topology;
using QuickGraph;
using QuickGraph.Algorithms;
using QuickGraph.Algorithms.Observers;
using QuickGraph.Algorithms.ShortestPath;
using static GTFS_Correction.DiscrepancyProcessor;
using static GTFS_Correction.ShapeProcessor;

namespace GTFS_Correction
{
    public class ShapeProcessor
    {
        private IFeatureSet roadNetwork;
        private AdjacencyGraph<IFeature, Edge<IFeature>> graph;
        private Action<string, bool> updateStatusAction;
        private DiscrepancyProcessor discrepancyProcessor;

        public ShapeProcessor(string shapefilePath, Action<string, bool> updateStatusAction)
        {
            this.updateStatusAction = updateStatusAction;

            if (1 == 0)
            {
                updateStatusAction("Loading road network shapefile...", false);
                roadNetwork = FeatureSet.Open(shapefilePath);
                updateStatusAction("Creating road network graph...", false);
                graph = CreateGraphFromRoadNetwork(roadNetwork);
                updateStatusAction("Graph creation complete.", false);
            }
        }

        public void ProcessShapes(string shapesFilePath, string logFilePath, List<Discrepancy> discrepancies)
        {
            try
            {
                var shapePoints = LoadShapeData(shapesFilePath);
                int removedDuplicates = RemoveDuplicateShapes(shapePoints, logFilePath);
                updateStatusAction($"Total records removed due to duplicates: {removedDuplicates}", false);
                UpdateShapesWithDiscrepancies(shapePoints, discrepancies, logFilePath);

                UpdateShapeDistTraveledAndLog(shapePoints, logFilePath);
                SaveShapeData(shapesFilePath, shapePoints);
            }
            catch (Exception ex)
            {
                updateStatusAction($"Error: {ex.Message}", false);
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private IFeature FindNearestSegment(Coordinate point)
        {
            double minDistance = double.MaxValue;
            IFeature nearestSegment = null;

            foreach (var feature in roadNetwork.Features)
            {
                var ntsPoint = new NetTopologySuite.Geometries.Point(point.X, point.Y);
                double distance = feature.Geometry.Distance(ntsPoint);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearestSegment = feature;
                }
            }

            return nearestSegment;
        }

        private AdjacencyGraph<IFeature, Edge<IFeature>> CreateGraphFromRoadNetwork(IFeatureSet roadNetwork)
        {
            var graph = new AdjacencyGraph<IFeature, Edge<IFeature>>();

            foreach (var feature in roadNetwork.Features)
            {
                graph.AddVertex(feature);

                foreach (var neighbor in roadNetwork.Features)
                {
                    if (feature != neighbor && AreConnected(feature, neighbor))
                    {
                        graph.AddEdge(new Edge<IFeature>(feature, neighbor));
                    }
                }
            }

            return graph;
        }

        private bool AreConnected(IFeature feature1, IFeature feature2)
        {
            return feature1.Geometry.Touches(feature2.Geometry);
        }

        private double CalculateShortestPath(IFeature startSegment, IFeature endSegment)
        {
            Func<Edge<IFeature>, double> edgeCost = e => e.Source.Geometry.Length * 0.000621371; // Convert meters to miles

            var dijkstra = new DijkstraShortestPathAlgorithm<IFeature, Edge<IFeature>>(graph, edgeCost);
            var predecessors = new VertexPredecessorRecorderObserver<IFeature, Edge<IFeature>>();
            using (predecessors.Attach(dijkstra))
            {
                dijkstra.Compute(startSegment);

                if (predecessors.TryGetPath(endSegment, out var path))
                {
                    return path.Sum(edge => edge.Source.Geometry.Length * 0.000621371); // Convert meters to miles
                }
            }

            return double.MaxValue;
        }

        public class ShapePoint
        {
            public string ShapeId { get; set; }
            public string LatitudeStr { get; set; }
            public string LongitudeStr { get; set; }
            public int Sequence { get; set; }
            public double ShapeDistTraveled { get; set; }
            public int LineNumber { get; set; }
            public string OriginalShapeDistTraveled { get; set; }

            public double Latitude => double.Parse(LatitudeStr, CultureInfo.InvariantCulture);
            public double Longitude => double.Parse(LongitudeStr, CultureInfo.InvariantCulture);
        }

        private List<ShapePoint> LoadShapeData(string filePath)
        {
            var shapePoints = new List<ShapePoint>();
            updateStatusAction("Loading shapes data...", false);

            using (var reader = new StreamReader(filePath))
            {
                var header = reader.ReadLine();
                var columnIndices = GetColumnIndices(header);
                int lineNumber = 1;

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var shapePoint = new ShapePoint
                    {
                        ShapeId = parts[columnIndices["shape_id"]],
                        LatitudeStr = parts[columnIndices["shape_pt_lat"]],
                        LongitudeStr = parts[columnIndices["shape_pt_lon"]],
                        Sequence = int.Parse(parts[columnIndices["shape_pt_sequence"]]),
                        ShapeDistTraveled = double.Parse(parts[columnIndices["shape_dist_traveled"]], CultureInfo.InvariantCulture),
                        LineNumber = lineNumber,
                        OriginalShapeDistTraveled = parts[columnIndices["shape_dist_traveled"]]
                    };
                    shapePoints.Add(shapePoint);
                    lineNumber++;
                }
            }

            updateStatusAction("Shapes data loaded.", false);
            return shapePoints;
        }

        private int RemoveDuplicateShapes(List<ShapePoint> shapePoints, string logFilePath)
        {
            int removedDuplicates = 0;

            using (var logWriter = new StreamWriter(logFilePath, true))
            {
                logWriter.WriteLine("Removed duplicate shapes:");
                for (int i = shapePoints.Count - 1; i > 0; i--)
                {
                    if (shapePoints[i].ShapeId == shapePoints[i - 1].ShapeId &&
                        shapePoints[i].Latitude == shapePoints[i - 1].Latitude &&
                        shapePoints[i].Longitude == shapePoints[i - 1].Longitude)
                    {
                        logWriter.WriteLine($"{shapePoints[i - 1].ShapeId},{shapePoints[i - 1].Sequence},{shapePoints[i - 1].LatitudeStr},{shapePoints[i - 1].LongitudeStr},{shapePoints[i - 1].OriginalShapeDistTraveled}");
                        shapePoints.RemoveAt(i - 1);
                        removedDuplicates++;
                    }
                }
            }

            return removedDuplicates;
        }

        private void UpdateShapesWithDiscrepancies(List<ShapePoint> shapes, List<Discrepancy> discrepancies, string logFilePath)
        {
            using (var logWriter = new StreamWriter(logFilePath, true))
            {
                foreach (var discrepancy in discrepancies)
                {
                    var shapePoint = shapes.LastOrDefault(s => s.ShapeId == discrepancy.ShapeId);
                    if (shapePoint != null)
                    {
                        logWriter.WriteLine($"Updating Shape ID {shapePoint.ShapeId}, distance traveled of {discrepancy.MaxShapeDistanceTraveled},  to match Trip ID {discrepancy.TripId} distance traveled of {discrepancy.MaxTripDistanceTraveled}.");

                        AdjustShapeDistances(shapes, shapePoint.ShapeId, discrepancy.MaxTripDistanceTraveled);
                    }
                }
            }
        }

        private void AdjustShapeDistances(List<ShapePoint> shapePoints, string shapeId, double maxTripDistanceTraveled)
        {
            var shapePointsForId = shapePoints.Where(s => s.ShapeId == shapeId).OrderBy(s => s.Sequence).ToList();
            double totalShapeDistance = shapePointsForId.Last().ShapeDistTraveled;
            double distanceRatio = maxTripDistanceTraveled / totalShapeDistance;

            for (int i = 0; i < shapePointsForId.Count; i++)
            {
                shapePointsForId[i].ShapeDistTraveled *= distanceRatio;
            }
        }

        private void UpdateShapeDistTraveledAndLog(List<ShapePoint> shapePoints, string logFilePath)
        {
            int totalPoints = shapePoints.Count;
            int lastReportedProgress = 0;

            using (var logWriter = new StreamWriter(logFilePath, true))
            {
                logWriter.WriteLine("ShapeId,Sequence,Latitude,Longitude,OldShapeDistTraveled,NewShapeDistTraveled");

                for (int i = 1; i < shapePoints.Count; i++)
                {
                    if (shapePoints[i].ShapeId == shapePoints[i - 1].ShapeId)
                    {
                        // If shape_dist_traveled is less than the previous value, make it at least the same or slightly higher
                        if (shapePoints[i].ShapeDistTraveled <= shapePoints[i - 1].ShapeDistTraveled)
                        {
                            shapePoints[i].ShapeDistTraveled = shapePoints[i - 1].ShapeDistTraveled + 0.000696221;
                        }

                        var p1 = new Coordinate(shapePoints[i - 1].Longitude, shapePoints[i - 1].Latitude);
                        var p2 = new Coordinate(shapePoints[i].Longitude, shapePoints[i].Latitude);

                        // Calculate the straight-line distance between consecutive points
                        double straightLineDistance = CalculateDistanceInMiles(p1, p2);

                        // If latitude and longitude are not the same, but shape distance traveled is the same, and shape_id is the same
                        if ((shapePoints[i].Latitude != shapePoints[i - 1].Latitude || shapePoints[i].Longitude != shapePoints[i - 1].Longitude) &&
                            shapePoints[i].ShapeDistTraveled == shapePoints[i - 1].ShapeDistTraveled)
                        {
                            // Update shape distance traveled based on straight-line distance to 9 decimal places
                            double newDistance = shapePoints[i - 1].ShapeDistTraveled + straightLineDistance;

                            // If the distance is less than 1 meter (0.000621371 miles), add 1.12 meters (0.000696221 miles) to avoid precision issues
                            if (straightLineDistance < 0.000621371)
                            {
                                newDistance += 0.000696221;
                            }

                            shapePoints[i].ShapeDistTraveled = Math.Round(newDistance, 9);
                            logWriter.WriteLine($"{shapePoints[i].ShapeId},{shapePoints[i].Sequence},{shapePoints[i].LatitudeStr},{shapePoints[i].LongitudeStr},{shapePoints[i - 1].OriginalShapeDistTraveled},{shapePoints[i].ShapeDistTraveled.ToString("F6", CultureInfo.InvariantCulture)}");
                        }

                        double percentProcessed = ((double)i / totalPoints) * 100;
                        int roundedProgress = (int)Math.Round(percentProcessed);

                        if (roundedProgress >= lastReportedProgress + 5)
                        {
                            updateStatusAction($"Processed {i + 1}/{shapePoints.Count} points ({roundedProgress}%)", true);
                            lastReportedProgress = roundedProgress;
                        }
                    }
                }
            }
        }

        private double CalculateDistanceInMiles(Coordinate p1, Coordinate p2)
        {
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            return Math.Round(Math.Sqrt(dx * dx + dy * dy) * 0.000621371, 9); // Convert meters to miles and round to 9 decimal places
        }

        private void SaveShapeData(string filePath, List<ShapePoint> shapePoints)
        {
            updateStatusAction("Saving updated shapes data...", false);
            using (var writer = new StreamWriter(filePath))
            {
                writer.WriteLine("shape_id,shape_pt_lat,shape_pt_lon,shape_pt_sequence,shape_dist_traveled");
                foreach (var point in shapePoints.OrderBy(p => p.LineNumber))
                {
                    string shapeDistTraveled = point.OriginalShapeDistTraveled;
                    if (Math.Abs(point.ShapeDistTraveled - double.Parse(point.OriginalShapeDistTraveled, CultureInfo.InvariantCulture)) > double.Epsilon)
                    {
                        shapeDistTraveled = point.ShapeDistTraveled.ToString("F9", CultureInfo.InvariantCulture);
                    }

                    writer.WriteLine($"{point.ShapeId},{point.LatitudeStr},{point.LongitudeStr},{point.Sequence},{shapeDistTraveled}");
                }
            }
            updateStatusAction("Updated shapes data saved.", false);
        }
    }
}


