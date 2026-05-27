using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
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
            
            // 1. Validar el token contra el perfil de usuario de GitHub para asegurar autenticidad
            var userResponse = await _client.GetAsync("https://api.github.com/user");
            if (!userResponse.IsSuccessStatusCode)
            {
                return "C"; // Token no válido
            }

            var userContent = await userResponse.Content.ReadAsStringAsync();
            using var userDoc = JsonDocument.Parse(userContent);
            var login = userDoc.RootElement.GetProperty("login").GetString() ?? string.Empty;

            // Asegurar que el usuario autenticado coincide con el ingresado en la interfaz
            if (!login.Equals(Username, StringComparison.OrdinalIgnoreCase))
            {
                return "C";
            }

            // 2. Si el usuario coincide con el propietario del repositorio, es Creador (Nivel A) por definición
            if (Username.Equals(RepoOwner, StringComparison.OrdinalIgnoreCase))
            {
                return "A";
            }

            // 3. De lo contrario, verificar sus permisos colaborativos específicos en el repositorio
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

    private async Task<string?> UploadToReleasesAsync(string imageFileName, byte[] imageBytes)
    {
        SetupHeaders();
        
        long releaseId = 0;
        
        // 1. Intentar obtener el release con tag "wallpapers"
        var getReleaseUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/tags/wallpapers";
        var getResponse = await _client.GetAsync(getReleaseUrl);
        
        if (getResponse.IsSuccessStatusCode)
        {
            var getContent = await getResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(getContent);
            releaseId = doc.RootElement.GetProperty("id").GetInt64();
        }
        else
        {
            // 2. Si no existe, intentar crearlo
            var createReleaseUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
            var createPayload = new
            {
                tag_name = "wallpapers",
                name = "Wallpapers Storage",
                body = "Release for storing wallpaper assets dynamically uploaded from BlackEngine.",
                draft = false,
                prerelease = false
            };
            var createJson = JsonSerializer.Serialize(createPayload);
            var createContent = new StringContent(createJson, Encoding.UTF8, "application/json");
            
            var createResponse = await _client.PostAsync(createReleaseUrl, createContent);
            if (createResponse.IsSuccessStatusCode)
            {
                var createResultContent = await createResponse.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(createResultContent);
                releaseId = doc.RootElement.GetProperty("id").GetInt64();
            }
            else
            {
                var errorMsg = await createResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"Error al crear release: {errorMsg}");
                return null;
            }
        }
        
        // 3. Subir el asset a la release
        var uploadUrl = $"https://uploads.github.com/repos/{RepoOwner}/{RepoName}/releases/{releaseId}/assets?name={Uri.EscapeDataString(imageFileName)}";
        
        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
        request.Headers.UserAgent.ParseAdd("BlackEngine-Client");
        request.Headers.Authorization = new AuthenticationHeaderValue("token", Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        
        request.Content = new ByteArrayContent(imageBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        
        var uploadResponse = await _client.SendAsync(request);
        if (uploadResponse.IsSuccessStatusCode)
        {
            var uploadContent = await uploadResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(uploadContent);
            var browserDownloadUrl = doc.RootElement.GetProperty("browser_download_url").GetString();
            return browserDownloadUrl;
        }
        else
        {
            var errorMsg = await uploadResponse.Content.ReadAsStringAsync();
            Console.WriteLine($"Error al subir asset: {errorMsg}");
            return null;
        }
    }

    // Subir un nuevo wallpaper a GitHub
    public async Task<bool> UploadWallpaperAsync(string title, string category, string author, string deviceType, byte[] imageBytes)
    {
        if (string.IsNullOrWhiteSpace(Token)) return false;

        try
        {
            var safeTitle = title.Replace(" ", "_").ToLower();
            var imageFileName = $"{safeTitle}_{DateTime.UtcNow.Ticks}.jpg";

            // 1. Subir la imagen física directamente a GitHub Releases
            var downloadUrl = await UploadToReleasesAsync(imageFileName, imageBytes);
            if (string.IsNullOrEmpty(downloadUrl))
            {
                return false;
            }

            // 2. Leer la base de datos actual database/wallpapers.json con su respectivo SHA
            var (currentList, dbSha) = await FetchWallpapersAsync();

            // 3. Crear el nuevo registro del Wallpaper
            var newWallpaper = new Wallpaper
            {
                Title = title,
                Author = author,
                Category = category,
                ImageUrl = downloadUrl,
                HighResUrl = downloadUrl,
                Likes = new Random().Next(100, 500),
                DownloadsCount = 0,
                Uploader = Username,
                DeviceType = deviceType
            };

            currentList.Add(newWallpaper);

            // 4. Guardar base de datos actualizada en GitHub
            var serializedDb = JsonSerializer.Serialize(currentList, new JsonSerializerOptions { WriteIndented = true });
            var base64Db = Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedDb));

            SetupHeaders();
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

            var wallpaperToDelete = currentList[index];

            // 2.5 Intentar borrar el archivo de imagen física si posee SHA (opcional, por simplicidad moderamos eliminando el registro)
            // Extraer el nombre de archivo del ImageUrl para buscarlo en el release "wallpapers"
            if (!string.IsNullOrEmpty(wallpaperToDelete.ImageUrl))
            {
                var fileName = wallpaperToDelete.ImageUrl.Split('/').LastOrDefault();
                if (!string.IsNullOrEmpty(fileName))
                {
                    try
                    {
                        var getReleaseUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/tags/wallpapers";
                        var getResponse = await _client.GetAsync(getReleaseUrl);
                        if (getResponse.IsSuccessStatusCode)
                        {
                            var getContent = await getResponse.Content.ReadAsStringAsync();
                            using var doc = JsonDocument.Parse(getContent);
                            var assets = doc.RootElement.GetProperty("assets");
                            foreach (var asset in assets.EnumerateArray())
                            {
                                if (asset.GetProperty("name").GetString() == fileName)
                                {
                                    var assetId = asset.GetProperty("id").GetInt64();
                                    var deleteAssetUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/assets/{assetId}";
                                    await _client.DeleteAsync(deleteAssetUrl);
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Aviso: No se pudo eliminar el asset físico de Releases: {ex.Message}");
                    }
                }
            }

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
