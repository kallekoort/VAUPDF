
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace VAUPDF
{
    public partial class Form1 : Form
    {
        private readonly TabControl tabs;
        private readonly FlowLayoutPanel toolbar;
        private readonly Label dropZone;

        public Form1()
        {
            InitializeComponent();

            Text = "VAUPDF";
            Width = 1200;
            Height = 800;
            MinimumSize = new Size(650, 400);
            StartPosition = FormStartPosition.CenterScreen;

            // Vahekaardid
            tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                ShowToolTips = true
            };

            // Ülemine tööriistariba
            toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(5),
                BackColor = Color.FromArgb(240, 240, 240),
                WrapContents = false
            };

            // Faili avamise nupp
            Button btnOpen = new Button
            {
                Text = "Ava PDF",
                Width = 100,
                Height = 30
            };

            btnOpen.Click += OpenPdfDialog;

            // Vahekaardi sulgemise nupp
            Button btnClose = new Button
            {
                Text = "Sulge vahekaart",
                Width = 130,
                Height = 30
            };

            btnClose.Click += (s, e) => CloseTab();

            // Failide lohistamise ala
            dropZone = new Label
            {
                Text = "Lohista PDF-failid siia",
                Width = 300,
                Height = 30,
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle,
                ForeColor = Color.DimGray
            };

            toolbar.Controls.Add(btnOpen);
            toolbar.Controls.Add(btnClose);
            toolbar.Controls.Add(dropZone);

            Controls.Add(tabs);
            Controls.Add(toolbar);

            // Drag & Drop
            EnableDragDrop(this);
            EnableDragDrop(toolbar);
            EnableDragDrop(dropZone);
            EnableDragDrop(tabs);

            // Akna pealkirja uuendamine
            tabs.SelectedIndexChanged += (s, e) =>
            {
                Text = tabs.SelectedTab != null
                    ? $"VAUPDF - {tabs.SelectedTab.Text}"
                    : "VAUPDF";
            };

            // Windows Explorerist avatud failid
            Shown += async (s, e) =>
            {
                string[] args = Environment
                    .GetCommandLineArgs()
                    .Skip(1)
                    .ToArray();

                foreach (string file in args)
                {
                    await OpenPdf(file);
                }
            };
        }

        // Failivaliku dialoog
        private async void OpenPdfDialog(
            object? sender, EventArgs e)
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "PDF failid (*.pdf)|*.pdf",
                Title = "Ava PDF dokument",
                Multiselect = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                foreach (string file in dialog.FileNames)
                {
                    await OpenPdf(file);
                }
            }
        }

        // PDF-faili avamine uuel vahekaardil
        private async Task OpenPdf(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            if (!string.Equals(
                Path.GetExtension(filePath),
                ".pdf",
                StringComparison.OrdinalIgnoreCase))
                return;

            filePath = Path.GetFullPath(filePath);

            // Kontrollime, kas fail on juba avatud
            foreach (TabPage existingTab in tabs.TabPages)
            {
                if (string.Equals(
                    existingTab.Tag as string,
                    filePath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    tabs.SelectedTab = existingTab;
                    return;
                }
            }

            TabPage page = new()
            {
                Text = Path.GetFileName(filePath),
                ToolTipText = filePath,
                Tag = filePath
            };

            // WebView2 andmed kasutaja kaustas
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "VAUPDF",
                "WebView2"
            );

            WebView2 viewer = new()
            {
                Dock = DockStyle.Fill,
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = userDataFolder
                }
            };

            page.Controls.Add(viewer);

            tabs.TabPages.Add(page);
            tabs.SelectedTab = page;

            try
            {
                await viewer.EnsureCoreWebView2Async();

                if (viewer.IsDisposed)
                    return;

                viewer.AllowExternalDrop = false;
                viewer.Source = new Uri(filePath);
            }
            catch (Exception ex)
            {
                if (page.IsDisposed)
                    return;

                tabs.TabPages.Remove(page);
                page.Dispose();

                MessageBox.Show(
                    ex.Message,
                    "PDF avamise viga",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        // Aktiivse vahekaardi sulgemine
        private void CloseTab()
        {
            TabPage? page = tabs.SelectedTab;

            if (page == null)
                return;

            tabs.TabPages.Remove(page);
            page.Dispose();
        }

        // Drag & Drop aktiveerimine
        private void EnableDragDrop(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += FileDragEnter;
            control.DragDrop += FileDragDrop;
        }

        private void FileDragEnter(
            object? sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;

            if (e.Data?.GetData(DataFormats.FileDrop)
                is string[] files)
            {
                if (files.Any(file =>
                    string.Equals(
                        Path.GetExtension(file),
                        ".pdf",
                        StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effect = DragDropEffects.Copy;
                }
            }
        }

        private async void FileDragDrop(
            object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop)
                is string[] files)
            {
                foreach (string file in files)
                {
                    await OpenPdf(file);
                }
            }
        }
    }
}
