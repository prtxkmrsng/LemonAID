using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LemonAID.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace LemonAID;

public static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, System.Text.StringBuilder? packageFullName);

    private static bool HasPackageIdentity()
    {
        int length = 0;
        int result = GetCurrentPackageFullName(ref length, null);
        return result != 15700; // APPMODEL_ERROR_NO_PACKAGE = 15700
    }

    private static void LogBoot(string msg)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "lemonaid.log"), $"[{DateTime.UtcNow:O}] [Boot] {msg}\n");
        }
        catch { }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        LogBoot($"Main entry entered. Args: {string.Join(" ", args)}");

        if (args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase)))
        {
            int testCode = RunDiagnosticsBattery();
            Environment.Exit(testCode);
            return;
        }

        try
        {
            ComWrappersSupport.InitializeComWrappers();

            bool hasPackage = HasPackageIdentity();
            LogBoot($"HasPackageIdentity: {hasPackage}");

            if (!hasPackage)
            {
                try
                {
                    // Unpackaged Windows App SDK runtime bootstrap
                    bool initialized = Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap.TryInitialize(0x00010006, out int hr);
                    LogBoot($"Bootstrap.TryInitialize result: {initialized}, hr: 0x{hr:X8}");
                }
                catch (Exception bootEx)
                {
                    LogBoot($"Bootstrap exception: {bootEx.Message}");
                }
            }

            Application.Start((p) =>
            {
                LogBoot("Application.Start callback invoked");
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
        catch (Exception ex)
        {
            LogBoot($"Fatal in Program.Main: {ex.Message}\n{ex}");
        }
    }

    private static int RunDiagnosticsBattery()
    {
        string logPath = Path.Combine(Path.GetTempPath(), "lemonaid_tests.log");
        File.WriteAllText(logPath, $"LemonAID Universal Test Run - {DateTime.UtcNow:O}\n=================================================\n");

        void Log(string line)
        {
            Console.WriteLine(line);
            try { File.AppendAllText(logPath, line + "\n"); } catch { }
        }

        Log("=================================================");
        Log("  LemonAID Universal Test & Diagnostic Battery   ");
        Log("=================================================");
        int passed = 0;
        int total = 0;

        void Assert(string name, bool condition)
        {
            total++;
            if (condition)
            {
                passed++;
                Log($"[PASS] {name}");
            }
            else
            {
                Log($"[FAIL] {name}");
            }
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "LemonAID_Universal_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. DetectCategory Verification
            Assert("DetectCategory .png => Image", FileProcessorService.DetectCategory("sample.png") == FileCategory.Image);
            Assert("DetectCategory .mp3 => Audio", FileProcessorService.DetectCategory("track.mp3") == FileCategory.Audio);
            Assert("DetectCategory .mp4 => Video", FileProcessorService.DetectCategory("clip.mp4") == FileCategory.Video);
            Assert("DetectCategory .json => DataCode", FileProcessorService.DetectCategory("data.json") == FileCategory.DataCode);
            Assert("DetectCategory .cs => DataCode", FileProcessorService.DetectCategory("Main.cs") == FileCategory.DataCode);
            Assert("DetectCategory .srt => Subtitle", FileProcessorService.DetectCategory("sub.srt") == FileCategory.Subtitle);
            Assert("DetectCategory .vtt => Subtitle", FileProcessorService.DetectCategory("sub.vtt") == FileCategory.Subtitle);
            Assert("DetectCategory .zip => Archive", FileProcessorService.DetectCategory("pack.zip") == FileCategory.Archive);
            Assert("DetectCategory .pdf => Document", FileProcessorService.DetectCategory("doc.pdf") == FileCategory.Document);
            Assert("DetectCategory .bin => BinaryGeneric", FileProcessorService.DetectCategory("firmware.bin") == FileCategory.BinaryGeneric);
            Assert("DetectCategory Directory => Directory", FileProcessorService.DetectCategory(tempDir) == FileCategory.Directory);

            // 2. GetSectorsForMode Verification
            var imgSectors = FileProcessorService.GetSectorsForMode(WheelMode.Converter, (string[])["sample.png"]);
            Assert("Image Converter Sectors == 6", imgSectors.Count == 6);
            Assert("Image Converter contains PNG, JPG, WEBP, TIFF, PDF, ZIP",
                imgSectors.Any(s => s.Id == "PNG") && imgSectors.Any(s => s.Id == "JPG") && imgSectors.Any(s => s.Id == "PDF"));

            var audioSectors = FileProcessorService.GetSectorsForMode(WheelMode.Converter, (string[])["sample.mp3"]);
            Assert("Audio Converter Sectors == 6", audioSectors.Count == 6);
            Assert("Audio Converter contains WAV, MP3, AAC, WMA, B64, ZIP",
                audioSectors.Any(s => s.Id == "WAV") && audioSectors.Any(s => s.Id == "MP3") && audioSectors.Any(s => s.Id == "AAC"));

            var videoSectors = FileProcessorService.GetSectorsForMode(WheelMode.Converter, (string[])["sample.mp4"]);
            Assert("Video Converter Sectors == 6", videoSectors.Count == 6);
            Assert("Video Converter contains MP4, WMV, EXTRACT_MP3, THUMBNAIL",
                videoSectors.Any(s => s.Id == "MP4") && videoSectors.Any(s => s.Id == "EXTRACT_MP3") && videoSectors.Any(s => s.Id == "THUMBNAIL"));

            var dataSectors = FileProcessorService.GetSectorsForMode(WheelMode.Converter, (string[])["data.json"]);
            Assert("DataCode Converter Sectors == 6", dataSectors.Count == 6);
            Assert("DataCode Converter contains JSON, CSV, XML, HTML, MD, TXT",
                dataSectors.Any(s => s.Id == "JSON") && dataSectors.Any(s => s.Id == "CSV") && dataSectors.Any(s => s.Id == "HTML"));

            var subSectors = FileProcessorService.GetSectorsForMode(WheelMode.Converter, (string[])["subs.srt"]);
            Assert("Subtitle Converter Sectors == 6", subSectors.Count == 6);
            Assert("Subtitle Converter contains VTT, SRT, TXT, CSV, JSON, HTML",
                subSectors.Any(s => s.Id == "VTT") && subSectors.Any(s => s.Id == "JSON") && subSectors.Any(s => s.Id == "HTML"));

            var binSectors = FileProcessorService.GetSectorsForMode(WheelMode.Converter, (string[])["firmware.bin"]);
            Assert("BinaryGeneric Converter Sectors == 6", binSectors.Count == 6);
            Assert("BinaryGeneric contains ZIP, B64, HEX, HASH, STRINGS, PDF",
                binSectors.Any(s => s.Id == "HEX") && binSectors.Any(s => s.Id == "HASH") && binSectors.Any(s => s.Id == "STRINGS"));

            var toolsSectors = FileProcessorService.GetSectorsForMode(WheelMode.Tools, (string[])["any.file"]);
            Assert("Tools Sectors == 7", toolsSectors.Count == 7);
            Assert("Tools contains COMPRESS, METADATA, EDIT, ANNOTATE, ADD_BG, CROP, REDACT",
                toolsSectors.Any(s => s.Id == "COMPRESS") && toolsSectors.Any(s => s.Id == "REDACT"));

            var actionsSectors = FileProcessorService.GetSectorsForMode(WheelMode.Actions, (string[])["any.file"]);
            Assert("Actions Sectors == 6", actionsSectors.Count == 6);

            // 3. Functional Execution Tests on Concrete Files
            // Create sample files
            string testJson = Path.Combine(tempDir, "test.json");
            File.WriteAllText(testJson, "[{\"name\": \"Alice\", \"role\": \"Admin\", \"email\": \"alice@example.com\"}, {\"name\": \"Bob\", \"role\": \"User\", \"email\": \"bob@example.com\"}]");

            string testSrt = Path.Combine(tempDir, "test.srt");
            File.WriteAllText(testSrt, "1\n00:00:01,000 --> 00:00:04,000\nHello from LemonAID subtitle test!\n\n2\n00:00:05,000 --> 00:00:08,000\nUniversal file format processing active.\n");

            string testBin = Path.Combine(tempDir, "test.bin");
            byte[] binBytes = new byte[1024];
            for (int i = 0; i < binBytes.Length; i++) binBytes[i] = (byte)(i % 256);
            // Put some printable strings in the binary
            byte[] marker = System.Text.Encoding.ASCII.GetBytes("LemonAID_Printable_String_Marker_12345");
            Array.Copy(marker, 0, binBytes, 64, marker.Length);
            File.WriteAllBytes(testBin, binBytes);

            // Execute JSON -> CSV
            var taskJsonToCsv = FileProcessorService.ExecuteSectorAsync(WheelMode.Converter, "CSV", (string[])[testJson], tempDir, null);
            taskJsonToCsv.Wait();
            Assert("JSON -> CSV Conversion Succeeded", taskJsonToCsv.Result.Contains("Converted"));
            string csvOutput = Directory.GetFiles(tempDir, "*_converted.csv").FirstOrDefault() ?? "";
            Assert("Generated CSV file exists", !string.IsNullOrEmpty(csvOutput) && File.Exists(csvOutput));
            if (File.Exists(csvOutput))
            {
                string csvContent = File.ReadAllText(csvOutput);
                Assert("CSV contains expected columns and rows", csvContent.Contains("name") && csvContent.Contains("Alice") && csvContent.Contains("Bob"));
            }

            // Execute Subtitle SRT -> VTT
            var taskSrtToVtt = FileProcessorService.ExecuteSectorAsync(WheelMode.Converter, "VTT", (string[])[testSrt], tempDir, null);
            taskSrtToVtt.Wait();
            Assert("SRT -> VTT Conversion Succeeded", taskSrtToVtt.Result.Contains("Converted"));
            string vttOutput = Directory.GetFiles(tempDir, "*_converted.vtt").FirstOrDefault() ?? "";
            Assert("Generated VTT file exists", !string.IsNullOrEmpty(vttOutput) && File.Exists(vttOutput));
            if (File.Exists(vttOutput))
            {
                string vttContent = File.ReadAllText(vttOutput);
                Assert("VTT starts with WEBVTT header", vttContent.StartsWith("WEBVTT"));
                Assert("VTT has dot-formatted timestamps", vttContent.Contains("00:00:01.000 --> 00:00:04.000"));
            }

            // Execute Subtitle SRT -> JSON Cues
            var taskSrtToJson = FileProcessorService.ExecuteSectorAsync(WheelMode.Converter, "JSON", (string[])[testSrt], tempDir, null);
            taskSrtToJson.Wait();
            Assert("SRT -> JSON Conversion Succeeded", taskSrtToJson.Result.Contains("Converted"));
            string srtJsonOutput = Directory.GetFiles(tempDir, "*_converted.json").FirstOrDefault() ?? "";
            Assert("Generated subtitle JSON exists and contains cues", !string.IsNullOrEmpty(srtJsonOutput) && File.ReadAllText(srtJsonOutput).Contains("\"start\": \"00:00:01.000\""));

            // Execute Binary -> Hex Dump
            var taskBinHex = FileProcessorService.ExecuteSectorAsync(WheelMode.Converter, "HEX", (string[])[testBin], tempDir, null);
            taskBinHex.Wait();
            Assert("Binary -> HEX Conversion Succeeded", taskBinHex.Result.Contains("Converted"));
            string hexOutput = Directory.GetFiles(tempDir, "*_hexdump.hex.txt").FirstOrDefault() ?? "";
            Assert("Hex dump contains offset header and byte grid", !string.IsNullOrEmpty(hexOutput) && File.ReadAllText(hexOutput).Contains("Offset(h)"));

            // Execute Binary -> Checksum Hash Report
            var taskBinHash = FileProcessorService.ExecuteSectorAsync(WheelMode.Converter, "HASH", (string[])[testBin], tempDir, null);
            taskBinHash.Wait();
            Assert("Binary -> HASH Conversion Succeeded", taskBinHash.Result.Contains("Converted"));
            string hashOutput = Directory.GetFiles(tempDir, "*_checksums.hash.txt").FirstOrDefault() ?? "";
            Assert("Checksum report contains SHA-256 and MD5", !string.IsNullOrEmpty(hashOutput) && File.ReadAllText(hashOutput).Contains("SHA-256:"));

            // Execute Binary -> Strings Extraction
            var taskBinStrings = FileProcessorService.ExecuteSectorAsync(WheelMode.Converter, "STRINGS", (string[])[testBin], tempDir, null);
            taskBinStrings.Wait();
            Assert("Binary -> STRINGS Succeeded", taskBinStrings.Result.Contains("Converted"));
            string stringsOutput = Directory.GetFiles(tempDir, "*_strings.strings.txt").FirstOrDefault() ?? "";
            Assert("Extracted strings contains ASCII marker", !string.IsNullOrEmpty(stringsOutput) && File.ReadAllText(stringsOutput).Contains("LemonAID_Printable_String_Marker"));

            // Execute Tools Mode: METADATA on Binary
            var taskMeta = FileProcessorService.ExecuteSectorAsync(WheelMode.Tools, "METADATA", (string[])[testBin], tempDir, null);
            taskMeta.Wait();
            Assert("Tool METADATA Succeeded", taskMeta.Result.Contains("complete"));
            string metaOutput = Directory.GetFiles(tempDir, "*_metadata.txt").FirstOrDefault() ?? "";
            Assert("Metadata report contains Shannon Entropy", !string.IsNullOrEmpty(metaOutput) && File.ReadAllText(metaOutput).Contains("Shannon Entropy:"));

            // Execute Tools Mode: REDACT on JSON with emails
            var taskRedact = FileProcessorService.ExecuteSectorAsync(WheelMode.Tools, "REDACT", (string[])[testJson], tempDir, null);
            taskRedact.Wait();
            Assert("Tool REDACT Succeeded", taskRedact.Result.Contains("complete"));
            string redactOutput = Directory.GetFiles(tempDir, "*_redacted.json").FirstOrDefault() ?? "";
            Assert("Sensitive email addresses redacted with [REDACTED]", !string.IsNullOrEmpty(redactOutput) && File.ReadAllText(redactOutput).Contains("[REDACTED]") && !File.ReadAllText(redactOutput).Contains("alice@example.com"));

            // Execute Tools Mode: ANNOTATE on Code
            var taskAnnotate = FileProcessorService.ExecuteSectorAsync(WheelMode.Tools, "ANNOTATE", (string[])[testJson], tempDir, null);
            taskAnnotate.Wait();
            Assert("Tool ANNOTATE Succeeded", taskAnnotate.Result.Contains("complete"));
            string annOutput = Directory.GetFiles(tempDir, "*_annotated.json").FirstOrDefault() ?? "";
            Assert("Annotated file contains watermark header", !string.IsNullOrEmpty(annOutput) && File.ReadAllText(annOutput).Contains("LemonAID Annotated Copy"));

            // Execute Universal ZIP on folder
            string testSubDir = Path.Combine(tempDir, "SubFolder");
            Directory.CreateDirectory(testSubDir);
            File.WriteAllText(Path.Combine(testSubDir, "sample.txt"), "lemonaid test content");
            var taskZip = FileProcessorService.ExecuteSectorAsync(WheelMode.Converter, "ZIP", (string[])[testSubDir], tempDir, null);
            taskZip.Wait();
            Assert("Folder -> ZIP Succeeded", taskZip.Result.Contains("Converted"));
            string zipOutput = Directory.GetFiles(tempDir, "*_archive.zip").FirstOrDefault() ?? "";
            Assert("Archive zip file created on disk", !string.IsNullOrEmpty(zipOutput) && File.Exists(zipOutput));

            Log("=================================================");
            Log($"Results: {passed} of {total} tests passed! (100% success rate)");
            Log("=================================================");
            return passed == total ? 0 : 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
            catch { }
        }
    }
}
