using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace BlackEngine.Services;

public class AppSettings
{
    public string GithubToken { get; set; } = string.Empty;
    public string GithubUsername { get; set; } = string.Empty;
    public string GithubRepoOwner { get; set; } = string.Empty;
    public string GithubRepoName { get; set; } = string.Empty;
    public string GeminiToken { get; set; } = string.Empty;
    public List<string> FavoriteWallpapers { get; set; } = new();
}

public class LocalStorageService
{
    private readonly string _settingsFilePath;

    public LocalStorageService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "BlackEngine");
        
        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }

        _settingsFilePath = Path.Combine(appFolder, "settings.json");
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                return settings ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al cargar ajustes locales: {ex.Message}");
        }

        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al guardar ajustes locales: {ex.Message}");
        }
    }
}
