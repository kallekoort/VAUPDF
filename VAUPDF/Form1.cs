
using Microsoft.Web.WebView2.WinForms;

namespace VAUPDF
{
    public partial class Form1 : Form
    {
        private readonly WebView2 pdfViewer;
        private readonly Button btnOpen;

        public Form1()
        {
            InitializeComponent();

            Text = "VAUPDF";
            Width = 1200;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;

            // PDF-vaatur
            pdfViewer = new WebView2
            {
                Dock = DockStyle.Fill
            };

            // PDF-faili avamise nupp
            btnOpen = new Button
            {
                Text = "Ava PDF",
                Dock = DockStyle.Top,
                Height = 40
            };

            btnOpen.Click += OpenPdf;

            Controls.Add(pdfViewer);
            Controls.Add(btnOpen);
        }

        private async void OpenPdf(object? sender, EventArgs e)
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "PDF failid (*.pdf)|*.pdf",
                Title = "Ava PDF dokument"
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    await pdfViewer.EnsureCoreWebView2Async();

                    pdfViewer.Source = new Uri(dialog.FileName);

                    Text = $"VAUPDF - {Path.GetFileName(dialog.FileName)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Viga");
                }
            }
        }
    }
}
