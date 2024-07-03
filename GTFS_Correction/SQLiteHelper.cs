using System;
using System.Data.SQLite;

namespace GTFS_Correction
{
    public class SQLiteHelper : IDisposable
    {
        private readonly SQLiteConnection connection;

        public SQLiteHelper(string databasePath)
        {
            connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
            connection.Open();
            CreateTables();
        }

        private void CreateTables()
        {
            string createTableQuery = @"
                CREATE TABLE IF NOT EXISTS StopNameLookup (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    stop_name TEXT,
                    stop_name_description TEXT,
                    stopnamekey TEXT UNIQUE COLLATE NOCASE
                );";
            using (var command = new SQLiteCommand(createTableQuery, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        public bool StopNameExists(string stopNameKey)
        {
            string query = "SELECT COUNT(1) FROM StopNameLookup WHERE stopnamekey = @stopnamekey";
            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@stopnamekey", stopNameKey);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        public string GetStopNameDescription(string stopNameKey)
        {
            string query = "SELECT stop_name_description FROM StopNameLookup WHERE stopnamekey = @stopnamekey";
            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@stopnamekey", stopNameKey);
                return command.ExecuteScalar()?.ToString();
            }
        }

        public void AddOrUpdateStopName(string stopName, string stopNameDescription)
        {
            string stopNameKey = NormalizeStopName(stopName);
            string query = @"
                INSERT INTO StopNameLookup (stop_name, stop_name_description, stopnamekey)
                VALUES (@stop_name, @stop_name_description, @stopnamekey)
                ON CONFLICT(stopnamekey) DO UPDATE SET stop_name_description = excluded.stop_name_description,stopnamekey=excluded.stopnamekey;";
            using (var command = new SQLiteCommand(query, connection))
            {
                command.Parameters.AddWithValue("@stop_name", stopName);
                command.Parameters.AddWithValue("@stop_name_description", stopNameDescription);
                command.Parameters.AddWithValue("@stopnamekey", stopNameKey);
                command.ExecuteNonQuery();
            }
        }

        private string NormalizeStopName(string stopName)
        {
            return stopName.Replace(" ", string.Empty).ToUpperInvariant();
        }

        public void Dispose()
        {
            connection?.Dispose();
        }
    }
}
