using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GTFS_Correction
{
    public partial class frmAbout : Form
    {
        public frmAbout()
        {
            InitializeComponent();
            PopulateAboutInfo();
        }

        private void PopulateAboutInfo()
        {
            string version = FileVersionInfo.GetVersionInfo(Application.ExecutablePath).FileVersion;

            lblAppName.Text = Application.ProductName;
            lblVersion.Text = $"Version {version}";
            lblCopyright.Text = $"© {DateTime.Now.Year} Broward County Transit. All rights reserved.";
            lblDeveloper.Text = "Developed by Roberto Galvez, Jr.";
        }

        private void lnkWebsite_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.broward.org/BCT",
                UseShellExecute = true
            });
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
