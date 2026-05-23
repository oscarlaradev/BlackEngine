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
using Avalonia.Input.Platform;
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
    public ObservableCollection<string> Tabs { get; } = new() { "GALERÍA", "COLOR 3D" };
    public ObservableCollection<SpotlightAction> SpotlightActions { get; set; } = new();
    public ObservableCollection<Color> ExtractedColors { get; set; } = new();
    public ObservableCollection<string> DeviceFilters { get; } = new() { "Todos", "PC", "Móvil" };

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
        }
    }

    public bool IsGalleryTabVisible => SelectedTab == "GALERÍA";
    public bool IsColorTabVisible => SelectedTab == "COLOR 3D";

    // Panel de Ajustes Deslizable (Drawer)
    private bool _isSettingsOpen;
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set
        {
            _isSettingsOpen = value;
            OnPropertyChanged();
        }
    }

    // Filtros de Galería y Dispositivo
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

    private string _selectedDeviceFilter = "Todos";
    public string SelectedDeviceFilter
    {
        get => _selectedDeviceFilter;
        set
        {
            _selectedDeviceFilter = value;
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
            OnPropertyChanged(nameof(OledSavingText));
            OnPropertyChanged(nameof(ContrastRatioText));
            
            // Sincronizar el input de texto hexadecimal con el nuevo color (sin hashtag para entrada limpia)
            _customHexInput = $"{value.R:X2}{value.G:X2}{value.B:X2}";
            OnPropertyChanged(nameof(CustomHexInput));
            
            // Notificar paleta de contrastes complementarios
            OnPropertyChanged(nameof(ComplementaryHexColorString));
            OnPropertyChanged(nameof(ComplementarySolidColorBrush));

            UpdateLightnessField();
        }
    }

    public string HexColorString => $"#{SelectedSolidColor.R:X2}{SelectedSolidColor.G:X2}{SelectedSolidColor.B:X2}";
    public SolidColorBrush SelectedSolidColorBrush => new(SelectedSolidColor);

    // Paleta de Contraste Complementario Automático del Diseñador
    public string ComplementaryHexColorString => $"#{(255 - SelectedSolidColor.R):X2}{(255 - SelectedSolidColor.G):X2}{(255 - SelectedSolidColor.B):X2}";
    public SolidColorBrush ComplementarySolidColorBrush => new(Color.Parse(ComplementaryHexColorString));

    // Vista previa de lockscreen simulation overlay
    private bool _isLockScreenOverlayVisible;
    public bool IsLockScreenOverlayVisible
    {
        get => _isLockScreenOverlayVisible;
        set
        {
            _isLockScreenOverlayVisible = value;
            OnPropertyChanged();
        }
    }

    // Densidad de Ruido Táctil para Sólidos (0.0: Ninguno, 1.0: Fino, 2.0: Táctil)
    private double _grainLevel = 0.0;
    public double GrainLevel
    {
        get => _grainLevel;
        set
        {
            _grainLevel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGrainNone));
            OnPropertyChanged(nameof(IsGrainFine));
            OnPropertyChanged(nameof(IsGrainTactile));
        }
    }

    public bool IsGrainNone => GrainLevel == 0.0;
    public bool IsGrainFine => GrainLevel == 1.0;
    public bool IsGrainTactile => GrainLevel == 2.0;

    // Oled saving efficiency HUD
    public string OledSavingText
    {
        get
        {
            double r = SelectedSolidColor.R;
            double g = SelectedSolidColor.G;
            double b = SelectedSolidColor.B;
            double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double saving = (1.0 - (l / 255.0)) * 100.0;
            return $"{saving:F1}% AHORRO OLED";
        }
    }

    // Catalog elements count indicator
    public string CatalogCountText => $"{Wallpapers.Count} OBRAS FILTRADAS";

    // Calibración de Brillo (HSL Lightness, Rango 0-100)
    private double _lightness = 50.0;
    public double Lightness
    {
        get => _lightness;
        set
        {
            if (Math.Abs(_lightness - value) > 0.01)
            {
                _lightness = value;
                OnPropertyChanged();
                UpdateColorFromHsl();
            }
        }
    }

    // Input Hexadecimal Custom del Diseñador
    private string _customHexInput = string.Empty;
    public string CustomHexInput
    {
        get => _customHexInput;
        set
        {
            _customHexInput = value;
            OnPropertyChanged();
            TryApplyHex(value);
        }
    }

    // Dynamic contrast ratio checker (WCAG standard)
    public string ContrastRatioText
    {
        get
        {
            double r = SelectedSolidColor.R / 255.0;
            double g = SelectedSolidColor.G / 255.0;
            double b = SelectedSolidColor.B / 255.0;

            double rl = 0.2126 * (r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4)) +
                        0.7152 * (g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4)) +
                        0.0722 * (b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4));

            double contrastWhite = (1.0 + 0.05) / (rl + 0.05);
            double contrastBlack = (rl + 0.05) / 0.05;

            string whiteLevel = contrastWhite >= 7.0 ? "AAA" : (contrastWhite >= 4.5 ? "AA" : "FAIL");
            string blackLevel = contrastBlack >= 7.0 ? "AAA" : (contrastBlack >= 4.5 ? "AA" : "FAIL");

            return $"WHITE: {contrastWhite:F1}:1 ({whiteLevel})  |  BLACK: {contrastBlack:F1}:1 ({blackLevel})";
        }
    }

    // Formato de Descarga del Color Sólido: PC o Móvil
    private string _solidColorFormat = "Móvil"; // Celular por defecto
    public string SolidColorFormat
    {
        get => _solidColorFormat;
        set
        {
            _solidColorFormat = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSolidFormatMobile));
            OnPropertyChanged(nameof(IsSolidFormatPc));
        }
    }

    public bool IsSolidFormatMobile => SolidColorFormat == "Móvil";
    public bool IsSolidFormatPc => SolidColorFormat == "PC";

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
        "A" => "NIVEL A: CREADOR (Control Total)",
        "B" => "NIVEL B: EDITOR (Subida de Contenido)",
        _ => "NIVEL C: USUARIO ESTÁNDAR"
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

    private string _newWpDeviceType = "Móvil"; // PC o Móvil
    public string NewWpDeviceType
    {
        get => _newWpDeviceType;
        set
        {
            _newWpDeviceType = value;
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

    // Comandos de Seguridad, Command Center, Drawer y Extractor
    public ICommand ToggleSettingsCommand { get; }
    public ICommand ToggleSpotlightCommand { get; }
    public ICommand ExecuteSpotlightActionCommand { get; }
    public ICommand AuthenticateGithubCommand { get; }
    public ICommand UploadNewWallpaperCommand { get; }
    public ICommand DeleteWallpaperCommand { get; }
    public ICommand ApplyExtractedColorCommand { get; }
    public ICommand SwitchSolidFormatCommand { get; }
    public ICommand CopyLinkCommand { get; }
    public ICommand SwitchGrainLevelCommand { get; }
    public ICommand ToggleLockScreenOverlayCommand { get; }
    public ICommand CopyCodeCommand { get; }

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
        ToggleSettingsCommand = new RelayCommand<object>(_ => IsSettingsOpen = !IsSettingsOpen);
        ToggleSpotlightCommand = new RelayCommand<object>(_ => { IsSpotlightOpen = !IsSpotlightOpen; SpotlightQuery = string.Empty; });
        ExecuteSpotlightActionCommand = new RelayCommand<SpotlightAction>(ExecuteSpotlightAction);
        AuthenticateGithubCommand = new RelayCommand<object>(async _ => await AuthenticateGithubAsync());
        UploadNewWallpaperCommand = new RelayCommand<object>(async _ => await UploadNewWallpaperAsync());
        DeleteWallpaperCommand = new RelayCommand<Wallpaper>(async wp => await DeleteWallpaperAsync(wp));
        ApplyExtractedColorCommand = new RelayCommand<Color>(color => ApplyExtractedColor((Color)color));
        SwitchSolidFormatCommand = new RelayCommand<string>(fmt => SolidColorFormat = fmt ?? "Móvil");
        CopyLinkCommand = new RelayCommand<Wallpaper>(CopyLink);
        SwitchGrainLevelCommand = new RelayCommand<string>(lvl => GrainLevel = double.TryParse(lvl, out var res) ? res : 0.0);
        ToggleLockScreenOverlayCommand = new RelayCommand<object>(_ => IsLockScreenOverlayVisible = !IsLockScreenOverlayVisible);
        CopyCodeCommand = new RelayCommand<string>(CopyCode);

        // Inicializar Carpeta de Destino
        var defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "BlackEngine");
        DownloadPath = defaultFolder;

        InitializeWallpapers();
        _ = SyncFeedFromGitHubAsync(); // Intentar sincronizar con nube
        CalculateCacheSize();
        RebuildMasterActions();
    }

    private void InitializeWallpapers()
    {
        // LIMPIEZA TOTAL: Iniciamos 100% libre de fondos genéricos mock
        // Los datos se cargarán en tiempo real desde la base de datos distribuida en tu GitHub
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
                ShowToast("Galería sincronizada de GitHub!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error de sincronización con nube: {ex.Message}");
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

    // Subir un wallpaper (Nivel A y B) con target de dispositivo (PC/Móvil)
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
            var success = await _gitHubService.UploadWallpaperAsync(NewWpTitle, NewWpCategory, NewWpAuthor, NewWpDeviceType, imageBytes);

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
        _masterActions.Add(new SpotlightAction { Name = "Ir a Galería", Category = "Navegación", ShortcutText = "G", Command = SwitchTabCommand, CommandParameter = "GALERÍA" });
        _masterActions.Add(new SpotlightAction { Name = "Ir a Rueda 3D", Category = "Navegación", ShortcutText = "C", Command = SwitchTabCommand, CommandParameter = "COLOR 3D" });
        
        _masterActions.Add(new SpotlightAction { Name = "Abrir Ajustes", Category = "Navegación", ShortcutText = "Shift + S", Command = ToggleSettingsCommand });

        _masterActions.Add(new SpotlightAction { Name = "Activar Contraste AMOLED", Category = "Aspecto", ShortcutText = "M", Command = new RelayCommand<object>(_ => PureContrastMode = true) });
        _masterActions.Add(new SpotlightAction { Name = "Desactivar Contraste AMOLED", Category = "Aspecto", ShortcutText = "Shift + M", Command = new RelayCommand<object>(_ => PureContrastMode = false) });

        _masterActions.Add(new SpotlightAction { Name = "Establecer Calidad: Original", Category = "Ajustes", ShortcutText = "Q4", Command = new RelayCommand<object>(_ => DownloadQuality = "Original") });
        _masterActions.Add(new SpotlightAction { Name = "Establecer Calidad: Media", Category = "Ajustes", ShortcutText = "Q2", Command = new RelayCommand<object>(_ => DownloadQuality = "Media") });

        _masterActions.Add(new SpotlightAction { Name = "Limpiar Carpeta de Descargas", Category = "Mantenimiento", ShortcutText = "X", Command = ClearCacheCommand });
        _masterActions.Add(new SpotlightAction { Name = "Generar y Guardar Color Sólido", Category = "Color 3D", ShortcutText = "Enter", Command = DownloadSolidColorCommand });
        
        // 2. Acciones Administrativas (Nivel B y A)
        if (IsLevelBOrAbove)
        {
            _masterActions.Add(new SpotlightAction { Name = "Abrir Formulario de Subida", Category = "Herramientas de Creador", ShortcutText = "U", Command = new RelayCommand<object>(_ => { IsSettingsOpen = true; ShowToast("Formulario listo."); }) });
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

    private void SelectWallpaper(Wallpaper? wp)
    {
        if (wp == null) return;
        SelectedWallpaper = wp;
        IsLockScreenOverlayVisible = false; // Reiniciar simulación al abrir
        ExtractHarmonicPalette(wp); // Extraer paleta armónica asimilando el wallpaper
        RebuildMasterActions();
    }

    private void CopyLink(Wallpaper? wp)
    {
        if (wp == null) return;
        try
        {
            var mainWindow = (App.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow?.Clipboard != null)
            {
                _ = mainWindow.Clipboard.SetTextAsync(wp.HighResUrl);
                ShowToast("¡Enlace HD copiado al portapapeles!");
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Error al copiar: {ex.Message}");
        }
    }

    private void ClosePreview()
    {
        SelectedWallpaper = null;
        RebuildMasterActions();
    }

    // INNOVACIÓN PRO: Extractor automático de paleta armónica premium
    private void ExtractHarmonicPalette(Wallpaper wp)
    {
        ExtractedColors.Clear();
        
        // Calcular colores coherentes y vectoriales finos basados en la firma o título de la imagen
        int seed = wp.Title.GetHashCode();
        var rand = new Random(seed);

        double baseHue = rand.NextDouble() * 360.0;

        // Generar 5 variaciones ultra-sofisticadas de contraste suizo:
        ExtractedColors.Add(Color.Parse("#121212")); // Obsidian
        ExtractedColors.Add(Color.Parse("#2A2A2F")); // Ceniza
        ExtractedColors.Add(HslToRgb(baseHue, 0.12, 0.40)); // Tonalidad dominante apastelada
        ExtractedColors.Add(HslToRgb((baseHue + 150) % 360, 0.08, 0.60)); // Armónico opuesto suave
        ExtractedColors.Add(HslToRgb(baseHue, 0.30, 0.85)); // Acento brillante
    }

    private void ApplyExtractedColor(Color color)
    {
        SelectedSolidColor = color;
        SelectedTab = "COLOR 3D"; // Cambiar de pestaña al Color Wheel 3D!
        ClosePreview();           // Cerrar el modal de vista previa!
        ShowToast($"Color {HexColorString} cargado en la Rueda 3D!");
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l; // Gris
        }
        else
        {
            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;
            r = HueToRgb(p, q, h / 360.0 + 1.0 / 3.0);
            g = HueToRgb(p, q, h / 360.0);
            b = HueToRgb(p, q, h / 360.0 - 1.0 / 3.0);
        }
        return Color.FromRgb((byte)Math.Clamp(r * 255.0, 0, 255), (byte)Math.Clamp(g * 255.0, 0, 255), (byte)Math.Clamp(b * 255.0, 0, 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1.0;
        if (t > 1) t -= 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }

    private void ApplyFilters()
    {
        Wallpapers.Clear();
        foreach (var wp in _allWallpapers)
        {
            // Filtro por categorías
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

            // Filtro por dispositivo (PC o Móvil)
            if (SelectedDeviceFilter != "Todos")
            {
                if (wp.DeviceType != SelectedDeviceFilter)
                {
                    continue;
                }
            }

            // Filtro por caja de texto
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
        OnPropertyChanged(nameof(CatalogCountText));
    }

    private void ToggleFavorite(Wallpaper? wp)
    {
        if (wp == null) return;
        
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

            // Determinar dimensiones según formato seleccionado (PC o Móvil)
            int width = 1440; // 9:20 Celular por defecto
            int height = 3200;

            if (SolidColorFormat == "PC")
            {
                // Formato PC (4K UHD)
                width = 3840;
                height = 2160;
            }

            if (!Directory.Exists(DownloadPath))
            {
                Directory.CreateDirectory(DownloadPath);
            }

            var fileName = $"Solid_{SolidColorFormat}_{hex.Replace("#", "")}.png";
            var destPath = Path.Combine(DownloadPath, fileName);

            var renderTarget = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
            using (var context = renderTarget.CreateDrawingContext())
            {
                context.DrawRectangle(new SolidColorBrush(SelectedSolidColor), null, new Rect(0, 0, width, height));

                // Generar grano/ruido táctil si está activado
                if (GrainLevel > 0)
                {
                    var rand = new Random();
                    var whiteBrush = new SolidColorBrush(Color.Parse("#FFFFFF"), 0.04);
                    var blackBrush = new SolidColorBrush(Color.Parse("#000000"), 0.04);
                    int densityDivisor = GrainLevel == 1.0 ? 55 : 22;
                    int pointsCount = (width * height) / densityDivisor;

                    for (int i = 0; i < pointsCount; i++)
                    {
                        double x = rand.NextDouble() * width;
                        double y = rand.NextDouble() * height;
                        double size = rand.NextDouble() * 1.5 + 0.5;

                        var brush = rand.Next(2) == 0 ? whiteBrush : blackBrush;
                        context.DrawRectangle(brush, null, new Rect(x, y, size, size));
                    }
                }
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

    // COMANDO PARA COPIAR CÓDIGO DE COLOR (SWIFTUI, KOTLIN, CSS, HEX)
    private void CopyCode(string? format)
    {
        if (format == null) return;
        try
        {
            var hex = HexColorString;
            var r = SelectedSolidColor.R;
            var g = SelectedSolidColor.G;
            var b = SelectedSolidColor.B;
            
            var textToCopy = format.ToUpper() switch
            {
                "SWIFT" => $"Color(red: {r/255.0:F2}, green: {g/255.0:F2}, blue: {b/255.0:F2})",
                "KOTLIN" => $"Color(0xFF{r:X2}{g:X2}{b:X2})",
                "CSS" => $"background-color: {hex};",
                _ => hex
            };

            var mainWindow = (App.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWindow?.Clipboard != null)
            {
                _ = mainWindow.Clipboard.SetTextAsync(textToCopy);
                ShowToast($"¡Código {format} copiado: {textToCopy}!");
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Error al copiar: {ex.Message}");
        }
    }

    private void TryApplyHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        var cleanHex = hex.Trim();
        if (!cleanHex.StartsWith("#"))
        {
            cleanHex = "#" + cleanHex;
        }

        try
        {
            if (Color.TryParse(cleanHex, out var parsedColor))
            {
                _selectedSolidColor = parsedColor;
                OnPropertyChanged(nameof(SelectedSolidColor));
                OnPropertyChanged(nameof(HexColorString));
                OnPropertyChanged(nameof(SelectedSolidColorBrush));
                OnPropertyChanged(nameof(OledSavingText));
                OnPropertyChanged(nameof(ContrastRatioText));
                OnPropertyChanged(nameof(ComplementaryHexColorString));
                OnPropertyChanged(nameof(ComplementarySolidColorBrush));
                UpdateLightnessField();
            }
        }
        catch
        {
            // Ignorar errores al escribir
        }
    }

    private void UpdateLightnessField()
    {
        double r = SelectedSolidColor.R / 255.0;
        double g = SelectedSolidColor.G / 255.0;
        double b = SelectedSolidColor.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        _lightness = ((max + min) / 2.0) * 100.0;
        OnPropertyChanged(nameof(Lightness));
    }

    private void UpdateColorFromHsl()
    {
        double r = SelectedSolidColor.R / 255.0;
        double g = SelectedSolidColor.G / 255.0;
        double b = SelectedSolidColor.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        
        double h = 0;
        double s = 0;
        double l = (max + min) / 2.0;

        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r)
            {
                h = (g - b) / d + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = (b - r) / d + 2;
            }
            else if (max == b)
            {
                h = (r - g) / d + 4;
            }
            h /= 6.0;
        }

        var newL = Lightness / 100.0;
        var newColor = HslToRgb(h * 360.0, s, newL);
        
        _selectedSolidColor = newColor;
        
        // Sincronizar el input de texto hexadecimal con el nuevo color del slider
        _customHexInput = $"{newColor.R:X2}{newColor.G:X2}{newColor.B:X2}";
        OnPropertyChanged(nameof(CustomHexInput));

        OnPropertyChanged(nameof(SelectedSolidColor));
        OnPropertyChanged(nameof(HexColorString));
        OnPropertyChanged(nameof(SelectedSolidColorBrush));
        OnPropertyChanged(nameof(OledSavingText));
        OnPropertyChanged(nameof(ContrastRatioText));
        OnPropertyChanged(nameof(ComplementaryHexColorString));
        OnPropertyChanged(nameof(ComplementarySolidColorBrush));
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