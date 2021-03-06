﻿using System.IO;

#if PLATFORM_WINDOWS
namespace Protobuild.Manager
{
    using System;
    using System.Diagnostics;
    using System.Drawing;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Web;
    using System.Windows.Forms;

    public class WindowsUIManager : IUIManager
    {
        private Form m_ActiveForm;
        
        private readonly RuntimeServer m_RuntimeServer;

        private readonly IAppHandlerManager m_AppHandlerManager;

        private readonly IBrandingEngine _brandingEngine;

        public WindowsUIManager(RuntimeServer server, IAppHandlerManager appHandlerManager, IBrandingEngine brandingEngine)
        {
            this.m_RuntimeServer = server;
            this.m_AppHandlerManager = appHandlerManager;
            _brandingEngine = brandingEngine;
        }

        public void Run()
        {
            BrowserEmulation.EnableLatestIE();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var form = new Form();
            this.m_ActiveForm = form;
            form.Text = _brandingEngine.ProductName;
            form.Icon = _brandingEngine.WindowsIcon;
            form.Width = 736;
            form.Height = 439;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.StartPosition = FormStartPosition.CenterScreen;

            var webBrowser = new WebBrowser();
            webBrowser.Dock = DockStyle.Fill;
            webBrowser.ObjectForScripting = new ScriptInterface();
            form.Controls.Add(webBrowser);

            this.m_RuntimeServer.RegisterRuntimeInjector(x =>
                this.SafeInvoke(form, () => ExecuteScript(webBrowser, x)));

            if (!Debugger.IsAttached)
            {
                webBrowser.ScriptErrorsSuppressed = true;
            }

            webBrowser.Navigate(this.m_RuntimeServer.BaseUri);

            webBrowser.Navigating += (o, a) => ExecuteAndCatch(
                () =>
                {
                    var uri = a.Url;

                    if (uri.Scheme != "app")
                    {
                        return;
                    }

                    this.m_AppHandlerManager.Handle(uri.AbsolutePath, HttpUtility.ParseQueryString(uri.Query));

                    a.Cancel = true;
                });

            webBrowser.Navigated += (sender, args) => ExecuteAndCatch(() =>
            {
                this.m_RuntimeServer.Flush();
            });

            form.Show();
            form.FormClosing += (o, a) =>
            {
                if (!Program.IsSubmitting)
                {
                    Application.Exit();
                }
            };

            Application.Run();
        }

        [System.Runtime.InteropServices.ComVisible(true)]
        public class ScriptInterface
        {
            public void ReportError(string errorMessage, string url, int lineNumber)
            {
                Console.WriteLine(url + ":" + lineNumber + ": " + errorMessage);
            }

            public void Log(string errorMessage)
            {
                Console.WriteLine(errorMessage);
            }
        }

        public void Quit()
        {
            Application.Exit();
        }

        public string SelectExistingProject()
        {
            var ofd = new System.Windows.Forms.OpenFileDialog();
            ofd.Filter = "Protobuild Module|Protobuild.exe";
            ofd.CheckFileExists = true;
            ofd.Multiselect = false;
            ofd.AutoUpgradeEnabled = true;
            ofd.CheckPathExists = true;
            ofd.Title = "Select Protobuild Module";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                var fileInfo = new FileInfo(ofd.FileName);
                if (!fileInfo.Exists || fileInfo.Name.ToLowerInvariant() != "Protobuild.exe".ToLowerInvariant())
                {
                    return null;
                }
                return fileInfo.DirectoryName;
            }
            return null;
        }

        public bool AskToRepairCorruptProtobuild()
        {
            return MessageBox.Show(
                "The version of Protobuild.exe in this module could not be loaded " +
                "and may be corrupt.  Do you want to download the latest version " +
                "of Protobuild to repair it (Yes), or fallback to the solutions that " +
                "have already been generated (No)?",
                "Unable to load Protobuild",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error) == DialogResult.Yes;
        }

        public void FailedToRepairCorruptProtobuild()
        {
            MessageBox.Show(
                "This program was unable to repair Protobuild and will now fallback " +
                "to using the existing solutions.",
                "Failed to repair Protobuild",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        public void UnableToLoadModule()
        {
            MessageBox.Show(
                "This program was unable to load the module or project definition " +
                "information.  Check that the Build/Module.xml and project " +
                "definition files are all valid XML and that they contain no " +
                "errors.  This program will now fallback to using the existing " +
                "solutions.",
                "Unable to load module",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        public string BrowseForProjectDirectory()
        {
            while (true)
            {
                string existingPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var odd = new System.Windows.Forms.FolderBrowserDialog();
                odd.Description = "Choose a directory to create the project in";
                odd.SelectedPath = existingPath;
                if (odd.ShowDialog() == DialogResult.OK)
                {
                    var directoryInfo = new DirectoryInfo(odd.SelectedPath);
                    if (directoryInfo.GetFiles().Length > 0)
                    {
                        if (MessageBox.Show("It doesn't look like the selected directory is empty.  You " +
                                            "should ideally create new projects in empty directories.  " +
                                            "Use it anyway?", "Directory Not Empty", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) ==
                            DialogResult.Yes)
                        {
                            return odd.SelectedPath;
                        }

                        existingPath = odd.SelectedPath;
                        continue;
                    }

                    return odd.SelectedPath;
                }

                return null;
            }
        }

        private void ExecuteAndCatch(Action eventHandler)
        {
            if (Debugger.IsAttached)
            {
                eventHandler();
            }
            else
            {
                try
                {
                    eventHandler();
                }
                catch (Exception e)
                {
                    lock (Program.SubmissionLock)
                    {
                        if (Program.IsSubmitting)
                        {
                            return;
                        }

                        Program.IsSubmitting = true;
                    }

                    if (this.m_ActiveForm != null)
                    {
                        this.m_ActiveForm.Close();
                    }

                    Application.DoEvents();

                    Application.Exit();
                }
            }
        }

        private void SafeInvoke(Form form, Action action)
        {
            try
            {
                form.Invoke(action);
            }
            catch (InvalidOperationException)
            {
            }
            catch (NullReferenceException)
            {
            }
        }

        [ComImport, ComVisible(true), Guid(@"3050f28b-98b5-11cf-bb82-00aa00bdce0b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
        [TypeLibType(TypeLibTypeFlags.FDispatchable)]
        public interface IHTMLScriptElement
        {
            [DispId(1006)]
            string text { set; [return: MarshalAs(UnmanagedType.BStr)] get; }
        }

        private static int m_NameCount = 0;

        private static void ExecuteScript(WebBrowser browser, string script)
        {
            var uniqueName = "call" + m_NameCount++;
            var doc = browser.Document;
            HtmlElement head = null;
            if (doc != null)
            {
                var tags = doc.GetElementsByTagName("head");
                if (tags.Count == 1)
                {
                    head = tags[0];
                }
            };
            if (head == null)
            {
                Console.WriteLine("WARNING: Can't execute script yet; document not ready!");
                return;
            }
            var scriptEl = browser.Document.CreateElement("script");
            var element = (IHTMLScriptElement)scriptEl.DomElement;
            element.text = "function " + uniqueName + "() { " + script + " }";
            head.AppendChild(scriptEl);
            browser.Document.InvokeScript(uniqueName);
        }
    }
}
#endif