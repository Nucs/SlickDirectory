namespace SlickDirectory;

public class TempDirectoryInstance
{
    public DirectoryInfo Path { get; set; }

    public TempDirectoryInstance(DirectoryInfo path)
    {
        Path = path;
    }

    public TempDirectoryInstance(string path) : this(new DirectoryInfo(path))
    {
    }


    public void LaunchExplorerAtDirectory()
    {
        var normalizedPath = Path.FullName.TrimEnd('\\', '/');

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", normalizedPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start temp directory: {ex.Message}");
        }
    }

    public bool DeleteTempDirectory()
    {
        var path = Path.FullName;
        Console.WriteLine($"Deleting temp directory: {path}");
        try
        {
            if (!Directory.Exists(path))
                return true;

            Directory.Delete(path, true);
            Console.WriteLine($"Deleted temp directory: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete temp directory: {ex.Message}");
            return false;
        }
    }

    public bool Exists()
    {
        return Directory.Exists(Path.FullName);
    }
}