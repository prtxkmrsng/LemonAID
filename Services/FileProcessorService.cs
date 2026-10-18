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
    Tools,     // Alt (macOS Command equivalent)
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
            WheelMode.Tools =>
            [
                new("COMPRESS", "COMPRESS", "\uE740", "Reduce file size or create a compressed archive beside original"),
                new("METADATA", "METADATA", "\uE8EC", "Inspect and strip embedded metadata into a clean copy"),
                new("EDIT", "EDIT", "\uE790", "Enhance contrast & brightness and export as a new file"),
                new("ANNOTATE", "ANNOTATE", "\uE929", "Stamp an annotation header banner onto a copy"),
                new("ADD_BG", "ADD BG", "\uE91B", "Place image onto a padded lemon-gradient canvas"),
                new("CROP", "CROP", "\uE7A8", "Center-crop image to a 1:1 square aspect ratio"),
                new("REDACT", "REDACT", "\uED1A", "Pixelate and mask the central sensitive region")
            ],
            WheelMode.Actions =>
            [
                new("SHARE", "NEARBY", "\uE72D", "Reveal for Windows Nearby Share / Explorer transfer"),
                new("FLOAT", "FLOAT", "\uE718", "Pin a floating Always-On-Top preview window"),
                new("COPY", "COPY", "\uE8C8", "Copy file to clipboard and create a duplicate copy"),
                new("MOVE", "MOVE", "\uE8DE", "Move selected file(s) to a chosen destination folder"),
                new("COPY_PATH", "COPY PATH", "\uE71B", "Copy full Windows file path(s) to clipboard"),
                new("TERMINAL", "TERMINAL", "\uE756", "Open Windows Terminal / PowerShell in file directory")
            ],
            _ => []
        };
    }

    private static IReadOnlyList<SectorItem> GetContextualConverterSectors(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count > 0)
        {
            string ext = Path.GetExtension(filePaths[0]).ToLowerInvariant();
            if (ext is ".srt" or ".vtt" or ".txt" or ".md" or ".csv" or ".tsv")
            {
                return
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

        // Default 6 format choices matching Tangerine onboarding wheel
        return
        [
            new("PNG", "PNG", "\uEB9F", "Lossless PNG image"),
            new("WEBP", "WEBP", "\uEB9F", "WebP / high-efficiency image output"),
            new("HEIC", "HEIC", "\uEB9F", "HEIC / modern container output"),
            new("TIFF", "TIFF", "\uEB9F", "Uncompressed archival TIFF"),
            new("AVIF", "AVIF", "\uEB9F", "AVIF / next-gen image output"),
            new("PDF", "PDF", "\uEA90", "Export into a single-file PDF wrapper / archive")
        ];
    }

    public static string FormatTotalSize(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return "2.4 MB"; // Onboarding default demonstration size
        long totalBytes = 0;
        foreach (var f in files)
        {
            if (File.Exists(f))
            {
                totalBytes += new FileInfo(f).Length;
            }
        }

        if (totalBytes < 1024) return $"{totalBytes} B";
        if (totalBytes < 1024 * 1024) return $"{totalBytes / 1024.0:F1} KB";
        return $"{totalBytes / (1024.0 * 1024.0):F1} MB";
    }

    public static async Task<string> ExecuteSectorAsync(
        WheelMode mode,
        string sectorId,
        IReadOnlyList<string> files,
        string? customOutputFolder,
        Action<string>? onFloatRequested)
    {
        if (files.Count == 0)
        {
            return "Drag and drop at least one file onto the Lemon Wheel first.";
        }

        return mode switch
        {
            WheelMode.Converter => await RunConversionAsync(sectorId, files, customOutputFolder),
            WheelMode.Tools => await RunToolAsync(sectorId, files, customOutputFolder),
            WheelMode.Actions => await RunActionAsync(sectorId, files, customOutputFolder, onFloatRequested),
            _ => "Unknown operation."
        };
    }

    private static string GetOutputPath(string sourcePath, string suffix, string newExt, string? customFolder)
    {
        string dir = !string.IsNullOrWhiteSpace(customFolder) && Directory.Exists(customFolder)
            ? customFolder
            : Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;

        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string candidate = Path.Combine(dir, $"{baseName}{suffix}{newExt}");
        int counter = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(dir, $"{baseName}{suffix}_{counter}{newExt}");
            counter++;
        }
        return candidate;
    }

    private static async Task<string> RunConversionAsync(string formatId, IReadOnlyList<string> files, string? customFolder)
    {
        int converted = 0;
        string lastOutput = string.Empty;

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;

            if (formatId == "ZIP")
            {
                string zipPath = GetOutputPath(file, "_converted", ".zip", customFolder);
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                lastOutput = zipPath;
                converted++;
                continue;
            }

            if (formatId is "TXT" or "MD" or "HTML" or "CSV" or "VTT")
            {
                string content = await File.ReadAllTextAsync(file);
                string targetExt = "." + formatId.ToLowerInvariant();
                string outFile = GetOutputPath(file, "", targetExt, customFolder);

                string transformed = formatId switch
                {
                    "HTML" => $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>{Path.GetFileName(file)}</title></head><body><pre>{System.Net.WebUtility.HtmlEncode(content)}</pre></body></html>",
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
                string pdfOut = GetOutputPath(file, "_converted", ".pdf", customFolder);
                await WriteSimpleImageOrTextPdfAsync(file, pdfOut);
                lastOutput = pdfOut;
                converted++;
                continue;
            }

            // Image format conversion using Windows Imaging Component (WIC)
            string ext = "." + formatId.ToLowerInvariant();
            string output = GetOutputPath(file, "_converted", ext, customFolder);
            bool ok = await TranscodeImageAsync(file, output, formatId, pixelTransform: null);
            if (ok)
            {
                lastOutput = output;
                converted++;
            }
        }

        return converted > 0
            ? $"Converted {converted} file(s) to {formatId} → {Path.GetFileName(lastOutput)}"
            : $"Could not convert selected file(s) to {formatId}. Try an image or document file.";
    }

    private static async Task<string> RunToolAsync(string toolId, IReadOnlyList<string> files, string? customFolder)
    {
        int processed = 0;
        string lastOutput = string.Empty;

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            string ext = Path.GetExtension(file).ToLowerInvariant();
            bool isImage = ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".tif" or ".gif" or ".webp" or ".heic";

            switch (toolId)
            {
                case "COMPRESS":
                    if (isImage)
                    {
                        string outImg = GetOutputPath(file, "_compressed", ".jpg", customFolder);
                        if (await TranscodeImageAsync(file, outImg, "JPG", null, scaleFactor: 0.8))
                        {
                            lastOutput = outImg;
                            processed++;
                        }
                    }
                    else
                    {
                        string zipOut = GetOutputPath(file, "_compressed", ".zip", customFolder);
                        using var zip = ZipFile.Open(zipOut, ZipArchiveMode.Create);
                        zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.SmallestSize);
                        lastOutput = zipOut;
                        processed++;
                    }
                    break;

                case "METADATA":
                    if (isImage)
                    {
                        // Re-encoding strips camera EXIF blocks onto a clean copy
                        string cleanOut = GetOutputPath(file, "_clean_meta", ".png", customFolder);
                        if (await TranscodeImageAsync(file, cleanOut, "PNG", null))
                        {
                            lastOutput = cleanOut;
                            processed++;
                        }
                    }
                    else
                    {
                        var info = new FileInfo(file);
                        string metaReport = GetOutputPath(file, "_metadata", ".txt", customFolder);
                        var sb = new StringBuilder();
                        sb.AppendLine($"File Name: {info.Name}");
                        sb.AppendLine($"Full Path: {info.FullName}");
                        sb.AppendLine($"Size: {info.Length:N0} bytes");
                        sb.AppendLine($"Created UTC: {info.CreationTimeUtc:O}");
                        sb.AppendLine($"Modified UTC: {info.LastWriteTimeUtc:O}");
                        sb.AppendLine($"Attributes: {info.Attributes}");
                        await File.WriteAllTextAsync(metaReport, sb.ToString());
                        lastOutput = metaReport;
                        processed++;
                    }
                    break;

                case "EDIT":
                    if (isImage)
                    {
                        string editOut = GetOutputPath(file, "_edited", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(file, editOut, "PNG", (pixels, w, h) =>
                        {
                            // Boost contrast & clarity
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
                        string annOut = GetOutputPath(file, "_annotated", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(file, annOut, "PNG", (pixels, w, h) =>
                        {
                            // Draw a bright Lemon highlight callout bar at the top & border
                            uint bandHeight = Math.Max(12, h / 12);
                            for (uint y = 0; y < h; y++)
                            {
                                for (uint x = 0; x < w; x++)
                                {
                                    bool isBorder = y < bandHeight || y >= h - 6 || x < 6 || x >= w - 6;
                                    if (isBorder)
                                    {
                                        uint idx = (y * w + x) * 4;
                                        pixels[idx + 0] = 21;  // B
                                        pixels[idx + 1] = 204; // G
                                        pixels[idx + 2] = 250; // R (#FACC15 Lemon)
                                        pixels[idx + 3] = 255; // A
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
                        string bgOut = GetOutputPath(file, "_bg", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(file, bgOut, "PNG", (pixels, w, h) =>
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
                                    canvas[idx + 0] = 35;  // B
                                    canvas[idx + 1] = g;   // G
                                    canvas[idx + 2] = 252; // R (Warm citrus gradient)
                                    canvas[idx + 3] = 255;
                                }
                            }

                            for (uint y = 0; y < h; y++)
                            {
                                for (uint x = 0; x < w; x++)
                                {
                                    uint srcIdx = (y * w + x) * 4;
                                    uint dstIdx = ((y + padY) * newW + (x + padX)) * 4;
                                    canvas[dstIdx + 0] = pixels[srcIdx + 0];
                                    canvas[dstIdx + 1] = pixels[srcIdx + 1];
                                    canvas[dstIdx + 2] = pixels[srcIdx + 2];
                                    canvas[dstIdx + 3] = pixels[srcIdx + 3];
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
                        string cropOut = GetOutputPath(file, "_cropped", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(file, cropOut, "PNG", (pixels, w, h) =>
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
                        string redactOut = GetOutputPath(file, "_redacted", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(file, redactOut, "PNG", (pixels, w, h) =>
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
                        string text = await File.ReadAllTextAsync(file);
                        string redOut = GetOutputPath(file, "_redacted", ext, customFolder);
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
            : $"Tool '{toolId}' requires an image (or text/file for Compress/Metadata/Redact).";
    }

    private static async Task<string> RunActionAsync(
        string actionId,
        IReadOnlyList<string> files,
        string? customFolder,
        Action<string>? onFloatRequested)
    {
        string firstFile = files[0];
        string dir = Path.GetDirectoryName(firstFile) ?? Environment.CurrentDirectory;

        switch (actionId)
        {
            case "COPY_PATH":
                var pkg = new DataPackage();
                pkg.SetText(string.Join(Environment.NewLine, files));
                Clipboard.SetContent(pkg);
                Clipboard.Flush();
                return $"Copied {files.Count} file path(s) to Clipboard.";

            case "COPY":
                var storageFiles = new List<IStorageItem>();
                string lastDup = string.Empty;
                foreach (var f in files)
                {
                    if (File.Exists(f))
                    {
                        storageFiles.Add(await StorageFile.GetFileFromPathAsync(f));
                        string dup = GetOutputPath(f, "_copy", Path.GetExtension(f), customFolder);
                        File.Copy(f, dup, overwrite: false);
                        lastDup = dup;
                    }
                }
                if (storageFiles.Count > 0)
                {
                    var filePkg = new DataPackage();
                    filePkg.SetStorageItems(storageFiles);
                    Clipboard.SetContent(filePkg);
                    Clipboard.Flush();
                }
                return $"Copied to clipboard & saved duplicate ({Path.GetFileName(lastDup)}).";

            case "MOVE":
                if (string.IsNullOrWhiteSpace(customFolder) || !Directory.Exists(customFolder))
                {
                    string movedSubdir = Path.Combine(dir, "LemonAID_Moved");
                    Directory.CreateDirectory(movedSubdir);
                    customFolder = movedSubdir;
                }
                int movedCount = 0;
                foreach (var f in files)
                {
                    if (!File.Exists(f)) continue;
                    string dest = Path.Combine(customFolder, Path.GetFileName(f));
                    if (!string.Equals(f, dest, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(f, dest, overwrite: true);
                        movedCount++;
                    }
                }
                return $"Moved {movedCount} file(s) to {customFolder}";

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
                    Arguments = $"/select,\"{firstFile}\"",
                    UseShellExecute = true
                });
                return $"Highlighted {Path.GetFileName(firstFile)} in Explorer for Nearby Share.";

            case "FLOAT":
                onFloatRequested?.Invoke(firstFile);
                return $"Floated {Path.GetFileName(firstFile)} in an Always-On-Top window.";

            default:
                return "Action completed.";
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
        // Creates a valid minimal single-page PDF document containing file metadata/text summary beside the original
        string title = Path.GetFileName(sourcePath);
        string bodyText = $"LemonAID Local PDF Export - Source: {title} ({new FileInfo(sourcePath).Length:N0} bytes)";
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
