using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace SlickDirectory;

public class PersistenceLayer
{
    private readonly string _stateFile;
    private readonly ILogger<PersistenceLayer> _logger;

    public PersistenceLayer(IConfiguration configuration, ILogger<PersistenceLayer> logger)
    {
        _stateFile = configuration["Configuration:PersistFile"];
        _logger = logger;
    }

    public List<StateObj> GetStates()
    {
        try
        {
            if (File.Exists(_stateFile))
            {
                string json = File.ReadAllText(_stateFile);
                return JsonConvert.DeserializeObject<List<StateObj>>(json) ?? new List<StateObj>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error reading state file: {ex.Message}");
        }

        return new List<StateObj>();
    }

    public void SaveStates(List<StateObj> tempDirs)
    {
        try
        {
            tempDirs = tempDirs.Select(td => new StateObj { TempDirectory = NormalizePath(td.TempDirectory) }).Distinct().ToList();
            string json = JsonConvert.SerializeObject(tempDirs);
            File.WriteAllText(_stateFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error saving states: {ex.Message}");
        }
    }

    public List<StateObj> AddState(StateObj tempDir)
    {
        try
        {
            tempDir.TempDirectory = NormalizePath(tempDir.TempDirectory);
            var tempDirs = GetStates();
            tempDirs.Add(tempDir);
            SaveStates(tempDirs);
            return tempDirs;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error adding state: {ex.Message}");
            return new List<StateObj>();
        }
    }

    public List<StateObj> RemoveState(StateObj tempDir)
    {
        try
        {
            var tempDirs = GetStates();
            tempDirs.RemoveAll(td => td == tempDir);
            SaveStates(tempDirs);
            return tempDirs;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error removing state: {ex.Message}");
            return new List<StateObj>();
        }
    }

    private static string NormalizePath(string x)
    {
        return new DirectoryInfo(x).FullName.Replace("/", "\\").TrimEnd('\\');
    }
}