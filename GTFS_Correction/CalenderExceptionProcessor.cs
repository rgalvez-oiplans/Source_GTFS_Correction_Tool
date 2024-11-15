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

    private void ProcessScheduledException(
    ExceptionInfo exception, List<TripData> trips, List<string[]> calendarDates, string calendarFilePath)
    {
        var routeServiceIds = GetActiveServiceIdsForExceptionDate(exception.ExceptionDate, trips, calendarFilePath);
        string exceptionDate = exception.ExceptionDate.ToString("yyyyMMdd");

        // Create and add distinct "No Service" entries
        var newEntries = routeServiceIds.Select(serviceId =>
            new[] { serviceId, exceptionDate, exception.ExceptionType == "No Service" ? "2" : "1" });

        calendarDates.AddRange(
            newEntries.Except(calendarDates, new CalendarDateComparer())
        );

        // Handle "Sunday Service" exceptions
        if (exception.ExceptionType == "Sunday Service")
        {
            var replacementServiceIds = GetReplacementServiceIds(exception, trips);
            var replacementEntries = replacementServiceIds.Select(serviceId =>
                new[] { serviceId, exceptionDate, "1" });

            calendarDates.AddRange(
                replacementEntries.Except(calendarDates, new CalendarDateComparer())
            );
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

        if (File.Exists(tripsFilePath))
        {
            var lines = File.ReadAllLines(tripsFilePath).Skip(1); // Skip header row
            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length > 3)
                {
                    trips.Add(new TripData
                    {
                        TripId = parts[0],
                        RouteId = parts[2],
                        ServiceId = parts[1]
                    });
                }
            }
        }

        return trips;
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
        var header = lines[0].Split(',');

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

        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length <= Math.Max(serviceIdIndex, endDateIndex)) continue;

            // Parse the start and end dates
            if (DateTime.TryParseExact(parts[startDateIndex], "yyyyMMdd", null, DateTimeStyles.None, out DateTime startDate) &&
                DateTime.TryParseExact(parts[endDateIndex], "yyyyMMdd", null, DateTimeStyles.None, out DateTime endDate))
            {
                // Check if the exception date falls within the start-end range and is active that day
                if (exceptionDate >= startDate && exceptionDate <= endDate && parts[dayOfWeekIndex] == "1")
                {
                    activeServiceIds.Add(parts[serviceIdIndex]);
                }
            }
        }

        return activeServiceIds.Distinct().ToList();
    }

    private int GetDayColumnIndex(string dayOfWeek, string[] header)
    {
        int dayIndex = Array.IndexOf(header, dayOfWeek);
        if (dayIndex == -1)
            throw new ArgumentException($"Invalid or missing day column: {dayOfWeek}");
        return dayIndex;
    }


    private bool CheckIfActiveOnDate(string[] calendarParts, DateTime exceptionDate)
    {
        string dayOfWeek = exceptionDate.DayOfWeek.ToString().ToLower();
        int dayIndex = GetDayColumnIndex(dayOfWeek);

        return calendarParts[dayIndex] == "1";
    }

    private int GetDayColumnIndex(string dayOfWeek)
    {
        switch (dayOfWeek.ToLower())
        {
            case "sunday":
                return 1;
            case "monday":
                return 2;
            case "tuesday":
                return 3;
            case "wednesday":
                return 4;
            case "thursday":
                return 5;
            case "friday":
                return 6;
            case "saturday":
                return 7;
            default:
                throw new ArgumentException("Invalid day of the week");
        }
    }

    private List<string> GetSundayServiceIds(List<TripData> trips, string calendarFilePath)
    {
        var sundayServiceIds = new List<string>();

        if (!File.Exists(calendarFilePath))
        {
            updateStatusAction?.Invoke($"Error: calendar.txt not found at {calendarFilePath}", false);
            return sundayServiceIds;
        }

        var lines = File.ReadAllLines(calendarFilePath);
        var header = lines[0].Split(','); // Read the header row to determine column indices

        // Get the index for 'service_id' and 'sunday' columns
        int serviceIdIndex = Array.IndexOf(header, "service_id");
        int sundayIndex = Array.IndexOf(header, "sunday");

        if (serviceIdIndex == -1 || sundayIndex == -1)
        {
            updateStatusAction?.Invoke("Error: 'service_id' or 'sunday' column not found in calendar.txt.", false);
            return sundayServiceIds;
        }

        // Read the rest of the calendar data and extract Sunday service IDs
        foreach (var line in lines.Skip(1))
        {
            var parts = line.Split(',');

            if (parts.Length > Math.Max(serviceIdIndex, sundayIndex) && parts[sundayIndex] == "1")
            {
                string serviceId = parts[serviceIdIndex];

                // Check if the service ID exists in the trips data
                if (trips.Any(t => t.ServiceId == serviceId))
                {
                    sundayServiceIds.Add(serviceId);
                }
            }
        }

        return sundayServiceIds.Distinct().ToList(); // Ensure unique service IDs
    }

    private List<ExceptionInfo> GetExceptionsFromExcel(ExcelWorksheet worksheet)
    {
        var exceptions = new List<ExceptionInfo>();

        for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
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
        var lines = calendarDates.Select(d => string.Join(",", d)).ToList();
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

