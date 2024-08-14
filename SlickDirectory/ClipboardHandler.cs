using System.Drawing.Imaging;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ImageProcessor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SlickDirectory;

/// <summary>
/// Handles clipboard operations, including extracting and processing various types of clipboard content.
/// </summary>
public class ClipboardHandler
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ClipboardHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the ClipboardHandler class.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="logger">The logger for this class.</param>
    /// <param name="persistenceLayer">The persistence layer (currently unused).</param>
    public ClipboardHandler(IConfiguration configuration, ILogger<ClipboardHandler> logger, PersistenceLayer persistenceLayer)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Extracts and processes content from the clipboard.
    /// </summary>
    /// <param name="tempDir">The temporary directory to store extracted content.</param>
    /// <param name="token">Cancellation token for async operations.</param>
    public async Task ExtractClipboardContent(string tempDir, CancellationToken token)
    {
        try
        {
            bool any = false;
            if (Clipboard.ContainsText() || Clipboard.ContainsData(DataFormats.UnicodeText) || Clipboard.ContainsData(DataFormats.Rtf))
            {
                string txt;
                if (Clipboard.ContainsData(DataFormats.UnicodeText) && (txt = (string)Clipboard.GetData(DataFormats.UnicodeText)) != null)
                {
                }
                else if (Clipboard.ContainsText() && (txt = Clipboard.GetText()) != null)
                {
                }
                else
                {
                    txt = (string)Clipboard.GetData(DataFormats.Rtf);
                }

                if (!string.IsNullOrWhiteSpace(txt) && !Regex.IsMatch(txt, @"^\s*$", RegexOptions.Singleline))
                {
                    var ext = ContentClassifier.Classify(txt);
                    await File.WriteAllTextAsync(Path.Combine(tempDir, $"clipboard.{ext}"), txt, token);

                    if (ext != "txt")
                        await File.WriteAllTextAsync(Path.Combine(tempDir, "clipboard.txt"), txt, token);

                    if (ext == "json")
                    {
                        var formatted = JsonConvert.SerializeObject(JsonConvert.DeserializeObject<JToken>(txt), Formatting.Indented);
                        if (formatted != txt)
                            await File.WriteAllTextAsync(Path.Combine(tempDir, "clipboard.formatted.json"), formatted, token);
                    }

                    if (ext == "url")
                    {
                        foreach (var url in txt.Replace("\r", "").Split("\n", StringSplitOptions.RemoveEmptyEntries))
                        {
                            await HandleUrl(tempDir, url);
                        }
                    }

                    if (Clipboard.ContainsData(DataFormats.Rtf))
                    {
                        var data = (string)Clipboard.GetData(DataFormats.Rtf);
                        await File.WriteAllTextAsync(Path.Combine(tempDir, "clipboard.rtf"), data, token);
                    }

                    any = true;
                }
            }

            if (Clipboard.ContainsFileDropList() || Clipboard.ContainsData(DataFormats.FileDrop))
            {
                await HandleFileDropList(tempDir);
                any = true;
            }

            if (Clipboard.ContainsData(DataFormats.CommaSeparatedValue))
            {
                var data = (string)Clipboard.GetData(DataFormats.CommaSeparatedValue);
                await File.WriteAllTextAsync(Path.Combine(tempDir, "clipboard.csv"), data, token);
                any = true;
            }

            if (Clipboard.ContainsData(DataFormats.Html))
            {
                await HandleHtmlData(tempDir);
                any = true;
            }

            if (Clipboard.ContainsData(DataFormats.WaveAudio))
            {
                var data = (byte[])Clipboard.GetData(DataFormats.WaveAudio);
                await File.WriteAllBytesAsync(Path.Combine(tempDir, "clipboard.wav"), data, token);
                any = true;
            }

            if (Clipboard.ContainsImage() || Clipboard.ContainsData(DataFormats.Bitmap))
            {
                using Image? data = Clipboard.GetImage();
                HandleImages(tempDir, data);
                any = true;
            }

            if (!any)
            {
                _logger.LogWarning("Unsupported clipboard format");
            }
        }
        catch (Exception e)
        {
            _logger.LogError($"Error in ExtractClipboardValue: {e.Message}");
            SystemSounds.Asterisk.Play();
        }
    }

    /// <summary>
    /// Handles file drop list from clipboard, including nested directories.
    /// </summary>
    /// <param name="tempDir">The temporary directory to store files.</param>
    private async Task HandleFileDropList(string tempDir)
    {
        try
        {
            var filesOrDirectories = (string[])Clipboard.GetData(DataFormats.FileDrop);

            int totalFileCount = CountFiles(filesOrDirectories);

            if (totalFileCount > 1000 &&
                MessageBox.Show($"There are {totalFileCount} files in total (including those in subdirectories). Do you want to proceed with copying to the temp directory?", "Warning", MessageBoxButtons.YesNo) == DialogResult.No)
            {
                return;
            }

            var maxSize = long.Parse(_configuration["Configuration:MaxFileSizeToCopyAsHardLink"]);

            foreach (var sourcePath in filesOrDirectories)
            {
                await CopyFileOrDirectory(sourcePath, tempDir, maxSize);
            }
        }
        catch (Exception e)
        {
            _logger.LogError($"Error in HandleFileDropList: {e.Message}");
            throw;
        }
    }


    private int CountFiles(string[] paths)
    {
        int count = 0;
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                count++;
            }
            else if (Directory.Exists(path))
            {
                count += Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
            }
        }

        return count;
    }

    /// <summary>
    /// Processes a file or directory recursively.
    /// </summary>
    /// <param name="sourcePath">The source file or directory path.</param>
    /// <param name="targetDir">The target directory to copy files to.</param>
    /// <param name="maxSize">The maximum file size for copying as a hard link.</param>
    private async Task CopyFileOrDirectory(string sourcePath, string targetDir, long maxSize)
    {
        if (File.Exists(sourcePath))
        {
            await CopyFile(sourcePath, targetDir, maxSize);
        }
        else if (Directory.Exists(sourcePath))
        {
            string dirName = Path.GetFileName(sourcePath);
            string newTargetDir = Path.Combine(targetDir, dirName);
            Directory.CreateDirectory(newTargetDir);

            foreach (var item in Directory.EnumerateFileSystemEntries(sourcePath))
            {
                await CopyFileOrDirectory(item, newTargetDir, maxSize);
            }
        }
    }

    /// <summary>
    /// Processes a single file.
    /// </summary>
    /// <param name="sourceFile">The source file path.</param>
    /// <param name="targetDir">The target directory to copy the file to.</param>
    /// <param name="maxSize">The maximum file size for copying as a hard link.</param>
    private async Task CopyFile(string sourceFile, string targetDir, long maxSize)
    {
        var targetFile = Path.Combine(targetDir, Path.GetFileName(sourceFile));

        if (maxSize > 0 && maxSize != int.MaxValue &&
            new FileInfo(sourceFile).Length > maxSize)
        {
            CreateHardLink(targetFile, sourceFile);
        }
        else
        {
            try
            {
                await using var fs = new FileStream(targetFile, FileMode.OpenOrCreate);
                fs.SetLength(0);
                await using var srcFs = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await srcFs.CopyToAsync(fs);
            }
            catch (IOException e)
            {
                _logger.LogError($"Error copying file: {e.Message}");
            }
        }

        try
        {
            HandleImageFile(targetDir, targetFile);
        }
        catch (Exception e)
        {
            _logger.LogError($"Error handling image file {targetFile}: {e.Message}");
        }
    }

    /// <summary>
    /// Handles image files, converting them if necessary.
    /// </summary>
    /// <param name="tempDir">The temporary directory to store processed images.</param>
    /// <param name="targetFile">The target file to process.</param>
    private void HandleImageFile(string tempDir, string targetFile)
    {
        _reenter_switch:
        switch (Path.GetExtension(targetFile).TrimStart('.').ToLowerInvariant())
        {
            case "webp":
            {
                using ImageFactory imageFactory = new ImageFactory(preserveExifData: false);
                imageFactory.Load(File.ReadAllBytes(targetFile));
                imageFactory.Format(new ImageProcessor.Imaging.Formats.PngFormat())
                    .Save(targetFile = Path.Combine(tempDir, Path.ChangeExtension(targetFile, "png")));
                goto _reenter_switch;
            }
            case "jpg":
            case "jpeg":
            case "png":
            case "gif":
            case "bmp":
            case "tiff":
            case "ico":
            case "emf":
            case "wmf":
            case "exif":
            case "memorybmp":
                using (var image = Image.FromFile(targetFile))
                {
                    HandleImages(tempDir, image, targetFile);
                }

                break;
        }
    }

    /// <summary>
    /// Handles HTML data from clipboard, including embedded images.
    /// </summary>
    /// <param name="tempDir">The temporary directory to store extracted data.</param>
    private async Task HandleHtmlData(string tempDir)
    {
        var data = (string)Clipboard.GetData(DataFormats.Html);
        await File.WriteAllTextAsync(Path.Combine(tempDir, "clipboard.html"), data);

        if (data.Contains("<img"))
        {
            var imgRegex = new Regex(@"<img.*?src=[""'](.+?)[""'].*?>", RegexOptions.IgnoreCase);
            var matches = imgRegex.Matches(data);

            if (matches.Count > 0)
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
                for (int i = 0; i < matches.Count; i++)
                {
                    string imageUrl = matches[i].Groups[1].Value;
                    string fileName = $"clipboard_image_{i + 1}.png";
                    string filePath = Path.Combine(tempDir, fileName);

                    try
                    {
                        byte[] imageData = await client.GetByteArrayAsync(imageUrl);
                        await File.WriteAllBytesAsync(filePath, imageData);
                        using (var image = Image.FromFile(filePath))
                        {
                            HandleImages(tempDir, image, Path.GetFileName(filePath));
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogError($"Error downloading image from {imageUrl}: {e.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates a hard link to a file.
    /// </summary>
    /// <param name="linkFileName">The name of the link file to create.</param>
    /// <param name="existingFileName">The name of the existing file.</param>
    private void CreateHardLink(string linkFileName, string existingFileName)
    {
        if (CreateHardLink(linkFileName, existingFileName, IntPtr.Zero))
        {
            _logger.LogInformation($"Hard link created: {linkFileName}");
        }
        else
        {
            _logger.LogError($"Failed to create hard link: {linkFileName}");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    /// <summary>
    /// Handles images from clipboard or file, saving them in various formats.
    /// </summary>
    /// <param name="tempDir">The temporary directory to store processed images.</param>
    /// <param name="data">The image data.</param>
    /// <param name="targetFileName">The target file name (optional).</param>
    /// <returns>True if an error occurred, false otherwise.</returns>
    private bool HandleImages(string tempDir, Image? data, string? targetFileName = null)
    {
        if (data == null)
        {
            var dataObj = Clipboard.GetDataObject();
            if (dataObj != null)
            {
                data = (Image)dataObj.GetData(DataFormats.Bitmap);
            }
        }

        if (data == null || data.RawFormat == null)
        {
            SystemSounds.Asterisk.Play();
            _logger.LogError("Failed to get image from clipboard");
            return true;
        }

        targetFileName ??= "clipboard";
        try
        {
            void SaveImageIfNotExists(Image data, string tempDir, string targetFileName, ImageFormat format, string extension)
            {
                string filePath = Path.Combine(tempDir, Path.ChangeExtension(targetFileName, extension));
                if (!File.Exists(filePath))
                {
                    data.Save(filePath, format);
                }
            }

            if (data.RawFormat.Equals(ImageFormat.Jpeg))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Jpeg, "jpg");
            else if (data.RawFormat.Equals(ImageFormat.Png))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Png, "png");
            else if (data.RawFormat.Equals(ImageFormat.Gif))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Gif, "gif");
            else if (data.RawFormat.Equals(ImageFormat.Bmp))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Bmp, "bmp");
            else if (data.RawFormat.Equals(ImageFormat.Emf))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Emf, "emf");
            else if (data.RawFormat.Equals(ImageFormat.Wmf))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Wmf, "wmf");
            else if (data.RawFormat.Equals(ImageFormat.Exif))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Exif, "exif");
            else if (data.RawFormat.Equals(ImageFormat.Tiff))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Tiff, "tiff");
            else if (data.RawFormat.Equals(ImageFormat.MemoryBmp))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Bmp, "bmp");
            else if (data.RawFormat.Equals(ImageFormat.Icon))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Icon, "ico");
            else
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Bmp, "bmp");

            if (!data.RawFormat.Equals(ImageFormat.Png))
                SaveImageIfNotExists(data, tempDir, targetFileName, ImageFormat.Png, "png");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error saving image: {ex.Message}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles URL content from clipboard, downloading and saving the content.
    /// </summary>
    /// <param name="tempDir">The temporary directory to store downloaded content.</param>
    /// <param name="txt">The URL text.</param>
    private async Task HandleUrl(string tempDir, string txt)
    {
        var fileName = "clipboard.url";
        var uri = new Uri(txt);
        await File.WriteAllTextAsync(Path.Combine(tempDir, fileName), $"[InternetShortcut]\nURL={uri}");

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3");
            var response = await client.GetAsync(uri);

            if (!response.IsSuccessStatusCode)
            {
                //try to read content
                string content = null;
                try
                {
                    content = await response.Content.ReadAsStringAsync();
                }
                catch (Exception e)
                {
                }

                await File.WriteAllBytesAsync(Path.Combine(tempDir, "clipboard.url.response"), Encoding.UTF8.GetBytes(content ?? ("rejected " + response.StatusCode)));
                return;
            }

            fileName = response.Content.Headers.ContentDisposition?.FileName;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                if (response.Content.Headers.ContentType?.MediaType?.Contains("image") == true)
                {
                    var ext = response.Content.Headers.ContentType?.MediaType.Split('/')[1];
                    fileName = "clipboard." + ext?.TrimStart('.');
                }
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "clipboard.url.response";
            }

            fileName = fileName?.Trim(new char[] { '\"', '\'', ' ' });

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var data = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(Path.Combine(tempDir, fileName), data);

                // Save as PNG as well
                if (!Path.GetExtension(fileName).EndsWith("png", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using ImageFactory imageFactory = new ImageFactory(preserveExifData: false);
                        // Load the image
                        imageFactory.Load(await File.ReadAllBytesAsync(Path.Combine(tempDir, fileName)));

                        // Save the image in PNG format
                        imageFactory.Format(new ImageProcessor.Imaging.Formats.PngFormat())
                            .Save(Path.Combine(tempDir, Path.ChangeExtension(fileName, "png")));
                    }
                    catch (Exception e)
                    {
                        _logger.LogError($"Error converting image to PNG: {e.Message}");
                    }
                }
            }
            else
            {
                var data = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(Path.Combine(tempDir, "clipboard.url.response"), data);
            }
        }
        catch (Exception e)
        {
            _logger.LogError($"Error handling URL: {e.Message}");
        }
    }
}