using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoMapper;
using ImageProcessor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace SlickDirectory
{
    public partial class Main : TransparentForm
    {
        private readonly BusinessLayer _businessLayer;
        private readonly IConfiguration _configuration;
        private int _tempFolderCount;
        private readonly PersistenceLayer _persistenceLayer;
        private readonly ILogger<BusinessLayer> _logger;
        private readonly IMapper _mapper;
        private NotifyIcon _trayIcon;
        private GlobalHotKey _hotKeyCreateTempDirectory;

        public Main(ILogger<BusinessLayer> logger, BusinessLayer businessLayer, PersistenceLayer persistenceLayer, IConfiguration configuration, IMapper mapper)
        {
            InitializeComponent();
            _businessLayer = businessLayer;
            _persistenceLayer = persistenceLayer;
            _logger = logger;
            _configuration = configuration;
            _mapper = mapper;

            Application.ApplicationExit += OnApplicationExit;

            try
            {
                _businessLayer.LoadState();

                RegisterHotkeys();

                InitializeTrayIcon();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in Main constructor: {ex.Message}");
                MessageBox.Show($"An error occurred during initialization: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        private void RegisterHotkeys()
        {
            // Register global hotkey
            var hotKeyModifiers = _configuration["Configuration:HotKeys:CreateTempDirectory:Modifiers"]
                .Split(new char[] { '|', ',', '+' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Enum.Parse<ModKeys>)
                .Aggregate((x, y) => x | y);
            var hotKeyKey = (Keys)Enum.Parse(typeof(Keys), _configuration["Configuration:HotKeys:CreateTempDirectory:Key"]);

            _hotKeyCreateTempDirectory = new GlobalHotKey(hotKeyModifiers, hotKeyKey, this.Handle);
            if (!_hotKeyCreateTempDirectory.Register())
            {
                _logger.LogError("Failed to register hotkey" + _hotKeyCreateTempDirectory);
            }
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _trayIcon = new NotifyIcon();
                var endsWith = "icon.ico";
                _trayIcon.Icon = new Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream(
                    Assembly.GetExecutingAssembly().GetManifestResourceNames().First(x => x.EndsWith(endsWith)))!);
                _trayIcon.Visible = true;

                // Create context menu
                var contextMenu = new ContextMenuStrip();
                var tempMenu = new ToolStripMenuItem("Temp Directory");
                contextMenu.Items.Add(tempMenu);

                var tempFoldersMenuItem = new ToolStripMenuItem($"Count: {_tempFolderCount}");
                _businessLayer.TempFolderCountChanged += count => UpdateTempFolderCount(tempFoldersMenuItem, count);
                tempMenu.DropDownItems.Add(tempFoldersMenuItem);
                tempMenu.DropDownItems.Add(new ToolStripSeparator());


                var createTempDirectoryMenuItem = new ToolStripMenuItem($"Create ({_hotKeyCreateTempDirectory})");
                createTempDirectoryMenuItem.Click += (o, args) => Task.Run(() => _businessLayer.OnHotkey_CreateTempDirectory());
                tempMenu.DropDownItems.Add(createTempDirectoryMenuItem);

                var openAllTempDirectory = new ToolStripMenuItem("Open All");
                openAllTempDirectory.Click += LaunchAllDirectories;
                tempMenu.DropDownItems.Add(openAllTempDirectory);

                var tempDirectoriesSubmenu = new ToolStripMenuItem("Open ->");
                UpdateTempDirectoriesSubmenu(tempDirectoriesSubmenu);
                _businessLayer.TempFolderCountChanged += _ => UpdateTempDirectoriesSubmenu(tempDirectoriesSubmenu);

                tempMenu.DropDownItems.Add(tempDirectoriesSubmenu);

                var flushAllMenuItem = new ToolStripMenuItem("Flush All");
                flushAllMenuItem.Click += (o, args) =>
                {
                    if (MessageBox.Show("Are you sure you want to flush all temp directories?", "Flush All", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        FlushAllDirectories();
                };
                tempMenu.DropDownItems.Add(flushAllMenuItem);

                var flushMenuItem = new ToolStripMenuItem("Flush ->");
                UpdateFlushSubmenu(flushMenuItem);
                _businessLayer.TempFolderCountChanged += _ => UpdateFlushSubmenu(flushMenuItem);
                tempMenu.DropDownItems.Add(flushMenuItem);

                var settingsMenu = new ToolStripMenuItem("Settings");
                contextMenu.Items.Add(settingsMenu);

                // Add the new startup toggle checkbox
                var startupToggle = new ToolStripMenuItem("Run at Startup")
                {
                    CheckOnClick = true,
                    Checked = IsInStartup()
                };
                startupToggle.Click += (sender, args) => ToggleStartup(startupToggle.Checked);
                settingsMenu.DropDownItems.Add(startupToggle);
                var openAppSettings = new ToolStripMenuItem("Open appsettings.json");
                openAppSettings.Click += (sender, args) => ShellStartTxtFile(Path.Combine(Application.StartupPath, "appsettings.json"));
                settingsMenu.DropDownItems.Add(openAppSettings);

                var exitMenuItem = new ToolStripMenuItem("Exit");
                exitMenuItem.Click += (sender, e) => Application.Exit();
                contextMenu.Items.Add(exitMenuItem);

                _trayIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in InitializeTrayIcon: {ex.Message}");
            }
        }

        private void ShellStartTxtFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in ShellStartTxtFile: {ex.Message}");
            }
        }

        private void UpdateFlushSubmenu(ToolStripMenuItem flushMenuItem)
        {
            flushMenuItem.DropDownItems.Clear();
            var tempDirectories = _persistenceLayer.GetStates();
            foreach (var dir in tempDirectories)
            {
                var menuItem = new ToolStripMenuItem(dir.TempDirectory.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Last());
                var dirInfo = _mapper.Map<TempDirectoryInstance>(dir);
                menuItem.Click += (sender, e) => { _businessLayer.FlushDirectory(dirInfo); };
                flushMenuItem.DropDownItems.Add(menuItem);
            }
        }

        private bool IsInStartup()
        {
            string startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string shortcutPath = Path.Combine(startupFolderPath, $"{Application.ProductName}.lnk");
            return File.Exists(shortcutPath);
        }

        private void ToggleStartup(bool enable)
        {
            string startupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string shortcutPath = Path.Combine(startupFolderPath, $"{Application.ProductName}.lnk");
            string exePath = Application.ExecutablePath;

            if (enable)
            {
                if (!File.Exists(shortcutPath))
                {
                    CreateShortcut(exePath, shortcutPath);
                }
            }
            else
            {
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
            }
        }

        private void CreateShortcut(string targetPath, string shortcutPath)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            var shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Save();
        }

        private void LaunchAllDirectories(object? sender, EventArgs e)
        {
            foreach (var state in _persistenceLayer.GetStates())
            {
                _mapper.Map<TempDirectoryInstance>(state).LaunchExplorerAtDirectory();
            }
        }

        private void UpdateTempDirectoriesSubmenu(ToolStripMenuItem submenu)
        {
            submenu.DropDownItems.Clear();
            var tempDirectories = _persistenceLayer.GetStates();
            foreach (var dir in tempDirectories)
            {
                var menuItem = new ToolStripMenuItem(dir.TempDirectory.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Last());
                menuItem.Click += (sender, e) => { _mapper.Map<TempDirectoryInstance>(dir).LaunchExplorerAtDirectory(); };
                submenu.DropDownItems.Add(menuItem);
            }
        }

        private void FlushAllDirectories()
        {
            try
            {
                _businessLayer.FlushAllDirectories();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in Flush operation: {ex.Message}");
                MessageBox.Show($"An error occurred during flush operation: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateTempFolderCount(ToolStripMenuItem menu, int? count = null)
        {
            try
            {
                _tempFolderCount = count ?? _persistenceLayer.GetStates().Count;
                menu.Text = $"Temp Folders: {_tempFolderCount}";
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in UpdateTempFolderCount: {ex.Message}");
            }
        }

        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == 0x0312 && m.WParam.ToInt32() == _hotKeyCreateTempDirectory.GetHashCode())
                {
                    try
                    {
                        TriggerTempDirectoryCreation();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error in WndProc: {ex}");
                    }

                    async void TriggerTempDirectoryCreation()
                    {
                        await _businessLayer.OnHotkey_CreateTempDirectory();
                    }
                }

                base.WndProc(ref m);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in WndProc: {ex}");
            }
        }

        private void OnApplicationExit(object sender, EventArgs e)
        {
            try
            {
                _logger.LogInformation("Application exiting");
                _businessLayer.Dispose();
                _hotKeyCreateTempDirectory.Unregister();
                _trayIcon.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during application exit: {ex.Message}");
            }
        }
    }
}