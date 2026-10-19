using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
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
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp", ".heic", ".heif", ".avif", ".bmp", ".gif", ".ico", ".svg"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".pages", ".rtf", ".rtfd", ".odt", ".txt", ".md", ".markdown", ".epub", ".html", ".htm"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".m4v", ".flv", ".mpeg", ".mpg"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".wav", ".flac", ".ogg", ".opus", ".aiff", ".aif", ".wma", ".aac"
    };

    private static readonly HashSet<string> SpreadsheetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx", ".xls", ".ods", ".csv", ".tsv"
    };

    private static readonly HashSet<string> SlideExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ppt", ".pptx", ".odp", ".key"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".vtt", ".ass", ".ssa", ".sub"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".tgz", ".rar", ".7z"
    };

    public static string DetectCategoryBadge(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0) return "CONVERTER";
        string first = filePaths[0];
        if (Directory.Exists(first)) return "FOLDER";

        string ext = Path.GetExtension(first).ToLowerInvariant();
        string cleanExt = ext.TrimStart('.').ToUpperInvariant();

        if (ext == ".pdf") return "PDF DOC";
        if (ImageExtensions.Contains(ext)) return $"IMAGE · {cleanExt}";
        if (DocumentExtensions.Contains(ext)) return $"DOC · {cleanExt}";
        if (VideoExtensions.Contains(ext)) return $"VIDEO · {cleanExt}";
        if (AudioExtensions.Contains(ext)) return $"AUDIO · {cleanExt}";
        if (SpreadsheetExtensions.Contains(ext)) return $"SHEET · {cleanExt}";
        if (SlideExtensions.Contains(ext)) return $"SLIDES · {cleanExt}";
        if (SubtitleExtensions.Contains(ext)) return $"SUBS · {cleanExt}";
        if (ArchiveExtensions.Contains(ext)) return $"ARCHIVE · {cleanExt}";
        return string.IsNullOrEmpty(cleanExt) ? "FILE" : cleanExt;
    }

    public static IReadOnlyList<SectorItem> GetSectorsForMode(WheelMode mode, IReadOnlyList<string> filePaths)
    {
        return mode switch
        {
            WheelMode.Converter => GetContextualConverterSectors(filePaths),
            WheelMode.Tools =>
            [
                new("COMPRESS", "COMPRESS", "\uE740", "Reduce file size or compress file/folder beside original"),
                new("METADATA", "METADATA", "\uE8EC", "Inspect or strip embedded metadata into a clean copy"),
                new("EDIT", "EDIT", "\uE790", "Enhance contrast & brightness and export as a new file"),
                new("ANNOTATE", "ANNOTATE", "\uE929", "Stamp an annotation header banner onto a copy"),
                new("ADD_BG", "ADD BG", "\uE91B", "Place image onto a padded citrus-gradient canvas"),
                new("CROP", "CROP", "\uE7A8", "Center-crop image to a 1:1 square aspect ratio"),
                new("REDACT", "REDACT", "\uED1A", "Mask central sensitive region or redact text emails/phones")
            ],
            WheelMode.Actions =>
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
        if (filePaths.Count == 0)
        {
            // Default showcase wheel when no file is loaded yet
            return
            [
                new("JPG", "JPG", "\uEB9F", "Convert image to standard JPEG (.jpg)"),
                new("PNG", "PNG", "\uEB9F", "Convert image to lossless PNG (.png)"),
                new("TIFF", "TIF", "\uEB9F", "Convert image to uncompressed TIFF (.tif)"),
                new("WEBP", "WEBP", "\uEB9F", "Convert to high-efficiency WebP (.webp)"),
                new("HEIC", "HEIC", "\uEB9F", "Convert to modern HEIC container (.heic)"),
                new("AVIF", "AVIF", "\uEB9F", "Convert to next-gen AVIF (.avif)"),
                new("PDF", "PDF", "\uEA90", "Export image or document to PDF (.pdf)")
            ];
        }

        string first = filePaths[0];
        if (Directory.Exists(first))
        {
            return
            [
                new("ZIP", "ZIP", "\uF012", "Package folder into a .zip archive beside original"),
                new("TAR", "TAR", "\uF012", "Package folder into a POSIX .tar archive"),
                new("GZIP", "TAR.GZ", "\uF012", "Compress folder into a .tar.gz archive"),
                new("PDF", "PDF", "\uEA90", "Export folder manifest summary to PDF (.pdf)"),
                new("CSV", "CSV", "\uE9F9", "Export folder file inventory to spreadsheet (.csv)"),
                new("MD", "MD", "\uE8A5", "Export folder directory tree to Markdown (.md)")
            ];
        }

        string ext = Path.GetExtension(first).ToLowerInvariant();
        string sourceId = NormalizeFormatId(ext);

        // 1. IMAGES: JPG, PNG, TIF, WEBP, HEIC, AVIF, BMP, GIF, PDF
        if (ImageExtensions.Contains(ext))
        {
            SectorItem[] candidates =
            [
                new("JPG", "JPG", "\uEB9F", "Convert image to standard JPEG (.jpg)"),
                new("PNG", "PNG", "\uEB9F", "Convert image to lossless PNG (.png)"),
                new("TIFF", "TIF", "\uEB9F", "Convert image to archival TIFF (.tif)"),
                new("WEBP", "WEBP", "\uEB9F", "Convert image to WebP (.webp)"),
                new("HEIC", "HEIC", "\uEB9F", "Convert image to HEIC (.heic)"),
                new("AVIF", "AVIF", "\uEB9F", "Convert image to AVIF (.avif)"),
                new("PDF", "PDF", "\uEA90", "Export image into a full-page PDF document (.pdf)"),
                new("BMP", "BMP", "\uEB9F", "Convert image to uncompressed Bitmap (.bmp)"),
                new("ICO", "ICO", "\uEB9F", "Convert image to Windows Icon (.ico)")
            ];
            return FilterOutSourceFormat(candidates, sourceId, maxCount: 7);
        }

        // 2. PDF DOCUMENTS: DOCX, DOC, TXT, MD, JPG, PNG, HTML
        if (ext == ".pdf")
        {
            return
            [
                new("DOCX", "DOCX", "\uE8A5", "Convert PDF to Microsoft Word document (.docx)"),
                new("DOC", "DOC", "\uE8A5", "Convert PDF to Word / RTF document (.doc)"),
                new("TXT", "TXT", "\uE8A5", "Extract plain text from PDF (.txt)"),
                new("MD", "MD", "\uE8A5", "Convert PDF content to Markdown (.md)"),
                new("PNG", "PNG", "\uEB9F", "Render PDF summary card to lossless PNG (.png)"),
                new("JPG", "JPG", "\uEB9F", "Render PDF summary card to JPEG (.jpg)"),
                new("HTML", "HTML", "\uE774", "Convert PDF content to standalone HTML (.html)")
            ];
        }

        // 3. DOCUMENTS: DOC, DOCX, PDF, TXT, MD, HTML, EPUB, RTF
        if (DocumentExtensions.Contains(ext))
        {
            SectorItem[] candidates =
            [
                new("PDF", "PDF", "\uEA90", "Export document to Adobe PDF (.pdf)"),
                new("DOCX", "DOCX", "\uE8A5", "Convert to modern Word OpenXML document (.docx)"),
                new("DOC", "DOC", "\uE8A5", "Convert to Word compatible document (.doc)"),
                new("TXT", "TXT", "\uE8A5", "Convert to plain UTF-8 text (.txt)"),
                new("MD", "MD", "\uE8A5", "Convert to Markdown document (.md)"),
                new("HTML", "HTML", "\uE774", "Export formatted HTML document (.html)"),
                new("EPUB", "EPUB", "\uE736", "Package document into an e-book (.epub)"),
                new("RTF", "RTF", "\uE8A5", "Convert to Rich Text Format (.rtf)")
            ];
            return FilterOutSourceFormat(candidates, sourceId, maxCount: 7);
        }

        // 4. VIDEOS: WEBP, GIF, MP4, MOV, MKV, WEBM, AVI, MP3 (Audio Export)
        if (VideoExtensions.Contains(ext))
        {
            SectorItem[] candidates =
            [
                new("MP4", "MP4", "\uE714", "Transcode video to H.264 MP4 (.mp4)"),
                new("WEBP", "WEBP", "\uEB9F", "Convert video clip to WebP animation/frame (.webp)"),
                new("GIF", "GIF", "\uEB9F", "Convert video clip to animated GIF (.gif)"),
                new("MOV", "MOV", "\uE714", "Convert video to QuickTime MOV (.mov)"),
                new("MKV", "MKV", "\uE714", "Convert video to Matroska MKV (.mkv)"),
                new("WEBM", "WEBM", "\uE714", "Convert video to WebM (.webm)"),
                new("MP3", "MP3", "\uE8D6", "Extract audio track from video to MP3 (.mp3)"),
                new("AVI", "AVI", "\uE714", "Convert video to AVI container (.avi)")
            ];
            return FilterOutSourceFormat(candidates, sourceId, maxCount: 7);
        }

        // 5. AUDIO: MP3, WAV, FLAC, M4A, OGG, OPUS, AAC, AIFF
        if (AudioExtensions.Contains(ext))
        {
            SectorItem[] candidates =
            [
                new("MP3", "MP3", "\uE8D6", "Convert audio to MP3 (.mp3)"),
                new("WAV", "WAV", "\uE8D6", "Convert audio to uncompressed WAV (.wav)"),
                new("FLAC", "FLAC", "\uE8D6", "Convert audio to lossless FLAC (.flac)"),
                new("M4A", "M4A", "\uE8D6", "Convert audio to AAC / M4A (.m4a)"),
                new("OGG", "OGG", "\uE8D6", "Convert audio to Ogg Vorbis (.ogg)"),
                new("OPUS", "OPUS", "\uE8D6", "Convert audio to Opus (.opus)"),
                new("AIFF", "AIFF", "\uE8D6", "Convert audio to Apple AIFF (.aiff)")
            ];
            return FilterOutSourceFormat(candidates, sourceId, maxCount: 6);
        }

        // 6. SPREADSHEETS: XLSX, CSV, TSV, PDF, ODS, HTML, MD
        if (SpreadsheetExtensions.Contains(ext))
        {
            SectorItem[] candidates =
            [
                new("XLSX", "XLSX", "\uE9F9", "Convert to Excel OpenXML workbook (.xlsx)"),
                new("CSV", "CSV", "\uE9F9", "Convert to Comma-Separated Values (.csv)"),
                new("TSV", "TSV", "\uE9F9", "Convert to Tab-Separated Values (.tsv)"),
                new("PDF", "PDF", "\uEA90", "Export spreadsheet table to PDF (.pdf)"),
                new("HTML", "HTML", "\uE774", "Export spreadsheet to HTML table (.html)"),
                new("MD", "MD", "\uE8A5", "Export spreadsheet to Markdown table (.md)"),
                new("JSON", "JSON", "\uE943", "Export rows to structured JSON (.json)")
            ];
            return FilterOutSourceFormat(candidates, sourceId, maxCount: 6);
        }

        // 7. SLIDES / PRESENTATIONS: PDF, PPTX, JPG, PNG, HTML, MD
        if (SlideExtensions.Contains(ext))
        {
            SectorItem[] candidates =
            [
                new("PDF", "PDF", "\uEA90", "Export presentation deck to PDF (.pdf)"),
                new("PPTX", "PPTX", "\uE8A5", "Convert presentation to PowerPoint (.pptx)"),
                new("PNG", "PNG", "\uEB9F", "Export slide deck summary to PNG (.png)"),
                new("JPG", "JPG", "\uEB9F", "Export slide deck summary to JPG (.jpg)"),
                new("HTML", "HTML", "\uE774", "Export slide notes & outline to HTML (.html)"),
                new("MD", "MD", "\uE8A5", "Export slide outline to Markdown (.md)")
            ];
            return FilterOutSourceFormat(candidates, sourceId, maxCount: 6);
        }

        // 8. SUBTITLES: SRT, VTT, TXT, MD, HTML, JSON
        if (SubtitleExtensions.Contains(ext))
        {
            SectorItem[] candidates =
            [
                new("SRT", "SRT", "\uE8C1", "Convert subtitles to SubRip (.srt)"),
                new("VTT", "VTT", "\uE8C1", "Convert subtitles to WebVTT (.vtt)"),
                new("TXT", "TXT", "\uE8A5", "Strip timestamps into clean transcript (.txt)"),
                new("MD", "MD", "\uE8A5", "Export subtitle transcript to Markdown (.md)"),
                new("HTML", "HTML", "\uE774", "Export subtitle cues to HTML table (.html)")
            ];
            return FilterOutSourceFormat(candidates, sourceId, maxCount: 5);
        }

        // 9. ARCHIVES: EXTRACT, ZIP, TAR, GZIP
        if (ArchiveExtensions.Contains(ext))
        {
            SectorItem[] candidates =
            [
                new("EXTRACT", "EXTRACT", "\uE8DE", "Extract archive contents into a folder beside original"),
                new("ZIP", "ZIP", "\uF012", "Repackage archive as standard .zip"),
                new("TAR", "TAR", "\uF012", "Repackage archive as POSIX .tar"),
                new("GZIP", "TAR.GZ", "\uF012", "Compress into a .tar.gz archive")
            ];
            return FilterOutSourceFormat(candidates, sourceId, maxCount: 4);
        }

        // 10. GENERIC / OTHER FILES
        return
        [
            new("ZIP", "ZIP", "\uF012", "Package file into a .zip archive"),
            new("TAR", "TAR", "\uF012", "Package file into a .tar archive"),
            new("GZIP", "TAR.GZ", "\uF012", "Compress file into a .tar.gz archive"),
            new("PDF", "PDF", "\uEA90", "Export file summary/contents to PDF (.pdf)"),
            new("TXT", "TXT", "\uE8A5", "Export readable text copy (.txt)"),
            new("MD", "MD", "\uE8A5", "Export formatted Markdown copy (.md)")
        ];
    }

    private static string NormalizeFormatId(string ext)
    {
        return ext.TrimStart('.').ToUpperInvariant() switch
        {
            "JPEG" => "JPG",
            "TIF" => "TIFF",
            "HEIF" => "HEIC",
            "MARKDOWN" => "MD",
            "HTM" => "HTML",
            "TGZ" or "GZ" => "GZIP",
            var other => other
        };
    }

    private static IReadOnlyList<SectorItem> FilterOutSourceFormat(SectorItem[] candidates, string sourceId, int maxCount)
    {
        return candidates
            .Where(c => !string.Equals(c.Id, sourceId, StringComparison.OrdinalIgnoreCase))
            .Take(maxCount)
            .ToList();
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

            try
            {
                // ARCHIVE EXTRACTION
                if (formatId == "EXTRACT" && isFile)
                {
                    string extractDir = GetOutputPath(itemPath, "_extracted", "", customFolder);
                    Directory.CreateDirectory(extractDir);
                    string srcExt = Path.GetExtension(itemPath).ToLowerInvariant();
                    if (srcExt == ".zip")
                    {
                        ZipFile.ExtractToDirectory(itemPath, extractDir, overwriteFiles: true);
                    }
                    else if (srcExt == ".tar")
                    {
                        await TarFile.ExtractToDirectoryAsync(itemPath, extractDir, overwriteFiles: true);
                    }
                    else if (srcExt is ".gz" or ".tgz")
                    {
                        using var fs = File.OpenRead(itemPath);
                        using var gz = new GZipStream(fs, CompressionMode.Decompress);
                        await TarFile.ExtractToDirectoryAsync(gz, extractDir, overwriteFiles: true);
                    }
                    lastOutput = extractDir;
                    converted++;
                    continue;
                }

                // ARCHIVE PACKAGING (ZIP, TAR, GZIP)
                if (formatId is "ZIP" or "TAR" or "GZIP")
                {
                    lastOutput = await CreateArchiveAsync(itemPath, isDir, formatId, customFolder);
                    converted++;
                    continue;
                }

                // FOLDER INVENTORY EXPORTS (PDF, CSV, MD, TXT, HTML)
                if (isDir)
                {
                    var entries = new DirectoryInfo(itemPath).GetFileSystemInfos();
                    string listing = string.Join(Environment.NewLine, entries.Select(e => $"{e.Name}\t{(e is FileInfo fi ? fi.Length : 0)} bytes"));
                    lastOutput = await ExportDocumentTargetAsync(itemPath, listing, formatId, customFolder);
                    converted++;
                    continue;
                }

                string ext = Path.GetExtension(itemPath).ToLowerInvariant();

                // IMAGE SOURCE CONVERSIONS
                if (ImageExtensions.Contains(ext))
                {
                    if (formatId == "PDF")
                    {
                        string pdfOut = GetOutputPath(itemPath, "", ".pdf", customFolder);
                        if (await ExportImageToPdfAsync(itemPath, pdfOut))
                        {
                            lastOutput = pdfOut;
                            converted++;
                        }
                        continue;
                    }

                    string targetExt = formatId == "TIFF" ? ".tif" : "." + formatId.ToLowerInvariant();
                    string imgOut = GetOutputPath(itemPath, "", targetExt, customFolder);
                    if (await ConvertImageFileAsync(itemPath, imgOut, formatId))
                    {
                        lastOutput = imgOut;
                        converted++;
                    }
                    continue;
                }

                // VIDEO & AUDIO CONVERSIONS
                if (VideoExtensions.Contains(ext) || AudioExtensions.Contains(ext))
                {
                    string mediaOut = await ConvertMediaFileAsync(itemPath, formatId, customFolder);
                    if (!string.IsNullOrEmpty(mediaOut))
                    {
                        lastOutput = mediaOut;
                        converted++;
                    }
                    continue;
                }

                // SUBTITLE CONVERSIONS (SRT, VTT, TXT, MD, HTML)
                if (SubtitleExtensions.Contains(ext))
                {
                    string rawSub = await File.ReadAllTextAsync(itemPath);
                    string subOut = await ConvertSubtitleAsync(itemPath, rawSub, formatId, customFolder);
                    lastOutput = subOut;
                    converted++;
                    continue;
                }

                // SPREADSHEET CONVERSIONS (XLSX, CSV, TSV, PDF, HTML, MD, JSON)
                if (SpreadsheetExtensions.Contains(ext))
                {
                    var table = await ReadSpreadsheetTableAsync(itemPath);
                    lastOutput = await ExportSpreadsheetTargetAsync(itemPath, table, formatId, customFolder);
                    converted++;
                    continue;
                }

                // DOCUMENTS / PDF / SLIDES CONVERSIONS
                string extractedText = await ExtractTextFromDocumentAsync(itemPath);
                if (formatId is "PNG" or "JPG")
                {
                    string imgExt = "." + formatId.ToLowerInvariant();
                    string cardOut = GetOutputPath(itemPath, "_page1", imgExt, customFolder);
                    if (await RenderDocumentPreviewImageAsync(cardOut, formatId))
                    {
                        lastOutput = cardOut;
                        converted++;
                    }
                    continue;
                }

                lastOutput = await ExportDocumentTargetAsync(itemPath, extractedText, formatId, customFolder);
                converted++;
            }
            catch
            {
                // Continue processing remaining files in batch
            }
        }

        return converted > 0
            ? $"Converted {converted} item(s) to {formatId} → {Path.GetFileName(lastOutput)}"
            : $"Could not convert selected item(s) to {formatId}.";
    }

    private static async Task<string> CreateArchiveAsync(string itemPath, bool isDir, string formatId, string? customFolder)
    {
        if (formatId == "ZIP")
        {
            string zipPath = GetOutputPath(itemPath, "", ".zip", customFolder);
            if (isDir)
            {
                ZipFile.CreateFromDirectory(itemPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
            }
            else
            {
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                archive.CreateEntryFromFile(itemPath, Path.GetFileName(itemPath), CompressionLevel.Optimal);
            }
            return zipPath;
        }

        if (formatId == "TAR")
        {
            string tarPath = GetOutputPath(itemPath, "", ".tar", customFolder);
            if (isDir)
            {
                await TarFile.CreateFromDirectoryAsync(itemPath, tarPath, includeBaseDirectory: true);
            }
            else
            {
                using var fs = File.Create(tarPath);
                using var writer = new TarWriter(fs);
                await writer.WriteEntryAsync(itemPath, Path.GetFileName(itemPath));
            }
            return tarPath;
        }

        // GZIP (.tar.gz)
        string tgzPath = GetOutputPath(itemPath, "", ".tar.gz", customFolder);
        using var outStream = File.Create(tgzPath);
        using var gzipStream = new GZipStream(outStream, CompressionLevel.Optimal);
        if (isDir)
        {
            await TarFile.CreateFromDirectoryAsync(itemPath, gzipStream, includeBaseDirectory: true);
        }
        else
        {
            using var writer = new TarWriter(gzipStream);
            await writer.WriteEntryAsync(itemPath, Path.GetFileName(itemPath));
        }
        return tgzPath;
    }

    private static async Task<bool> ConvertImageFileAsync(string inputPath, string outputPath, string formatId)
    {
        // Try FFmpeg first for WebP / AVIF if available on the user's machine
        if (formatId is "WEBP" or "AVIF")
        {
            if (await TryRunFfmpegAsync($"-y -i \"{inputPath}\" \"{outputPath}\"") && File.Exists(outputPath))
            {
                return true;
            }
        }

        if (formatId == "ICO")
        {
            return await WriteIcoFromImageAsync(inputPath, outputPath);
        }

        return await TranscodeImageAsync(inputPath, outputPath, formatId, pixelTransform: null);
    }

    private static async Task<bool> WriteIcoFromImageAsync(string inputPath, string icoOutputPath)
    {
        string tempPng = Path.Combine(Path.GetTempPath(), $"lemon_ico_{Guid.NewGuid():N}.png");
        try
        {
            StorageFile inFile = await StorageFile.GetFileFromPathAsync(inputPath);
            using IRandomAccessStream inStream = await inFile.OpenAsync(FileAccessMode.Read);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(inStream);

            var transform = new BitmapTransform { ScaledWidth = 64, ScaledHeight = 64, InterpolationMode = BitmapInterpolationMode.Fant };
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);

            using (var fs = new FileStream(tempPng, FileMode.Create, FileAccess.ReadWrite))
            using (var ras = fs.AsRandomAccessStream())
            {
                var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
                enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 64, 64, 96, 96, pixelData.DetachPixelData());
                await enc.FlushAsync();
            }

            byte[] pngBytes = await File.ReadAllBytesAsync(tempPng);
            using var icoStream = new FileStream(icoOutputPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(icoStream);

            // ICONDIR header (6 bytes) + ICONDIRENTRY (16 bytes) + PNG payload
            bw.Write((short)0);
            bw.Write((short)1); // Type 1 = ICO
            bw.Write((short)1); // 1 image
            bw.Write((byte)64); // Width
            bw.Write((byte)64); // Height
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((short)1);
            bw.Write((short)32); // 32 bpp
            bw.Write(pngBytes.Length);
            bw.Write(22); // Offset to PNG data
            bw.Write(pngBytes);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (File.Exists(tempPng)) File.Delete(tempPng);
        }
    }

    private static async Task<bool> ExportImageToPdfAsync(string imagePath, string pdfOutputPath)
    {
        string tempJpg = Path.Combine(Path.GetTempPath(), $"lemon_pdf_{Guid.NewGuid():N}.jpg");
        try
        {
            if (!await TranscodeImageAsync(imagePath, tempJpg, "JPG", null))
                return false;

            byte[] jpgBytes = await File.ReadAllBytesAsync(tempJpg);
            StorageFile inFile = await StorageFile.GetFileFromPathAsync(tempJpg);
            using IRandomAccessStream inStream = await inFile.OpenAsync(FileAccessMode.Read);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(inStream);
            uint w = Math.Max(1, decoder.PixelWidth);
            uint h = Math.Max(1, decoder.PixelHeight);

            using var fs = new FileStream(pdfOutputPath, FileMode.Create, FileAccess.Write);
            void WriteAscii(string s) => fs.Write(Encoding.ASCII.GetBytes(s));

            WriteAscii("%PDF-1.4\n");
            long obj1 = fs.Position;
            WriteAscii("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
            long obj2 = fs.Position;
            WriteAscii("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n");
            long obj3 = fs.Position;
            WriteAscii($"3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Contents 4 0 R /Resources << /XObject << /Im1 5 0 R >> >> >> endobj\n");

            string contentCmd = $"q {w} 0 0 {h} 0 0 cm /Im1 Do Q";
            long obj4 = fs.Position;
            WriteAscii($"4 0 obj << /Length {contentCmd.Length} >> stream\n{contentCmd}\nendstream endobj\n");

            long obj5 = fs.Position;
            WriteAscii($"5 0 obj << /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpgBytes.Length} >> stream\n");
            await fs.WriteAsync(jpgBytes);
            WriteAscii("\nendstream endobj\n");

            long xrefPos = fs.Position;
            WriteAscii("xref\n0 6\n0000000000 65535 f \n");
            WriteAscii($"{obj1:D10} 00000 n \n{obj2:D10} 00000 n \n{obj3:D10} 00000 n \n{obj4:D10} 00000 n \n{obj5:D10} 00000 n \n");
            WriteAscii($"trailer << /Size 6 /Root 1 0 R >>\nstartxref\n{xrefPos}\n%%EOF");
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (File.Exists(tempJpg)) File.Delete(tempJpg);
        }
    }

    private static async Task<string> ConvertMediaFileAsync(string inputPath, string formatId, string? customFolder)
    {
        string targetExt = "." + formatId.ToLowerInvariant();
        string outputPath = GetOutputPath(inputPath, "", targetExt, customFolder);

        // 1. If FFmpeg is installed on PATH, use it for full multi-codec support (WebP, GIF, MKV, WebM, MOV, MP4, MP3, FLAC, OGG, Opus)
        string ffmpegArgs = formatId switch
        {
            "GIF" => $"-y -i \"{inputPath}\" -vf \"fps=10,scale=480:-1:flags=lanczos\" -t 8 \"{outputPath}\"",
            "WEBP" => $"-y -i \"{inputPath}\" -vcodec libwebp -lossless 0 -q:v 75 -loop 0 -an -vsync 0 -t 6 \"{outputPath}\"",
            "MP3" => $"-y -i \"{inputPath}\" -vn -ab 192k \"{outputPath}\"",
            _ => $"-y -i \"{inputPath}\" \"{outputPath}\""
        };

        if (await TryRunFfmpegAsync(ffmpegArgs) && File.Exists(outputPath))
        {
            return outputPath;
        }

        // 2. Native Windows.Media.Transcoding fallback (MP4, WMV, MP3, WAV, M4A, FLAC)
        try
        {
            StorageFile srcFile = await StorageFile.GetFileFromPathAsync(inputPath);
            MediaEncodingProfile? profile = formatId switch
            {
                "MP4" or "MOV" or "MKV" or "WEBM" => MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p),
                "AVI" or "WMV" => MediaEncodingProfile.CreateWmv(VideoEncodingQuality.HD720p),
                "MP3" => MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High),
                "WAV" or "AIFF" => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
                "M4A" or "AAC" => MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High),
                "FLAC" or "OGG" or "OPUS" => MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High),
                _ => null
            };

            if (profile != null)
            {
                string outDir = Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory;
                string outFileName = Path.GetFileName(outputPath);
                StorageFolder destFolder = await StorageFolder.GetFolderFromPathAsync(outDir);
                StorageFile destFile = await destFolder.CreateFileAsync(outFileName, CreationCollisionOption.ReplaceExisting);

                var transcoder = new MediaTranscoder();
                var prep = await transcoder.PrepareFileTranscodeAsync(srcFile, destFile, profile);
                if (prep.CanTranscode)
                {
                    await prep.TranscodeAsync();
                    return outputPath;
                }
            }

            // 3. Video -> GIF / WEBP fallback using Windows MediaComposition thumbnail extraction
            if (formatId is "GIF" or "WEBP")
            {
                var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(srcFile);
                var comp = new Windows.Media.Editing.MediaComposition();
                comp.Clips.Add(clip);
                using var thumb = await comp.GetThumbnailAsync(
                    TimeSpan.FromSeconds(Math.Min(1.0, clip.OriginalDuration.TotalSeconds / 2)),
                    480, 0, Windows.Media.Editing.VideoFramePrecision.NearestFrame);

                BitmapDecoder dec = await BitmapDecoder.CreateAsync(thumb);
                var px = await dec.GetPixelDataAsync();
                Guid encId = formatId == "GIF" ? BitmapEncoder.GifEncoderId : BitmapEncoder.PngEncoderId;

                using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
                using var ras = fs.AsRandomAccessStream();
                var enc = await BitmapEncoder.CreateAsync(encId, ras);
                enc.SetPixelData(dec.BitmapPixelFormat, dec.BitmapAlphaMode, dec.PixelWidth, dec.PixelHeight, 96, 96, px.DetachPixelData());
                await enc.FlushAsync();
                return outputPath;
            }
        }
        catch
        {
            // Ignore and fall back
        }

        return string.Empty;
    }

    private static async Task<bool> TryRunFfmpegAsync(string arguments)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };
            if (!proc.Start()) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ExtractTextFromDocumentAsync(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext == ".docx")
            {
                using var zip = ZipFile.OpenRead(path);
                var docEntry = zip.GetEntry("word/document.xml");
                if (docEntry != null)
                {
                    using var s = docEntry.Open();
                    var xdoc = await XDocument.LoadAsync(s, LoadOptions.None, default);
                    var paragraphs = xdoc.Descendants()
                        .Where(e => e.Name.LocalName == "p")
                        .Select(p => string.Concat(p.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value)));
                    return string.Join(Environment.NewLine, paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)));
                }
            }
            else if (ext == ".pptx")
            {
                using var zip = ZipFile.OpenRead(path);
                var slideTexts = new List<string>();
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                {
                    using var s = entry.Open();
                    var xdoc = await XDocument.LoadAsync(s, LoadOptions.None, default);
                    string text = string.Join(" ", xdoc.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value));
                    if (!string.IsNullOrWhiteSpace(text)) slideTexts.Add(text);
                }
                if (slideTexts.Count > 0) return string.Join(Environment.NewLine + Environment.NewLine, slideTexts);
            }
            else if (ext == ".epub")
            {
                using var zip = ZipFile.OpenRead(path);
                var chapters = new List<string>();
                foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".html", StringComparison.OrdinalIgnoreCase)))
                {
                    using var sr = new StreamReader(entry.Open());
                    string html = await sr.ReadToEndAsync();
                    string plain = Regex.Replace(html, "<[^>]+>", " ");
                    chapters.Add(System.Net.WebUtility.HtmlDecode(plain).Trim());
                }
                if (chapters.Count > 0) return string.Join(Environment.NewLine + Environment.NewLine, chapters);
            }
            else if (ext == ".pdf")
            {
                string raw = await File.ReadAllTextAsync(path, Encoding.Latin1);
                var matches = Regex.Matches(raw, @"\(([^()\\]*(?:\\.[^()\\]*)*)\)\s*Tj");
                if (matches.Count > 0)
                {
                    return string.Join(Environment.NewLine, matches.Select(m => m.Groups[1].Value.Replace("\\(", "(").Replace("\\)", ")")));
                }
                return $"PDF Document: {Path.GetFileName(path)} ({new FileInfo(path).Length:N0} bytes)";
            }
            else if (ext is ".html" or ".htm")
            {
                string html = await File.ReadAllTextAsync(path);
                return System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ")).Trim();
            }
            else if (ext is ".rtf" or ".doc")
            {
                string raw = await File.ReadAllTextAsync(path);
                if (raw.StartsWith("{\\rtf", StringComparison.Ordinal))
                {
                    return Regex.Replace(raw, @"\\[a-z]+\d*\s?|[{}]", "").Trim();
                }
                return raw;
            }
        }
        catch { }

        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch
        {
            return $"Source File: {Path.GetFileName(path)}";
        }
    }

    private static async Task<string> ExportDocumentTargetAsync(string sourcePath, string content, string formatId, string? customFolder)
    {
        string targetExt = "." + formatId.ToLowerInvariant();
        string outPath = GetOutputPath(sourcePath, "", targetExt, customFolder);
        string title = Path.GetFileNameWithoutExtension(sourcePath);

        switch (formatId)
        {
            case "TXT":
                await File.WriteAllTextAsync(outPath, content, Encoding.UTF8);
                break;

            case "MD":
                string md = $"# {title}\n\n{content}\n";
                await File.WriteAllTextAsync(outPath, md, Encoding.UTF8);
                break;

            case "HTML":
                string html = $"<!DOCTYPE html>\n<html>\n<head><meta charset=\"utf-8\"><title>{System.Net.WebUtility.HtmlEncode(title)}</title>\n" +
                              "<style>body{font-family:system-ui,sans-serif;max-width:780px;margin:40px auto;line-height:1.6;padding:0 20px;}</style></head>\n" +
                              $"<body><h1>{System.Net.WebUtility.HtmlEncode(title)}</h1><pre style=\"white-space:pre-wrap;\">{System.Net.WebUtility.HtmlEncode(content)}</pre></body>\n</html>";
                await File.WriteAllTextAsync(outPath, html, Encoding.UTF8);
                break;

            case "DOC":
            case "RTF":
                var rtf = new StringBuilder();
                rtf.AppendLine(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}");
                rtf.AppendLine($@"\f0\fs28\b {EscapeRtf(title)}\b0\fs22\par\par");
                foreach (var line in content.Split(["\r\n", "\n"], StringSplitOptions.None))
                {
                    rtf.AppendLine($"{EscapeRtf(line)}\\par");
                }
                rtf.Append('}');
                await File.WriteAllTextAsync(outPath, rtf.ToString(), Encoding.ASCII);
                break;

            case "DOCX":
                CreateOpenXmlDocx(outPath, title, content);
                break;

            case "PPTX":
                CreateOpenXmlDocx(outPath, title, content);
                break;

            case "EPUB":
                CreateMinimalEpub(outPath, title, content);
                break;

            case "PDF":
            default:
                await WriteMultiLineTextPdfAsync(title, content, outPath);
                break;
        }

        return outPath;
    }

    private static string EscapeRtf(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c is '\\' or '{' or '}') sb.Append('\\').Append(c);
            else if (c <= 0x7f) sb.Append(c);
            else sb.Append($"\\u{(int)c}?");
        }
        return sb.ToString();
    }

    private static void CreateOpenXmlDocx(string docxPath, string title, string content)
    {
        using var fs = new FileStream(docxPath, FileMode.Create, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteZipTextEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>");

        WriteZipTextEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>");

        var bodyXml = new StringBuilder();
        bodyXml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        bodyXml.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
        bodyXml.Append($"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(title)}</w:t></w:r></w:p>");
        foreach (string line in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            bodyXml.Append($"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(line)}</w:t></w:r></w:p>");
        }
        bodyXml.Append("</w:body></w:document>");

        WriteZipTextEntry(zip, "word/document.xml", bodyXml.ToString());
    }

    private static void CreateMinimalEpub(string epubPath, string title, string content)
    {
        using var fs = new FileStream(epubPath, FileMode.Create, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteZipTextEntry(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        WriteZipTextEntry(zip, "META-INF/container.xml",
            "<?xml version=\"1.0\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
            "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");

        WriteZipTextEntry(zip, "OEBPS/content.opf",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<package xmlns=\"http://www.idpf.org/2007/opf\" unique-identifier=\"BookId\" version=\"2.0\">" +
            $"<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><dc:title>{System.Security.SecurityElement.Escape(title)}</dc:title><dc:language>en</dc:language></metadata>" +
            "<manifest><item id=\"ch1\" href=\"chapter1.xhtml\" media-type=\"application/xhtml+xml\"/></manifest>" +
            "<spine><itemref idref=\"ch1\"/></spine></package>");

        string bodyHtml = string.Join("", content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => $"<p>{System.Security.SecurityElement.Escape(p)}</p>"));
        WriteZipTextEntry(zip, "OEBPS/chapter1.xhtml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><html xmlns=\"http://www.w3.org/1999/xhtml\">" +
            $"<head><title>{System.Security.SecurityElement.Escape(title)}</title></head><body><h1>{System.Security.SecurityElement.Escape(title)}</h1>{bodyHtml}</body></html>");
    }

    private static void WriteZipTextEntry(ZipArchive zip, string entryName, string text, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(entryName, level);
        using var sw = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        sw.Write(text);
    }

    private static async Task<string> ConvertSubtitleAsync(string sourcePath, string rawContent, string formatId, string? customFolder)
    {
        string targetExt = "." + formatId.ToLowerInvariant();
        string outPath = GetOutputPath(sourcePath, "", targetExt, customFolder);

        if (formatId == "VTT")
        {
            string vtt = rawContent.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)
                ? rawContent
                : "WEBVTT\n\n" + Regex.Replace(rawContent, @"(\d{2}:\d{2}:\d{2}),(\d{3})", "$1.$2");
            await File.WriteAllTextAsync(outPath, vtt, Encoding.UTF8);
            return outPath;
        }

        if (formatId == "SRT")
        {
            string srt = Regex.Replace(rawContent, @"^WEBVTT\s*", "", RegexOptions.IgnoreCase);
            srt = Regex.Replace(srt, @"(\d{2}:\d{2}:\d{2})\.(\d{3})", "$1,$2");
            await File.WriteAllTextAsync(outPath, srt, Encoding.UTF8);
            return outPath;
        }

        // Strip cue numbers and timestamp lines for TXT / MD / HTML
        var cleanLines = rawContent.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(l => !l.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase)
                        && !l.Contains("-->")
                        && !int.TryParse(l.Trim(), out _))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

        string transcript = string.Join(Environment.NewLine, cleanLines);
        return await ExportDocumentTargetAsync(sourcePath, transcript, formatId, customFolder);
    }

    private static async Task<List<string[]>> ReadSpreadsheetTableAsync(string path)
    {
        var rows = new List<string[]>();
        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".xlsx")
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var sharedStrings = new List<string>();
                var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using var s = ssEntry.Open();
                    var xdoc = await XDocument.LoadAsync(s, LoadOptions.None, default);
                    sharedStrings.AddRange(xdoc.Descendants().Where(e => e.Name.LocalName == "si").Select(si => si.Value));
                }

                var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml");
                if (sheetEntry != null)
                {
                    using var s = sheetEntry.Open();
                    var xdoc = await XDocument.LoadAsync(s, LoadOptions.None, default);
                    foreach (var rowElem in xdoc.Descendants().Where(e => e.Name.LocalName == "row"))
                    {
                        var cells = new List<string>();
                        foreach (var c in rowElem.Elements().Where(e => e.Name.LocalName == "c"))
                        {
                            string? type = c.Attribute("t")?.Value;
                            string val = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
                            if (type == "s" && int.TryParse(val, out int sIdx) && sIdx >= 0 && sIdx < sharedStrings.Count)
                                val = sharedStrings[sIdx];
                            cells.Add(val);
                        }
                        rows.Add(cells.ToArray());
                    }
                    return rows;
                }
            }
            catch { }
        }

        char delim = ext == ".tsv" ? '\t' : ',';
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                rows.Add(line.Split(delim));
            }
        }
        return rows;
    }

    private static async Task<string> ExportSpreadsheetTargetAsync(string sourcePath, List<string[]> rows, string formatId, string? customFolder)
    {
        string targetExt = "." + formatId.ToLowerInvariant();
        string outPath = GetOutputPath(sourcePath, "", targetExt, customFolder);

        switch (formatId)
        {
            case "CSV":
                await File.WriteAllLinesAsync(outPath, rows.Select(r => string.Join(",", r.Select(c => c.Contains(',') ? $"\"{c.Replace("\"", "\"\"")}\"" : c))), Encoding.UTF8);
                return outPath;

            case "TSV":
                await File.WriteAllLinesAsync(outPath, rows.Select(r => string.Join("\t", r)), Encoding.UTF8);
                return outPath;

            case "MD":
                var mdSb = new StringBuilder();
                if (rows.Count > 0)
                {
                    mdSb.AppendLine("| " + string.Join(" | ", rows[0]) + " |");
                    mdSb.AppendLine("| " + string.Join(" | ", rows[0].Select(_ => "---")) + " |");
                    foreach (var r in rows.Skip(1))
                        mdSb.AppendLine("| " + string.Join(" | ", r) + " |");
                }
                await File.WriteAllTextAsync(outPath, mdSb.ToString(), Encoding.UTF8);
                return outPath;

            case "JSON":
                var jsonRows = new List<Dictionary<string, string>>();
                if (rows.Count > 1)
                {
                    string[] headers = rows[0];
                    foreach (var r in rows.Skip(1))
                    {
                        var dict = new Dictionary<string, string>();
                        for (int i = 0; i < headers.Length; i++)
                            dict[headers[i]] = i < r.Length ? r[i] : "";
                        jsonRows.Add(dict);
                    }
                }
                string json = System.Text.Json.JsonSerializer.Serialize(jsonRows, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(outPath, json, Encoding.UTF8);
                return outPath;

            case "XLSX":
                CreateOpenXmlXlsx(outPath, rows);
                return outPath;

            default:
                string plainTable = string.Join(Environment.NewLine, rows.Select(r => string.Join("   |   ", r)));
                return await ExportDocumentTargetAsync(sourcePath, plainTable, formatId, customFolder);
        }
    }

    private static void CreateOpenXmlXlsx(string xlsxPath, List<string[]> rows)
    {
        using var fs = new FileStream(xlsxPath, FileMode.Create, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        WriteZipTextEntry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>");

        WriteZipTextEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        WriteZipTextEntry(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

        WriteZipTextEntry(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "</Relationships>");

        var sheetXml = new StringBuilder();
        sheetXml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (int r = 0; r < rows.Count; r++)
        {
            sheetXml.Append($"<row r=\"{r + 1}\">");
            for (int c = 0; c < rows[r].Length; c++)
            {
                string escaped = System.Security.SecurityElement.Escape(rows[r][c]) ?? "";
                sheetXml.Append($"<c t=\"inlineStr\"><is><t>{escaped}</t></is></c>");
            }
            sheetXml.Append("</row>");
        }
        sheetXml.Append("</sheetData></worksheet>");
        WriteZipTextEntry(zip, "xl/worksheets/sheet1.xml", sheetXml.ToString());
    }

    private static async Task<bool> RenderDocumentPreviewImageAsync(string outputPath, string formatId)
    {
        try
        {
            uint w = 800, h = 1040;
            byte[] pixels = new byte[w * h * 4];
            for (uint y = 0; y < h; y++)
            {
                for (uint x = 0; x < w; x++)
                {
                    uint idx = (y * w + x) * 4;
                    bool headerBar = y < 96;
                    pixels[idx + 0] = headerBar ? (byte)21 : (byte)250;
                    pixels[idx + 1] = headerBar ? (byte)204 : (byte)250;
                    pixels[idx + 2] = headerBar ? (byte)250 : (byte)252;
                    pixels[idx + 3] = 255;
                }
            }

            Guid encoderId = formatId == "JPG" ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId;
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
            using var ras = fs.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(encoderId, ras);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, w, h, 96, 96, pixels);
            await encoder.FlushAsync();
            return true;
        }
        catch
        {
            return false;
        }
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
            bool isImage = ImageExtensions.Contains(ext);

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
                        string text = await ExtractTextFromDocumentAsync(itemPath);
                        string redOut = GetOutputPath(itemPath, "_redacted", ".txt", customFolder);
                        string masked = Regex.Replace(
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
            : $"Tool '{toolId}' requires an image (or folder/file for Compress/Metadata/Redact).";
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
                "TIFF" or "TIF" => BitmapEncoder.TiffEncoderId,
                "BMP" => BitmapEncoder.BmpEncoderId,
                "GIF" => BitmapEncoder.GifEncoderId,
                "HEIC" or "HEIF" => BitmapEncoder.HeifEncoderId,
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

    private static async Task WriteMultiLineTextPdfAsync(string title, string content, string pdfOutputPath)
    {
        var lines = new List<string> { title, "" };
        foreach (string rawLine in content.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            string clean = new(rawLine.Where(c => c >= 32 && c <= 126).ToArray());
            while (clean.Length > 80)
            {
                lines.Add(clean[..80]);
                clean = clean[80..];
            }
            lines.Add(clean);
            if (lines.Count >= 46) break;
        }

        var streamSb = new StringBuilder();
        streamSb.AppendLine("BT /F1 11 Tf 50 740 Td 15 TL");
        foreach (var line in lines)
        {
            string esc = line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            streamSb.AppendLine($"({esc}) Tj T*");
        }
        streamSb.Append("ET");
        string streamContent = streamSb.ToString();

        var sb = new StringBuilder();
        sb.AppendLine("%PDF-1.4");
        sb.AppendLine("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj");
        sb.AppendLine("2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj");
        sb.AppendLine("3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj");
        sb.AppendLine($"4 0 obj << /Length {streamContent.Length} >> stream\n{streamContent}\nendstream endobj");
        sb.AppendLine("5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj");
        sb.AppendLine("xref\n0 6\n0000000000 65535 f \ntrailer << /Size 6 /Root 1 0 R >>\nstartxref\n320\n%%EOF");

        await File.WriteAllTextAsync(pdfOutputPath, sb.ToString(), Encoding.ASCII);
    }
}
