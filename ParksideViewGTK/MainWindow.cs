﻿using System;
using Gtk;
using System.Diagnostics;
using System.Text;
using System.IO.Ports;
using System.Globalization;
using System.IO;

namespace ParksideViewGTK
{
    internal partial class MainWindow: Gtk.Window
    {
        private bool isConnected = false, isPaused = false, isRecording = false,
            isTimerRunning = false, isTimerUpdatePending = false, isAlive = true;
        private StringBuilder recordingBuffer = new StringBuilder();
        private DateTime recordingStart = DateTime.Now;
        private int blankCount = 0, recordingCount = 0, timerInterval = 550, portCount = 0;
        private Multimeter meter = null;

        public MainWindow()
            : base(Gtk.WindowType.Toplevel)
        {
            Build();

            // Configure the fonts
            valueLabel.ModifyFont(Pango.FontDescription.FromString("Bold 52"));
            unitLabel.ModifyFont(Pango.FontDescription.FromString("Bold 36"));
            modeLabel.ModifyFont(Pango.FontDescription.FromString("Bold 18"));

            // And colors
            readoutEventBox.ModifyBg(StateType.Normal, new Gdk.Color(0xff, 0xff, 0xff));
            copyrightLabel.ModifyFg(StateType.Normal, new Gdk.Color(139, 69, 19));

            // And cursors
            readoutEventBox.GdkWindow.Cursor = new Gdk.Cursor(Gdk.CursorType.Xterm);
            copyrightEventBox.GdkWindow.Cursor = new Gdk.Cursor(Gdk.CursorType.Hand1);

            // Translate the static parts of the UI
            // Connection group
            connectionGroup.Label = Language.ConnectionHeading;
            portLabel.Text = Language.PortLabel;
            // TODO: Improve the icon buttons
            //refreshPortsButton.Label = Language.RefreshButton;
            // Acquisition group
            acquisitionGroup.Label = Language.AcquisitionHeading;
            intervalLabel.Text = Language.Interval;
            // CSV group
            csvFormatGroup.Label = Language.CSVFormatHeading;

            // Finish the title
            Version appVer = typeof(Program).Assembly.GetName().Version;
            this.Title += string.Format(" {0}.{1}", appVer.Major, appVer.Minor);

            // Refresh the ports list
            RefreshPorts();

            // Setup the UI (handles translation of the dynamic parts)
            ChangeUI(false, false, false);
        }

        protected void OnDeleteEvent(object sender, DeleteEventArgs a)
        {
            // Pause the acquisition
            PauseAcquire();

            // Stop and save any recording
            if (isRecording)
            {
                StopRecording();
                SaveRecording();
            }

            // Quit the application
            Application.Quit();
            a.RetVal = true;
        }

        protected void OnReadoutEventBoxButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            CopyValue();
        }

        protected void OnCopyrightEventBoxButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Process.Start("https://mikrocontroller.net/topic/491973");
        }

        protected void OnRefreshPortsButtonClicked(object sender, EventArgs e)
        {
            RefreshPorts();
        }

        protected void OnConnectButtonClicked(object sender, EventArgs e)
        {
            // If already connected, stop the connection and return
            if (isConnected)
            {
                // Stop the acquisition
                PauseAcquire();

                // Stop and save any recording
                if (isRecording)
                {
                    StopRecording();
                    SaveRecording();
                }

                // Disconnect and return
                Disconnect();
                return;
            }

            // If not connected, establish the connection
            // Try to fetch the port name first
            if (string.IsNullOrWhiteSpace(portsListBox.ActiveText))
                return;
            string portName = portsListBox.ActiveText;

            // Establish the connection
            Connect(portName);
        }

        protected void OnAcquisitionPauseButtonClicked(object sender, EventArgs e)
        {
            // Toggle the acquisition state
            if (isPaused)
                StartAcquire();
            else
                PauseAcquire();
        }

        protected void OnRecordToggleButtonClicked(object sender, EventArgs e)
        {
            // Handle running recordings
            if (isRecording)
            {
                StopRecording();
                SaveRecording();
                return;
            }

            // Make sure that the DMM is connected
            if (!isConnected)
                return;

            // Start the recording
            StartRecording();
        }

        protected void OnIntervalSetButtonClicked(object sender, EventArgs e)
        {
            // Update the timer
            if (!isConnected || isPaused)
                UpdateTimer();
            else
                isTimerUpdatePending = true;
        }

        private bool AcquisitionCallback()
        {
            // Make sure that the state and meter objects are valid
            if (!isConnected || meter == null || !meter.IsConnected)
            {
                // Disconnect and return, if not
                Disconnect();
                return false;
            }

            // Handle pause mode
            if (isPaused)
            {
                // Discard the samples, start the timer again and continue
                meter.Flush();
                return isTimerRunning;
            }

            // Check, if the maximum duration of missed screen updates has been exceeded
            if ((blankCount - 1) * timerInterval >= 1000)
            {
                // Clear the readout, bargraph and alive flag after a certain number of missed updates
                isAlive = false;
                UpdateStatusLabels();
                ClearReadout();
                ClearBargraph();
            }

            // Always update the blank count (it will be reset if there was a successful update)
            blankCount++;

            // Handle pending timer updates (requires ending the current one)
            if (isTimerUpdatePending)
            {
                UpdateTimer();
                return false;
            }

            // Read all available packets
            while (meter.IsAvailable)
            {
                Packet sample;
                if (!meter.Receive(out sample))
                {
                    // Update the blank screen count
                    blankCount++;

                    // And turn the sampling on again
                    return isTimerRunning;
                }

                // Skip invalid packets
                if (!sample.ChecksumValid)
                    continue;

                // Clear the blank screen count and set the alive flag after a successful reception
                blankCount = 0;
                if (!isAlive)
                {
                    isAlive = true;
                    UpdateStatusLabels();
                }

                // Handle regular display
                // Set the mode label accordingly first
                bool validMode = true;
                bool valueMode = true;
                switch (sample.Mode)
                {
                    case Mode.Ampere:
                        modeLabel.Text = Language.ModeCurrent;
                        break;

                    case Mode.AmpereMicro:
                        modeLabel.Text = Language.ModeCurrent;
                        break;

                    case Mode.AmpereMilli:
                        modeLabel.Text = Language.ModeCurrent;
                        break;

                    case Mode.ContinuityOhm:
                        modeLabel.Text = Language.ModeContinuity;
                        break;

                    case Mode.DiodeVolt:
                        modeLabel.Text = Language.ModeDiode;
                        break;

                    case Mode.ResistanceOhm:
                        modeLabel.Text = Language.ModeResistance;
                        break;

                    case Mode.VoltAC:
                        modeLabel.Text = Language.ModeVoltageAC;
                        break;

                    case Mode.VoltDC:
                        modeLabel.Text = Language.ModeVoltageDC;
                        break;

                    case Mode.Squarewave:
                        modeLabel.Text = Language.ModeSquarewave;
                        valueMode = false;
                        break;

                    default:
                        modeLabel.Text = Language.ModeUnknown;
                        valueMode = false;
                        validMode = false;
                        break;
                }

                // Allocate variables for the number parsing
                bool negative = false;
                int integer = 0, fractional = 0, exponent = 0, precision = 0;
                char unit = '\0', unitPrefix = '\0';

                // Attempt to parse the number
                if (validMode && valueMode)
                    validMode = Multimeter.Parse(sample, out negative, out integer, out fractional, out exponent, out precision, out unit, out unitPrefix);

                // Check, if the mode is invalid or that the number is OL
                bool overloaded = Multimeter.IsOverloaded(sample);
                if (overloaded && valueMode) // Overload
                {
                    // Update the value and unit labels
                    valueLabel.Text = "OL";
                    unitLabel.Text = unit.ToString();

                    // Fill the bargraph
                    FillBargraph();
                }
                else if (!validMode || !valueMode)
                {
                    // No valid mode
                    valueLabel.Text = "";
                    unitLabel.Text = "";

                    // Clear the bargraph
                    ClearBargraph();

                    // Continue and skip the rest
                    continue;
                }
                else
                {
                    // Just format the value according to the precision
                    valueLabel.Text = precision < 1 ? integer.ToString() :
                        string.Format("{0}{1}.{2:D" + precision.ToString() + "}", negative ? "-" : "", integer, fractional);
                    // Also update the unit label
                    unitLabel.Text = unitPrefix == '\0' ? unit.ToString() : string.Format("{0}{1}", unitPrefix, unit);
                }

                // Update the UI
                if (!overloaded)
                {
                    bargraphBar.Adjustment.Lower = 0;
                    bargraphBar.Adjustment.Upper = sample.Value < 0 ? Math.Abs(Multimeter.RangeMin(sample.Mode, sample.Range)) : Multimeter.RangeMax(sample.Mode, sample.Range);
                    bargraphBar.Adjustment.Value = Math.Abs(sample.Value);
                }

                // Handle record mode only for valid value modes
                if (isRecording)
                {
                    // Calculate the offset in seconds
                    TimeSpan delta = sample.ReceptionTime - recordingStart;

                    // Assemble the line, e.g.: 4.32,12.34E-3,V
                    // Start with the time offset
                    recordingBuffer.AppendFormat(new NumberFormatInfo() { NumberDecimalSeparator = GetCSVFractionalSeparator().ToString(), NumberDecimalDigits = 2 },
                        "{0:E}", delta.TotalSeconds);
                    // Followed by the delimiter
                    recordingBuffer.Append(GetCSVDelimiter());
                    // Followed by the value
                    if (overloaded) // Overload
                        recordingBuffer.Append("OL");
                    else if (precision < 1) // Integer value
                        recordingBuffer.AppendFormat("{0}E{1}", integer, exponent);
                    else // Fixed point value
                        recordingBuffer.AppendFormat("{0}{1}{2}{3:D" + precision.ToString() + "}E{4}", negative ? "-" : "", integer,
                            GetCSVFractionalSeparator(), fractional, exponent);
                    // Followed by the delimiter
                    recordingBuffer.Append(GetCSVDelimiter());
                    // Followed by the unit
                    recordingBuffer.Append(unit);
                    // And a final line break
                    recordingBuffer.AppendLine();

                    // Update the running for label and increment the sample counter
                    recordingCount++;
                    UpdateStatusLabels();
                }
            }

            // Start the timer again
            return isTimerRunning;
        }

        /// <summary>
        /// Copies the currently displayed value to the clipboard.
        /// </summary>
        private void CopyValue()
        {
            // Assemble the value
            string value = valueLabel.Text + unitLabel.Text;

            // Only continue, if the value is not empty
            if (string.IsNullOrWhiteSpace(value))
                return;

            // Ask the user about copying the value into the clipboard
            if (ShowQuestionBox(string.Format(Language.CopyValueText, value), Language.CopyValueTitle))
                this.GetClipboard(Gdk.Selection.Clipboard).Text = value;
        }

        /// <summary>
        /// Refreshes the list of displayed ports.
        /// </summary>
        private void RefreshPorts()
        {
            // Make sure that the list might be refreshed
            if (!portsListBox.Sensitive)
                return;

            // Catch any potential errors from the SerialPort function
            try
            {
                // Fetch the list of ports
                string[] ports = SerialPort.GetPortNames();

                // Clear the old list
                for (int i = 0; i < portCount; i++)
                    portsListBox.RemoveText(0);

                // Add all items to it
                foreach (string port in ports)
                    portsListBox.AppendText(port);

                // Update the number of items
                portCount = ports.Length;
            }
            catch (Exception ex)
            {
                // If there was any error, exit, as there is no recovery from this
                ShowErrorBox(string.Format(Language.PortsListErrorText, ex.Message), Language.PortsListErrorTitle);
                Application.Quit();
            }
        }

        /// <summary>
        /// Establish a connection to the DMM.
        /// </summary>
        /// <param name="portName">The name of the serial port to use.</param>
        private void Connect(string portName)
        {
            // Validate the port name
            if (string.IsNullOrWhiteSpace(portName))
                return;

            // Initialize the multimeter object
            meter = new Multimeter(portName);

            // Set the alive flag and clear the blank counter
            isAlive = true;
            blankCount = 0;

            // Try to establish the connection
            Exception error = meter.Connect();
            if (error != null)
            {
                // If there was any error, cancel and change the UI
                ShowWarningBox(string.Format(Language.ConnectionErrorText, portName, error.Message), Language.ConnectionErrorTitle);
                ChangeUI(false);
                return;
            }

            // Change the UI
            ChangeUI(true);

            // Bootstrap the timer
            isTimerRunning = true;
            UpdateTimer();
        }

        /// <summary>
        /// Disconnect from the DMM.
        /// </summary>
        private void Disconnect()
        {
            // If there is a valid DMM object, call its' disconnect function
            if (meter != null)
                meter.Disconnect();

            // Stop the timer
            isTimerRunning = false;

            // Update the UI
            ChangeUI(false);
        }

        /// <summary>
        /// If possible, begins acquiring data
        /// </summary>
        private void StartAcquire()
        {
            // Make sure the connection is established and that the acquisition is paused
            if (!isConnected || !isPaused)
                return;

            // Update the UI
            ChangeUI(true, false, isRecording);
        }

        /// <summary>
        /// If possible, pauses acquiring data
        /// </summary>
        private void PauseAcquire()
        {
            // Make sure the connection is established and that the acquisition is running
            if (!isConnected || isPaused)
                return;

            // Update the UI
            ChangeUI(true, true, isRecording);
        }

        /// <summary>
        /// Starts the recording and writes the CSV header.
        /// </summary>
        private void StartRecording()
        {
            // Make sure that the recording is not running
            if (!isConnected || isRecording)
                return;

            // Clear the buffer and get the current timestamp
            recordingBuffer.Clear();
            recordingStart = DateTime.Now;

            // Write the model
            recordingBuffer.Append(Language.CSVModel);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.Append("Parkside PDM-300-C2");
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.AppendLine();

            // Write the version
            recordingBuffer.Append(Language.CSVSoftware);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.AppendFormat(Language.CSVVersionFormat, typeof(Program).Assembly.GetName().Version);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.AppendLine();

            // Write the date
            recordingBuffer.Append(Language.CSVDate);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.AppendFormat("{0:00}.{1:00}.{2:0000}", recordingStart.Day, recordingStart.Month, recordingStart.Year);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.AppendLine();

            // Write the time
            recordingBuffer.Append(Language.CSVTime);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.AppendFormat("{0:00}:{1:00}:{2:00}:{3:000}",
                recordingStart.Hour, recordingStart.Minute, recordingStart.Second, recordingStart.Millisecond);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.AppendLine();

            recordingBuffer.Append(Language.CSVInterval);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.AppendFormat(new NumberFormatInfo() { NumberDecimalSeparator = GetCSVFractionalSeparator().ToString(), NumberDecimalDigits = 2 },
                "{0:E}", timerInterval / 1000d);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.AppendLine();
            recordingBuffer.AppendLine();

            // Write the CSV header (3 columns)
            recordingBuffer.Append(Language.CSVDelta);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.Append(Language.CSVValue);
            recordingBuffer.Append(GetCSVDelimiter());
            recordingBuffer.Append(Language.CSVUnit);
            recordingBuffer.AppendLine();

            // Update the UI
            ChangeUI(isConnected, isPaused, true);
        }

        /// <summary>
        /// Stops the recording.
        /// </summary>
        private void StopRecording()
        {
            // Make sure that the recording is running
            if (!isRecording)
                return;

            // Change the UI and flags
            ChangeUI(isConnected, isPaused, false);
        }

        /// <summary>
        /// Attempts to save the recording. Asks the user to specify a filename.
        /// </summary>
        private void SaveRecording()
        {
            // Make sure the recording is stopped
            if (isRecording)
                StopRecording();

            // Make sure that data is available
            if (recordingBuffer.Length < 1)
                return;

            // Present the save dialog and keep asking to save
            while (true)
            {
                // Keep asking for a valid path
                string path;
                while (string.IsNullOrWhiteSpace(path = ShowSaveDialog()))
                    if (ShowQuestionBox(string.Format(Language.DiscardDataText, recordingCount), Language.DiscardDataTitle, false))
                        return;

                // Save the data
                try
                {
                    File.WriteAllText(path, recordingBuffer.ToString());
                    recordingBuffer.Clear();
                    return;
                }
                catch (Exception)
                {
                    ShowWarningBox(Language.SavingFailedText, Language.SavingFailedTitle);
                }
            }
        }

        /// <summary>
        /// Change the current mode and the UI accordingly, without affecting the recording setting, as long, as the connection is not stopped.
        /// </summary>
        /// <param name="isConnected">Indicates, whether the DMM is currently connected.</param>
        private void ChangeUI(bool isConnected)
        {
            ChangeUI(isConnected, false, false);
        }

        /// <summary>
        /// Change the current mode and the UI accordingly.
        /// </summary>
        /// <param name="isConnected">Indicates, whether the DMM is currently connected.</param>
        /// <param name="isPaused">Indicates, whether the acquisition is currently paused.</param>
        /// <param name="isRecording">Indicates, whether the recorder is currently running.</param>
        private void ChangeUI(bool isConnected, bool isPaused, bool isRecording)
        {
            // Sanitize the new local values
            isPaused = isConnected ? isPaused : false;
            isRecording = isConnected ? isRecording : false;

            // Copy the values to the global context
            this.isConnected = isConnected;
            this.isPaused = isPaused;
            this.isRecording = isRecording;

            // Update the status labels
            UpdateStatusLabels();

            // Decide what to do with the UI
            refreshPortsButton.Sensitive = !isConnected;
            portsListBox.Sensitive = !isConnected;
            // TODO: improve this
            ((Image)connectButton.Image).Pixbuf = isConnected ?
                Stetic.IconLoader.LoadIcon(this, "gtk-disconnect", global::Gtk.IconSize.Button) :
                Stetic.IconLoader.LoadIcon(this, "gtk-connect", global::Gtk.IconSize.Button);
            //connectButton.Label = isConnected ? Language.StopButton : Language.StartButton;
            recordToggleButton.Label = (isRecording && isConnected) ? Language.SaveButton : Language.RecordButton;
            acquisitionPauseButton.Label = (isPaused && isConnected) ? Language.ContinueButton : Language.PauseButton;
            recordToggleButton.Sensitive = isConnected;
            acquisitionPauseButton.Sensitive = isConnected;
            csvFormatGroup.Sensitive = isConnected && !isRecording;
            intervalNumeric.Sensitive = !isRecording;

            // Clear the readout and bargraph after disconnecting
            if (!isConnected)
            {
                ClearReadout();
                ClearBargraph();
            }
        }

        /// <summary>
        /// Updates the status labels.
        /// </summary>
        private void UpdateStatusLabels()
        {
            // Handle idle
            if (!isConnected)
            {
                connectionStatusLabel.Text = Language.AcqStatusPrefix + Language.AcqStatusDisconnected;
                acquisitionStatusLabel.Text = connectionStatusLabel.Text;
                return;
            }

            // Always set the connected label
            connectionStatusLabel.Text = Language.AcqStatusPrefix + Language.AcqStatusConnected;

            // Assemble the acquire status text
            StringBuilder statusBuilder = new StringBuilder(Language.AcqStatusPrefix);
            statusBuilder.Append(isAlive ? (isPaused ? Language.AcqStatusPaused : Language.AcqStatusRunning) : Language.AcqStatusSilent);
            if (isRecording)
            {
                // Calculate the time that has passed
                TimeSpan recordingSpan = DateTime.Now - recordingStart;
                int seconds = recordingSpan.Seconds, minutes = recordingSpan.Minutes, hours = recordingSpan.Hours + recordingSpan.Days * 24;

                // Update the builder
                statusBuilder.AppendFormat(Language.AcqStatusRecording, recordingCount, hours, minutes, seconds);
            }

            // Apply the label
            acquisitionStatusLabel.Text = string.Copy(statusBuilder.ToString());
            statusBuilder.Clear();
        }

        /// <summary>
        /// Returns the localized CSV delimiter.
        /// </summary>
        /// <returns></returns>
        private char GetCSVDelimiter()
        {
            return csvFormatDERadio.Active ? ';' : ',';
        }

        /// <summary>
        /// Returns the localized CSV fractional separator.
        /// </summary>
        /// <returns></returns>
        private char GetCSVFractionalSeparator()
        {
            return csvFormatDERadio.Active ? ',' : '.';
        }

        /// <summary>
        /// Updates the timer's interval immediately and clears the pending flag.
        /// </summary>
        private void UpdateTimer()
        {
            isTimerUpdatePending = false;
            timerInterval = (int)intervalNumeric.Value * 500 + 50;
            GLib.Timeout.Add((uint)timerInterval, AcquisitionCallback);
        }

        /// <summary>
        /// Clears the whole readout.
        /// </summary>
        private void ClearReadout()
        {
            modeLabel.Text = "";
            unitLabel.Text = "";
            valueLabel.Text = "";
        }

        /// <summary>
        /// Fills the bargraph display.
        /// </summary>
        private void FillBargraph()
        {
            bargraphBar.Adjustment.Lower = 0;
            bargraphBar.Adjustment.Upper = 1;
            bargraphBar.Adjustment.Value = 1;
        }

        /// <summary>
        /// Clears the bargraph display.
        /// </summary>
        private void ClearBargraph()
        {
            bargraphBar.Adjustment.Lower = 0;
            bargraphBar.Adjustment.Upper = 1;
            bargraphBar.Adjustment.Value = 0;
        }

        /// <summary>
        /// Shows the save dialog.
        /// </summary>
        /// <returns>The chosen path or null on cancel.</returns>
        private string ShowSaveDialog()
        {
            // Allocate the result path string
            string path = null;

            // Create the dialog
            FileChooserDialog dialog = new FileChooserDialog(Language.SaveDialogTitle,
                                           this, FileChooserAction.Save, Language.SaveDialogCancel, ResponseType.Cancel,
                                           Language.SaveDialogAccept, ResponseType.Accept);
            dialog.Filter = new FileFilter();
            dialog.Filter.AddMimeType("text/csv");

            // Run the dialog and check the response
            if (dialog.Run() == (int)ResponseType.Accept)
                path = dialog.Filename;

            // Clean up and return the result
            dialog.Destroy();
            return path;
        }

        /// <summary>
        /// Prompts the user for a yes/no answer.
        /// </summary>
        /// <returns>Returns true, if the user clicked on yes, or false, on no.</returns>
        /// <param name="text">The text of the dialog.</param>
        /// <param name="caption">The title of the dialog.</param>
        /// <param name="defaultYes">If true, make yes the default button. Otherwise make no the default button.</param>
        private bool ShowQuestionBox(string text, string caption, bool defaultYes = true)
        {
            // Allocate the result variable
            bool response = false;

            // Create the dialog
            MessageDialog dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Question, ButtonsType.YesNo, false, text)
                { Title = caption };
            // TODO: Implement the defaultYes feature

            // Run the dialog and check the response
            if (dialog.Run() == (int)ResponseType.Yes)
                response = true;
                
            // Clean up and return the result
            dialog.Destroy();
            return response;
        }

        /// <summary>
        /// Shows an error box to the user.
        /// </summary>
        /// <param name="text">The text of the dialog.</param>
        /// <param name="caption">The title of the dialog.</param>
        private void ShowErrorBox(string text, string caption)
        {
            // Create the dialog
            MessageDialog dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok, false, text)
                { Title = caption };

            // Run the dialog
            dialog.Run();

            // Clean up
            dialog.Destroy();
        }

        /// <summary>
        /// Shows a warning box to the user.
        /// </summary>
        /// <param name="text">The text of the dialog.</param>
        /// <param name="caption">The title of the dialog.</param>
        private void ShowWarningBox(string text, string caption)
        {
            // Create the dialog
            MessageDialog dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Warning, ButtonsType.Ok, false, text)
                { Title = caption };

            // Run the dialog
            dialog.Run();

            // Clean up
            dialog.Destroy();
        }
    }
}