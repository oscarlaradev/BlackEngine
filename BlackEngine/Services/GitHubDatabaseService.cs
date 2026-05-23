using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlackEngine.Models;

namespace BlackEngine.Services;

public class GitHubDatabaseService
{
    private readonly HttpClient _client;
    
    public string Token { get; set; } = string.Empty;
    public string RepoOwner { get; set; } = "oscaralfredoperezlara";
    public string RepoName { get; set; } = "BlackEngine";
    public string Username { get; set; } = string.Empty;

    public GitHubDatabaseService()
    {
        _client = new HttpClient();
        _client.Timeout = TimeSpan.FromSeconds(25);
    }

    private void SetupHeaders()
    {
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("User-Agent", "BlackEngine-Client");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        
        if (!string.IsNullOrWhiteSpace(Token))
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", Token);
        }
    }

    // Comprobar privilegios del colaborador en el repositorio de GitHub
    public async Task<string> CheckUserPermissionAsync()
    {
        if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(Username))
        {
            return "C"; // Usuario normal por defecto
        }

        try
        {
            SetupHeaders();
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/collaborators/{Username}/permission";
            var response = await _client.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var permission = doc.RootElement.GetProperty("permission").GetString();

                // Nivel A: Creador (Admin)
                if (permission == "admin")
                {
                    return "A";
                }
                // Nivel B: Editor (Write)
                else if (permission == "write" || permission == "maintain")
                {
                    return "B";
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error de autenticación con GitHub: {ex.Message}");
        }

        return "C"; // Usuario normal en caso de fallo o permisos reducidos
    }

    // Obtener lista de wallpapers desde database/wallpapers.json en GitHub
    public async Task<(List<Wallpaper> list, string sha)> FetchWallpapersAsync()
    {
        var resultList = new List<Wallpaper>();
        string fileSha = string.Empty;

        try
        {
            SetupHeaders();
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contents/database/wallpapers.json";
            var response = await _client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                
                fileSha = root.GetProperty("sha").GetString() ?? string.Empty;
                var base64Content = root.GetProperty("content").GetString() ?? string.Empty;
                
                // Limpiar saltos de línea del base64 de GitHub
                base64Content = base64Content.Replace("\n", "").Replace("\r", "");
                var jsonBytes = Convert.FromBase64String(base64Content);
                var jsonString = Encoding.UTF8.GetString(jsonBytes);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var deserialized = JsonSerializer.Deserialize<List<Wallpaper>>(jsonString, options);
                
                if (deserialized != null)
                {
                    resultList = deserialized;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al leer la base de datos de GitHub: {ex.Message}");
        }

        return (resultList, fileSha);
    }

    // Subir un nuevo wallpaper a GitHub
    public async Task<bool> UploadWallpaperAsync(string title, string category, string author, byte[] imageBytes)
    {
        if (string.IsNullOrWhiteSpace(Token)) return false;

        try
        {
            SetupHeaders();
            
            // 1. Subir la imagen física al repositorio (carpeta /images/)
            var safeTitle = title.Replace(" ", "_").ToLower();
            var imageFileName = $"{safeTitle}_{DateTime.UtcNow.Ticks}.jpg";
            var imageUrlPath = $"images/{imageFileName}";
            var base64Image = Convert.ToBase64String(imageBytes);

            var imageUploadPayload = new
            {
                message = $"Add wallpaper image: {title}",
                content = base64Image
            };

            var imageJson = JsonSerializer.Serialize(imageUploadPayload);
            var imageContent = new StringContent(imageJson, Encoding.UTF8, "application/json");
            
            var imageUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contents/{imageUrlPath}";
            var imageResponse = await _client.PutAsync(imageUrl, imageContent);

            if (!imageResponse.IsSuccessStatusCode)
            {
                return false;
            }

            // Obtener URL de la imagen subida en Unsplash / Raw GitHub
            var rawImageUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/{imageUrlPath}";

            // 2. Leer la base de datos actual database/wallpapers.json con su respectivo SHA
            var (currentList, dbSha) = await FetchWallpapersAsync();

            // 3. Crear el nuevo registro del Wallpaper
            var newWallpaper = new Wallpaper
            {
                Title = title,
                Author = author,
                Category = category,
                ImageUrl = rawImageUrl,
                HighResUrl = rawImageUrl,
                Likes = new Random().Next(100, 500),
                DownloadsCount = 0,
                Uploader = Username
            };

            currentList.Add(newWallpaper);

            // 4. Guardar base de datos actualizada en GitHub
            var serializedDb = JsonSerializer.Serialize(currentList, new JsonSerializerOptions { WriteIndented = true });
            var base64Db = Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedDb));

            var dbPayload = new
            {
                message = $"Update wallpapers database: Add {title}",
                content = base64Db,
                sha = string.IsNullOrEmpty(dbSha) ? null : dbSha
            };

            var dbJson = JsonSerializer.Serialize(dbPayload);
            var dbContent = new StringContent(dbJson, Encoding.UTF8, "application/json");

            var dbUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contents/database/wallpapers.json";
            var dbResponse = await _client.PutAsync(dbUrl, dbContent);

            return dbResponse.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al subir wallpaper: {ex.Message}");
            return false;
        }
    }

    // Eliminar un wallpaper por título (Moderación - Nivel A)
    public async Task<bool> DeleteWallpaperAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(Token)) return false;

        try
        {
            SetupHeaders();

            // 1. Obtener la base de datos actual con su respectivo SHA
            var (currentList, dbSha) = await FetchWallpapersAsync();
            if (string.IsNullOrEmpty(dbSha)) return false;

            // 2. Buscar y eliminar de la lista
            int index = currentList.FindIndex(w => w.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            if (index == -1) return false;

            // También podemos intentar borrar el archivo de imagen física si posee SHA (opcional, por simplicidad moderamos eliminando el registro)
            currentList.RemoveAt(index);

            // 3. Comprometer los cambios de vuelta en GitHub
            var serializedDb = JsonSerializer.Serialize(currentList, new JsonSerializerOptions { WriteIndented = true });
            var base64Db = Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedDb));

            var dbPayload = new
            {
                message = $"Delete wallpaper from database: {title}",
                content = base64Db,
                sha = dbSha
            };

            var dbJson = JsonSerializer.Serialize(dbPayload);
            var dbContent = new StringContent(dbJson, Encoding.UTF8, "application/json");

            var dbUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contents/database/wallpapers.json";
            var dbResponse = await _client.PutAsync(dbUrl, dbContent);

            return dbResponse.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al eliminar wallpaper: {ex.Message}");
            return false;
        }
    }
}
