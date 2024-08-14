using System.Media;
using AutoMapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SlickDirectory
{
    public class BusinessLayer : IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ClipboardHandler _clipboardHandler;
        private readonly PersistenceLayer _persistenceLayer;
        private readonly ILogger<BusinessLayer> _logger;
        private readonly IMapper _mapper;

        public event Action<TempDirectoryInstance>? TempDirectoryCreated;
        public event Action<TempDirectoryInstance>? TempDirectoryDeleted;
        public event Action<int>? TempFolderCountChanged;

        public BusinessLayer(ILogger<BusinessLayer> logger, IConfiguration configuration, PersistenceLayer persistenceLayer, ClipboardHandler clipboardHandler, IMapper mapper)
        {
            _logger = logger;
            _configuration = configuration;
            _persistenceLayer = persistenceLayer;
            _clipboardHandler = clipboardHandler;
            _mapper = mapper;
            TempDirectoryCreated += OnTempDirectoryCreated;
            TempDirectoryDeleted += OnTempDirectoryDeleted;

            // Register for application exit events
            Application.ApplicationExit += (sender, args) => Dispose();
        }

        private void OnTempDirectoryCreated(TempDirectoryInstance obj)
        {
            var states = _persistenceLayer.AddState(new StateObj { TempDirectory = obj.Path.FullName });
            TempFolderCountChanged?.Invoke(states.Count);
        }

        private void OnTempDirectoryDeleted(TempDirectoryInstance obj)
        {
            var states = _persistenceLayer.RemoveState(new StateObj { TempDirectory = obj.Path.FullName });
            TempFolderCountChanged?.Invoke(states.Count);
        }

        public void LoadState()
        {
            try
            {
                var states = _persistenceLayer.GetStates();
                TempFolderCountChanged?.Invoke(states.Count);
                bool changed = false;

                for (var i = states.Count - 1; i >= 0; i--)
                {
                    var tempDir = states[i];
                    var instance = new TempDirectoryInstance(tempDir.TempDirectory);
                    if (!instance.Exists())
                    {
                        states.RemoveAt(i);
                        Console.WriteLine($"Removing non-existent temp directory: {tempDir.TempDirectory}");
                        changed = true;
                        continue;
                    }

                    void OnExiting(object? sender, EventArgs e)
                    {
                        if (instance.DeleteTempDirectory())
                            TempDirectoryDeleted?.Invoke(instance);

                        // Remove from persisted list
                        _persistenceLayer.RemoveState(new StateObj { TempDirectory = tempDir.TempDirectory });
                    }

                    AppDomain.CurrentDomain.ProcessExit += OnExiting;
                    Application.ApplicationExit += OnExiting;
                    if (_configuration.GetValue<bool>("Configuration:OpenTempExplorersOnStart"))
                        instance.LaunchExplorerAtDirectory();
                }

                if (changed)
                {
                    _persistenceLayer.SaveStates(states);
                    TempFolderCountChanged?.Invoke(states.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in LoadState: {ex.Message}");
            }
        }

        public async Task OnHotkey_CreateTempDirectory()
        {
            await CreateTempDirectory();
        }

        public async Task CreateTempDirectory()
        {
            try
            {
                // Generate a unique temporary directory path
                string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")[8..16]);
                var instance = new TempDirectoryInstance(tempDir);

                // Create the temporary directory
                Directory.CreateDirectory(tempDir);

                var cancellationTokenSource = new CancellationTokenSource();

                //hook up to delete the temp directory on exit
                AppDomain.CurrentDomain.ProcessExit += OnExiting;
                Application.ApplicationExit += OnExiting;
                instance.LaunchExplorerAtDirectory();

                //persist the temp directory

                TempDirectoryCreated?.Invoke(instance);

                await _clipboardHandler.ExtractClipboardContent(tempDir, cancellationTokenSource.Token).ConfigureAwait(true);

                void OnExiting(object? sender, EventArgs e)
                {
                    if (instance.DeleteTempDirectory())
                        TempDirectoryDeleted?.Invoke(instance);

                    // Remove from persisted list
                    if (!cancellationTokenSource.Token.IsCancellationRequested)
                        cancellationTokenSource.Cancel();
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"Error in CreateTempDirectoryAndQueueForDeletion: {e.Message}");
                SystemSounds.Asterisk.Play();
            }
        }

        public void Dispose()
        {
            try
            {
                _logger.LogInformation("Application exiting");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during application exit: {ex.Message}");
            }
        }

        public void FlushAllDirectories()
        {
            var states = _persistenceLayer.GetStates();
            var remaining = new List<StateObj>();
            foreach (var state in states)
            {
                var instance = _mapper.Map<TempDirectoryInstance>(state);
                if (!FlushDirectory(instance))
                {
                    remaining.Add(state);
                }
            }

            _persistenceLayer.SaveStates(remaining);
            TempFolderCountChanged?.Invoke(remaining.Count);
        }

        public bool FlushDirectory(TempDirectoryInstance instance)
        {
            if (instance.DeleteTempDirectory())
            {
                TempDirectoryDeleted?.Invoke(instance);
                TempFolderCountChanged?.Invoke(_persistenceLayer.GetStates().Count);
                return true;
            }

            return false;
        }
    }
}