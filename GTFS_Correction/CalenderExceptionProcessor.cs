using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;

public class CalendarExceptionProcessor
{
    private readonly Action<string, bool> updateStatusAction;

    public CalendarExceptionProcessor(Action<string, bool> updateStatusAction)
    {
        this.updateStatusAction = updateStatusAction;
    }

    public void ProcessExceptionsFromExcel(string calendarDatesFilePath, string tripsFilePath, string calendarFilePath)
    {
        string spreadsheetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CalendarExceptions.xlsx");

        if (!File.Exists(spreadsheetPath))
        {
            updateStatusAction?.Invoke("Error: CalendarExceptions.xlsx not found.", false);
            return;
        }

        var yearRange = GetYearRangeFromCalendarFile(calendarFilePath);
        int minYear = yearRange.Item1;
        int maxYear = yearRange.Item2;

        for (int year = minYear; year <= maxYear; year++)
        {
            ProcessScheduledExceptionsForYear(spreadsheetPath, calendarDatesFilePath, tripsFilePath, year, calendarFilePath);
        }
    }

    private void ProcessScheduledExceptionsForYear(string spreadsheetPath, string calendarDatesFilePath, string tripsFilePath, int year, string calendarFilePath)
    {
        try
        {
            FileInfo fileInfo = new FileInfo(spreadsheetPath);
            using (var package = new ExcelPackage(fileInfo))
            {
                var worksheet = package.Workbook.Worksheets[0];
                var exceptions = GetExceptionsFromExcel(worksheet);

                var trips = ReadTripsData(tripsFilePath);
                var calendarDates = ReadCalendarDates(calendarDatesFilePath);

                foreach (var exception in exceptions)
                {
                    if (exception.ExceptionDate.Year == year)
                    {
                        ProcessScheduledException(exception, trips, calendarDates, calendarFilePath);
                    }
                }

                WriteCalendarDates(calendarDatesFilePath, calendarDates);
            }
        }
        catch (Exception ex)
        {
            updateStatusAction?.Invoke($"Error processing exceptions: {ex.Message}", false);
        }
    }
    private int GetDayColumnIndex(string dayOfWeek, string[] header)
    {
        int dayIndex = Array.IndexOf(header, dayOfWeek);
        if (dayIndex == -1)
            throw new ArgumentException($"Invalid or missing day column: {dayOfWeek}");
        return dayIndex;
    }

    private List<string> GetActiveServiceIdsForExceptionDate(DateTime exceptionDate, List<TripData> trips, string calendarFilePath)
    {
        var activeServiceIds = new List<string>();

        if (!File.Exists(calendarFilePath))
        {
            updateStatusAction?.Invoke($"Error: calendar.txt not found at {calendarFilePath}", false);
            return activeServiceIds;
        }  

        var lines = File.ReadAllLines(calendarFilePath);
        var header = lines[0].Split(','); // Read the header row to determine column indices

        // Detect relevant column indices dynamically
        int serviceIdIndex = Array.IndexOf(header, "service_id");
        int startDateIndex = Array.IndexOf(header, "start_date");
        int endDateIndex = Array.IndexOf(header, "end_date");
        int dayOfWeekIndex = GetDayColumnIndex(exceptionDate.DayOfWeek.ToString().ToLower(), header);

        if (serviceIdIndex == -1 || startDateIndex == -1 || endDateIndex == -1 || dayOfWeekIndex == -1)
        {
            updateStatusAction?.Invoke("Error: Required columns not found in calendar.txt.", false);
            return activeServiceIds;
        }

        foreach (var line in lines.Skip(1)) // Skip header row
        {
            var parts = line.Split(',');
            if (parts.Length <= Math.Max(serviceIdIndex, endDateIndex)) continue;

            if (DateTime.TryParseExact(parts[startDateIndex], "yyyyMMdd", null, DateTimeStyles.None, out DateTime startDate) &&
                DateTime.TryParseExact(parts[endDateIndex], "yyyyMMdd", null, DateTimeStyles.None, out DateTime endDate))
            {
                if (exceptionDate >= startDate && exceptionDate <= endDate && parts[dayOfWeekIndex] == "1")
                {
                    activeServiceIds.Add(parts[serviceIdIndex]);
                }
            }
        }

        return activeServiceIds.Distinct().ToList();
    }


    private void ProcessScheduledException(ExceptionInfo exception, List<TripData> trips, List<string[]> calendarDates, string calendarFilePath)
    {
        // Only process routes that match the RouteId in the exception
        var routeServiceIds = GetActiveServiceIdsForExceptionDate(exception.ExceptionDate, trips, calendarFilePath)
                                .Where(serviceId => trips.Any(t => t.ServiceId == serviceId && t.RouteId == exception.RouteId))
                                .ToList();

        string exceptionDate = exception.ExceptionDate.ToString("yyyyMMdd");

        // Create and add distinct "No Service" entries
        if (exception.ExceptionType == "No Service")
        {
            var noServiceEntries = routeServiceIds
                .Select(serviceId => new[] { serviceId, exceptionDate, "2" })
                .Distinct(new CalendarDateComparer());

            calendarDates.AddRange(noServiceEntries);
        }

        // Handle "Sunday Service" exceptions
        if (exception.ExceptionType == "Sunday Service")
        {
            var replacementServiceIds = GetReplacementServiceIds(exception, trips);
            var replacementEntries = replacementServiceIds
                .Select(serviceId => new[] { serviceId, exceptionDate, "1" })
                .Distinct(new CalendarDateComparer());

            calendarDates.AddRange(replacementEntries);
        }
    }

    private List<string> GetReplacementServiceIds(ExceptionInfo exception, List<TripData> trips)
    {
        return trips
            .Where(t => t.RouteId == exception.RouteId && t.ServiceId.Contains("sunday"))
            .Select(t => t.ServiceId)
            .Distinct()
            .ToList();
    }

    private List<TripData> ReadTripsData(string tripsFilePath)
    {
        var trips = new List<TripData>();

        if (!File.Exists(tripsFilePath))
        {
            updateStatusAction?.Invoke($"Error: {tripsFilePath} not found.", false);
            return trips;
        }

        var lines = File.ReadAllLines(tripsFilePath);

        if (lines.Length < 2)
        {
            updateStatusAction?.Invoke("Error: trips.txt file is empty or has no data.", false);
            return trips;
        }

        // Split the header to detect the column indices
        var header = lines[0].Split(',');

        // Dynamically detect indices of TripId, RouteId, and ServiceId columns
        int tripIdIndex = Array.IndexOf(header, "trip_id");
        int routeIdIndex = Array.IndexOf(header, "route_id");
        int serviceIdIndex = Array.IndexOf(header, "service_id");

        // Check if the required columns are found
        if (tripIdIndex == -1 || routeIdIndex == -1 || serviceIdIndex == -1)
        {
            updateStatusAction?.Invoke("Error: Required columns (trip_id, route_id, service_id) not found in trips.txt.", false);
            return trips;
        }

        // Read each line of the file and map it to a TripData object
        foreach (var line in lines.Skip(1)) // Skip header
        {
            var parts = line.Split(',');

            // Ensure the line has enough columns to process
            if (parts.Length > Math.Max(tripIdIndex, Math.Max(routeIdIndex, serviceIdIndex)))
            {
                var tripData = new TripData
                {
                    TripId = parts[tripIdIndex],
                    RouteId = parts[routeIdIndex],
                    ServiceId = parts[serviceIdIndex]
                };

                trips.Add(tripData);
            }
        }

        updateStatusAction?.Invoke($"Successfully loaded {trips.Count} trips from trips.txt.", false);
        return trips;
    }


    private List<string[]> ReadCalendarDates(string calendarDatesFilePath)
    {
        var calendarDates = new List<string[]>();

        if (File.Exists(calendarDatesFilePath))
        {
            var lines = File.ReadAllLines(calendarDatesFilePath);
            foreach (var line in lines)
            {
                calendarDates.Add(line.Split(','));
            }
        }

        return calendarDates;
    }

    private void WriteCalendarDates(string calendarDatesFilePath, List<string[]> calendarDates)
    {
        var lines = calendarDates.Select(d => string.Join(",", d)).ToList().Distinct();
        File.WriteAllLines(calendarDatesFilePath, lines);
    }

    private (int, int) GetYearRangeFromCalendarFile(string calendarFilePath)
    {
        int minYear = int.MaxValue;
        int maxYear = int.MinValue;

        var lines = File.ReadAllLines(calendarFilePath);
        int startDateIndex = Array.FindIndex(lines[0].Split(','), x => x.Equals("start_date", StringComparison.OrdinalIgnoreCase));
        int endDateIndex = Array.FindIndex(lines[0].Split(','), x => x.Equals("end_date", StringComparison.OrdinalIgnoreCase));

        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');
            if (DateTime.TryParseExact(parts[startDateIndex], "yyyyMMdd", null, DateTimeStyles.None, out DateTime startDate) &&
                DateTime.TryParseExact(parts[endDateIndex], "yyyyMMdd", null, DateTimeStyles.None, out DateTime endDate))
            {
                minYear = Math.Min(minYear, startDate.Year);
                maxYear = Math.Max(maxYear, endDate.Year);
            }
        }

        return (minYear, maxYear);
    }
    private List<ExceptionInfo> GetExceptionsFromExcel(ExcelWorksheet worksheet)
    {
        var exceptions = new List<ExceptionInfo>();

        for (int row = 2; row <= worksheet.Dimension.End.Row; row++) // Skip header row
        {
            string municipality = worksheet.Cells[row, 1].Text;
            string routeId = worksheet.Cells[row, 2].Text;
            string exceptionReason = worksheet.Cells[row, 3].Text;
            string exceptionDateText = worksheet.Cells[row, 4].Text;
            string exceptionType = worksheet.Cells[row, 5].Text;

            if (DateTime.TryParseExact(exceptionDateText, "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime exceptionDate))
            {
                exceptions.Add(new ExceptionInfo
                {
                    Municipality = municipality,
                    RouteId = routeId,
                    ExceptionReason = exceptionReason,
                    ExceptionDate = exceptionDate,
                    ExceptionType = exceptionType
                });
            }
        }

        return exceptions;
    }


    private class ExceptionInfo
    {
        public string Municipality { get; set; }
        public string RouteId { get; set; }
        public string ExceptionReason { get; set; }
        public DateTime ExceptionDate { get; set; }
        public string ExceptionType { get; set; }
    }

    private class TripData
    {
        public string TripId { get; set; }
        public string RouteId { get; set; }
        public string ServiceId { get; set; }
    }

    public class CalendarDateComparer : IEqualityComparer<string[]>
    {
        public bool Equals(string[] x, string[] y)
        {
            return x.SequenceEqual(y);
        }

        public int GetHashCode(string[] obj)
        {
            return string.Join(",", obj).GetHashCode();
        }
    }
}
