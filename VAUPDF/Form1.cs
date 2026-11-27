
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
        private readonly MenuStrip menubar = new();
        private readonly ToolStrip toolbar = new();
        private readonly StatusStrip statusbar = new();
        private readonly ToolStripStatusLabel status = new();

        private readonly ToolStripMenuItem recentMenu = new("&Recent files");
        private readonly ToolStripMenuItem closeMenu = new("&Close");
        private readonly ToolStripButton printButton;
        private readonly ToolStripButton saveAsButton;

        private readonly List<string> recentFiles = new();

        // Rakenduse seadistuste kaust
        private readonly string dataFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VAUPDF");

        private string RecentFilePath =>
            Path.Combine(dataFolder, "recent.json");

        // Rakenduse versioon projektifailist
        private static string AppVersion =>
            typeof(Form1).Assembly.GetName().Version?
                .ToString(3) ?? "0.0.0";

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

            // ==========================================
            // MENÜÜ JA TÖÖRIISTARIBA
            // ==========================================

            menubar.Dock = DockStyle.Top;
            menubar.RenderMode = ToolStripRenderMode.System;
            menubar.ShowItemToolTips = true;

            ToolStripMenuItem fileMenu = new("&File");
            ToolStripMenuItem openMenu = new("&Open...")
            {
                ShortcutKeys = Keys.Control | Keys.O
            };
            openMenu.Click += (s, e) => OpenDialog();

            closeMenu.ShortcutKeys = Keys.Control | Keys.W;
            closeMenu.Click += (s, e) => CloseTab(tabs.SelectedTab);

            ToolStripMenuItem exitMenu = new("E&xit")
            {
                ShortcutKeys = Keys.Alt | Keys.F4
            };
            exitMenu.Click += (s, e) => Close();

            fileMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                openMenu, closeMenu, recentMenu,
                new ToolStripSeparator(), exitMenu
            });
            menubar.Items.Add(fileMenu);

            MainMenuStrip = menubar;

            toolbar.Dock = DockStyle.Top;
            toolbar.GripStyle = ToolStripGripStyle.Hidden;
            toolbar.Padding = new Padding(6);
            toolbar.RenderMode = ToolStripRenderMode.System;

            toolbar.ImageScalingSize = new Size(24, 24);
            printButton = CreateIconButton(
                "Print", "\uE749", PrintPdf, "Print (Ctrl+P)");
            saveAsButton = CreateIconButton(
                "Save As", "\uE792", SavePdfAs, "Save As (Ctrl+Shift+S)");
            toolbar.Items.AddRange(new ToolStripItem[] { printButton, saveAsButton });

            // ==========================================
            // VAHEKAARDID
            // ==========================================

            tabs.Dock = DockStyle.Fill;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.Padding = new Point(22, 6);
            tabs.ShowToolTips = true;

            tabs.DrawItem += DrawTab;
            tabs.MouseDown += TabMouseDown;

            tabs.SelectedIndexChanged +=
                (s, e) => UpdateStatus();

            // ==========================================
            // OLEKURIBA
            // ==========================================

            statusbar.Dock = DockStyle.Bottom;
            status.Spring = true;
            status.TextAlign = ContentAlignment.MiddleLeft;

            statusbar.Items.Add(status);

            statusbar.Items.Add(
                new ToolStripStatusLabel(
                    $"VAUPDF v{AppVersion}"));

            // Kasutajaliidese lisamine
            Controls.Add(tabs);
            Controls.Add(statusbar);
            Controls.Add(toolbar);
            Controls.Add(menubar);

            // Hiljutised failid
            LoadRecentFiles();
            UpdateRecentMenu();
            UpdateStatus();
        }

        // ==============================================
        // TÖÖRIISTARIBA NUPUD
        // ==============================================

        private static ToolStripButton CreateIconButton(
            string text,
            string glyph,
            Action action,
            string tooltip)
        {
            Bitmap image = new(32, 32);
            using (Graphics graphics = Graphics.FromImage(image))
            using (Font font = new("Segoe MDL2 Assets", 24, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush brush = new(SystemColors.ControlText))
            using (StringFormat format = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString(glyph, font, brush, new RectangleF(0, 0, 32, 32), format);
            }

            ToolStripButton button = new(text)
            {
                Image = image,
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                AccessibleName = text,
                ToolTipText = tooltip,
                Padding = new Padding(5)
            };

            button.Click += (s, e) => action();
            button.Disposed += (s, e) => image.Dispose();

            return button;
        }

        // ==============================================
        // FAILIVALIKU DIALOOG
        // ==============================================

        private async void OpenDialog()
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "PDF failid (*.pdf)|*.pdf",
                Title = "Ava PDF dokument",
                Multiselect = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                await OpenFilesAsync(dialog.FileNames);
            }
        }

        // ==============================================
        // MITME FAILI AVAMINE
        // ==============================================

        public async Task OpenFilesAsync(
            IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                if (IsDisposed || Disposing)
                    break;

                await OpenPdf(file);
            }
        }

        // ==============================================
        // PDF-FAILI AVAMINE
        // ==============================================

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
                {
                    return;
                }

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

                // Loo uus vahekaart
                page = new TabPage
                {
                    Text = Path.GetFileName(filePath),
                    ToolTipText = filePath,
                    Tag = filePath
                };

                // WebView2 komponent
                WebView2 viewer = new()
                {
                    Dock = DockStyle.Fill,

                    CreationProperties =
                        new CoreWebView2CreationProperties
                        {
                            UserDataFolder = Path.Combine(
                                dataFolder,
                                "WebView2")
                        }
                };

                page.Controls.Add(viewer);

                tabs.TabPages.Add(page);
                tabs.SelectedTab = page;

                await viewer.EnsureCoreWebView2Async();

                if (viewer.IsDisposed || page.IsDisposed)
                    return;

                // Failide lohistamine on keelatud
                viewer.AllowExternalDrop = false;

                // Kiirklahvid PDF-vaaturi fookuses
                viewer.KeyDown += ViewerKeyDown;

                // Ava PDF
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

        // ==============================================
        // AKTIIVNE PDF-VAATUR
        // ==============================================

        private WebView2? ActiveViewer =>
            tabs.SelectedTab?.Controls
                .OfType<WebView2>()
                .FirstOrDefault();

        // ==============================================
        // PRINTIMINE
        // ==============================================

        private void PrintPdf()
        {
            try
            {
                ActiveViewer?.CoreWebView2?.ShowPrintUI(
                    CoreWebView2PrintDialogKind.Browser);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Printimise viga",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void SavePdfAs()
        {
            if (tabs.SelectedTab?.Tag is not string sourcePath)
                return;

            try
            {
                using SaveFileDialog dialog = new()
                {
                    Filter = "PDF failid (*.pdf)|*.pdf",
                    Title = "Save As",
                    FileName = Path.GetFileName(sourcePath),
                    DefaultExt = "pdf",
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                string destinationPath = Path.GetFullPath(dialog.FileName);
                if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                    return;

                File.Copy(sourcePath, destinationPath, overwrite: true);
                RememberFile(destinationPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Salvestamise viga",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ==============================================
        // VAHEKAARDI SULGEMINE
        // ==============================================

        private void CloseTab(TabPage? page)
        {
            if (page == null)
                return;

            tabs.TabPages.Remove(page);
            page.Dispose();

            UpdateStatus();
        }

        // ==============================================
        // SULGEMISRISTI ASUKOHT
        // ==============================================

        private Rectangle CloseRectangle(int index)
        {
            Rectangle r = tabs.GetTabRect(index);

            return new Rectangle(
                r.Right - 23,
                r.Top + (r.Height - 16) / 2,
                16,
                16);
        }

        // ==============================================
        // VAHEKAARTIDE KUJUNDUS
        // ==============================================

        private void DrawTab(
            object? sender,
            DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= tabs.TabCount)
                return;

            Rectangle r = tabs.GetTabRect(e.Index);

            bool selected =
                e.Index == tabs.SelectedIndex;

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
                close.Left + 4,
                close.Top + 4,
                close.Right - 4,
                close.Bottom - 4);

            e.Graphics.DrawLine(
                pen,
                close.Right - 4,
                close.Top + 4,
                close.Left + 4,
                close.Bottom - 4);

            // Aktiivse vahekaardi punane joon
            if (selected)
            {
                using Pen activePen = new(
                    Color.FromArgb(185, 37, 54),
                    3);

                e.Graphics.DrawLine(
                    activePen,
                    r.Left,
                    r.Bottom - 2,
                    r.Right,
                    r.Bottom - 2);
            }
        }

        // ==============================================
        // SULGEMISRIST JA HIIRE KESKMINE NUPP
        // ==============================================

        private void TabMouseDown(
            object? sender,
            MouseEventArgs e)
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

        // ==============================================
        // VAHEKAARTIDE VAHETAMINE
        // ==============================================

        private void SwitchTab(int direction)
        {
            if (tabs.TabCount < 2)
                return;

            tabs.SelectedIndex =
                (tabs.SelectedIndex +
                 direction +
                 tabs.TabCount)
                % tabs.TabCount;
        }

        // ==============================================
        // KLAVIATUURI KIIRKLAHVID
        // ==============================================

        private bool IsShortcut(Keys key)
        {
            return key == (Keys.Control | Keys.O)
                || key == (Keys.Control | Keys.P)
                || key == (Keys.Control | Keys.Shift | Keys.S)
                || key == (Keys.Control | Keys.W)
                || key == (Keys.Control | Keys.Tab)
                || key == (
                    Keys.Control |
                    Keys.Shift |
                    Keys.Tab);
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

                case Keys.Control | Keys.Shift | Keys.S:
                    SavePdfAs();
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
            }
        }

        protected override bool ProcessCmdKey(
            ref Message msg,
            Keys keyData)
        {
            if (IsShortcut(keyData))
            {
                ExecuteShortcut(keyData);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        // WebView2 kiirklahvide töötlemine
        private void ViewerKeyDown(
            object? sender,
            KeyEventArgs e)
        {
            if (!IsShortcut(e.KeyData))
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;

            Keys key = e.KeyData;

            BeginInvoke(new Action(
                () => ExecuteShortcut(key)));
        }

        // ==============================================
        // HILJUTISTE FAILIDE LUGEMINE
        // ==============================================

        private void LoadRecentFiles()
        {
            try
            {
                if (!File.Exists(RecentFilePath))
                    return;

                var files =
                    JsonSerializer.Deserialize<List<string>>(
                        File.ReadAllText(RecentFilePath));

                if (files != null)
                {
                    recentFiles.AddRange(
                        files.Where(
                            p => !string.IsNullOrWhiteSpace(p))
                        .Take(10));
                }
            }
            catch
            {
                // Vigane seadistusfail ei takista käivitamist
            }
        }

        // ==============================================
        // HILJUTISTE FAILIDE SALVESTAMINE
        // ==============================================

        private void SaveRecentFiles()
        {
            try
            {
                Directory.CreateDirectory(dataFolder);

                File.WriteAllText(
                    RecentFilePath,
                    JsonSerializer.Serialize(
                        recentFiles,
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
                    x,
                    path,
                    StringComparison.OrdinalIgnoreCase));

            recentFiles.Insert(0, path);

            if (recentFiles.Count > 10)
            {
                recentFiles.RemoveRange(
                    10,
                    recentFiles.Count - 10);
            }

            SaveRecentFiles();
            UpdateRecentMenu();
        }

        // ==============================================
        // HILJUTISTE FAILIDE MENÜÜ
        // ==============================================

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
                    new ToolStripMenuItem(
                        "Nimekiri on tühi")
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

        // ==============================================
        // OLEKURIBA JA AKNA PEALKIRI
        // ==============================================

        private void UpdateStatus()
        {
            closeMenu.Enabled = tabs.SelectedTab != null;
            printButton.Enabled = ActiveViewer?.CoreWebView2 != null;
            saveAsButton.Enabled = printButton.Enabled;

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
