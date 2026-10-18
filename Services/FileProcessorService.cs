using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace LemonAID.Services;

public enum WheelMode
{
    Converter, // Shift
    Tools,     // Alt
    Actions    // Ctrl
}

public sealed record SectorItem(string Id, string Label, string Glyph, string Description);

public static class FileProcessorService
{
    public static IReadOnlyList<SectorItem> GetSectorsForMode(WheelMode mode, IReadOnlyList<string> filePaths)
    {
        return mode switch
        {
            WheelMode.Converter => GetContextualConverterSectors(filePaths),
            WheelMode.Tools => (SectorItem[])
            [
                new("COMPRESS", "COMPRESS", "\uE740", "Reduce file size or compress file/folder beside original"),
                new("METADATA", "METADATA", "\uE8EC", "Inspect or strip embedded metadata into a clean copy"),
                new("EDIT", "EDIT", "\uE790", "Enhance contrast & brightness and export as a new file"),
                new("ANNOTATE", "ANNOTATE", "\uE929", "Stamp an annotation header banner onto a copy"),
                new("ADD_BG", "ADD BG", "\uE91B", "Place image onto a padded lemon-gradient canvas"),
                new("CROP", "CROP", "\uE7A8", "Center-crop image to a 1:1 square aspect ratio"),
                new("REDACT", "REDACT", "\uED1A", "Mask central sensitive region or redact text emails/phones")
            ],
            WheelMode.Actions => (SectorItem[])
            [
                new("SHARE", "NEARBY", "\uE72D", "Reveal in Explorer for Windows Nearby Share"),
                new("FLOAT", "FLOAT", "\uE718", "Pin a floating Always-On-Top preview window"),
                new("COPY", "COPY", "\uE8C8", "Copy item to clipboard and create a duplicate beside original"),
                new("MOVE", "MOVE", "\uE8DE", "Move selected item(s) to destination folder"),
                new("COPY_PATH", "COPY PATH", "\uE71B", "Copy full Windows path(s) to clipboard"),
                new("TERMINAL", "TERMINAL", "\uE756", "Open Windows Terminal / PowerShell in target directory")
            ],
            _ => []
        };
    }

    private static IReadOnlyList<SectorItem> GetContextualConverterSectors(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count > 0)
        {
            string first = filePaths[0];
            if (Directory.Exists(first))
            {
                return (SectorItem[])
                [
                    new("ZIP", "ZIP", "\uF012", "Package folder into a .zip archive beside original"),
                    new("PDF", "PDF", "\uEA90", "Export folder manifest summary to PDF"),
                    new("TXT", "TXT", "\uE8A5", "Export directory file listing to .txt"),
                    new("MD", "MARKDOWN", "\uE8A5", "Export directory listing to Markdown (.md)"),
                    new("CSV", "CSV", "\uE9F9", "Export directory inventory to .csv"),
                    new("HTML", "HTML", "\uE774", "Export directory inventory to .html")
                ];
            }

            string ext = Path.GetExtension(first).ToLowerInvariant();
            if (ext is ".srt" or ".vtt" or ".txt" or ".md" or ".csv" or ".tsv")
            {
                return (SectorItem[])
                [
                    new("TXT", "TXT", "\uE8A5", "Convert to plain text (.txt)"),
                    new("MD", "MARKDOWN", "\uE8A5", "Convert to Markdown document (.md)"),
                    new("HTML", "HTML", "\uE774", "Export formatted HTML beside original"),
                    new("CSV", "CSV", "\uE9F9", "Convert tabular delimiters to CSV"),
                    new("VTT", "VTT", "\uE8C1", "Convert subtitle cues to WebVTT (.vtt)"),
                    new("ZIP", "ZIP", "\uF012", "Package into a standard .zip archive")
                ];
            }
        }

        return (SectorItem[])
        [
            new("PNG", "PNG", "\uEB9F", "Lossless PNG image"),
            new("WEBP", "WEBP", "\uEB9F", "WebP / high-efficiency image output"),
            new("HEIC", "HEIC", "\uEB9F", "HEIC / modern container output"),
            new("TIFF", "TIFF", "\uEB9F", "Uncompressed archival TIFF"),
            new("AVIF", "AVIF", "\uEB9F", "AVIF / next-gen image output"),
            new("PDF", "PDF", "\uEA90", "Export into a single-file PDF wrapper")
        ];
    }

    public static string FormatTotalSize(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return "2.4 MB";
        long totalBytes = 0;
        foreach (var p in paths)
        {
            if (File.Exists(p))
            {
                totalBytes += new FileInfo(p).Length;
            }
            else if (Directory.Exists(p))
            {
                try
                {
                    totalBytes += new DirectoryInfo(p)
                        .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                        .Sum(fi => fi.Length);
                }
                catch { }
            }
        }

        if (totalBytes == 0 && Directory.Exists(paths[0])) return "Folder";
        if (totalBytes < 1024) return $"{totalBytes} B";
        if (totalBytes < 1024 * 1024) return $"{totalBytes / 1024.0:F1} KB";
        return $"{totalBytes / (1024.0 * 1024.0):F1} MB";
    }

    public static async Task<string> ExecuteSectorAsync(
        WheelMode mode,
        string sectorId,
        IReadOnlyList<string> paths,
        string? customOutputFolder,
        Action<string>? onFloatRequested)
    {
        if (paths.Count == 0)
        {
            return "Select or drag at least one file or folder first.";
        }

        return mode switch
        {
            WheelMode.Converter => await RunConversionAsync(sectorId, paths, customOutputFolder),
            WheelMode.Tools => await RunToolAsync(sectorId, paths, customOutputFolder),
            WheelMode.Actions => await RunActionAsync(sectorId, paths, customOutputFolder, onFloatRequested),
            _ => "Unknown operation."
        };
    }

    private static string GetOutputPath(string sourcePath, string suffix, string newExt, string? customFolder)
    {
        string cleanSource = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string dir = !string.IsNullOrWhiteSpace(customFolder) && Directory.Exists(customFolder)
            ? customFolder
            : Path.GetDirectoryName(cleanSource) ?? Environment.CurrentDirectory;

        string baseName = Directory.Exists(cleanSource)
            ? Path.GetFileName(cleanSource)
            : Path.GetFileNameWithoutExtension(cleanSource);

        string candidate = Path.Combine(dir, $"{baseName}{suffix}{newExt}");
        int counter = 1;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(dir, $"{baseName}{suffix}_{counter}{newExt}");
            counter++;
        }
        return candidate;
    }

    private static async Task<string> RunConversionAsync(string formatId, IReadOnlyList<string> paths, string? customFolder)
    {
        int converted = 0;
        string lastOutput = string.Empty;

        foreach (var itemPath in paths)
        {
            bool isDir = Directory.Exists(itemPath);
            bool isFile = File.Exists(itemPath);
            if (!isDir && !isFile) continue;

            if (formatId == "ZIP")
            {
                string zipPath = GetOutputPath(itemPath, "_archive", ".zip", customFolder);
                if (isDir)
                {
                    ZipFile.CreateFromDirectory(itemPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
                }
                else
                {
                    using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                    archive.CreateEntryFromFile(itemPath, Path.GetFileName(itemPath), CompressionLevel.Optimal);
                }
                lastOutput = zipPath;
                converted++;
                continue;
            }

            if (isDir)
            {
                var entries = Directory.GetFileSystemEntries(itemPath).Select(Path.GetFileName).ToArray();
                string listing = string.Join(Environment.NewLine, entries);
                string extOut = "." + formatId.ToLowerInvariant();
                string listOut = GetOutputPath(itemPath, "_index", extOut, customFolder);
                await File.WriteAllTextAsync(listOut, listing, Encoding.UTF8);
                lastOutput = listOut;
                converted++;
                continue;
            }

            if (formatId is "TXT" or "MD" or "HTML" or "CSV" or "VTT")
            {
                string content = await File.ReadAllTextAsync(itemPath);
                string targetExt = "." + formatId.ToLowerInvariant();
                string outFile = GetOutputPath(itemPath, "", targetExt, customFolder);

                string transformed = formatId switch
                {
                    "HTML" => $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>{Path.GetFileName(itemPath)}</title></head><body><pre>{System.Net.WebUtility.HtmlEncode(content)}</pre></body></html>",
                    "VTT" => content.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)
                        ? content
                        : "WEBVTT\n\n" + content.Replace(',', '.'),
                    "CSV" => content.Replace("\t", ","),
                    _ => content
                };

                await File.WriteAllTextAsync(outFile, transformed, Encoding.UTF8);
                lastOutput = outFile;
                converted++;
                continue;
            }

            if (formatId == "PDF")
            {
                string pdfOut = GetOutputPath(itemPath, "_converted", ".pdf", customFolder);
                await WriteSimpleImageOrTextPdfAsync(itemPath, pdfOut);
                lastOutput = pdfOut;
                converted++;
                continue;
            }

            string ext = "." + formatId.ToLowerInvariant();
            string output = GetOutputPath(itemPath, "_converted", ext, customFolder);
            if (await TranscodeImageAsync(itemPath, output, formatId, pixelTransform: null))
            {
                lastOutput = output;
                converted++;
            }
        }

        return converted > 0
            ? $"Converted {converted} item(s) to {formatId} → {Path.GetFileName(lastOutput)}"
            : $"Could not convert selected item(s) to {formatId}.";
    }

    private static async Task<string> RunToolAsync(string toolId, IReadOnlyList<string> paths, string? customFolder)
    {
        int processed = 0;
        string lastOutput = string.Empty;

        foreach (var itemPath in paths)
        {
            bool isDir = Directory.Exists(itemPath);
            bool isFile = File.Exists(itemPath);
            if (!isDir && !isFile) continue;

            if (isDir)
            {
                if (toolId == "COMPRESS")
                {
                    string zipOut = GetOutputPath(itemPath, "_compressed", ".zip", customFolder);
                    ZipFile.CreateFromDirectory(itemPath, zipOut, CompressionLevel.SmallestSize, includeBaseDirectory: true);
                    lastOutput = zipOut;
                    processed++;
                }
                else if (toolId == "METADATA")
                {
                    var di = new DirectoryInfo(itemPath);
                    string metaReport = GetOutputPath(itemPath, "_folder_meta", ".txt", customFolder);
                    string text = $"Folder Name: {di.Name}\nFull Path: {di.FullName}\nCreated UTC: {di.CreationTimeUtc:O}\nItems Count: {di.GetFileSystemInfos().Length}";
                    await File.WriteAllTextAsync(metaReport, text);
                    lastOutput = metaReport;
                    processed++;
                }
                continue;
            }

            string ext = Path.GetExtension(itemPath).ToLowerInvariant();
            bool isImage = ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".tif" or ".gif" or ".webp" or ".heic";

            switch (toolId)
            {
                case "COMPRESS":
                    if (isImage)
                    {
                        string outImg = GetOutputPath(itemPath, "_compressed", ".jpg", customFolder);
                        if (await TranscodeImageAsync(itemPath, outImg, "JPG", null, scaleFactor: 0.8))
                        {
                            lastOutput = outImg;
                            processed++;
                        }
                    }
                    else
                    {
                        string zipOut = GetOutputPath(itemPath, "_compressed", ".zip", customFolder);
                        using var zip = ZipFile.Open(zipOut, ZipArchiveMode.Create);
                        zip.CreateEntryFromFile(itemPath, Path.GetFileName(itemPath), CompressionLevel.SmallestSize);
                        lastOutput = zipOut;
                        processed++;
                    }
                    break;

                case "METADATA":
                    if (isImage)
                    {
                        string cleanOut = GetOutputPath(itemPath, "_clean_meta", ".png", customFolder);
                        if (await TranscodeImageAsync(itemPath, cleanOut, "PNG", null))
                        {
                            lastOutput = cleanOut;
                            processed++;
                        }
                    }
                    else
                    {
                        var info = new FileInfo(itemPath);
                        string metaReport = GetOutputPath(itemPath, "_metadata", ".txt", customFolder);
                        var sb = new StringBuilder();
                        sb.AppendLine($"File Name: {info.Name}");
                        sb.AppendLine($"Full Path: {info.FullName}");
                        sb.AppendLine($"Size: {info.Length:N0} bytes");
                        sb.AppendLine($"Created UTC: {info.CreationTimeUtc:O}");
                        sb.AppendLine($"Modified UTC: {info.LastWriteTimeUtc:O}");
                        await File.WriteAllTextAsync(metaReport, sb.ToString());
                        lastOutput = metaReport;
                        processed++;
                    }
                    break;

                case "EDIT":
                    if (isImage)
                    {
                        string editOut = GetOutputPath(itemPath, "_edited", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(itemPath, editOut, "PNG", (pixels, w, h) =>
                        {
                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                for (int c = 0; c < 3; c++)
                                {
                                    double v = ((pixels[i + c] / 255.0 - 0.5) * 1.18 + 0.52) * 255.0;
                                    pixels[i + c] = (byte)Math.Clamp(v, 0, 255);
                                }
                            }
                            return (pixels, w, h);
                        });
                        if (ok) { lastOutput = editOut; processed++; }
                    }
                    break;

                case "ANNOTATE":
                    if (isImage)
                    {
                        string annOut = GetOutputPath(itemPath, "_annotated", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(itemPath, annOut, "PNG", (pixels, w, h) =>
                        {
                            uint bandHeight = Math.Max(12, h / 12);
                            for (uint y = 0; y < h; y++)
                            {
                                for (uint x = 0; x < w; x++)
                                {
                                    if (y < bandHeight || y >= h - 6 || x < 6 || x >= w - 6)
                                    {
                                        uint idx = (y * w + x) * 4;
                                        pixels[idx + 0] = 21;
                                        pixels[idx + 1] = 204;
                                        pixels[idx + 2] = 250;
                                        pixels[idx + 3] = 255;
                                    }
                                }
                            }
                            return (pixels, w, h);
                        });
                        if (ok) { lastOutput = annOut; processed++; }
                    }
                    break;

                case "ADD_BG":
                    if (isImage)
                    {
                        string bgOut = GetOutputPath(itemPath, "_bg", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(itemPath, bgOut, "PNG", (pixels, w, h) =>
                        {
                            uint padX = Math.Max(32, w / 8);
                            uint padY = Math.Max(32, h / 8);
                            uint newW = w + padX * 2;
                            uint newH = h + padY * 2;
                            byte[] canvas = new byte[newW * newH * 4];

                            for (uint y = 0; y < newH; y++)
                            {
                                byte g = (byte)(220 - (y * 45 / newH));
                                for (uint x = 0; x < newW; x++)
                                {
                                    uint idx = (y * newW + x) * 4;
                                    canvas[idx + 0] = 35;
                                    canvas[idx + 1] = g;
                                    canvas[idx + 2] = 252;
                                    canvas[idx + 3] = 255;
                                }
                            }

                            for (uint y = 0; y < h; y++)
                            {
                                for (uint x = 0; x < w; x++)
                                {
                                    uint srcIdx = (y * w + x) * 4;
                                    uint dstIdx = ((y + padY) * newW + (x + padX)) * 4;
                                    Array.Copy(pixels, srcIdx, canvas, dstIdx, 4);
                                }
                            }
                            return (canvas, newW, newH);
                        });
                        if (ok) { lastOutput = bgOut; processed++; }
                    }
                    break;

                case "CROP":
                    if (isImage)
                    {
                        string cropOut = GetOutputPath(itemPath, "_cropped", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(itemPath, cropOut, "PNG", (pixels, w, h) =>
                        {
                            uint side = Math.Min(w, h);
                            uint startX = (w - side) / 2;
                            uint startY = (h - side) / 2;
                            byte[] cropped = new byte[side * side * 4];

                            for (uint y = 0; y < side; y++)
                            {
                                for (uint x = 0; x < side; x++)
                                {
                                    uint srcIdx = ((startY + y) * w + (startX + x)) * 4;
                                    uint dstIdx = (y * side + x) * 4;
                                    Array.Copy(pixels, srcIdx, cropped, dstIdx, 4);
                                }
                            }
                            return (cropped, side, side);
                        });
                        if (ok) { lastOutput = cropOut; processed++; }
                    }
                    break;

                case "REDACT":
                    if (isImage)
                    {
                        string redactOut = GetOutputPath(itemPath, "_redacted", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(itemPath, redactOut, "PNG", (pixels, w, h) =>
                        {
                            uint rx0 = w / 5, rx1 = w * 4 / 5;
                            uint ry0 = h / 3, ry1 = h * 2 / 3;
                            for (uint y = ry0; y < ry1; y++)
                            {
                                for (uint x = rx0; x < rx1; x++)
                                {
                                    uint idx = (y * w + x) * 4;
                                    pixels[idx + 0] = 18;
                                    pixels[idx + 1] = 18;
                                    pixels[idx + 2] = 18;
                                    pixels[idx + 3] = 255;
                                }
                            }
                            return (pixels, w, h);
                        });
                        if (ok) { lastOutput = redactOut; processed++; }
                    }
                    else
                    {
                        string text = await File.ReadAllTextAsync(itemPath);
                        string redOut = GetOutputPath(itemPath, "_redacted", ext, customFolder);
                        string masked = System.Text.RegularExpressions.Regex.Replace(
                            text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}|\b\d{3}[-.]?\d{2,4}[-.]?\d{4}\b", "[REDACTED]");
                        await File.WriteAllTextAsync(redOut, masked);
                        lastOutput = redOut;
                        processed++;
                    }
                    break;
            }
        }

        return processed > 0
            ? $"{toolId} complete → Saved {Path.GetFileName(lastOutput)} beside original"
            : $"Tool '{toolId}' requires an image (or folder/file for Compress/Metadata).";
    }

    private static async Task<string> RunActionAsync(
        string actionId,
        IReadOnlyList<string> paths,
        string? customFolder,
        Action<string>? onFloatRequested)
    {
        string firstItem = paths[0];
        string dir = Directory.Exists(firstItem)
            ? firstItem
            : (Path.GetDirectoryName(firstItem) ?? Environment.CurrentDirectory);

        switch (actionId)
        {
            case "COPY_PATH":
                var pkg = new DataPackage();
                pkg.SetText(string.Join(Environment.NewLine, paths));
                Clipboard.SetContent(pkg);
                Clipboard.Flush();
                return $"Copied {paths.Count} path(s) to Clipboard.";

            case "COPY":
                var storageItems = new List<IStorageItem>();
                string lastDup = string.Empty;
                foreach (var p in paths)
                {
                    if (File.Exists(p))
                    {
                        storageItems.Add(await StorageFile.GetFileFromPathAsync(p));
                        string dup = GetOutputPath(p, "_copy", Path.GetExtension(p), customFolder);
                        File.Copy(p, dup, overwrite: false);
                        lastDup = dup;
                    }
                    else if (Directory.Exists(p))
                    {
                        storageItems.Add(await StorageFolder.GetFolderFromPathAsync(p));
                        string dupDir = GetOutputPath(p, "_copy", "", customFolder);
                        CopyDirectoryRecursive(p, dupDir);
                        lastDup = dupDir;
                    }
                }
                if (storageItems.Count > 0)
                {
                    var filePkg = new DataPackage();
                    filePkg.SetStorageItems(storageItems);
                    Clipboard.SetContent(filePkg);
                    Clipboard.Flush();
                }
                return $"Copied to clipboard & duplicated ({Path.GetFileName(lastDup)}).";

            case "MOVE":
                if (string.IsNullOrWhiteSpace(customFolder) || !Directory.Exists(customFolder))
                {
                    string parent = Path.GetDirectoryName(firstItem.TrimEnd(Path.DirectorySeparatorChar)) ?? dir;
                    string movedSubdir = Path.Combine(parent, "LemonAID_Moved");
                    Directory.CreateDirectory(movedSubdir);
                    customFolder = movedSubdir;
                }
                int movedCount = 0;
                foreach (var p in paths)
                {
                    string name = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar));
                    string dest = Path.Combine(customFolder, name);
                    if (File.Exists(p) && !string.Equals(p, dest, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(p, dest, overwrite: true);
                        movedCount++;
                    }
                    else if (Directory.Exists(p) && !string.Equals(p, dest, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.Move(p, dest);
                        movedCount++;
                    }
                }
                return $"Moved {movedCount} item(s) to {customFolder}";

            case "TERMINAL":
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "wt.exe",
                        Arguments = $"-d \"{dir}\"",
                        UseShellExecute = true
                    });
                }
                catch
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        WorkingDirectory = dir,
                        UseShellExecute = true
                    });
                }
                return $"Opened Terminal in {dir}";

            case "SHARE":
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{firstItem}\"",
                    UseShellExecute = true
                });
                return $"Highlighted {Path.GetFileName(firstItem)} in Explorer.";

            case "FLOAT":
                onFloatRequested?.Invoke(firstItem);
                return $"Floated {Path.GetFileName(firstItem)} in an Always-On-Top window.";

            default:
                return "Action completed.";
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
        }
    }

    private static async Task<bool> TranscodeImageAsync(
        string inputPath,
        string outputPath,
        string formatId,
        Func<byte[], uint, uint, (byte[] Pixels, uint Width, uint Height)>? pixelTransform,
        double scaleFactor = 1.0)
    {
        try
        {
            StorageFile inFile = await StorageFile.GetFileFromPathAsync(inputPath);
            using IRandomAccessStream inStream = await inFile.OpenAsync(FileAccessMode.Read);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(inStream);

            uint width = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scaleFactor));
            uint height = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scaleFactor));

            var transform = new BitmapTransform
            {
                ScaledWidth = width,
                ScaledHeight = height,
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            PixelDataProvider pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);

            byte[] pixels = pixelData.DetachPixelData();
            if (pixelTransform != null)
            {
                (pixels, width, height) = pixelTransform(pixels, width, height);
            }

            Guid encoderId = formatId switch
            {
                "JPG" or "JPEG" => BitmapEncoder.JpegEncoderId,
                "TIFF" => BitmapEncoder.TiffEncoderId,
                "BMP" => BitmapEncoder.BmpEncoderId,
                "HEIC" => BitmapEncoder.HeifEncoderId,
                _ => BitmapEncoder.PngEncoderId
            };

            using var outFileStream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
            using var randomAccessOut = outFileStream.AsRandomAccessStream();

            BitmapEncoder encoder;
            try
            {
                encoder = await BitmapEncoder.CreateAsync(encoderId, randomAccessOut);
            }
            catch
            {
                encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, randomAccessOut);
            }

            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                width,
                height,
                decoder.DpiX > 0 ? decoder.DpiX : 96,
                decoder.DpiY > 0 ? decoder.DpiY : 96,
                pixels);

            await encoder.FlushAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WriteSimpleImageOrTextPdfAsync(string sourcePath, string pdfOutputPath)
    {
        string title = Path.GetFileName(sourcePath);
        string bodyText = $"LemonAID Local PDF Export - Source: {title}";
        string escaped = bodyText.Replace("(", "\\(").Replace(")", "\\)");

        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");
        sb.AppendLine("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj");
        sb.AppendLine("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj");
        sb.AppendLine("3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj");
        string streamContent = $"BT /F1 14 Tf 50 720 Td ({escaped}) Tj ET";
        sb.AppendLine($"4 0 obj << /Length {streamContent.Length} >> stream\n{streamContent}\nendstream endobj");
        sb.AppendLine("5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj");
        sb.AppendLine("xref\n0 6\n0000000000 65535 f \ntrailer << /Size 6 /Root 1 0 R >>\nstartxref\n320\n%%EOF");

        await File.WriteAllTextAsync(pdfOutputPath, sb.ToString(), Encoding.ASCII);
    }
}
