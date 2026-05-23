using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using BlackEngine.Models;
using BlackEngine.Services;

namespace BlackEngine.ViewModels;

public class SpotlightAction
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ShortcutText { get; set; } = string.Empty;
    public ICommand Command { get; set; } = null!;
    public object? CommandParameter { get; set; }
}

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly List<Wallpaper> _allWallpapers = new();
    private readonly GitHubDatabaseService _gitHubService;
    private readonly List<SpotlightAction> _masterActions = new();

    // Colecciones observables
    public ObservableCollection<Wallpaper> Wallpapers { get; set; } = new();
    public ObservableCollection<string> Categories { get; } = new() { "Todos", "Minimalista", "Arquitectura", "Texturas", "Líneas", "Favoritos" };
    public ObservableCollection<string> Tabs { get; } = new() { "GALERÍA", "COLOR 3D", "CONFIGURACIÓN" };
    public ObservableCollection<SpotlightAction> SpotlightActions { get; set; } = new();

    // Navegación principal
    private string _selectedTab = "GALERÍA";
    public string SelectedTab
    {
        get => _selectedTab;
        set
        {
            _selectedTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGalleryTabVisible));
            OnPropertyChanged(nameof(IsColorTabVisible));
            OnPropertyChanged(nameof(IsSettingsTabVisible));
        }
    }

    public bool IsGalleryTabVisible => SelectedTab == "GALERÍA";
    public bool IsColorTabVisible => SelectedTab == "COLOR 3D";
    public bool IsSettingsTabVisible => SelectedTab == "CONFIGURACIÓN";

    // Filtros de Galería
    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    private string _selectedCategory = "Todos";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            _selectedCategory = value;
            OnPropertyChanged();
            ApplyFilters();
        }
    }

    // Vista previa de Wallpaper
    private Wallpaper? _selectedWallpaper;
    public Wallpaper? SelectedWallpaper
    {
        get => _selectedWallpaper;
        set
        {
            _selectedWallpaper = value;
            OnPropertyChanged();
        }
    }

    // Rueda de Color 3D e Indicador de Color Sólido
    private Color _selectedSolidColor = Color.Parse("#FFFFFF");
    public Color SelectedSolidColor
    {
        get => _selectedSolidColor;
        set
        {
            _selectedSolidColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HexColorString));
            OnPropertyChanged(nameof(SelectedSolidColorBrush));
        }
    }

    public string HexColorString => $"#{SelectedSolidColor.R:X2}{SelectedSolidColor.G:X2}{SelectedSolidColor.B:X2}";
    public SolidColorBrush SelectedSolidColorBrush => new(SelectedSolidColor);

    // Spotlight Command Center (Raycast Style)
    private bool _isSpotlightOpen;
    public bool IsSpotlightOpen
    {
        get => _isSpotlightOpen;
        set
        {
            _isSpotlightOpen = value;
            OnPropertyChanged();
        }
    }

    private string _spotlightQuery = string.Empty;
    public string SpotlightQuery
    {
        get => _spotlightQuery;
        set
        {
            _spotlightQuery = value;
            OnPropertyChanged();
            FilterSpotlightActions();
        }
    }

    // Credenciales de Seguridad de GitHub (Base de datos local segura)
    private string _githubUsername = string.Empty;
    public string GithubUsername
    {
        get => _githubUsername;
        set
        {
            _githubUsername = value;
            OnPropertyChanged();
        }
    }

    private string _githubToken = string.Empty;
    public string GithubToken
    {
        get => _githubToken;
        set
        {
            _githubToken = value;
            OnPropertyChanged();
        }
    }

    private string _repoOwner = "oscaralfredoperezlara";
    public string RepoOwner
    {
        get => _repoOwner;
        set
        {
            _repoOwner = value;
            OnPropertyChanged();
        }
    }

    private string _repoName = "BlackEngine";
    public string RepoName
    {
        get => _repoName;
        set
        {
            _repoName = value;
            OnPropertyChanged();
        }
    }

    private string _userRole = "C"; // C: Usuario, B: Editor, A: Creador
    public string UserRole
    {
        get => _userRole;
        set
        {
            _userRole = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UserRoleText));
            OnPropertyChanged(nameof(IsLevelA));
            OnPropertyChanged(nameof(IsLevelBOrAbove));
            RebuildMasterActions(); // Re-indexar comandos Spotlight según privilegios
        }
    }

    public string UserRoleText => UserRole switch
    {
        "A" => "NIVEL A: CREADOR ABSOLUTO (Acceso total)",
        "B" => "NIVEL B: EDITOR AUTORIZADO (Permiso de subida)",
        _ => "NIVEL C: USUARIO ESTÁNDAR (Lectura)"
    };

    public bool IsLevelA => UserRole == "A";
    public bool IsLevelBOrAbove => UserRole == "A" || UserRole == "B";

    // Campos de Subida de Wallpaper para Admin/Editor
    private string _newWpTitle = string.Empty;
    public string NewWpTitle
    {
        get => _newWpTitle;
        set
        {
            _newWpTitle = value;
            OnPropertyChanged();
        }
    }

    private string _newWpAuthor = string.Empty;
    public string NewWpAuthor
    {
        get => _newWpAuthor;
        set
        {
            _newWpAuthor = value;
            OnPropertyChanged();
        }
    }

    private string _newWpCategory = "Minimalista";
    public string NewWpCategory
    {
        get => _newWpCategory;
        set
        {
            _newWpCategory = value;
            OnPropertyChanged();
        }
    }

    private string _newWpImagePath = string.Empty;
    public string NewWpImagePath
    {
        get => _newWpImagePath;
        set
        {
            _newWpImagePath = value;
            OnPropertyChanged();
        }
    }

    // Configuraciones de la Aplicación
    private string _downloadQuality = "Original"; // Original, Media, Baja
    public string DownloadQuality
    {
        get => _downloadQuality;
        set
        {
            _downloadQuality = value;
            OnPropertyChanged();
        }
    }

    private string _downloadPath = string.Empty;
    public string DownloadPath
    {
        get => _downloadPath;
        set
        {
            _downloadPath = value;
            OnPropertyChanged();
        }
    }

    private bool _pureContrastMode;
    public bool PureContrastMode
    {
        get => _pureContrastMode;
        set
        {
            _pureContrastMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowBackgroundString));
        }
    }

    public string WindowBackgroundString => PureContrastMode ? "#000000" : "#08080A";

    private string _cacheSizeText = "Calculando...";
    public string CacheSizeText
    {
        get => _cacheSizeText;
        set
        {
            _cacheSizeText = value;
            OnPropertyChanged();
        }
    }

    // Notificaciones Toast
    private string _toastMessage = string.Empty;
    public string ToastMessage
    {
        get => _toastMessage;
        set
        {
            _toastMessage = value;
            OnPropertyChanged();
        }
    }

    private bool _isToastVisible;
    public bool IsToastVisible
    {
        get => _isToastVisible;
        set
        {
            _isToastVisible = value;
            OnPropertyChanged();
        }
    }

    // Comandos de Interfaz
    public ICommand SwitchTabCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand SelectWallpaperCommand { get; }
    public ICommand ClosePreviewCommand { get; }
    public ICommand DownloadSolidColorCommand { get; }
    public ICommand ClearCacheCommand { get; }

    // Comandos de Seguridad & Command Center
    public ICommand ToggleSpotlightCommand { get; }
    public ICommand ExecuteSpotlightActionCommand { get; }
    public ICommand AuthenticateGithubCommand { get; }
    public ICommand UploadNewWallpaperCommand { get; }
    public ICommand DeleteWallpaperCommand { get; }

    public MainWindowViewModel()
    {
        // Instanciar servicio descentralizado
        _gitHubService = new GitHubDatabaseService();

        // Inicializar Comandos Estándar
        SwitchTabCommand = new RelayCommand<string>(tab => SelectedTab = tab ?? "GALERÍA");
        DownloadCommand = new RelayCommand<Wallpaper>(async wp => await DownloadWallpaperAsync(wp));
        ToggleFavoriteCommand = new RelayCommand<Wallpaper>(ToggleFavorite);
        SelectWallpaperCommand = new RelayCommand<Wallpaper>(SelectWallpaper);
        ClosePreviewCommand = new RelayCommand<object>(_ => ClosePreview());
        DownloadSolidColorCommand = new RelayCommand<object>(async _ => await DownloadSolidColorAsync());
        ClearCacheCommand = new RelayCommand<object>(_ => ClearCache());

        // Comandos Especializados
        ToggleSpotlightCommand = new RelayCommand<object>(_ => { IsSpotlightOpen = !IsSpotlightOpen; SpotlightQuery = string.Empty; });
        ExecuteSpotlightActionCommand = new RelayCommand<SpotlightAction>(ExecuteSpotlightAction);
        AuthenticateGithubCommand = new RelayCommand<object>(async _ => await AuthenticateGithubAsync());
        UploadNewWallpaperCommand = new RelayCommand<object>(async _ => await UploadNewWallpaperAsync());
        DeleteWallpaperCommand = new RelayCommand<Wallpaper>(async wp => await DeleteWallpaperAsync(wp));

        // Inicializar Carpeta de Destino
        var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BlackEngine");
        DownloadPath = defaultFolder;

        InitializeWallpapers();
        _ = SyncFeedFromGitHubAsync(); // Intentar sincronizar con nube, si falla usa fallback
        CalculateCacheSize();
        RebuildMasterActions();
    }

    private void InitializeWallpapers()
    {
        // Wallpapers locales de respaldo inicial
        var fallbackList = new[]
        {
            new Wallpaper
            {
                Title = "Silence in Mist",
                Author = "@minimal_fog",
                Category = "Minimalista",
                ImageUrl = "https://images.unsplash.com/photo-1464822759023-fed622ff2c3b?w=400&q=80",
                HighResUrl = "https://images.unsplash.com/photo-1464822759023-fed622ff2c3b?q=100",
                Likes = 142,
                DownloadsCount = 12
            },
            new Wallpaper
            {
                Title = "Brutalist Concrete",
                Author = "@architect_bnw",
                Category = "Arquitectura",
                ImageUrl = "https://images.unsplash.com/photo-1600585154340-be6161a56a0c?w=400&q=80",
                HighResUrl = "https://images.unsplash.com/photo-1600585154340-be6161a56a0c?q=100",
                Likes = 285,
                DownloadsCount = 34
            },
            new Wallpaper
            {
                Title = "Obsidian Dunes",
                Author = "@deserts_bnw",
                Category = "Texturas",
                ImageUrl = "https://images.unsplash.com/photo-1509316975850-ff9c5deb0cd9?w=400&q=80",
                HighResUrl = "https://images.unsplash.com/photo-1509316975850-ff9c5deb0cd9?q=100",
                Likes = 523,
                DownloadsCount = 98
            }
        };

        foreach (var wp in fallbackList)
        {
            _allWallpapers.Add(wp);
        }

        ApplyFilters();
    }

    // Sincronizar galería desde GitHub
    private async Task SyncFeedFromGitHubAsync()
    {
        try
        {
            _gitHubService.RepoOwner = RepoOwner;
            _gitHubService.RepoName = RepoName;

            var (gitList, sha) = await _gitHubService.FetchWallpapersAsync();

            if (gitList.Count > 0)
            {
                _allWallpapers.Clear();
                foreach (var wp in gitList)
                {
                    _allWallpapers.Add(wp);
                }
                ApplyFilters();
                ShowToast("Feed sincronizado desde la nube!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error de sincronización, usando fallbacks locales: {ex.Message}");
        }

        // Carga de bitmaps asíncrona
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        foreach (var wp in _allWallpapers)
        {
            try
            {
                if (wp.ImageBitmap == null)
                {
                    var imageBytes = await client.GetByteArrayAsync(wp.ImageUrl);
                    using var stream = new MemoryStream(imageBytes);
                    wp.ImageBitmap = new Bitmap(stream);
                }
            }
            catch
            {
                wp.ImageBitmap = GenerateMinimalFallback(wp.Title);
            }
        }
    }

    private Bitmap GenerateMinimalFallback(string title)
    {
        int width = 500;
        int height = 500;
        var renderTarget = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = renderTarget.CreateDrawingContext())
        {
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#121212")), null, new Rect(0, 0, width, height));
            var mainPen = new Pen(new SolidColorBrush(Color.Parse("#222222")), 1.5);
            var accentPen = new Pen(new SolidColorBrush(Color.Parse("#FFFFFF"), 0.3), 1.0);
            var center = new Point(width / 2.0, height / 2.0);
            for (int r = 50; r <= 220; r += 45)
            {
                context.DrawEllipse(null, mainPen, center, r, r);
            }
            context.DrawLine(mainPen, new Point(0, 0), new Point(width, height));
            context.DrawLine(mainPen, new Point(width, 0), new Point(0, height));
            context.DrawEllipse(null, accentPen, center, 30, 30);
        }
        return renderTarget;
    }

    // Autenticación de GitHub y cálculo criptográfico de privilegios
    private async Task AuthenticateGithubAsync()
    {
        if (string.IsNullOrWhiteSpace(GithubToken) || string.IsNullOrWhiteSpace(GithubUsername))
        {
            ShowToast("Ingresa credenciales completas.");
            return;
        }

        try
        {
            ShowToast("Autenticando en GitHub Cloud...");
            
            _gitHubService.Token = GithubToken;
            _gitHubService.Username = GithubUsername;
            _gitHubService.RepoOwner = RepoOwner;
            _gitHubService.RepoName = RepoName;

            var role = await _gitHubService.CheckUserPermissionAsync();
            UserRole = role;

            ShowToast($"¡Bienvenido! {UserRoleText}");
            
            // Re-sincronizar el feed ahora que poseemos privilegios de moderación
            await SyncFeedFromGitHubAsync();
        }
        catch (Exception ex)
        {
            ShowToast($"Error de sesión: {ex.Message}");
            UserRole = "C";
        }
    }

    // Subir un wallpaper (Nivel A y B)
    private async Task UploadNewWallpaperAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWpTitle) || string.IsNullOrWhiteSpace(NewWpAuthor) || string.IsNullOrWhiteSpace(NewWpImagePath))
        {
            ShowToast("Completa los campos obligatorios.");
            return;
        }

        try
        {
            ShowToast("Procesando imagen...");
            byte[] imageBytes;

            // Determinar si es un archivo local o una URL web
            if (NewWpImagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var client = new HttpClient();
                imageBytes = await client.GetByteArrayAsync(NewWpImagePath);
            }
            else
            {
                if (!File.Exists(NewWpImagePath))
                {
                    ShowToast("El archivo local no existe.");
                    return;
                }
                imageBytes = await File.ReadAllBytesAsync(NewWpImagePath);
            }

            ShowToast("Confirmando carga en GitHub...");
            var success = await _gitHubService.UploadWallpaperAsync(NewWpTitle, NewWpCategory, NewWpAuthor, imageBytes);

            if (success)
            {
                ShowToast("¡Wallpaper subido con éxito!");
                
                // Limpiar campos
                NewWpTitle = string.Empty;
                NewWpAuthor = string.Empty;
                NewWpImagePath = string.Empty;

                // Forzar resincronización de galería
                await SyncFeedFromGitHubAsync();
            }
            else
            {
                ShowToast("Error al subir a GitHub. Verifica tus privilegios.");
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Error de subida: {ex.Message}");
        }
    }

    private void SelectWallpaper(Wallpaper? wp)
    {
        if (wp == null) return;
        SelectedWallpaper = wp;
        RebuildMasterActions();
    }

    private void ClosePreview()
    {
        SelectedWallpaper = null;
        RebuildMasterActions();
    }

    // Eliminar / Banear wallpaper de la base de datos (Nivel A)
    private async Task DeleteWallpaperAsync(Wallpaper? wp)
    {
        if (wp == null) return;
        if (!IsLevelA)
        {
            ShowToast("Restringido: Requiere Nivel A (Creador).");
            return;
        }

        try
        {
            ShowToast($"Eliminando: {wp.Title}...");
            var success = await _gitHubService.DeleteWallpaperAsync(wp.Title);

            if (success)
            {
                ShowToast("¡Wallpaper eliminado globalmente!");
                ClosePreview();
                await SyncFeedFromGitHubAsync();
            }
            else
            {
                ShowToast("Fallo al eliminar de GitHub. Verifica tu token.");
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Error: {ex.Message}");
        }
    }

    // Lógica del Spotlight Command Center
    private void RebuildMasterActions()
    {
        _masterActions.Clear();

        // 1. Acciones Generales (Para todos los usuarios)
        _masterActions.Add(new SpotlightAction { Name = "Ir a Pestaña: Galería", Category = "Navegación", ShortcutText = "G", Command = SwitchTabCommand, CommandParameter = "GALERÍA" });
        _masterActions.Add(new SpotlightAction { Name = "Ir a Pestaña: Rueda 3D", Category = "Navegación", ShortcutText = "C", Command = SwitchTabCommand, CommandParameter = "COLOR 3D" });
        _masterActions.Add(new SpotlightAction { Name = "Ir a Pestaña: Configuración", Category = "Navegación", ShortcutText = "S", Command = SwitchTabCommand, CommandParameter = "CONFIGURACIÓN" });
        
        _masterActions.Add(new SpotlightAction { Name = "Activar Contraste AMOLED", Category = "Aspecto", ShortcutText = "M", Command = new RelayCommand<object>(_ => PureContrastMode = true) });
        _masterActions.Add(new SpotlightAction { Name = "Desactivar Contraste AMOLED", Category = "Aspecto", ShortcutText = "Shift + M", Command = new RelayCommand<object>(_ => PureContrastMode = false) });

        _masterActions.Add(new SpotlightAction { Name = "Establecer Calidad: Original (4K)", Category = "Ajustes", ShortcutText = "Q4", Command = new RelayCommand<object>(_ => DownloadQuality = "Original") });
        _masterActions.Add(new SpotlightAction { Name = "Establecer Calidad: Media (1080p)", Category = "Ajustes", ShortcutText = "Q2", Command = new RelayCommand<object>(_ => DownloadQuality = "Media") });
        _masterActions.Add(new SpotlightAction { Name = "Establecer Calidad: Baja (720p)", Category = "Ajustes", ShortcutText = "Q1", Command = new RelayCommand<object>(_ => DownloadQuality = "Baja") });

        _masterActions.Add(new SpotlightAction { Name = "Limpiar Carpeta de Descargas", Category = "Mantenimiento", ShortcutText = "X", Command = ClearCacheCommand });
        _masterActions.Add(new SpotlightAction { Name = "Generar y Guardar Color Sólido", Category = "Color 3D", ShortcutText = "Enter", Command = DownloadSolidColorCommand });
        
        // 2. Acciones Administrativas (Nivel B y A)
        if (IsLevelBOrAbove)
        {
            _masterActions.Add(new SpotlightAction { Name = "Abrir Formulario de Subida", Category = "Herramientas de Creador", ShortcutText = "U", Command = new RelayCommand<object>(_ => { SelectedTab = "CONFIGURACIÓN"; ShowToast("Formulario listo."); }) });
        }

        // 3. Acciones de Moderador Principal (Nivel A)
        if (IsLevelA)
        {
            _masterActions.Add(new SpotlightAction { Name = "Eliminar Selección (Moderación)", Category = "Herramientas de Admin", ShortcutText = "Backspace", Command = DeleteWallpaperCommand, CommandParameter = SelectedWallpaper });
        }

        FilterSpotlightActions();
    }

    private void FilterSpotlightActions()
    {
        SpotlightActions.Clear();
        foreach (var action in _masterActions)
        {
            if (string.IsNullOrWhiteSpace(SpotlightQuery))
            {
                SpotlightActions.Add(action);
            }
            else if (action.Name.Contains(SpotlightQuery, StringComparison.OrdinalIgnoreCase) || 
                     action.Category.Contains(SpotlightQuery, StringComparison.OrdinalIgnoreCase))
            {
                SpotlightActions.Add(action);
            }
        }
    }

    private void ExecuteSpotlightAction(SpotlightAction? action)
    {
        if (action == null) return;
        IsSpotlightOpen = false; // Cerrar panel
        action.Command.Execute(action.CommandParameter);
    }

    private void ApplyFilters()
    {
        Wallpapers.Clear();
        foreach (var wp in _allWallpapers)
        {
            if (SelectedCategory != "Todos")
            {
                if (SelectedCategory == "Favoritos")
                {
                    if (!wp.IsFavorite) continue;
                }
                else if (wp.Category != SelectedCategory)
                {
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var matchTitle = wp.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
                var matchAuthor = wp.Author.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
                var matchCategory = wp.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
                if (!matchTitle && !matchAuthor && !matchCategory)
                {
                    continue;
                }
            }

            Wallpapers.Add(wp);
        }
    }

    private void ToggleFavorite(Wallpaper? wp)
    {
        if (wp == null) return;
        wp.IsFavorite = !wp.IsFavorite;
        
        if (SelectedCategory == "Favoritos")
        {
            ApplyFilters();
        }
    }

    private async Task DownloadWallpaperAsync(Wallpaper? wp)
    {
        if (wp == null) return;

        try
        {
            ShowToast("Iniciando descarga física...");

            if (!Directory.Exists(DownloadPath))
            {
                Directory.CreateDirectory(DownloadPath);
            }

            var fileName = $"{wp.Title.Replace(" ", "_")}_HD.jpg";
            var destPath = Path.Combine(DownloadPath, fileName);

            if (wp.ImageUrl.Contains("raw.githubusercontent.com"))
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(25);
                var imageBytes = await client.GetByteArrayAsync(wp.HighResUrl);
                await File.WriteAllBytesAsync(destPath, imageBytes);
            }
            else if (wp.ImageBitmap != null)
            {
                // Fallback / Imagen local
                wp.ImageBitmap.Save(destPath);
            }

            wp.DownloadsCount++;
            ShowToast("¡Wallpaper guardado en disco!");
            CalculateCacheSize();
            OpenFolder(DownloadPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fallo de descarga remota: {ex.Message}");
            try
            {
                if (wp.ImageBitmap != null)
                {
                    var fallbackFileName = $"{wp.Title.Replace(" ", "_")}_Local.png";
                    var fallbackPath = Path.Combine(DownloadPath, fallbackFileName);
                    wp.ImageBitmap.Save(fallbackPath);
                    ShowToast("¡Guardado desde copia en memoria!");
                    CalculateCacheSize();
                    OpenFolder(DownloadPath);
                }
            }
            catch (Exception innerEx)
            {
                ShowToast($"Error de guardado: {innerEx.Message}");
            }
        }
    }

    private async Task DownloadSolidColorAsync()
    {
        try
        {
            var hex = HexColorString;
            ShowToast($"Generando sólido {hex}...");

            int width = 3840; // 4K
            int height = 2160;

            if (DownloadQuality == "Media")
            {
                width = 1920;
                height = 1080;
            }
            else if (DownloadQuality == "Baja")
            {
                width = 1280;
                height = 720;
            }

            if (!Directory.Exists(DownloadPath))
            {
                Directory.CreateDirectory(DownloadPath);
            }

            var fileName = $"Solid_{hex.Replace("#", "")}.png";
            var destPath = Path.Combine(DownloadPath, fileName);

            var renderTarget = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            using (var context = renderTarget.CreateDrawingContext())
            {
                context.DrawRectangle(new SolidColorBrush(SelectedSolidColor), null, new Rect(0, 0, width, height));
            }

            renderTarget.Save(destPath);

            ShowToast("¡Fondo sólido descargado!");
            CalculateCacheSize();
            OpenFolder(DownloadPath);
        }
        catch (Exception ex)
        {
            ShowToast($"Error: {ex.Message}");
        }
    }

    private void CalculateCacheSize()
    {
        try
        {
            if (!Directory.Exists(DownloadPath))
            {
                CacheSizeText = "0 KB";
                return;
            }

            var files = Directory.GetFiles(DownloadPath);
            long totalBytes = 0;
            foreach (var file in files)
            {
                totalBytes += new FileInfo(file).Length;
            }

            double mb = totalBytes / 1024.0 / 1024.0;
            CacheSizeText = $"{mb:F2} MB ({files.Length} archivos)";
        }
        catch
        {
            CacheSizeText = "No disponible";
        }
    }

    private void ClearCache()
    {
        try
        {
            if (Directory.Exists(DownloadPath))
            {
                var files = Directory.GetFiles(DownloadPath);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                ShowToast("¡Caché física liberada!");
                CalculateCacheSize();
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Error al limpiar: {ex.Message}");
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", path);
            }
            else if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", path);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al abrir directorio nativo: {ex.Message}");
        }
    }

    private async void ShowToast(string message)
    {
        ToastMessage = message;
        IsToastVisible = true;

        await Task.Delay(3500);

        if (ToastMessage == message)
        {
            IsToastVisible = false;
        }
    }

    // INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute((T?)parameter);

    public void Execute(object? parameter) => _execute((T?)parameter);

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}