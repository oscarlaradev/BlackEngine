using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BlackEngine.Services;

public class AIVisionService
{
    private readonly HttpClient _httpClient = new HttpClient();
    private string _apiKey = string.Empty;

    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<WallpaperMetadata?> AnalyzeWallpaperAsync(byte[] imageBytes, string mimeType)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("API Key de Gemini no configurada.");

        string base64Image = Convert.ToBase64String(imageBytes);
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

        // Prompt para obtener el JSON estructurado
        string prompt = "Analiza este wallpaper y devuelve la siguiente información en formato JSON puro (sin bloques de código markdown, solo el objeto JSON valid): {\"Title\": \"Un título épico y minimalista\", \"Author\": \"Si hay firma, pon el autor, si no, pon 'Desconocido'\", \"Category\": \"Elige una: Minimalista, Abstracto, Paisaje, Cyberpunk, Fotografía, Otro\", \"DeviceType\": \"Elige: Desktop (si es apaisado u horizontal) o Mobile (si es vertical)\"}";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new
                        {
                            inlineData = new
                            {
                                mimeType = mimeType,
                                data = base64Image
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.4,
                responseMimeType = "application/json"
            }
        };

        string jsonBody = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            string errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Gemini API error: {response.StatusCode} - {errorBody}");
        }

        string responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        
        var root = doc.RootElement;
        if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
        {
            var text = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                text = text.Trim();
                if (text.StartsWith("```json"))
                {
                    text = text.Substring(7);
                    if (text.EndsWith("```"))
                        text = text.Substring(0, text.Length - 3);
                }

                try
                {
                    var result = JsonSerializer.Deserialize<WallpaperMetadata>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return result;
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }
}

public class WallpaperMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
}
