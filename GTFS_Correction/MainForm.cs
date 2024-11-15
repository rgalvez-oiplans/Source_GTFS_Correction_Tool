using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Windows.Forms;
using static GTFS_Correction.DiscrepancyProcessor;

namespace GTFS_Correction
{
    public partial class MainForm : Form
    {
        private string shapefilePath;
        private string gtfsZipPath;

        public MainForm()
        {
            InitializeComponent();
            SetDoubleBuffered(lstStatus);
            SetDoubleBuffered(txtLog);
            SetDoubleBuffered(this);
        }

        private void SetDoubleBuffered(Control control)
        {
            if (SystemInformation.TerminalServerSession)
                return;

            var doubleBufferPropertyInfo = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            doubleBufferPropertyInfo.SetValue(control, true, null);
        }

        private void btnUploadGTFS_Click(object sender, EventArgs e)
        {
            string databasePath = Path.Combine(Application.StartupPath, "GTFSToolDB.sqlite");

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "ZIP files (*.zip)|*.zip";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    gtfsZipPath = openFileDialog.FileName;
                    lstStatus.Items.Clear();
                    txtLog.Clear();
                    lstStatus.Items.Add("GTFS ZIP file uploaded: " + gtfsZipPath);

                    string extractPath = Path.Combine(Path.GetDirectoryName(gtfsZipPath), Path.GetFileNameWithoutExtension(gtfsZipPath));

                    if (Directory.Exists(extractPath))
                    {
                        try
                        {
                            Directory.Delete(extractPath, true);
                        }
                        catch (IOException ex)
                        {
                            MessageBox.Show($"The file is locked or being used by another process: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return; // Exit the method to avoid further processing
                        }
                    }

                    try
                    {
                        lstStatus.Items.Add("Extracting ZIP file...");
                        ZipFile.ExtractToDirectory(gtfsZipPath, extractPath);

                        string stopsFilePath = Path.Combine(extractPath, "stops.txt");
                        string logFilePath = Path.Combine(extractPath, "logfile.txt");

                        if (File.Exists(stopsFilePath))
                        {
                            lstStatus.Items.Add("Exporting stop names to XLSX...");
                            var stopProcessor = new StopProcessor(databasePath, (message, updateLast) => UpdateStatusList(message, updateLast));
                            stopProcessor.ExportStopNamesToXlsx(stopsFilePath, logFilePath);
                 
                        }
                        else
                        {
                            MessageBox.Show("stops.txt not found in the ZIP file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        lstStatus.Items.Add($"Error: {ex.Message}");
                        MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            CheckIfReadyToValidate();
        }

        private void btnUploadShapefile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Shapefiles (*.shp)|*.shp";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    shapefilePath = openFileDialog.FileName;
                    lstStatus.Items.Add("Shapefile uploaded: " + shapefilePath);
                    CheckIfReadyToValidate();
                }
            }
        }

        private void CheckIfReadyToValidate()
        {
      
                if (!string.IsNullOrEmpty(gtfsZipPath))
                {
                    btnValidate.Visible = true;
                };

            
        }

        private async void btnValidate_Click(object sender, EventArgs e)
        {
            string extractPath = Path.Combine(Path.GetDirectoryName(gtfsZipPath), Path.GetFileNameWithoutExtension(gtfsZipPath));

            if (Directory.Exists(extractPath))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(extractPath))
                    {
                        if (!file.ToUpper().EndsWith("STOPNAMES.XLSX"))
                        {
                            File.Delete(file);
                        }
                    }
                }
                catch (IOException ex)
                {
                    MessageBox.Show($"The file is locked or being used by another process: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return; // Exit the method to avoid further processing
                }
            }


            try
            {
                lstStatus.Items.Add("Extracting ZIP file...");
                await Task.Run(() => ZipFile.ExtractToDirectory(gtfsZipPath, extractPath));

                string agencyFilePath = Path.Combine(extractPath, "agency.txt");
                string routesFilePath = Path.Combine(extractPath, "routes.txt");
                string shapesFilePath = Path.Combine(extractPath, "shapes.txt");
                string stopsFilePath = Path.Combine(extractPath, "stops.txt");
                string stopTimesFilePath = Path.Combine(extractPath, "stop_times.txt");
                string tripsFilePath = Path.Combine(extractPath, "trips.txt");
                string logFilePath = Path.Combine(extractPath, "logfile.txt");
                string calendarFilePath = Path.Combine(extractPath, "calendar.txt");
                string configFilePath = Path.Combine(Application.StartupPath, "config.txt");
                string databasePath = Path.Combine(Application.StartupPath, "GTFSToolDB.sqlite");
                string feedInfoFilePath = Path.Combine(extractPath, "feed_info.txt");
                string stopNamesFilePath = Path.Combine(extractPath, "stopnames.xlsx");
                string calendarDatesFilePath = Path.Combine(extractPath, "calendar_dates.txt");


                CreateLogFile(logFilePath);
                List<Discrepancy> discrepancies = null;
                Dictionary<string, string> agencyIdMap = null;

                if (File.Exists(agencyFilePath) && File.Exists(calendarFilePath))
                {
                    lstStatus.Items.Add("Processing feed info...");
                    await Task.Run(() =>
                    {
                        var feedInfoProcessor = new FeedInfoProcessor((message, updateLast) => UpdateStatusList(message, updateLast));
                        feedInfoProcessor.ProcessFeedInfo(agencyFilePath, calendarFilePath, feedInfoFilePath,configFilePath, logFilePath);
                    });
                }

                // Process Agency Data
                if (File.Exists(agencyFilePath))
                {
                    lstStatus.Items.Add("Processing agency data...");
                    await Task.Run(() =>
                    {
                        var agencyProcessor = new AgencyProcessor((message, updateLast) => UpdateStatusList(message, updateLast));
                        agencyProcessor.ProcessAgencies(agencyFilePath,logFilePath);
                        agencyIdMap = agencyProcessor.LoadAgencyIdMap(agencyFilePath);
                    });
                }
                else
                {
                    MessageBox.Show("agency.txt not found in the ZIP file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                

                if (File.Exists(routesFilePath))
                {
                    lstStatus.Items.Add("Processing routes data...");
                    await Task.Run(() =>
                    {
                        var routesProcessor = new RoutesProcessor((message, updateLast) => UpdateStatusList(message, updateLast), agencyIdMap);
                        routesProcessor.ProcessRoutes(routesFilePath, logFilePath);
                    });
                }
                else
                {
                    MessageBox.Show("routes.txt not found in the ZIP file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }


                // Process Stops Data
                if (File.Exists(stopsFilePath))
                {
                    lstStatus.Items.Add("Processing stops data...");
                    await Task.Run(() =>
                    {
                        var stopProcessor = new StopProcessor(databasePath, (message, updateLast) => UpdateStatusList(message, updateLast));
                        stopProcessor.ProcessStops(stopsFilePath, logFilePath, stopNamesFilePath);
        
                    });
                }
                else
                {
                    MessageBox.Show("stops.txt not found in the ZIP file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }



                if (File.Exists(stopTimesFilePath))
                {
                    lstStatus.Items.Add("Processing stop times data...");
                    await Task.Run(() =>
                    {
                        var stopTimeProcessor = new StopTimeProcessor((message, updateLast) => UpdateStatusList(message, updateLast));
                        stopTimeProcessor.ProcessStopTimes(stopTimesFilePath, stopsFilePath,logFilePath);
                    });
                }
                else
                {
                    MessageBox.Show("stop_times.txt not found in the ZIP file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

             

                if (File.Exists(tripsFilePath))
                {
                    lstStatus.Items.Add("Processing Trips...");
                    await Task.Run(() =>
                    {
                        var tripProcessor = new TripProcessor((message, updateLast) => UpdateStatusList(message, updateLast));
                        tripProcessor.ProcessTrips(tripsFilePath, logFilePath);
                    });
                }
                else
                {
                    MessageBox.Show("trips.txt not found in the ZIP file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
              

                if (File.Exists(stopsFilePath) && File.Exists(stopTimesFilePath) && File.Exists(shapesFilePath) && File.Exists(tripsFilePath))
                {
                    lstStatus.Items.Add("Processing Last Stop/Last Shape discrepancies...");
                    await Task.Run(() =>
                    {
                        var discrepancyProcessor = new DiscrepancyProcessor((message, updateLast) => UpdateStatusList(message, updateLast));
                        string outputFilePath = Path.Combine(extractPath, "discrepancies.txt");
                    discrepancyProcessor.ProcessDiscrepancies(tripsFilePath, stopTimesFilePath, stopsFilePath, shapesFilePath, outputFilePath);
                        return discrepancies = LoadDiscrepancies(outputFilePath);
                    });
                }
                if (File.Exists(shapesFilePath))
                {
                    lstStatus.Items.Add("Processing shapes data...");
                    await Task.Run(() =>
                    {
                
                        var shapeProcessor = new ShapeProcessor(shapefilePath, (message, updateLast) => UpdateStatusList(message, updateLast));
                        shapeProcessor.ProcessShapes(shapesFilePath, logFilePath, discrepancies);
                    });
                }
                else
                {
                    MessageBox.Show("shapes.txt not found in the ZIP file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                // Process exceptions from the spreadsheet
                try
                {
                    lstStatus.Items.Add("Processing calendar exceptions...");
                    await Task.Run(() =>
                    {
                        var exceptionProcessor = new CalendarExceptionProcessor((message, updateLast) => UpdateStatusList(message, updateLast));
                        exceptionProcessor.ProcessExceptionsFromExcel(calendarDatesFilePath,tripsFilePath,calendarFilePath);
                    });

                    lstStatus.Items.Add("Calendar exceptions processed successfully.");
                }
                catch (Exception ex)
                {
                    lstStatus.Items.Add($"Error processing calendar exceptions: {ex.Message}");
                    MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                lstStatus.Items.Add("Processing complete. Check the log file for details.");



                lstStatus.Items.Add("Processing complete. Check the log file for details.");
                txtLog.Text = await ReadAllTextAsync(logFilePath);

                string zipFilename = GetZipFilename(configFilePath);
                string outputZipPath = Path.Combine(Path.GetDirectoryName(gtfsZipPath), zipFilename);

                lstStatus.Items.Add("Creating output ZIP file...");
                await Task.Run(() => CreateOutputZipFile(extractPath, outputZipPath));
                lstStatus.Items.Add("Output ZIP file created: " + outputZipPath);
            }
            catch (Exception ex)
            {
                lstStatus.Items.Add($"Error: {ex.Message}");
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task<string> ReadAllTextAsync(string path)
        {
            using (var reader = new StreamReader(path))
            {
                return await reader.ReadToEndAsync();
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

        private string GetZipFilename(string configFilePath)
        {
            string zipFilename = "output.zip";
            if (File.Exists(configFilePath))
            {
                using (var reader = new StreamReader(configFilePath))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var parts = line.Split('=');
                        if (parts.Length == 2 && parts[0].Trim() == "zipfilename")
                        {
                            zipFilename = parts[1].Trim();
                        }
                    }
                }
            }

            string dateSuffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.GetFileNameWithoutExtension(zipFilename) + "_" + dateSuffix + Path.GetExtension(zipFilename);
        }

        private void CreateOutputZipFile(string sourceDir, string outputZipPath)
        {
            if (File.Exists(outputZipPath))
            {
                File.Delete(outputZipPath);
            }

            using (var zipArchive = ZipFile.Open(outputZipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(sourceDir))
                {
                    if (Path.GetFileName(file) != "logfile.txt")
                    {
                        zipArchive.CreateEntryFromFile(file, Path.GetFileName(file));
                    }
                }
            }
        }

        private void UpdateStatusList(string message, bool updateLast = false)
        {
            if (lstStatus.InvokeRequired)
            {
                lstStatus.Invoke(new Action(() => UpdateStatusList(message, updateLast)));
            }
            else
            {
                if (updateLast && lstStatus.Items.Count > 0)
                {
                    lstStatus.Items[lstStatus.Items.Count - 1] = message;
                }
                else
                {
                    lstStatus.BeginUpdate();
                    lstStatus.Items.Add(message);
                    lstStatus.EndUpdate();
                }

                // Scroll to the bottom to ensure the latest message is visible
                lstStatus.TopIndex = lstStatus.Items.Count - 1;
            }
        }
        private List<Discrepancy> LoadDiscrepancies(string outputFilePath)
        {
            var discrepancies = new List<Discrepancy>();
            using (var reader = new StreamReader(outputFilePath))
            {
                var header = reader.ReadLine(); // Read header

                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var parts = line.Split(',');

                    var discrepancy = new Discrepancy
                    {
                        TripId = parts[0],
                        ShapeId = parts[1],
                        MaxTripDistanceTraveled = double.Parse(parts[2], CultureInfo.InvariantCulture),
                        MaxShapeDistanceTraveled = double.Parse(parts[3], CultureInfo.InvariantCulture),
                        GeoDistanceToShape = double.Parse(parts[4], CultureInfo.InvariantCulture),
                        StopLat = double.Parse(parts[5], CultureInfo.InvariantCulture),
                        StopLon = double.Parse(parts[6], CultureInfo.InvariantCulture),
                        ShapeLat = double.Parse(parts[7], CultureInfo.InvariantCulture),
                        ShapeLon = double.Parse(parts[8], CultureInfo.InvariantCulture)
                    };

                    discrepancies.Add(discrepancy);
                }
            }

            return discrepancies;

        }

        private void btnMergeGTFS_Click(object sender, EventArgs e)
        { 
            // Create a new instance of frmMergeGTFS
            frmMergeGTFS mergeForm = new frmMergeGTFS();

            // Show the form
            mergeForm.ShowDialog();  // Or use mergeForm.Show() if you don't want it to be modal

        }
    }
}



