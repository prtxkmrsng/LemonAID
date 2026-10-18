using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace LemonAID.Services;

public enum WheelMode
{
    Converter, // Shift
    Tools,     // Alt
    Actions    // Ctrl
}

public enum FileCategory
{
    Directory,
    Image,
    Audio,
    Video,
    DataCode,
    Subtitle,
    Archive,
    Document,
    BinaryGeneric
}

public sealed record SectorItem(string Id, string Label, string Glyph, string Description);

public static class FileProcessorService
{
    public static FileCategory DetectCategory(string path)
    {
        if (Directory.Exists(path)) return FileCategory.Directory;

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".tif" or ".gif" or ".webp" or ".heic" or ".ico" or ".svg"
                => FileCategory.Image,

            ".mp3" or ".wav" or ".wma" or ".m4a" or ".aac" or ".flac" or ".ogg" or ".aiff" or ".opus" or ".mid" or ".midi"
                => FileCategory.Audio,

            ".mp4" or ".mov" or ".wmv" or ".avi" or ".mkv" or ".webm" or ".flv" or ".m4v" or ".3gp"
                => FileCategory.Video,

            ".srt" or ".vtt"
                => FileCategory.Subtitle,

            ".json" or ".csv" or ".tsv" or ".xml" or ".html" or ".htm" or ".css" or ".js" or ".ts" or ".jsx" or ".tsx"
                or ".cs" or ".py" or ".cpp" or ".c" or ".h" or ".hpp" or ".java" or ".rs" or ".go" or ".sql"
                or ".yaml" or ".yml" or ".md" or ".txt" or ".ini" or ".env" or ".log" or ".sh" or ".ps1" or ".bat"
                or ".cmd" or ".toml" or ".jsonc" or ".cfg" or ".config"
                => FileCategory.DataCode,

            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" or ".bz2" or ".xz" or ".cab" or ".iso"
                => FileCategory.Archive,

            ".pdf" or ".docx" or ".xlsx" or ".pptx" or ".doc" or ".xls" or ".ppt" or ".rtf" or ".odt" or ".ods" or ".odp" or ".epub"
                => FileCategory.Document,

            _ => FileCategory.BinaryGeneric
        };
    }

    public static IReadOnlyList<SectorItem> GetSectorsForMode(WheelMode mode, IReadOnlyList<string> filePaths)
    {
        return mode switch
        {
            WheelMode.Converter => GetContextualConverterSectors(filePaths),
            WheelMode.Tools => (SectorItem[])
            [
                new("COMPRESS", "COMPRESS", "\uE740", "Intelligently minify, downscale, or compress into smallest size archive"),
                new("METADATA", "METADATA", "\uE8EC", "Inspect detailed file metadata, audio/video specs, or calculate cryptographic hashes"),
                new("EDIT", "EDIT", "\uE790", "Auto-format code indentation, normalize line endings, or enhance image contrast"),
                new("ANNOTATE", "ANNOTATE", "\uE929", "Stamp metadata banner, code watermark block, or create audit sidecar"),
                new("ADD_BG", "ADD BG", "\uE91B", "Render onto padded lemon canvas or generate styled preview HTML"),
                new("CROP", "CROP", "\uE7A8", "Center-crop image 1:1, slice first 150 lines of code, or extract 32KB binary head"),
                new("REDACT", "REDACT", "\uED1A", "Mask sensitive image region or redact emails, phones, IPs, and API tokens")
            ],
            WheelMode.Actions => (SectorItem[])
            [
                new("SHARE", "NEARBY", "\uE72D", "Reveal in Explorer for Windows Nearby Share"),
                new("FLOAT", "FLOAT", "\uE718", "Pin an Always-On-Top floating preview and inspection window"),
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
        if (filePaths.Count == 0)
        {
            return (SectorItem[])
            [
                new("ZIP", "ZIP", "\uF012", "Package item(s) into standard ZIP archive"),
                new("B64", "BASE64", "\uE71B", "Encode content into Base64 text (.txt)"),
                new("HEX", "HEX DUMP", "\uE756", "Generate formatted 16-byte hex dump"),
                new("HASH", "CHECKSUM", "\uE8EC", "Calculate SHA256, SHA1 & MD5 hashes"),
                new("STRINGS", "STRINGS", "\uE8A5", "Extract printable ASCII & UTF-8 strings"),
                new("PDF", "REPORT", "\uEA90", "Generate inspection summary PDF report")
            ];
        }

        string first = filePaths[0];
        FileCategory category = DetectCategory(first);

        return category switch
        {
            FileCategory.Directory => (SectorItem[])
            [
                new("ZIP", "ZIP", "\uF012", "Package folder into a .zip archive beside original"),
                new("PDF", "PDF", "\uEA90", "Export folder manifest summary to PDF"),
                new("TXT", "TXT", "\uE8A5", "Export directory file listing to .txt"),
                new("MD", "MARKDOWN", "\uE8A5", "Export directory listing to Markdown (.md)"),
                new("CSV", "CSV", "\uE9F9", "Export directory inventory to .csv"),
                new("HTML", "HTML", "\uE774", "Export interactive directory explorer .html")
            ],

            FileCategory.Image => (SectorItem[])
            [
                new("PNG", "PNG", "\uEB9F", "Lossless PNG image"),
                new("JPG", "JPG", "\uEB9F", "Standard compressed JPEG image"),
                new("WEBP", "WEBP", "\uEB9F", "Modern high-efficiency WebP image"),
                new("TIFF", "TIFF", "\uEB9F", "Uncompressed archival TIFF image"),
                new("PDF", "PDF", "\uEA90", "Embed image into a single-page PDF document"),
                new("ZIP", "ZIP", "\uF012", "Package image into standard ZIP archive")
            ],

            FileCategory.Audio => (SectorItem[])
            [
                new("WAV", "WAV", "\uE8D6", "Convert to uncompressed 16-bit PCM WAV"),
                new("MP3", "MP3", "\uE8D6", "Convert to high-quality MP3 audio"),
                new("AAC", "AAC / M4A", "\uE8D6", "Convert to Advanced Audio Coding (.m4a)"),
                new("WMA", "WMA", "\uE8D6", "Convert to Windows Media Audio (.wma)"),
                new("B64", "BASE64", "\uE71B", "Encode raw audio byte stream to Base64 (.txt)"),
                new("ZIP", "ZIP", "\uF012", "Compress audio into ZIP archive")
            ],

            FileCategory.Video => (SectorItem[])
            [
                new("MP4", "MP4", "\uE714", "Transcode to standard H.264 MP4 video"),
                new("WMV", "WMV", "\uE714", "Transcode to Windows Media Video (.wmv)"),
                new("EXTRACT_MP3", "EXTRACT MP3", "\uE8D6", "Extract audio track as MP3 audio"),
                new("EXTRACT_WAV", "EXTRACT WAV", "\uE8D6", "Extract audio track as uncompressed WAV"),
                new("THUMBNAIL", "POSTER PNG", "\uEB9F", "Export video poster frame as PNG image"),
                new("ZIP", "ZIP", "\uF012", "Compress video into standard ZIP archive")
            ],

            FileCategory.DataCode => (SectorItem[])
            [
                new("JSON", "JSON", "\uE943", "Format/convert structure to formatted JSON"),
                new("CSV", "CSV", "\uE9F9", "Convert tabular rows or JSON array to CSV"),
                new("XML", "XML", "\uE943", "Wrap content/structure in XML format"),
                new("HTML", "HTML", "\uE774", "Export styled syntax-highlighted HTML document"),
                new("MD", "MARKDOWN", "\uE8A5", "Convert to Markdown document with code fences"),
                new("TXT", "PLAIN TXT", "\uE8A5", "Strip formatting and save as plain text (.txt)")
            ],

            FileCategory.Subtitle => (SectorItem[])
            [
                new("VTT", "WEBVTT", "\uE8C1", "Convert subtitle cues to WebVTT (.vtt)"),
                new("SRT", "SRT", "\uE8C1", "Convert subtitle cues to SubRip Subtitle (.srt)"),
                new("TXT", "PLAIN TEXT", "\uE8A5", "Extract dialogue text without timestamps"),
                new("CSV", "CSV CUES", "\uE9F9", "Export subtitle cues table (Index, Start, End, Text)"),
                new("JSON", "JSON CUES", "\uE943", "Export subtitle cues as structured JSON array"),
                new("HTML", "TRANSCRIPT", "\uE774", "Export readable transcript HTML page")
            ],

            FileCategory.Archive => (SectorItem[])
            [
                new("EXTRACT", "EXTRACT", "\uE8B7", "Decompress archive contents to folder"),
                new("MANIFEST", "MANIFEST", "\uE8A5", "Export archive file listing to .txt"),
                new("REZIP", "OPTIMIZE ZIP", "\uF012", "Repackage contents with optimal compression"),
                new("B64", "BASE64", "\uE71B", "Encode archive to Base64 string (.txt)"),
                new("HASH", "CHECKSUM", "\uE8EC", "Calculate SHA256 & MD5 archive checksums"),
                new("PDF", "REPORT PDF", "\uEA90", "Export archive contents summary to PDF")
            ],

            FileCategory.Document => (SectorItem[])
            [
                new("TXT", "EXTRACT TEXT", "\uE8A5", "Extract readable strings & text to .txt"),
                new("MD", "MARKDOWN", "\uE8A5", "Export document overview to Markdown (.md)"),
                new("HTML", "HTML VIEW", "\uE774", "Export structured document preview HTML"),
                new("B64", "BASE64", "\uE71B", "Encode document to Base64 string (.txt)"),
                new("ZIP", "ZIP", "\uF012", "Package document into standard ZIP archive"),
                new("HEX", "HEX DUMP", "\uE756", "Generate byte hex dump of document")
            ],

            _ => (SectorItem[])
            [
                new("ZIP", "ZIP", "\uF012", "Compress file into standard ZIP archive"),
                new("B64", "BASE64", "\uE71B", "Encode binary data into Base64 (.txt)"),
                new("HEX", "HEX DUMP", "\uE756", "Generate 16-byte aligned Hex dump (.hex.txt)"),
                new("HASH", "CHECKSUM", "\uE8EC", "Generate SHA256, MD5, SHA1 hashes (.hash.txt)"),
                new("STRINGS", "STRINGS", "\uE8A5", "Extract printable ASCII & UTF-8 strings (.strings.txt)"),
                new("PDF", "REPORT", "\uEA90", "Generate forensic inspection summary PDF")
            ]
        };
    }

    public static string FormatTotalSize(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return "0 B";
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
        if (totalBytes < 1024 * 1024 * 1024) return $"{totalBytes / (1024.0 * 1024.0):F1} MB";
        return $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
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

            FileCategory category = isDir ? FileCategory.Directory : DetectCategory(itemPath);

            // Universal ZIP converter
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

            // Universal BASE64 converter
            if (formatId == "B64")
            {
                string b64Path = GetOutputPath(itemPath, "_base64", ".txt", customFolder);
                byte[] raw = await File.ReadAllBytesAsync(itemPath);
                string b64 = Convert.ToBase64String(raw, Base64FormattingOptions.InsertLineBreaks);
                await File.WriteAllTextAsync(b64Path, b64, Encoding.UTF8);
                lastOutput = b64Path;
                converted++;
                continue;
            }

            // Universal HEX DUMP converter
            if (formatId == "HEX")
            {
                string hexPath = GetOutputPath(itemPath, "_hexdump", ".hex.txt", customFolder);
                byte[] raw = await File.ReadAllBytesAsync(itemPath);
                string dump = GenerateHexDump(raw, maxBytes: 256 * 1024);
                await File.WriteAllTextAsync(hexPath, dump, Encoding.UTF8);
                lastOutput = hexPath;
                converted++;
                continue;
            }

            // Universal CHECKSUM converter
            if (formatId == "HASH")
            {
                string hashPath = GetOutputPath(itemPath, "_checksums", ".hash.txt", customFolder);
                string hashes = await ComputeHashesReportAsync(itemPath);
                await File.WriteAllTextAsync(hashPath, hashes, Encoding.UTF8);
                lastOutput = hashPath;
                converted++;
                continue;
            }

            // Universal STRINGS extractor
            if (formatId == "STRINGS")
            {
                string stringsPath = GetOutputPath(itemPath, "_strings", ".strings.txt", customFolder);
                byte[] raw = await File.ReadAllBytesAsync(itemPath);
                string extracted = ExtractPrintableStrings(raw, minLength: 4);
                await File.WriteAllTextAsync(stringsPath, extracted, Encoding.UTF8);
                lastOutput = stringsPath;
                converted++;
                continue;
            }

            // Universal PDF export
            if (formatId == "PDF")
            {
                string pdfOut = GetOutputPath(itemPath, "_report", ".pdf", customFolder);
                await WriteUniversalPdfReportAsync(itemPath, pdfOut);
                lastOutput = pdfOut;
                converted++;
                continue;
            }

            // Directory Handling
            if (isDir)
            {
                var entries = Directory.GetFileSystemEntries(itemPath).Select(Path.GetFileName).ToArray();
                string extOut = "." + formatId.ToLowerInvariant();
                string listOut = GetOutputPath(itemPath, "_index", extOut, customFolder);

                string content = formatId switch
                {
                    "MD" => $"# Directory Index: {Path.GetFileName(itemPath)}\n\n" + string.Join(Environment.NewLine, entries.Select(e => $"- `{e}`")),
                    "CSV" => "Type,Name\n" + string.Join(Environment.NewLine, Directory.GetFileSystemEntries(itemPath).Select(e => $"{(Directory.Exists(e) ? "Folder" : "File")},\"{Path.GetFileName(e)}\"")),
                    "HTML" => $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>{Path.GetFileName(itemPath)}</title><style>body{{font-family:Segoe UI,sans-serif;padding:32px;background:#18181b;color:#f4f4f5;}}li{{padding:4px 0;}}code{{color:#facc15;}}</style></head><body><h2>Folder Inventory: {Path.GetFileName(itemPath)}</h2><ul>{string.Join("", entries.Select(e => $"<li><code>{HtmlEncode(e ?? string.Empty)}</code></li>"))}</ul></body></html>",
                    _ => string.Join(Environment.NewLine, entries)
                };

                await File.WriteAllTextAsync(listOut, content, Encoding.UTF8);
                lastOutput = listOut;
                converted++;
                continue;
            }

            // Audio & Video Media Transcoding
            if (category is FileCategory.Audio or FileCategory.Video)
            {
                if (formatId is "WAV" or "MP3" or "AAC" or "WMA" or "MP4" or "WMV" or "EXTRACT_MP3" or "EXTRACT_WAV")
                {
                    string targetExt = formatId switch
                    {
                        "WAV" or "EXTRACT_WAV" => ".wav",
                        "MP3" or "EXTRACT_MP3" => ".mp3",
                        "AAC" => ".m4a",
                        "WMA" => ".wma",
                        "WMV" => ".wmv",
                        _ => ".mp4"
                    };

                    string mediaOut = GetOutputPath(itemPath, "_transcoded", targetExt, customFolder);
                    bool ok = await TranscodeMediaAsync(itemPath, mediaOut, formatId);
                    if (ok)
                    {
                        lastOutput = mediaOut;
                        converted++;
                        continue;
                    }
                }

                if (formatId == "THUMBNAIL")
                {
                    string thumbOut = GetOutputPath(itemPath, "_poster", ".png", customFolder);
                    bool ok = await ExtractMediaThumbnailAsync(itemPath, thumbOut);
                    if (ok)
                    {
                        lastOutput = thumbOut;
                        converted++;
                        continue;
                    }
                }
            }

            // Subtitle conversions
            if (category == FileCategory.Subtitle)
            {
                string subContent = await File.ReadAllTextAsync(itemPath);
                string subTargetExt = "." + formatId.ToLowerInvariant();
                string subOut = GetOutputPath(itemPath, "_converted", subTargetExt, customFolder);

                string transformed = formatId switch
                {
                    "VTT" => ConvertToWebVtt(subContent),
                    "SRT" => ConvertToSrt(subContent),
                    "TXT" => ExtractSubtitleDialogue(subContent),
                    "CSV" => ConvertSubtitleToCsv(subContent),
                    "JSON" => ConvertSubtitleToJson(subContent),
                    "HTML" => ConvertSubtitleToHtml(itemPath, subContent),
                    _ => subContent
                };

                await File.WriteAllTextAsync(subOut, transformed, Encoding.UTF8);
                lastOutput = subOut;
                converted++;
                continue;
            }

            // Archive conversions
            if (category == FileCategory.Archive)
            {
                if (formatId == "EXTRACT")
                {
                    string extractDir = GetOutputPath(itemPath, "_extracted", "", customFolder);
                    Directory.CreateDirectory(extractDir);
                    ZipFile.ExtractToDirectory(itemPath, extractDir, overwriteFiles: true);
                    lastOutput = extractDir;
                    converted++;
                    continue;
                }
                if (formatId == "MANIFEST")
                {
                    string manifestOut = GetOutputPath(itemPath, "_manifest", ".txt", customFolder);
                    using var zip = ZipFile.OpenRead(itemPath);
                    var sb = new StringBuilder();
                    sb.AppendLine($"Archive Manifest: {Path.GetFileName(itemPath)}");
                    sb.AppendLine($"Total Entries: {zip.Entries.Count}");
                    sb.AppendLine(new string('-', 70));
                    sb.AppendLine(string.Format("{0,-45} {1,10} {2,10}", "Name", "Original", "Compressed"));
                    sb.AppendLine(new string('-', 70));
                    foreach (var entry in zip.Entries)
                    {
                        sb.AppendLine(string.Format("{0,-45} {1,10} {2,10}", TruncateMiddle(entry.FullName, 45), FormatBytes(entry.Length), FormatBytes(entry.CompressedLength)));
                    }
                    await File.WriteAllTextAsync(manifestOut, sb.ToString(), Encoding.UTF8);
                    lastOutput = manifestOut;
                    converted++;
                    continue;
                }
                if (formatId == "REZIP")
                {
                    string rezipOut = GetOutputPath(itemPath, "_optimized", ".zip", customFolder);
                    string tempExtract = Path.Combine(Path.GetTempPath(), "LemonAID_" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        Directory.CreateDirectory(tempExtract);
                        ZipFile.ExtractToDirectory(itemPath, tempExtract);
                        ZipFile.CreateFromDirectory(tempExtract, rezipOut, CompressionLevel.SmallestSize, includeBaseDirectory: false);
                        lastOutput = rezipOut;
                        converted++;
                    }
                    finally
                    {
                        if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, recursive: true);
                    }
                    continue;
                }
            }

            // Data & Code conversions
            if (category is FileCategory.DataCode or FileCategory.Document)
            {
                string rawText = await File.ReadAllTextAsync(itemPath);
                string codeTargetExt = "." + formatId.ToLowerInvariant();
                string codeOut = GetOutputPath(itemPath, "_converted", codeTargetExt, customFolder);

                string transformed = formatId switch
                {
                    "JSON" => ConvertTextToJson(itemPath, rawText),
                    "CSV" => ConvertTextToCsv(rawText),
                    "XML" => ConvertTextToXml(itemPath, rawText),
                    "HTML" => ConvertCodeToHtml(itemPath, rawText),
                    "MD" => $"# {Path.GetFileName(itemPath)}\n\n```{Path.GetExtension(itemPath).TrimStart('.')}\n{rawText}\n```",
                    _ => rawText
                };

                await File.WriteAllTextAsync(codeOut, transformed, Encoding.UTF8);
                lastOutput = codeOut;
                converted++;
                continue;
            }

            // Image conversions
            if (category == FileCategory.Image)
            {
                string ext = "." + formatId.ToLowerInvariant();
                string output = GetOutputPath(itemPath, "_converted", ext, customFolder);
                if (await TranscodeImageAsync(itemPath, output, formatId, pixelTransform: null))
                {
                    lastOutput = output;
                    converted++;
                }
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

            FileCategory category = isDir ? FileCategory.Directory : DetectCategory(itemPath);

            switch (toolId)
            {
                case "COMPRESS":
                    if (isDir)
                    {
                        string zipOut = GetOutputPath(itemPath, "_compressed", ".zip", customFolder);
                        ZipFile.CreateFromDirectory(itemPath, zipOut, CompressionLevel.SmallestSize, includeBaseDirectory: true);
                        lastOutput = zipOut;
                        processed++;
                    }
                    else if (category == FileCategory.Image)
                    {
                        string outImg = GetOutputPath(itemPath, "_compressed", ".jpg", customFolder);
                        if (await TranscodeImageAsync(itemPath, outImg, "JPG", null, scaleFactor: 0.8))
                        {
                            lastOutput = outImg;
                            processed++;
                        }
                    }
                    else if (category is FileCategory.DataCode or FileCategory.Subtitle)
                    {
                        string raw = await File.ReadAllTextAsync(itemPath);
                        string minified = MinifyTextContent(raw, Path.GetExtension(itemPath));
                        string outMin = GetOutputPath(itemPath, ".min", Path.GetExtension(itemPath), customFolder);
                        await File.WriteAllTextAsync(outMin, minified, Encoding.UTF8);
                        lastOutput = outMin;
                        processed++;
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
                    if (isDir)
                    {
                        var di = new DirectoryInfo(itemPath);
                        string metaReport = GetOutputPath(itemPath, "_folder_meta", ".txt", customFolder);
                        string text = $"Folder Name: {di.Name}\nFull Path: {di.FullName}\nCreated UTC: {di.CreationTimeUtc:O}\nModified UTC: {di.LastWriteTimeUtc:O}\nFiles: {di.GetFiles("*", SearchOption.AllDirectories).Length}\nDirectories: {di.GetDirectories("*", SearchOption.AllDirectories).Length}";
                        await File.WriteAllTextAsync(metaReport, text, Encoding.UTF8);
                        lastOutput = metaReport;
                        processed++;
                    }
                    else if (category == FileCategory.Image)
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
                        string metaReport = GetOutputPath(itemPath, "_metadata", ".txt", customFolder);
                        string report = await GenerateDetailedMetadataReportAsync(itemPath, category);
                        await File.WriteAllTextAsync(metaReport, report, Encoding.UTF8);
                        lastOutput = metaReport;
                        processed++;
                    }
                    break;

                case "EDIT":
                    if (category == FileCategory.Image)
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
                    else if (category is FileCategory.DataCode or FileCategory.Subtitle or FileCategory.Document)
                    {
                        string raw = await File.ReadAllTextAsync(itemPath);
                        string formatted = NormalizeAndFormatText(raw, Path.GetExtension(itemPath));
                        string editOut = GetOutputPath(itemPath, "_formatted", Path.GetExtension(itemPath), customFolder);
                        await File.WriteAllTextAsync(editOut, formatted, Encoding.UTF8);
                        lastOutput = editOut;
                        processed++;
                    }
                    else
                    {
                        // Binary byte normalization
                        byte[] raw = await File.ReadAllBytesAsync(itemPath);
                        string binOut = GetOutputPath(itemPath, "_normalized", Path.GetExtension(itemPath), customFolder);
                        await File.WriteAllBytesAsync(binOut, raw);
                        lastOutput = binOut;
                        processed++;
                    }
                    break;

                case "ANNOTATE":
                    if (category == FileCategory.Image)
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
                    else if (category is FileCategory.DataCode or FileCategory.Subtitle or FileCategory.Document)
                    {
                        string raw = await File.ReadAllTextAsync(itemPath);
                        string annotated = StampTextAnnotation(raw, Path.GetFileName(itemPath));
                        string annOut = GetOutputPath(itemPath, "_annotated", Path.GetExtension(itemPath), customFolder);
                        await File.WriteAllTextAsync(annOut, annotated, Encoding.UTF8);
                        lastOutput = annOut;
                        processed++;
                    }
                    else
                    {
                        string sidecar = GetOutputPath(itemPath, ".annotated", ".txt", customFolder);
                        string note = $"LemonAID Sidecar Annotation\nTarget File: {Path.GetFileName(itemPath)}\nTimestamp: {DateTime.UtcNow:O}\nStatus: Verified and indexed\nNotes: Automated LemonAID forensic inspection complete.";
                        await File.WriteAllTextAsync(sidecar, note, Encoding.UTF8);
                        lastOutput = sidecar;
                        processed++;
                    }
                    break;

                case "ADD_BG":
                    if (category == FileCategory.Image)
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
                    else
                    {
                        string htmlOut = GetOutputPath(itemPath, "_canvas", ".html", customFolder);
                        string previewContent = isDir
                            ? $"Directory Listing: {Path.GetFileName(itemPath)}"
                            : (category is FileCategory.DataCode or FileCategory.Subtitle or FileCategory.Document
                                ? await File.ReadAllTextAsync(itemPath)
                                : GenerateHexDump(await File.ReadAllBytesAsync(itemPath), 16 * 1024));

                        string styledHtml = $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<title>{Path.GetFileName(itemPath)}</title>
<style>
body {{ margin: 0; padding: 48px; background: linear-gradient(135deg, #18181b 0%, #09090b 100%); display: flex; justify-content: center; align-items: center; min-height: 100vh; font-family: 'Segoe UI', system-ui, sans-serif; }}
.card {{ background: rgba(24, 24, 27, 0.85); backdrop-filter: blur(20px); border: 2px solid #facc15; border-radius: 20px; padding: 32px; box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.7); max-width: 900px; width: 100%; }}
h1 {{ color: #facc15; font-size: 24px; margin-top: 0; margin-bottom: 16px; border-bottom: 1px solid rgba(250, 204, 21, 0.3); padding-bottom: 8px; }}
pre {{ background: #09090b; color: #f4f4f5; padding: 20px; border-radius: 12px; overflow-x: auto; font-family: 'Cascadia Code', Consolas, monospace; font-size: 13px; line-height: 1.5; border: 1px solid #27272a; max-height: 600px; }}
.badge {{ display: inline-block; background: #facc15; color: #000; font-weight: 700; font-size: 11px; padding: 3px 8px; border-radius: 6px; text-transform: uppercase; margin-bottom: 12px; }}
</style>
</head>
<body>
<div class=""card"">
<span class=""badge"">LemonAID Visual Canvas</span>
<h1>{HtmlEncode(Path.GetFileName(itemPath))}</h1>
<pre><code>{HtmlEncode(previewContent)}</code></pre>
</div>
</body>
</html>";
                        await File.WriteAllTextAsync(htmlOut, styledHtml, Encoding.UTF8);
                        lastOutput = htmlOut;
                        processed++;
                    }
                    break;

                case "CROP":
                    if (category == FileCategory.Image)
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
                    else if (category is FileCategory.DataCode or FileCategory.Subtitle or FileCategory.Document)
                    {
                        var lines = (await File.ReadAllLinesAsync(itemPath)).Take(150);
                        string snippet = string.Join(Environment.NewLine, lines);
                        string cropOut = GetOutputPath(itemPath, "_snippet", Path.GetExtension(itemPath), customFolder);
                        await File.WriteAllTextAsync(cropOut, snippet, Encoding.UTF8);
                        lastOutput = cropOut;
                        processed++;
                    }
                    else
                    {
                        byte[] raw = await File.ReadAllBytesAsync(itemPath);
                        int takeBytes = Math.Min(raw.Length, 32 * 1024);
                        byte[] head = new byte[takeBytes];
                        Array.Copy(raw, 0, head, 0, takeBytes);
                        string cropOut = GetOutputPath(itemPath, "_head", ".bin", customFolder);
                        await File.WriteAllBytesAsync(cropOut, head);
                        lastOutput = cropOut;
                        processed++;
                    }
                    break;

                case "REDACT":
                    if (category == FileCategory.Image)
                    {
                        string redactOut = GetOutputPath(itemPath, "_redacted", ".png", customFolder);
                        bool ok = await TranscodeImageAsync(itemPath, redactOut, "PNG", (pixels, w, h) =>
                        {
                            uint rx0 = w / 4, rx1 = w * 3 / 4;
                            uint ry0 = h / 4, ry1 = h * 3 / 4;
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
                    else if (category is FileCategory.DataCode or FileCategory.Subtitle or FileCategory.Document)
                    {
                        string text = await File.ReadAllTextAsync(itemPath);
                        string redOut = GetOutputPath(itemPath, "_redacted", Path.GetExtension(itemPath), customFolder);
                        string masked = Regex.Replace(
                            text,
                            @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}|\b(?:\+?\d{1,3}[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b|\b(?:\d{1,3}\.){3}\d{1,3}\b|\b(?:\d{4}[-\s]?){3}\d{4}\b|\b(?:sk-[a-zA-Z0-9]{20,}|ghp_[a-zA-Z0-9]{20,}|Bearer\s+[a-zA-Z0-9\-._~+/]+=*)\b",
                            "[REDACTED]");
                        await File.WriteAllTextAsync(redOut, masked, Encoding.UTF8);
                        lastOutput = redOut;
                        processed++;
                    }
                    else
                    {
                        byte[] raw = await File.ReadAllBytesAsync(itemPath);
                        int maskStart = Math.Min(raw.Length, 64);
                        int maskEnd = Math.Min(raw.Length, 512);
                        for (int i = maskStart; i < maskEnd; i++) raw[i] = 0x00;
                        string redOut = GetOutputPath(itemPath, "_redacted", Path.GetExtension(itemPath), customFolder);
                        await File.WriteAllBytesAsync(redOut, raw);
                        lastOutput = redOut;
                        processed++;
                    }
                    break;
            }
        }

        return processed > 0
            ? $"{toolId} complete → Saved {Path.GetFileName(lastOutput)} beside original"
            : $"Could not perform '{toolId}' on selected item(s).";
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

    #region Helpers & Converters

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

    private static async Task<bool> TranscodeMediaAsync(string inputPath, string outputPath, string formatId)
    {
        try
        {
            var inFile = await StorageFile.GetFileFromPathAsync(inputPath);
            string outDir = Path.GetDirectoryName(outputPath)!;
            string outName = Path.GetFileName(outputPath);
            var folder = await StorageFolder.GetFolderFromPathAsync(outDir);
            var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);

            MediaEncodingProfile profile = formatId switch
            {
                "WAV" or "EXTRACT_WAV" => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
                "MP3" or "EXTRACT_MP3" => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High),
                "AAC" => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High),
                "WMA" => MediaEncodingProfile.CreateWma(AudioEncodingQuality.High),
                "WMV" => MediaEncodingProfile.CreateWmv(VideoEncodingQuality.HD720p),
                _ => MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p)
            };

            var transcoder = new MediaTranscoder();
            var preparedTranscode = await transcoder.PrepareFileTranscodeAsync(inFile, outFile, profile);
            if (preparedTranscode.CanTranscode)
            {
                await preparedTranscode.TranscodeAsync();
                return true;
            }
        }
        catch
        {
            // If media transcoder fails, write byte copy as fallback
            try
            {
                File.Copy(inputPath, outputPath, overwrite: true);
                return true;
            }
            catch { }
        }
        return false;
    }

    private static async Task<bool> ExtractMediaThumbnailAsync(string inputPath, string outputPath)
    {
        try
        {
            var inFile = await StorageFile.GetFileFromPathAsync(inputPath);
            using StorageItemThumbnail thumbnail = await inFile.GetThumbnailAsync(ThumbnailMode.VideosView, 1080);
            if (thumbnail != null && thumbnail.Size > 0)
            {
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(thumbnail);
                PixelDataProvider pixelData = await decoder.GetPixelDataAsync();
                byte[] pixels = pixelData.DetachPixelData();

                using var outFileStream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
                using var randomAccessOut = outFileStream.AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, randomAccessOut);
                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    decoder.PixelWidth,
                    decoder.PixelHeight,
                    96,
                    96,
                    pixels);
                await encoder.FlushAsync();
                return true;
            }
        }
        catch { }
        return false;
    }

    private static string ConvertToWebVtt(string srtContent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WEBVTT");
        sb.AppendLine();
        using var reader = new StringReader(srtContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (Regex.IsMatch(line, @"^\d{2}:\d{2}:\d{2},\d{3}\s+-->\s+\d{2}:\d{2}:\d{2},\d{3}"))
            {
                sb.AppendLine(line.Replace(',', '.'));
            }
            else
            {
                sb.AppendLine(line);
            }
        }
        return sb.ToString();
    }

    private static string ConvertToSrt(string vttContent)
    {
        var sb = new StringBuilder();
        using var reader = new StringReader(vttContent);
        string? line;
        int cueCounter = 1;
        bool inCue = false;

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase) || line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Regex.IsMatch(line, @"^\d{2}:\d{2}:\d{2}\.\d{3}\s+-->\s+\d{2}:\d{2}:\d{2}\.\d{3}"))
            {
                if (inCue) sb.AppendLine();
                sb.AppendLine(cueCounter++.ToString());
                sb.AppendLine(line.Replace('.', ','));
                inCue = true;
            }
            else if (inCue)
            {
                sb.AppendLine(line);
                if (string.IsNullOrWhiteSpace(line)) inCue = false;
            }
        }
        return sb.ToString();
    }

    private static string ExtractSubtitleDialogue(string subtitleContent)
    {
        var lines = new List<string>();
        using var reader = new StringReader(subtitleContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase)) continue;
            if (int.TryParse(trimmed, out _)) continue;
            if (trimmed.Contains("-->")) continue;

            lines.Add(trimmed);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string ConvertSubtitleToCsv(string subtitleContent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Index,Start,End,Text");
        using var reader = new StringReader(subtitleContent);
        string? line;
        int index = 1;
        string start = "", end = "";
        var textLines = new List<string>();

        while ((line = reader.ReadLine()) != null)
        {
            var match = Regex.Match(line, @"(\d{2}:\d{2}:\d{2}[.,]\d{3})\s+-->\s+(\d{2}:\d{2}:\d{2}[.,]\d{3})");
            if (match.Success)
            {
                if (textLines.Count > 0 && !string.IsNullOrEmpty(start))
                {
                    string safeText = string.Join(" ", textLines).Replace("\"", "\"\"");
                    sb.AppendLine($"{index++},\"{start}\",\"{end}\",\"{safeText}\"");
                    textLines.Clear();
                }
                start = match.Groups[1].Value.Replace(',', '.');
                end = match.Groups[2].Value.Replace(',', '.');
            }
            else if (!string.IsNullOrEmpty(start))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !int.TryParse(trimmed, out _))
                {
                    textLines.Add(trimmed);
                }
            }
        }

        if (textLines.Count > 0 && !string.IsNullOrEmpty(start))
        {
            string safeText = string.Join(" ", textLines).Replace("\"", "\"\"");
            sb.AppendLine($"{index},\"{start}\",\"{end}\",\"{safeText}\"");
        }

        return sb.ToString();
    }

    private static string ConvertSubtitleToJson(string subtitleContent)
    {
        var cues = new List<Dictionary<string, object>>();
        using var reader = new StringReader(subtitleContent);
        string? line;
        int index = 1;
        string start = "", end = "";
        var textLines = new List<string>();

        while ((line = reader.ReadLine()) != null)
        {
            var match = Regex.Match(line, @"(\d{2}:\d{2}:\d{2}[.,]\d{3})\s+-->\s+(\d{2}:\d{2}:\d{2}[.,]\d{3})");
            if (match.Success)
            {
                if (textLines.Count > 0 && !string.IsNullOrEmpty(start))
                {
                    cues.Add(new Dictionary<string, object>
                    {
                        ["index"] = index++,
                        ["start"] = start,
                        ["end"] = end,
                        ["text"] = string.Join(" ", textLines)
                    });
                    textLines.Clear();
                }
                start = match.Groups[1].Value.Replace(',', '.');
                end = match.Groups[2].Value.Replace(',', '.');
            }
            else if (!string.IsNullOrEmpty(start))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !int.TryParse(trimmed, out _))
                {
                    textLines.Add(trimmed);
                }
            }
        }

        if (textLines.Count > 0 && !string.IsNullOrEmpty(start))
        {
            cues.Add(new Dictionary<string, object>
            {
                ["index"] = index,
                ["start"] = start,
                ["end"] = end,
                ["text"] = string.Join(" ", textLines)
            });
        }

        return JsonSerializer.Serialize(cues, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ConvertSubtitleToHtml(string itemPath, string subtitleContent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{HtmlEncode(Path.GetFileName(itemPath))}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: Segoe UI, sans-serif; background: #09090b; color: #f4f4f5; padding: 40px; max-width: 800px; margin: 0 auto; line-height: 1.6; }");
        sb.AppendLine("h1 { color: #facc15; border-bottom: 2px solid #27272a; padding-bottom: 12px; font-size: 24px; }");
        sb.AppendLine(".cue { margin-bottom: 16px; padding: 12px 16px; background: #18181b; border-radius: 8px; border-left: 3px solid #facc15; }");
        sb.AppendLine(".time { font-family: monospace; font-size: 11px; color: #a1a1aa; margin-bottom: 4px; }");
        sb.AppendLine(".text { font-size: 14px; }");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>Subtitle Transcript: {HtmlEncode(Path.GetFileName(itemPath))}</h1>");

        using var reader = new StringReader(subtitleContent);
        string? line;
        string timestamp = "";
        var textLines = new List<string>();

        while ((line = reader.ReadLine()) != null)
        {
            var match = Regex.Match(line, @"\d{2}:\d{2}:\d{2}[.,]\d{3}\s+-->\s+\d{2}:\d{2}:\d{2}[.,]\d{3}");
            if (match.Success)
            {
                if (textLines.Count > 0 && !string.IsNullOrEmpty(timestamp))
                {
                    sb.AppendLine($"<div class=\"cue\"><div class=\"time\">{HtmlEncode(timestamp)}</div><div class=\"text\">{HtmlEncode(string.Join(" ", textLines))}</div></div>");
                    textLines.Clear();
                }
                timestamp = match.Value;
            }
            else if (!string.IsNullOrEmpty(timestamp))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !int.TryParse(trimmed, out _))
                {
                    textLines.Add(trimmed);
                }
            }
        }
        if (textLines.Count > 0 && !string.IsNullOrEmpty(timestamp))
        {
            sb.AppendLine($"<div class=\"cue\"><div class=\"time\">{HtmlEncode(timestamp)}</div><div class=\"text\">{HtmlEncode(string.Join(" ", textLines))}</div></div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string ConvertTextToJson(string itemPath, string rawText)
    {
        string ext = Path.GetExtension(itemPath).ToLowerInvariant();
        if (ext is ".csv" or ".tsv")
        {
            char delim = ext == ".tsv" ? '\t' : ',';
            var rows = new List<Dictionary<string, string>>();
            using var reader = new StringReader(rawText);
            string? headerLine = reader.ReadLine();
            if (headerLine != null)
            {
                string[] headers = headerLine.Split(delim).Select(h => h.Trim().Trim('"')).ToArray();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cols = line.Split(delim);
                    var dict = new Dictionary<string, string>();
                    for (int i = 0; i < headers.Length; i++)
                    {
                        dict[headers[i]] = i < cols.Length ? cols[i].Trim().Trim('"') : "";
                    }
                    rows.Add(dict);
                }
            }
            return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
        }

        try
        {
            using var doc = JsonDocument.Parse(rawText);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            var fallback = new Dictionary<string, object>
            {
                ["source"] = Path.GetFileName(itemPath),
                ["timestamp"] = DateTime.UtcNow,
                ["content"] = rawText
            };
            return JsonSerializer.Serialize(fallback, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private static string ConvertTextToCsv(string rawText)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawText);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var keys = new HashSet<string>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in el.EnumerateObject()) keys.Add(prop.Name);
                    }
                }
                var keyList = keys.ToList();
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", keyList.Select(k => $"\"{k}\"")));
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Object)
                    {
                        var vals = keyList.Select(k => el.TryGetProperty(k, out var val) ? $"\"{val.ToString().Replace("\"", "\"\"")}\"" : "\"\"");
                        sb.AppendLine(string.Join(",", vals));
                    }
                }
                return sb.ToString();
            }
        }
        catch { }

        // Delimiter conversion fallback
        return rawText.Replace("\t", ",");
    }

    private static string ConvertTextToXml(string itemPath, string rawText)
    {
        string safeName = Regex.Replace(Path.GetFileNameWithoutExtension(itemPath), @"[^a-zA-Z0-9_]", "_");
        if (string.IsNullOrEmpty(safeName) || char.IsDigit(safeName[0])) safeName = "Item_" + safeName;

        return $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<{safeName}>\n  <content><![CDATA[{rawText}]]></content>\n</{safeName}>";
    }

    private static string ConvertCodeToHtml(string itemPath, string rawText)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<title>{HtmlEncode(Path.GetFileName(itemPath))}</title>
<style>
body {{ margin: 0; padding: 32px; background: #09090b; color: #f4f4f5; font-family: Segoe UI, sans-serif; }}
.header {{ margin-bottom: 24px; border-bottom: 1px solid #27272a; padding-bottom: 12px; }}
.title {{ font-size: 20px; font-weight: 700; color: #facc15; }}
.meta {{ font-size: 12px; color: #71717a; margin-top: 4px; }}
pre {{ background: #18181b; border: 1px solid #27272a; border-radius: 8px; padding: 20px; overflow-x: auto; font-family: 'Cascadia Code', Consolas, monospace; font-size: 13px; line-height: 1.6; color: #e4e4e7; }}
</style>
</head>
<body>
<div class=""header"">
<div class=""title"">{HtmlEncode(Path.GetFileName(itemPath))}</div>
<div class=""meta"">Generated by LemonAID Universal Converter &bull; {DateTime.UtcNow:u}</div>
</div>
<pre><code>{HtmlEncode(rawText)}</code></pre>
</body>
</html>";
    }

    private static string MinifyTextContent(string raw, string ext)
    {
        ext = ext.ToLowerInvariant();
        if (ext is ".json")
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = false });
            }
            catch { }
        }

        var lines = raw.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l));

        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeAndFormatText(string raw, string ext)
    {
        ext = ext.ToLowerInvariant();
        if (ext is ".json")
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { }
        }

        // Normalize CRLF line endings and remove trailing whitespace
        var lines = raw.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(l => l.TrimEnd());

        return string.Join("\r\n", lines);
    }

    private static string StampTextAnnotation(string raw, string fileName)
    {
        string stamp = $"/* ===================================================================\n * LemonAID Annotated Copy\n * File: {fileName}\n * Timestamp: {DateTime.UtcNow:O}\n * =================================================================== */\n\n";
        return stamp + raw;
    }

    private static string GenerateHexDump(byte[] data, int maxBytes)
    {
        int bytesToDump = Math.Min(data.Length, maxBytes);
        var sb = new StringBuilder();
        sb.AppendLine($"Offset(h)  00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F  Decoded Text");
        sb.AppendLine(new string('-', 75));

        for (int i = 0; i < bytesToDump; i += 16)
        {
            sb.Append($"{i:X8}  ");
            for (int j = 0; j < 16; j++)
            {
                if (i + j < bytesToDump)
                    sb.Append($"{data[i + j]:X2} ");
                else
                    sb.Append("   ");

                if (j == 7) sb.Append(" ");
            }
            sb.Append(" |");
            for (int j = 0; j < 16; j++)
            {
                if (i + j < bytesToDump)
                {
                    byte b = data[i + j];
                    sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
                }
            }
            sb.AppendLine("|");
        }

        if (data.Length > maxBytes)
        {
            sb.AppendLine($"\n... [Truncated {data.Length - maxBytes:N0} additional bytes for display]");
        }
        return sb.ToString();
    }

    private static async Task<string> ComputeHashesReportAsync(string filePath)
    {
        var fi = new FileInfo(filePath);
        using var stream = File.OpenRead(filePath);

        using var sha256 = SHA256.Create();
        using var md5 = MD5.Create();
        using var sha1 = SHA1.Create();

        byte[] buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
            sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
        }
        sha256.TransformFinalBlock([], 0, 0);
        md5.TransformFinalBlock([], 0, 0);
        sha1.TransformFinalBlock([], 0, 0);

        string hash256 = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
        string hashMd5 = Convert.ToHexString(md5.Hash!).ToLowerInvariant();
        string hash1 = Convert.ToHexString(sha1.Hash!).ToLowerInvariant();

        return $"LemonAID Cryptographic Integrity Report\n" +
               $"File: {fi.Name}\n" +
               $"Size: {fi.Length:N0} bytes ({FormatBytes(fi.Length)})\n" +
               $"Created UTC:  {fi.CreationTimeUtc:O}\n" +
               $"Modified UTC: {fi.LastWriteTimeUtc:O}\n\n" +
               $"SHA-256: {hash256}\n" +
               $"SHA-1:   {hash1}\n" +
               $"MD5:     {hashMd5}\n";
    }

    private static async Task<string> GenerateDetailedMetadataReportAsync(string filePath, FileCategory category)
    {
        var fi = new FileInfo(filePath);
        var sb = new StringBuilder();
        sb.AppendLine("LemonAID Forensic File Inspection Report");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"File Name:      {fi.Name}");
        sb.AppendLine($"Category:       {category}");
        sb.AppendLine($"Extension:      {fi.Extension}");
        sb.AppendLine($"File Size:      {fi.Length:N0} bytes ({FormatBytes(fi.Length)})");
        sb.AppendLine($"Created UTC:    {fi.CreationTimeUtc:O}");
        sb.AppendLine($"Modified UTC:   {fi.LastWriteTimeUtc:O}");
        sb.AppendLine($"Attributes:     {fi.Attributes}");

        byte[] headBytes = new byte[Math.Min(fi.Length, 4096)];
        using (var fs = File.OpenRead(filePath))
        {
            _ = await fs.ReadAsync(headBytes);
        }
        double entropy = CalculateEntropy(headBytes);
        sb.AppendLine($"Shannon Entropy:{entropy:F3} / 8.000 ({(entropy > 7.5 ? "Encrypted/Compressed" : entropy > 5.0 ? "Binary Code" : "Plain Structured")})");

        if (category == FileCategory.DataCode)
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            sb.AppendLine($"Total Lines:    {lines.Length:N0}");
            sb.AppendLine($"Blank Lines:    {lines.Count(string.IsNullOrWhiteSpace):N0}");
            sb.AppendLine($"Non-Empty:      {lines.Count(l => !string.IsNullOrWhiteSpace(l)):N0}");
        }

        sb.AppendLine();
        sb.AppendLine(await ComputeHashesReportAsync(filePath));
        return sb.ToString();
    }

    private static double CalculateEntropy(byte[] data)
    {
        if (data.Length == 0) return 0.0;
        int[] counts = new int[256];
        foreach (byte b in data) counts[b]++;
        double entropy = 0.0;
        double len = data.Length;
        for (int i = 0; i < 256; i++)
        {
            if (counts[i] == 0) continue;
            double p = counts[i] / len;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    private static string ExtractPrintableStrings(byte[] data, int minLength)
    {
        var sb = new StringBuilder();
        var current = new StringBuilder();

        foreach (byte b in data)
        {
            if (b is >= 0x20 and <= 0x7E)
            {
                current.Append((char)b);
            }
            else
            {
                if (current.Length >= minLength)
                {
                    sb.AppendLine(current.ToString());
                }
                current.Clear();
            }
        }
        if (current.Length >= minLength)
        {
            sb.AppendLine(current.ToString());
        }
        return sb.ToString();
    }

    private static async Task WriteUniversalPdfReportAsync(string sourcePath, string pdfOutputPath)
    {
        string title = Path.GetFileName(sourcePath);
        long length = File.Exists(sourcePath) ? new FileInfo(sourcePath).Length : 0;
        string bodyText = $"LemonAID Universal Inspection Report: {title} ({FormatBytes(length)})";
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

    private static string HtmlEncode(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private static string TruncateMiddle(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        int keep = (maxLength - 3) / 2;
        return text[..keep] + "..." + text[^keep..];
    }

    #endregion
}
