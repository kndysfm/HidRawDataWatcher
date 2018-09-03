namespace Watcher
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Documents;
    using System.Windows.Media;
    using Djlastnight.Hid;
    using Djlastnight.Hid.Usage;
    using System.Diagnostics;

    internal partial class HidRawDataWatcher : Window
    {
        private List<CheckBox> dataCheckBoxes;
        private byte[] lastData;
        private HidDataReader reader;

        public HidRawDataWatcher()
        {
            this.InitializeComponent();
        }

        private void UpdateDeviceList()
        {
            if (!this.listView.IsEnabled)
            {
                return;
            }

            var devices = HidDataReader.GetDevices();
            foreach (var d in devices)
            {
                ushort vid = d.VendorId;
                ushort pid = d.ProductId;
            }

            this.listView.ItemsSource = devices;
            this.listView.SelectedIndex = -1;
            var view = (CollectionView)CollectionViewSource.GetDefaultView(this.listView.ItemsSource);
            var groupDescription = new PropertyGroupDescription("DeviceType");
            view.GroupDescriptions.Add(groupDescription);

            this.lastData = new byte[0];
        }

        private string[] GetLines(string text)
        {
            if (text == null)
            {
                return null;
            }

            return text.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void OnStartClicked(object sender, RoutedEventArgs e)
        {
            if (this.listView.SelectedIndex == -1)
            {
                MessageBox.Show("Please choose device first", "No device selected", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int pid = 0;
            int vid = 0;
            try
            {
                pid = Convert.ToInt32(this.pid.Text, 16);
                vid = Convert.ToInt32(this.vid.Text, 16);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error, while parsing pid or vid: " + ex.Message);
                return;
            }

            if (pid < 0 || vid < 0)
            {
                MessageBox.Show("Error! Negative pid or vid are not accepted!");
                return;
            }

            this.reader = new HidDataReader(this);
            this.reader.Device = (this.listView.SelectedItem as Device);
            this.reader.HidDataReceived += this.OnHidDataReceived;

            this.startButton.IsEnabled = false;
            this.stopButton.IsEnabled = true;
            this.listView.IsEnabled = false;
            this.rescanButton.IsEnabled = false;
            this.Title = ApplicationInfo.AppName + " - " + (this.listView.SelectedItem as Device).FriendlyName;
        }

        private void OnHidDataReceived(object sender, HidEvent e)
        {
            try
            {
                if (this.startButton.IsEnabled)
                {
                    return;
                }

                var currentDevice = this.listView.SelectedItem as Device;
                if (currentDevice == null)
                {
                    return;
                }

                if (e.Device == null)
                {
                    if (e.IsKeyboard)
                    {
                        // On-Screen Keyboard
                        var log = string.Format(
                            "{0} {1} | Make code: {2} | VKey: {3}",
                            e.VirtualKey,
                            e.IsButtonDown ? "[pressed]" : "[released]",
                            e.RawInput.keyboard.MakeCode,
                            e.RawInput.keyboard.VKey);

                        this.rtb.Document.Blocks.Add(new Paragraph(new Run(log)));
                    }

                    return;
                }

                if (currentDevice.IsGamePad() && e.InputReport == null)
                {
                    return;
                }

                if (currentDevice.Name != e.Device.Name)
                {
                    return;
                }

                if (e.InputReport != null)
                {
                    if (this.dataCheckBoxes == null || this.dataCheckBoxes.Count != e.InputReport.Length)
                    {
                        this.dataCheckBoxes = new List<CheckBox>();
                        this.lastData = new byte[e.InputReport.Length];
                        this.layersWrapPanel.Children.Clear();

                        for (int i = 0; i < e.InputReport.Length; i++)
                        {
                            var checkbox = new CheckBox()
                            {
                                Content = i,
                                Margin = new Thickness(5),
                                IsChecked = true,
                                ToolTip = "Subscribe for byte " + i + " changes"
                            };

                            this.dataCheckBoxes.Add(checkbox);
                            this.layersWrapPanel.Children.Add(checkbox);
                        }
                    }
                }

                var currentText = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text;
                if (this.GetLines(currentText).Length > 100)
                {
                    this.rtb.Document.Blocks.Clear();
                    this.rtb.Document.Blocks.Add(new Paragraph(new Run("--- AutoCleared (exeed 100 lines) ---")));
                }

                var paragraph = new Paragraph();

                if (currentDevice.IsGamePad())
                {
                    paragraph = this.CreateColorParagraphFromData(e.InputReport, this.lastData);
                    this.lastData = e.InputReport;
                }
                else if (currentDevice.IsKeyboard)
                {
                    paragraph.Inlines.Add(new Run(string.Format("{0} {1} | Make code: {2} | VKey: {3}", e.VirtualKey, e.IsButtonDown ? "[pressed]" : "[released]", e.RawInput.keyboard.MakeCode, e.RawInput.keyboard.VKey)));
                }
                else if (currentDevice.IsMouse)
                {
                    // Getting the mouse buttons flags
                    var mouseButtonFlags = e.RawInput.mouse.buttonsStr.usButtonFlags;
                    if (mouseButtonFlags != 0)
                    {
                        paragraph.Inlines.Add(new Run(mouseButtonFlags.ToString()));
                    }
                    else
                    {
                        // Getting the mouse delta move coordinates
                        paragraph.Inlines.Add(new Run(string.Format("Mouse delta move X:{0} Y:{1}", e.RawInput.mouse.lLastX, e.RawInput.mouse.lLastY)));
                    }
                }
                else
                {
                    if (currentDevice.IsStylus() || currentDevice.IsFinger())
                    {
                        foreach (var c in e.Device.InputValueCapabilities)
                        {
                            Debug.Assert(e.UsageValues.ContainsKey(c)); // must be contained in dictionary
                            if ((c.UsagePage & 0xFF00) == 0xFF00) continue; // skip Vendor-defined usage

                            Debug.WriteLine("");
                            if (c.HasLink()) Debug.Write($"{c.GetLinkName()} | ");
                            Debug.Write($"{c.GetName()} : {e.UsageValues[c]}"); // cap is key of dictionary
                            if (c.HasUnit()) Debug.Write($" = {c.ConvertUnit(e.UsageValues[c])} [{c.GetUnit()}]");
                        }

                        foreach (var c in e.Device.InputButtonCapabilities)
                        {
                            Debug.Assert(e.Buttons.ContainsKey(c)); // must be contained in dictionary

                            Debug.WriteLine("");
                            if (c.HasLink()) Debug.Write($"{c.GetLinkName()} | ");
                            var btn = e.Buttons[c] ? "on": "off";
                            Debug.Write($"{c.GetName()} : {btn}");
                        }

                        Debug.WriteLine("");
                    }
                    else
                    // Other device
                    if (e.InputReport != null)
                    {
                        paragraph = this.CreateColorParagraphFromData(e.InputReport, this.lastData);
                        this.lastData = e.InputReport;
                    }
                    else
                    {
                        // Displaying the unknown data
                        paragraph.Inlines.Add(new Run(string.Format("Unknown raw data received:{0}{1}", Environment.NewLine, e.RawInput)));
                        paragraph.Foreground = Brushes.Red;
                        this.rtb.Document.Blocks.Add(paragraph);
                        return;
                    }
                }

                if (paragraph.Inlines.Count == 0)
                {
                    return;
                }

                var lastLine = this.GetLines(currentText).LastOrDefault();
                var newLine = string.Join(string.Empty, paragraph.Inlines.Select(line => line.ContentStart.GetTextInRun(LogicalDirection.Forward)));
                if (lastLine != newLine)
                {
                    this.rtb.Document.Blocks.Add(paragraph);
                }
            }
            catch (Exception ex)
            {
                this.rtb.Document.Blocks.Add(new Paragraph(new Run("Error: " + ex.Message) { Foreground = Brushes.Red, Background = Brushes.Black }));
            }
        }

        private Paragraph CreateColorParagraphFromData(byte[] newData, byte[] previousData)
        {
            if (newData == null)
            {
                throw new ArgumentNullException("data");
            }

            if (previousData == null)
            {
                throw new ArgumentNullException("previousData");
            }

            var paragraph = new Paragraph();

            for (int i = 0; i < newData.Length; i++)
            {
                if (this.dataCheckBoxes[i].IsChecked == true)
                {
                    var run = new Run(string.Format("{0:000} ", newData[i]));
                    run.Foreground = previousData[i] != newData[i] ? Brushes.Red : Brushes.Blue;
                    paragraph.Inlines.Add(run);
                }
                else
                {
                    paragraph.Inlines.Add(new Run("*--* ") { Foreground = Brushes.Gray });
                }
            }

            return paragraph;
        }

        private void OnStopClicked(object sender, RoutedEventArgs e)
        {
            this.dataCheckBoxes = null;

            this.startButton.IsEnabled = true;
            this.stopButton.IsEnabled = false;
            this.listView.IsEnabled = true;
            this.rescanButton.IsEnabled = true;

            this.rtb.Document.Blocks.Clear();
            this.layersWrapPanel.Children.Clear();
            this.Title = ApplicationInfo.AppNameVersion;
            this.reader.Dispose();
            this.reader = null;
        }

        private void OnListviewLoaded(object sender, RoutedEventArgs e)
        {
            this.UpdateDeviceList();
        }

        private void OnListviewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = this.listView.SelectedItem as Device;
            if (item == null)
            {
                this.vid.Text = null;
                this.pid.Text = null;
                this.deviceId.Text = null;
            }
            else
            {
                this.vid.Text = item.VendorId.ToString("X04");
                this.pid.Text = item.ProductId.ToString("X04");
                this.deviceId.Text = item.Name;
            }
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            this.scrollviewer.ScrollToEnd();
        }

        private void OnRescanButtonClicked(object sender, RoutedEventArgs e)
        {
            this.UpdateDeviceList();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            this.Title = ApplicationInfo.AppNameVersion;
        }

        private void OnClearButtonClicked(object sender, RoutedEventArgs e)
        {
            this.rtb.Document.Blocks.Clear();
        }
    }
}