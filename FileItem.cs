using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SkiaSharp;

namespace SelectSight;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

public class FileItem(string fullPath) : INotifyPropertyChanged
{
    public string FullPath { get; } = fullPath;
    public string Name { get; } = Path.GetFileName(fullPath);

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set => SetField(ref _thumbnail, value);
    }

    public async Task LoadThumbnailAsync()
    {
        try
        {
            var extension = Path.GetExtension(FullPath).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tiff" or ".tif" or ".ico" or ".nef")
            {
                // Attempt to load image thumbnail
                await using var stream = new FileStream(FullPath, FileMode.Open, FileAccess.Read);
                Thumbnail = await ThumbnailUtils.GenerateBitmap(stream);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading thumbnail for {FullPath}: {ex.Message}");
        }

        Thumbnail ??= GetDefaultIcon();
    }
    
    private static Bitmap? _defaultIcon;
    private static Bitmap GetDefaultIcon()
    {
        if (_defaultIcon is not null) return _defaultIcon;
        
        var uri = new Uri("avares://SelectSight/Assets/no-photo.png");
        _defaultIcon = new Bitmap(Avalonia.Platform.AssetLoader.Open(uri));
        return _defaultIcon;
    }

    public override bool Equals(object? obj)
        => obj is FileItem otherItem && FullPath.Equals(otherItem.FullPath, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => FullPath.ToUpperInvariant().GetHashCode();

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    #endregion
}