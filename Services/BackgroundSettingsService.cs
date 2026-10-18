using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace LemonAID.Services;

public sealed class AppSettingsData
{
    public bool OnboardingCompleted { get; set; }
    public bool LaunchAtLogin { get; set; } = true;
    public bool StartMinimizedHeadless { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool GlobalHookEnabled { get; set; } = true;
    public string? CustomOutputFolder { get; set; }
}

public static class BackgroundSettingsService
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "LemonAID";

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LemonAID");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDir, "settings.json");

    public static AppSettingsData Current { get; private set; } = Load();

    public static AppSettingsData Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettingsData>(json);
                if (loaded != null)
                {
                    Current = loaded;
                    return Current;
                }
            }
        }
        catch
        {
            // Fall back to default settings if file cannot be read
        }

        Current = new AppSettingsData();
        return Current;
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore write errors in restricted environments
        }
    }

    public static void ApplyStartupRegistry(bool enable)
    {
        Current.LaunchAtLogin = enable;
        Save();

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            if (key == null) return;

            if (enable)
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    // Launch silently in headless background mode on Windows sign-in
                    key.SetValue(AppRegistryName, $"\"{exePath}\" --headless");
                }
            }
            else
            {
                key.DeleteValue(AppRegistryName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Ignore if registry access is restricted by policy
        }
    }
}
