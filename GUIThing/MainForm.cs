/*
MIT License

Copyright (c) 2017 Mihail Chilyashev <m@chilyashev.com>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO.Ports;
using System.Diagnostics;
using System.Threading;

namespace GUIThing
{
    public partial class MainForm : Form
    {
        /// <summary>
        /// Whichever port was selected
        /// </summary>
        public string selectedPort { get; set; }
        /// <summary>
        /// List of ports available on the system
        /// </summary>
        private string[] _ports { get; set; }
        /// <summary>
        /// The current COM port
        /// </summary>
        private SerialPort serial { get; set; }

        /// <summary>
        /// The slave to take care of all the work without the UI freezing. We're not MS, we can't have unresponsive software
        /// </summary>
        BackgroundWorker worker;

        /// <summary>
        /// The COM port speed (in baud). 
        /// Should be the same speed as the one set in the Arduino. Otherwise stuff will look funny.
        /// </summary>
        private int COM_PORT_SPEED = 115200;

        // Some constants to use when the worker reports its status
        private static readonly int REPORT_TYPE_ERROR = 0;
        private static readonly int REPORT_TYPE_CRITICAL_ERROR = 1;

        /// <summary>
        /// Gives us access to MSI Afterburner's shared memory. It's kinda like a wrapper.
        /// You pass a buffer to this monstrosity and it fills it with stuff.
        /// </summary>
        /// <param name="buf">The buffer to be filled</param>
        /// <param name="maxLen">The buffer's max size</param>
        /// <returns></returns>
        [DllImport("OLEDThing.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetAfterburnerData(byte[] buf, int maxLen);


        public MainForm()
        {
            InitializeComponent();
            serial = new SerialPort();
        }

        private void startButton_Click(object sender, EventArgs e)
        {
            // Save the port in this here variable so we don't have to acces UI stuff from other threads
            selectedPort = cmbPort.Text;


            // Being <del>lazy<del> smart, we use a single button to do not one, but TWO things!
            if (serial.IsOpen)
            {
                StopCommunication();
            }
            else
            {
                selectedPort = cmbPort.SelectedItem as String;
                // What even are you doing? 
                if (selectedPort == null || selectedPort.Length == 0)
                {
                    ShowError("Select a COM port");
                    return;
                }

                // Prepare the UI for the job
                UpdateUIForCommunicationStart();
                // Connect to the COM port and start spamming!
                ConnectToCOMPort();
            }

        }
        /// <summary>
        /// Disable some controls so nothing is changed while the thing is working.
        /// </summary>
        private void UpdateUIForCommunicationStart()
        {
            startButton.Text = "Stop";
            cmbPort.Enabled = false;
            btnRefreshCOMPorts.Enabled = false;
        }

        /// <summary>
        /// Stop the communication and update the UI accordingly (i.e. enable some controls).
        /// </summary>
        private void StopCommunication()
        {
            startButton.Text = "Connect";
            if (serial.IsOpen)
            {
                serial.Dispose();
                serial.Close();
            }
            cmbPort.Enabled = true;
            btnRefreshCOMPorts.Enabled = true;
        }

        /// <summary>
        /// Where the magic happens.
        /// This method starts the communication with the microcontroller, starts collecting data and spamming
        /// </summary>
        private void ConnectToCOMPort()
        {
            // Kill it just in case.
            if (serial.IsOpen)
            {
                serial.Dispose();
                serial.Close();
                return;
            }
            else
            {
                // Create the worker thread and make it suffer
                worker = new BackgroundWorker();

                // Set some params 
                worker.WorkerReportsProgress = true;
                worker.DoWork += worker_Suffer; // The real deal - here's where all the real work is done
                worker.ProgressChanged += worker_Whine; // We need a way to tell the UI thread that something went wrong. This is where the whining comes in play.
                worker.RunWorkerAsync(); // Start working asynchronously 
            }
        }

        /// <summary>
        /// The main method of the worker thread. 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void worker_Suffer(object sender, DoWorkEventArgs e)
        {
            string encodedLine = "";
            BackgroundWorker worker = sender as BackgroundWorker;

            if (selectedPort == null)
            {
                worker.ReportProgress(REPORT_TYPE_CRITICAL_ERROR, "No port selected");
                return;
            }

            Debug.WriteLine("Port: " + selectedPort);

            // Set the COM port up.
            serial.PortName = selectedPort;
            serial.BaudRate = COM_PORT_SPEED;
            serial.Parity = Parity.None;
            serial.StopBits = StopBits.One;
            serial.DataBits = 8;
            serial.Handshake = Handshake.XOnXOff;
            serial.DtrEnable = true;

            // Try to open the port and say something if this fails
            try
            {
                serial.Open();
            }
            catch (Exception exc)
            {
                worker.ReportProgress(REPORT_TYPE_CRITICAL_ERROR, "Couldn't connect to COM port: " + exc.Message);
                return;
            }

            try
            {
                byte[] buf = new byte[300];

                // Send stuff to the Arduino every second
                while (true)
                {

                    // Test it here. Just in case it got killed 
                    if (!serial.IsOpen)
                    {
                        break;
                    }

                    int responseLength = GetAfterburnerData(buf, 300);

                    if (responseLength > 0)
                    {
                        var str = System.Text.Encoding.ASCII.GetString(buf, 0, responseLength);
                        // Doing this ugly spliting and building the pack again, because the encoding is wrong and I'm too lazy to make it prettier.
                        string[] parts = str.Split(';');
                        string pack = String.Format("{0:0}C;{1:0}C;{2:0.00};$", Double.Parse(parts[0]), Double.Parse(parts[1]), Double.Parse(parts[2]));
                        serial.WriteLine(pack);
                        Debug.WriteLine("\n=================\nRet: {0}, str: {1}\n=================", responseLength, pack);
                    }
                    else
                    {
                        serial.WriteLine("0C;0C;0.00;$");
                        worker.ReportProgress(REPORT_TYPE_CRITICAL_ERROR, "Error getting data from MSI Afterburner.\nIs it installed and running?");
                        return;
                    }
                    serial.BaseStream.Flush();

                    Thread.Sleep(1000); // Wait half a second two times
                }
            }
            catch (Exception exc) // CATCH ALL THE EXCEPTIONS!
            {
                // Free the COM port
                if (serial.IsOpen)
                {
                    serial.Close();
                    serial.Dispose();
                }
                worker.ReportProgress(REPORT_TYPE_ERROR, "Something went wrong: " + exc.Message);
                Debug.WriteLine(exc.Message);
            }
        }

        /// <summary>
        /// Using the ReportProgress method to cheat a bit.
        /// Since this is running on the UI thread, we can access controls and do all kinds of fun stuff without tons of delegating.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void worker_Whine(object sender, ProgressChangedEventArgs e)
        {
            if (e.ProgressPercentage == REPORT_TYPE_CRITICAL_ERROR)
            { // Something went really wrong. Report it and kill the communication
                ShowError(e.UserState as string);
                StopCommunication();
            }
            else if (e.ProgressPercentage == REPORT_TYPE_ERROR)
            { // Something went just a bit wrong. Report it and carry on
                ShowError(e.UserState as string);
            }
        }


        /// <summary>
        /// Load list of available COM ports and populate the combo
        /// </summary>
        private void LoadCOMPortsList()
        {
            // Get list
            _ports = SerialPort.GetPortNames();
            // Clear the ports from the UI
            cmbPort.Items.Clear();

            // If there are any ports available, populate the combo
            if (_ports.Length > 0)
            {
                cmbPort.Enabled = true;
                cmbPort.Items.AddRange(_ports);
                cmbPort.SelectedIndex = 0;
            }
            else
            {
                // Otherwise, bitch about it and disable the combo.
                ShowError("No available COM ports.");
                cmbPort.Enabled = false;
            }
        }

        /// <summary>
        /// Display a message about a non-success
        /// </summary>
        /// <param name="errorText"></param>
        private void ShowError(string errorText)
        {
            MessageBox.Show(this, errorText, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Load available COM ports on startup
            LoadCOMPortsList();
        }

        private void btnRefreshCOMPorts_Click(object sender, EventArgs e)
        {
            LoadCOMPortsList();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StopCommunication();
            Application.Exit();
        }

        private void lblRepo_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(lblRepo.Text);
        }
    }
}
