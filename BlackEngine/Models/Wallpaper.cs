using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace BlackEngine.Models;

// Este archivo define qué es un "Wallpaper" y avisa a la interfaz cuando la imagen ya se descargó
public class Wallpaper : INotifyPropertyChanged
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;
    public string HighResUrl { get; set; } = string.Empty;
    public string Sha { get; set; } = string.Empty;
    public string Uploader { get; set; } = string.Empty;
    private int _likes;
    public int Likes
    {
        get => _likes;
        set
        {
            _likes = value;
            OnPropertyChanged();
        }
    }

    private int _downloadsCount;
    public int DownloadsCount
    {
        get => _downloadsCount;
        set
        {
            _downloadsCount = value;
            OnPropertyChanged();
        }
    }

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            _isFavorite = value;
            OnPropertyChanged();
        }
    }

    // En Avalonia, las imágenes en memoria se manejan como Bitmap
    private Bitmap? _imageBitmap;
    public Bitmap? ImageBitmap
    {
        get => _imageBitmap;
        set
        {
            _imageBitmap = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}