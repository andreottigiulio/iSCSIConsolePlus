/* Copyright (C) 2016 Tal Aloni <tal.aloni.il@gmail.com>. All rights reserved.
 * 
 * You can redistribute this program and/or modify it under the terms of
 * the GNU Lesser Public License as published by the Free Software Foundation,
 * either version 3 of the License, or (at your option) any later version.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ISCSIConsole
{
    public partial class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.ThreadException += new ThreadExceptionEventHandler(Application_ThreadException);
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            CommandLineOptions options = CommandLineOptions.Parse(args);

            if (options.ShowHelp)
            {
                MessageBox.Show(CommandLineOptions.GetUsage(), "iSCSI Console");
                return;
            }

            if (options.LogToFile)
            {
                string path = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                if (!path.EndsWith(@"\"))
                {
                    path += @"\";
                }
                path += String.Format("Log {0}.txt", DateTime.Now.ToString("yyyy-MM-dd HH-mm"));
                bool success = OpenLogFile(path);
                if (!success)
                {
                    MessageBox.Show("Cannot open log file", "Error");
                }
            }

            if (options.Error != null)
            {
                MessageBox.Show(options.Error + Environment.NewLine + Environment.NewLine + CommandLineOptions.GetUsage(), "Error");
                return;
            }

            if (!String.IsNullOrEmpty(options.HeadlessAction))
            {
                RunHeadless(options);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(options));
        }

        /// <summary>
        /// Runs an elevated operation (iSCSI initiator / disk management) without any UI,
        /// writing a report that the non-elevated instance can display.
        /// </summary>
        private static void RunHeadless(CommandLineOptions options)
        {
            Reporter reporter = new Reporter(options.ReportPath);
            int exitCode = 1;
            try
            {
                exitCode = HeadlessActions.Run(options.HeadlessAction, AppSettings.Load(), reporter);
            }
            catch (Exception ex)
            {
                reporter.Write("UNEXPECTED ERROR: " + ex.GetType() + ": " + ex.Message);
                reporter.Write("RESULT: FAILED");
            }
            Environment.ExitCode = exitCode;
        }

        public static void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            HandleUnhandledException(e.Exception);
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject != null)
            {
                Exception ex = (Exception)e.ExceptionObject;
                HandleUnhandledException(ex);
            }
        }

        private static void HandleUnhandledException(Exception ex)
        {
            string message = String.Format("Exception: {0}: {1} Source: {2} {3}", ex.GetType(), ex.Message, ex.Source, ex.StackTrace);
            MessageBox.Show(message, "Error");
            Application.Exit();
        }
    }
}
