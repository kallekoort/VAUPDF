
using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualBasic.ApplicationServices;

namespace VAUPDF
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();

            string[] files = args
                .Where(File.Exists)
                .Select(path => Path.GetFullPath(path))
                .ToArray();

            new SingleInstanceApp().Run(files);
        }
    }

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
                await form.OpenFilesAsync(CommandLineArgs);
            };
        }

        protected override async void OnStartupNextInstance(
            StartupNextInstanceEventArgs e)
        {
            e.BringToForeground = true;

            base.OnStartupNextInstance(e);

            if (MainForm is Form1 form)
            {
                if (form.WindowState ==
                    FormWindowState.Minimized)
                {
                    form.WindowState =
                        FormWindowState.Normal;
                }

                form.Activate();

                await form.OpenFilesAsync(e.CommandLine);
            }
        }
    }
}
