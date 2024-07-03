using System;
using System.IO;

namespace GTFS_Correction
{
    public class GTFSProcessor
    {
        private readonly string databasePath;
        private readonly Action<string, bool> updateStatusAction;

        public GTFSProcessor(string databasePath, Action<string, bool> updateStatusAction)
        {
            this.databasePath = databasePath;
            this.updateStatusAction = updateStatusAction;
        }

        public void ProcessGTFS(string shapesFilePath, string stopsFilePath, string logFilePath, string configFilePath)
        {
            CreateLogFile(logFilePath);

            if (File.Exists(stopsFilePath))
            {
                //var stopProcessor = new StopProcessor(databasePath, updateStatusAction);
                //stopProcessor.ProcessStops(stopsFilePath, logFilePath);
            }

            if (File.Exists(shapesFilePath))
            {
                // Process shapes if necessary
            }
        }

        private void CreateLogFile(string logFilePath)
        {
            if (!File.Exists(logFilePath))
            {
                using (var logWriter = new StreamWriter(logFilePath))
                {
                    logWriter.WriteLine("Log file created.");
                }
            }
        }
    }
}
