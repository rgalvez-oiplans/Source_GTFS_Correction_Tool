using System;
using System.Data;
using System.IO;
using ExcelDataReader;
using GTFS_Correction;

namespace GTFS_Import
{
    class Program
    {
        static void Main(string[] args)
        {
            string excelFilePath = "stops.xlsx";
            string databasePath = "GTFSToolDB.sqlite";

            var sqliteHelper = new SQLiteHelper(databasePath);
            ImportStopsData(excelFilePath, sqliteHelper);

        }

        private static void ImportStopsData(string excelFilePath, SQLiteHelper sqliteHelper)
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
                                if (!sqliteHelper.StopNameExists(stopNameKey))
                                {
                                    sqliteHelper.AddOrUpdateStopName(stopName, stopNameDescription);
                                }
                                else
                                {
                                    Console.WriteLine($"Stop name '{stopName}' already exists in the database.");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error importing row {i}: {ex.Message}");
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Import completed.");
        }

        private static string NormalizeStopName(string stopName)
        {
            return stopName.Replace(" ", string.Empty).ToUpperInvariant();
        }
    }
}
