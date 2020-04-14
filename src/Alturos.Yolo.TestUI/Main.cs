﻿using Alturos.Yolo.Model;
using Alturos.Yolo.TestUI.Model;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Alturos.Yolo.TestUI
{    
    public class GpuConfig
    {
        public int GpuIndex { get; set; }
    }
    public partial class Main : Form
    {
        private YoloWrapper _yoloWrapper;

        public Main()
        {
            this.InitializeComponent();

            this.buttonProcessImage.Enabled = false;
            this.buttonStartTracking.Enabled = false;

            this.menuStrip1.Visible = false;

            this.toolStripStatusLabelYoloInfo.Text = string.Empty;

            this.Text = $"Alturos Yolo TestUI {Application.ProductVersion}";
            this.dataGridViewFiles.AutoGenerateColumns = false;
            this.dataGridViewResult.AutoGenerateColumns = false;

            var imageInfos = new DirectoryImageReader().Analyze(@".\Images");
            this.dataGridViewFiles.DataSource = imageInfos.ToList();

            Task.Run(() => this.Initialize("."));
            this.LoadAvailableConfigurations();
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            this._yoloWrapper?.Dispose();
        }

        private void LoadAvailableConfigurations()
        {
            var configPath = "config";

            if (!Directory.Exists(configPath))
            {
                return;
            }

            var configs = Directory.GetDirectories(configPath);
            if (configs.Length == 0)
            {
                return;
            }

            this.menuStrip1.Visible = true;

            foreach (var config in configs)
            {
                var menuItem = new ToolStripMenuItem();
                menuItem.Text = config;
                menuItem.Click += (object sender, EventArgs e) => { this.Initialize(config); };
                this.configurationToolStripMenuItem.DropDownItems.Add(menuItem);
            }
        }

        private ImageInfo GetCurrentImage()
        {
            var item = this.dataGridViewFiles.CurrentRow?.DataBoundItem as ImageInfo;
            return item;
        }

        private void dataGridViewFiles_SelectionChanged(object sender, EventArgs e)
        {
            var oldImage = this.pictureBox1.Image;
            var imageInfo = this.GetCurrentImage();           
            this.pictureBox1.Image = Image.FromFile(imageInfo.Path);            
            oldImage?.Dispose();

            this.dataGridViewResult.DataSource = null;
            this.groupBoxResult.Text = $"Result";
        }

        private void dataGridViewFiles_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                this.DetectSelectedImage();
            }
        }

        private void dataGridViewResult_SelectionChanged(object sender, EventArgs e)
        {
            if (!this.dataGridViewResult.Focused)
            {
                return;
            }

            var items = this.dataGridViewResult.DataSource as List<YoloItem>;
            var selectedItem = this.dataGridViewResult.CurrentRow?.DataBoundItem as YoloItem;
            this.DrawBoundingBoxes(items, selectedItem);
        }

        private void openFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var dialogResult = this.folderBrowserDialog1.ShowDialog();
            if (dialogResult != DialogResult.OK)
            {
                return;
            }

            var imageInfos = new DirectoryImageReader().Analyze(this.folderBrowserDialog1.SelectedPath);
            this.dataGridViewFiles.DataSource = imageInfos.ToList();
        }

        private void buttonProcessImage_Click(object sender, EventArgs e)
        {
            this.DetectSelectedImage();
        }

        private async void buttonStartTracking_Click(object sender, EventArgs e)
        {
            await this.StartTrackingAsync();
        }

        private async Task StartTrackingAsync()
        {
            this.buttonStartTracking.Enabled = false;

            var imageInfo = this.GetCurrentImage();

            var yoloTracking = new YoloTracking(imageInfo.Width, imageInfo.Height);
            var count = this.dataGridViewFiles.RowCount;
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    this.dataGridViewFiles.Rows[i - 1].Selected = false;
                }

                this.dataGridViewFiles.Rows[i].Selected = true;
                this.dataGridViewFiles.CurrentCell = this.dataGridViewFiles.Rows[i].Cells[0];

                var items = this.Detect();

                var trackingItems = yoloTracking.Analyse(items);
                this.DrawBoundingBoxes(trackingItems);

                await Task.Delay(100);
            }

            this.buttonStartTracking.Enabled = true;
        }

        public class YoloConfigurationDetector
        {
            /// <summary>
            /// Automatict detect the yolo configuration on the given path
            /// </summary>
            /// <param name="path"></param>
            /// <returns></returns>
            /// <exception cref="FileNotFoundException">Thrown when cannot found one of the required yolo files</exception>
            public YoloConfiguration Detect(string path = ".")
            {
                var files = this.GetYoloFiles(path);
                var yoloConfiguration = this.MapFiles(files);
                var configValid = this.AreValidYoloFiles(yoloConfiguration);

                if (configValid)
                {
                    return yoloConfiguration;
                }

                throw new FileNotFoundException("Cannot found pre-trained model, check all config files available (.cfg, .weights, .names)");
            }

            private string[] GetYoloFiles(string path)
            {
                return Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly).Where(o => o.EndsWith(".names") || o.EndsWith(".cfg") || o.EndsWith(".weights")).ToArray();
            }

            private YoloConfiguration MapFiles(string[] files)
            {
                var configurationFile = files.FirstOrDefault(o => o.EndsWith(".cfg"));
                var weightsFile = files.FirstOrDefault(o => o.EndsWith(".weights"));
                var namesFile = files.FirstOrDefault(o => o.EndsWith(".names"));

                return new YoloConfiguration(configurationFile, weightsFile, namesFile);
            }

            private bool AreValidYoloFiles(YoloConfiguration config)
            {
                if (string.IsNullOrEmpty(config.ConfigFile) ||
                    string.IsNullOrEmpty(config.WeightsFile) ||
                    string.IsNullOrEmpty(config.NamesFile))
                {
                    return false;
                }

                return true;
            }
        }

        private void DrawBoundingBoxes(IEnumerable<YoloTrackingItem> items)
        {
            var imageInfo = this.GetCurrentImage();
            //Load the image(probably from your stream)
            var image = Image.FromFile(imageInfo.Path);

            using (var font = new Font(FontFamily.GenericSansSerif, 16))
            using (var canvas = Graphics.FromImage(image))
            {
                foreach (var item in items)
                {
                    var x = item.X;
                    var y = item.Y;
                    var width = item.Width;
                    var height = item.Height;

                    var brush = this.GetBrush(item.Confidence);
                    var penSize = image.Width / 100.0f;

                    using (var pen = new Pen(brush, penSize))
                    {
                        canvas.DrawRectangle(pen, x, y, width, height);
                        canvas.FillRectangle(brush, x - (penSize / 2), y - 15, width + penSize, 25);
                    }
                }

                foreach (var item in items)
                {
                    canvas.DrawString(item.ObjectId.ToString(), font, Brushes.White, item.X, item.Y - 12);
                }

                canvas.Flush();
            }

            var oldImage = this.pictureBox1.Image;
            this.pictureBox1.Image = image;
            oldImage?.Dispose();
        }

        private void DrawBoundingBoxes(List<YoloItem> items, YoloItem selectedItem = null)
        {
            var imageInfo = this.GetCurrentImage();
            //Load the image(probably from your stream)
            var image = Image.FromFile(imageInfo.Path);

            using (var canvas = Graphics.FromImage(image))
            {
                foreach (var item in items)
                {
                    var x = item.X;
                    var y = item.Y;
                    var width = item.Width;
                    var height = item.Height;
                    
                    var brush = this.GetBrush(item.Confidence);
                    var penSize = image.Width / 100.0f;

                    using (var pen = new Pen(brush, penSize))
                    using (var overlayBrush = new SolidBrush(Color.FromArgb(150, 255, 255, 102)))
                    {
                        if (item.Equals(selectedItem))
                        {
                            canvas.FillRectangle(overlayBrush, x, y, width, height);
                        }

                        canvas.DrawRectangle(pen, x, y, width, height);
                    }
                }

                canvas.Flush();
            }

            var oldImage = this.pictureBox1.Image;
            this.pictureBox1.Image = image;
            oldImage?.Dispose();
        }

        private Brush GetBrush(double confidence)
        {
            if (confidence > 0.5)
            {
                return Brushes.GreenYellow;
            }
            else if (confidence > 0.2 && confidence <= 0.5)
            {
                return Brushes.Orange;
            }

            return Brushes.DarkRed;
        }

        public string GetGraphicDeviceName(GpuConfig gpuConfig)
        {
            if (gpuConfig == null)
            {
                return string.Empty;
            }

            //var systemReport = this._yoloSystemValidator.Validate();
            //if (!systemReport.CudaExists || !systemReport.CudnnExists)
            //{
                return "unknown";
            //}

            //var deviceName = new StringBuilder(); //allocate memory for string
            //GetDeviceName(gpuConfig.GpuIndex, deviceName);
            return "";
        }

        private void Initialize(string path)
        {
            var configurationDetector = new YoloConfigurationDetector();
            try
            {
                var config = configurationDetector.Detect(path);
                if (config == null)
                {
                    return;
                }

                this.Initialize(config);
            }
            catch (Exception exception)
            {
                MessageBox.Show($"Cannot found a valid dataset {exception}", "No Dataset available", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private void Initialize(YoloConfiguration config)
        {
            try
            {
                if (this._yoloWrapper != null)
                {
                    this._yoloWrapper.Dispose();
                }

                var gpuConfig = new GpuConfig();
                var useOnlyCpu = this.cpuToolStripMenuItem.Checked;
                if (useOnlyCpu)
                {
                    gpuConfig = null;
                }

                this.toolStripStatusLabelYoloInfo.Text = $"Initialize...";

                var sw = new Stopwatch();
                sw.Start();
                this._yoloWrapper = new YoloWrapper(config.ConfigFile, config.WeightsFile, config.NamesFile, 0);
                sw.Stop();

                var action = new MethodInvoker(delegate ()
                {
                    //var deviceName = this._yoloWrapper.GetGraphicDeviceName(gpuConfig);
                    this.toolStripStatusLabelYoloInfo.Text = $"Initialize Yolo in {sw.Elapsed.TotalMilliseconds:0} ms - Detection System:{this._yoloWrapper.DetectionSystem} {""} Weights:{config.WeightsFile}";
                });

                this.statusStrip1.Invoke(action);
                this.buttonProcessImage.Invoke(new MethodInvoker(delegate () { this.buttonProcessImage.Enabled = true; }));
                this.buttonStartTracking.Invoke(new MethodInvoker(delegate () { this.buttonStartTracking.Enabled = true; }));
            }
            catch (Exception exception)
            {
                MessageBox.Show($"{nameof(Initialize)} - {exception}", "Error Initialize", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }        

        private void DetectSelectedImage()
        {
            var items = this.Detect();
            this.dataGridViewResult.DataSource = items;
            this.DrawBoundingBoxes(items);
        }

        private List<YoloItem> Detect(bool memoryTransfer = true)
        {
            if (this._yoloWrapper == null)
            {
                return null;
            }

            var imageInfo = this.GetCurrentImage();
            var imageData = File.ReadAllBytes(imageInfo.Path);

            var sw = new Stopwatch();
            sw.Start();
            List<YoloItem> items;
            if (memoryTransfer)
            {
                items = this._yoloWrapper.Detect(imageData).ToList();
            }
            else
            {
                items = this._yoloWrapper.Detect(imageInfo.Path).ToList();
            }
            sw.Stop();
            this.groupBoxResult.Text = $"Result [ processed in {sw.Elapsed.TotalMilliseconds:0} ms ]";

            return items;
        }

        private void gpuToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.cpuToolStripMenuItem.Checked = !this.cpuToolStripMenuItem.Checked;
        }

        private async void downloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var repository = new YoloPreTrainedDatasetRepository();
            var datasets = await repository.GetDatasetsAsync();
            foreach (var dataset in datasets)
            {
                this.statusStrip1.Invoke(new MethodInvoker(delegate () { this.toolStripStatusLabelYoloInfo.Text = $"Start download for {dataset} dataset..."; }));
                await repository.DownloadDatasetAsync(dataset, $@"config\{dataset}");
            }

            this.LoadAvailableConfigurations();
            this.statusStrip1.Invoke(new MethodInvoker(delegate () { this.toolStripStatusLabelYoloInfo.Text = $"Download done"; }));
        }
    }
}
