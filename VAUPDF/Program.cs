
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.VisualBasic.ApplicationServices;
using Microsoft.Web.WebView2.Core;

namespace VAUPDF
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // ======================================
            // WEBVIEW2 DLL ASUKOHA PARANDUS
            // ======================================

            string loaderPath = Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                "win-x64",
                "native");

            if (File.Exists(Path.Combine(
                loaderPath,
                "WebView2Loader.dll")))
            {
                CoreWebView2Environment
                    .SetLoaderDllFolderPath(loaderPath);
            }

            // Rakenduse käivitamine
            ApplicationConfiguration.Initialize();

            if (!CheckRequirements())
                return;

            // Käsurealt saadud failid
            string[] files = args
                .Where(File.Exists)
                .Select(path => Path.GetFullPath(path))
                .ToArray();

            // Üks rakenduse eksemplar
            new SingleInstanceApp().Run(files);
        }

        private static bool CheckRequirements()
        {
            if (!Environment.Is64BitProcess || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                MessageBox.Show("VAUPDF vajab Windows 11 ja x64 rakenduse versiooni.",
                    "VAUPDF – süsteeminõuded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                CoreWebView2Environment.GetAvailableBrowserVersionString();
                return true;
            }
            catch (WebView2RuntimeNotFoundException)
            {
                DialogResult result = MessageBox.Show(
                    "PDF-failide kuvamiseks on vaja Microsoft Edge WebView2 Runtime’i.\n\n" +
                    "Käivita VAUPDF-i setup.exe või paigalda WebView2 Evergreen Runtime.\n" +
                    "Allalaadimiseks on vaja internetiühendust.\n\n" +
                    "Kas avada Microsofti allalaadimisleht?",
                    "WebView2 puudub", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(
                            "https://developer.microsoft.com/microsoft-edge/webview2/#download")
                        {
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Allalaadimislehte ei õnnestunud avada.\n\n" + ex.Message,
                            "VAUPDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("WebView2 käivitamine ebaõnnestus. " +
                    "Paranda VAUPDF-i või WebView2 paigaldus.\n\n" + ex.Message,
                    "VAUPDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }

    // ==============================================
    // SINGLE INSTANCE
    // ==============================================

    internal sealed class SingleInstanceApp
        : WindowsFormsApplicationBase
    {
        public SingleInstanceApp()
        {
            IsSingleInstance = true;
        }

        protected override void OnCreateMainForm()
        {
            Form1 form = new();

            MainForm = form;

            form.Shown += async (s, e) =>
            {
                await form.OpenFilesAsync(
                    CommandLineArgs);
            };
        }

        protected override async void OnStartupNextInstance(
            StartupNextInstanceEventArgs e)
        {
            e.BringToForeground = true;

            base.OnStartupNextInstance(e);

            if (MainForm is Form1 form)
            {
                // Taasta minimeeritud aken
                if (form.WindowState ==
                    FormWindowState.Minimized)
                {
                    form.WindowState =
                        FormWindowState.Normal;
                }

                form.Activate();

                // Ava uued failid olemasolevas aknas
                await form.OpenFilesAsync(
                    e.CommandLine);
            }
        }
    }
}
