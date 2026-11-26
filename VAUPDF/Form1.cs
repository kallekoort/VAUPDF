
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace VAUPDF
{
    public partial class Form1 : Form
    {
        private readonly TabControl tabs = new();
        private readonly ToolStrip toolbar = new();
        private readonly StatusStrip statusbar = new();
        private readonly ToolStripStatusLabel status = new();
        private readonly ToolStripDropDownButton recentMenu = new("Hiljutised");

        private readonly List<string> recentFiles = new();

        private readonly string dataFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VAUPDF");

        private string RecentFilePath =>
            Path.Combine(dataFolder, "recent.json");

        private bool fullscreen = false;
        private FormWindowState previousWindowState;
        private Rectangle previousBounds;

        public Form1()
        {
            InitializeComponent();

            Text = "VAUPDF";
            Width = 1200;
            Height = 800;
            MinimumSize = new Size(650, 400);
            StartPosition = FormStartPosition.CenterScreen;

            Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                Application.ExecutablePath)
                ?? SystemIcons.Application;

            // Tööriistariba
            toolbar.Dock = DockStyle.Top;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.Padding = new Padding(6);
            toolbar.RenderMode = ToolStripRenderMode.System;

            AddButton("Ava PDF", () => OpenDialog(), "Ctrl+O");
            AddButton("Prindi", PrintPdf, "Ctrl+P");

            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(recentMenu);
            toolbar.Items.Add(new ToolStripSeparator());

            AddButton("Sulge", () => CloseTab(tabs.SelectedTab), "Ctrl+W");
            AddButton("Täisekraan", ToggleFullscreen, "F11");

            toolbar.Items.Add(new ToolStripSeparator());

            toolbar.Items.Add(new ToolStripLabel(
                "Lohista PDF-failid siia"));

            // Vahekaardid
            tabs.Dock = DockStyle.Fill;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.Padding = new Point(22, 6);
            tabs.ShowToolTips = true;

            tabs.DrawItem += DrawTab;
            tabs.MouseDown += TabMouseDown;
            tabs.SelectedIndexChanged += (s, e) => UpdateStatus();

            // Alumine olekuriba
            statusbar.Dock = DockStyle.Bottom;
            status.Spring = true;
            status.TextAlign = ContentAlignment.MiddleLeft;

            statusbar.Items.Add(status);
            statusbar.Items.Add(
                new ToolStripStatusLabel("VAUPDF v0.3"));

            Controls.Add(tabs);
            Controls.Add(statusbar);
            Controls.Add(toolbar);

            EnableDragDrop(this);
            EnableDragDrop(toolbar);
            EnableDragDrop(tabs);

            LoadRecentFiles();
            UpdateRecentMenu();
            UpdateStatus();
        }

        // Tööriistariba nupud
        private void AddButton(
            string text, Action action, string shortcut)
        {
            ToolStripButton button = new(text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = $"{text} ({shortcut})",
                Padding = new Padding(5)
            };

            button.Click += (s, e) => action();
            toolbar.Items.Add(button);
        }

        // Failivaliku dialoog
        private async void OpenDialog()
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "PDF failid (*.pdf)|*.pdf",
                Title = "Ava PDF dokument",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
                await OpenFilesAsync(dialog.FileNames);
        }

        // Mitme faili avamine
        public async Task OpenFilesAsync(IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                if (IsDisposed || Disposing)
                    break;

                await OpenPdf(file);
            }
        }

        // PDF avamine
        private async Task OpenPdf(string filePath)
        {
            TabPage? page = null;

            try
            {
                if (!File.Exists(filePath))
                    return;

                if (!string.Equals(
                    Path.GetExtension(filePath),
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
                    return;

                filePath = Path.GetFullPath(filePath);

                // Väldi sama faili korduvat avamist
                foreach (TabPage existingTab in tabs.TabPages)
                {
                    if (string.Equals(
                        existingTab.Tag as string,
                        filePath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        tabs.SelectedTab = existingTab;
                        RememberFile(filePath);
                        return;
                    }
                }

                page = new TabPage
                {
                    Text = Path.GetFileName(filePath),
                    ToolTipText = filePath,
                    Tag = filePath
                };

                WebView2 viewer = new()
                {
                    Dock = DockStyle.Fill,
                    CreationProperties =
                        new CoreWebView2CreationProperties
                        {
                            UserDataFolder = Path.Combine(
                                dataFolder, "WebView2")
                        }
                };

                page.Controls.Add(viewer);
                tabs.TabPages.Add(page);
                tabs.SelectedTab = page;

                await viewer.EnsureCoreWebView2Async();

                if (viewer.IsDisposed || page.IsDisposed)
                    return;

                viewer.AllowExternalDrop = false;

                // Kiirklahvid ka siis, kui PDF on fookuses
                viewer.KeyDown += ViewerKeyDown;

                viewer.Source = new Uri(filePath);

                RememberFile(filePath);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing)
                    return;

                if (page != null && !page.IsDisposed)
                {
                    tabs.TabPages.Remove(page);
                    page.Dispose();
                }

                MessageBox.Show(
                    this,
                    ex.Message,
                    "PDF avamise viga",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                UpdateStatus();
            }
        }

        // Aktiivne PDF-vaatur
        private WebView2? ActiveViewer =>
            tabs.SelectedTab?.Controls
                .OfType<WebView2>()
                .FirstOrDefault();

        // Printimine
        private void PrintPdf()
        {
            try
            {
                ActiveViewer?.CoreWebView2?.ShowPrintUI(
                    CoreWebView2PrintDialogKind.Browser);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Printimise viga");
            }
        }

        // Vahekaardi sulgemine
        private void CloseTab(TabPage? page)
        {
            if (page == null)
                return;

            tabs.TabPages.Remove(page);
            page.Dispose();
            UpdateStatus();
        }

        // Sulgemisristi asukoht
        private Rectangle CloseRectangle(int index)
        {
            Rectangle r = tabs.GetTabRect(index);

            return new Rectangle(
                r.Right - 23,
                r.Top + (r.Height - 16) / 2,
                16,
                16);
        }

        // Vahekaartide kujundus
        private void DrawTab(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= tabs.TabCount)
                return;

            Rectangle r = tabs.GetTabRect(e.Index);
            bool selected = e.Index == tabs.SelectedIndex;

            using SolidBrush background = new(
                selected
                    ? Color.White
                    : Color.FromArgb(230, 233, 238));

            e.Graphics.FillRectangle(background, r);

            Rectangle textArea = new(
                r.Left + 8,
                r.Top,
                Math.Max(0, r.Width - 34),
                r.Height);

            TextRenderer.DrawText(
                e.Graphics,
                tabs.TabPages[e.Index].Text,
                Font,
                textArea,
                Color.FromArgb(40, 45, 55),
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix);

            Rectangle close = CloseRectangle(e.Index);

            using Pen pen = new(Color.DimGray, 1.6f);

            e.Graphics.DrawLine(
                pen,
                close.Left + 4, close.Top + 4,
                close.Right - 4, close.Bottom - 4);

            e.Graphics.DrawLine(
                pen,
                close.Right - 4, close.Top + 4,
                close.Left + 4, close.Bottom - 4);

            if (selected)
            {
                using Pen activePen = new(
                    Color.FromArgb(185, 37, 54), 3);

                e.Graphics.DrawLine(
                    activePen,
                    r.Left,
                    r.Bottom - 2,
                    r.Right,
                    r.Bottom - 2);
            }
        }

        // Sulgemisrist või hiire keskmine nupp
        private void TabMouseDown(object? sender, MouseEventArgs e)
        {
            for (int i = 0; i < tabs.TabCount; i++)
            {
                bool closeClick =
                    e.Button == MouseButtons.Left &&
                    CloseRectangle(i).Contains(e.Location);

                bool middleClick =
                    e.Button == MouseButtons.Middle &&
                    tabs.GetTabRect(i).Contains(e.Location);

                if (closeClick || middleClick)
                {
                    CloseTab(tabs.TabPages[i]);
                    return;
                }
            }
        }

        // Vahekaartide vahetamine
        private void SwitchTab(int direction)
        {
            if (tabs.TabCount < 2)
                return;

            tabs.SelectedIndex =
                (tabs.SelectedIndex + direction + tabs.TabCount)
                % tabs.TabCount;
        }

        // Toetatud kiirklahvid
        private bool IsShortcut(Keys key)
        {
            return key == (Keys.Control | Keys.O)
                || key == (Keys.Control | Keys.P)
                || key == (Keys.Control | Keys.W)
                || key == (Keys.Control | Keys.Tab)
                || key == (Keys.Control | Keys.Shift | Keys.Tab)
                || key == Keys.F11
                || (key == Keys.Escape && fullscreen);
        }

        private void ExecuteShortcut(Keys key)
        {
            switch (key)
            {
                case Keys.Control | Keys.O:
                    OpenDialog();
                    break;

                case Keys.Control | Keys.P:
                    PrintPdf();
                    break;

                case Keys.Control | Keys.W:
                    CloseTab(tabs.SelectedTab);
                    break;

                case Keys.Control | Keys.Tab:
                    SwitchTab(1);
                    break;

                case Keys.Control | Keys.Shift | Keys.Tab:
                    SwitchTab(-1);
                    break;

                case Keys.F11:
                    ToggleFullscreen();
                    break;

                case Keys.Escape:
                    if (fullscreen)
                        ToggleFullscreen();
                    break;
            }
        }

        protected override bool ProcessCmdKey(
            ref Message msg, Keys keyData)
        {
            if (IsShortcut(keyData))
            {
                ExecuteShortcut(keyData);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // WebView2 kiirklahvide edastamine
        private void ViewerKeyDown(object? sender, KeyEventArgs e)
        {
            if (!IsShortcut(e.KeyData))
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;

            Keys key = e.KeyData;

            BeginInvoke(new Action(
                () => ExecuteShortcut(key)));
        }

        // Täisekraan
        private void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                previousWindowState = WindowState;
                previousBounds = RestoreBounds;

                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;

                toolbar.Visible = false;
                statusbar.Visible = false;

                WindowState = FormWindowState.Maximized;
                fullscreen = true;
            }
            else
            {
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.Sizable;

                Bounds = previousBounds;

                toolbar.Visible = true;
                statusbar.Visible = true;

                WindowState = previousWindowState;
                fullscreen = false;
            }
        }

        // Hiljutiste failide lugemine
        private void LoadRecentFiles()
        {
            try
            {
                if (File.Exists(RecentFilePath))
                {
                    var files = JsonSerializer.Deserialize<List<string>>(
                        File.ReadAllText(RecentFilePath));

                    if (files != null)
                        recentFiles.AddRange(files.Take(10));
                }
            }
            catch
            {
                // Vigane seadistusfail ei takista käivitamist
            }
        }

        // Hiljutiste failide salvestamine
        private void SaveRecentFiles()
        {
            try
            {
                Directory.CreateDirectory(dataFolder);

                File.WriteAllText(
                    RecentFilePath,
                    JsonSerializer.Serialize(recentFiles,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        }));
            }
            catch
            {
                // Salvestusviga ei takista PDF-i vaatamist
            }
        }

        private void RememberFile(string path)
        {
            recentFiles.RemoveAll(x =>
                string.Equals(
                    x, path,
                    StringComparison.OrdinalIgnoreCase));

            recentFiles.Insert(0, path);

            if (recentFiles.Count > 10)
                recentFiles.RemoveRange(
                    10, recentFiles.Count - 10);

            SaveRecentFiles();
            UpdateRecentMenu();
        }

        // Hiljutiste failide menüü
        private void UpdateRecentMenu()
        {
            while (recentMenu.DropDownItems.Count > 0)
            {
                var item = recentMenu.DropDownItems[0];
                recentMenu.DropDownItems.RemoveAt(0);
                item.Dispose();
            }

            foreach (string path in recentFiles)
            {
                ToolStripMenuItem item = new(
                    Path.GetFileName(path))
                {
                    ToolTipText = path,
                    Enabled = File.Exists(path)
                };

                item.Click += async (s, e) =>
                    await OpenPdf(path);

                recentMenu.DropDownItems.Add(item);
            }

            if (recentFiles.Count == 0)
            {
                recentMenu.DropDownItems.Add(
                    new ToolStripMenuItem("Nimekiri on tühi")
                    {
                        Enabled = false
                    });
            }
            else
            {
                recentMenu.DropDownItems.Add(
                    new ToolStripSeparator());

                ToolStripMenuItem clear = new(
                    "Tühjenda nimekiri");

                clear.Click += (s, e) =>
                {
                    recentFiles.Clear();
                    SaveRecentFiles();
                    UpdateRecentMenu();
                };

                recentMenu.DropDownItems.Add(clear);
            }
        }

        // Drag & Drop
        private void EnableDragDrop(Control control)
        {
            control.AllowDrop = true;

            control.DragEnter += (s, e) =>
            {
                e.Effect = DragDropEffects.None;

                if (e.Data?.GetData(DataFormats.FileDrop)
                    is string[] files &&
                    files.Any(f => string.Equals(
                        Path.GetExtension(f),
                        ".pdf",
                        StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effect = DragDropEffects.Copy;
                }
            };

            control.DragDrop += async (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop)
                    is string[] files)
                {
                    await OpenFilesAsync(files);
                }
            };
        }

        // Olekuriba ja akna pealkiri
        private void UpdateStatus()
        {
            Text = tabs.SelectedTab == null
                ? "VAUPDF"
                : $"VAUPDF - {tabs.SelectedTab.Text}";

            status.Text = tabs.TabCount == 0
                ? "Valmis"
                : $"Avatud dokumente: {tabs.TabCount}";

            tabs.Invalidate();
        }
    }
}
