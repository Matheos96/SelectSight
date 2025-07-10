using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SkiaSharp;

namespace SelectSight;

public static class ThumbnailUtils
{
    public static async Task<Bitmap?> GenerateBitmap(Stream fileStream)
    {
        var thumbnailSize = Program.AppSettings.ThumbnailSize;
        // First check app settings to see if we should read EXIF data
        if (!Program.AppSettings.ReadExif) return Bitmap.DecodeToWidth(fileStream, thumbnailSize);
        
        var skEncodedOrigin = GetSkiaEncodedOrigin(fileStream);
        fileStream.Position = 0; // Reset stream position to the beginning for decoding
        
        // If no adjustment is needed, decode directly to the target size
        if (skEncodedOrigin == SKEncodedOrigin.Default) return Bitmap.DecodeToWidth(fileStream, thumbnailSize);

        // Needs adjustment for orientation
        using var originalDecodedBitmap = SKBitmap.Decode(fileStream);
        if (originalDecodedBitmap is null) return null;

        // Determine dimensions *after* orientation if it's 90/270 degrees
        var orientedWidth = originalDecodedBitmap.Width;
        var orientedHeight = originalDecodedBitmap.Height;

        // For 90/270 degree rotations, swap width and height for the new bitmap
        if (skEncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightBottom)
        {
            (orientedWidth, orientedHeight) = (orientedHeight, orientedWidth); // Swap
        }

        // Create a new bitmap for the oriented image
        using var orientedBitmap = new SKBitmap(orientedWidth, orientedHeight, originalDecodedBitmap.ColorType, originalDecodedBitmap.AlphaType);
        using var canvas = new SKCanvas(orientedBitmap);

        var rotationMatrix = GetOrientationMatrix(skEncodedOrigin, originalDecodedBitmap.Width, originalDecodedBitmap.Height);
        canvas.SetMatrix(rotationMatrix);

        // Draw the original bitmap onto the transformed canvas
        canvas.DrawBitmap(originalDecodedBitmap, 0, 0);
        
        // 3. Resize the finalSkBitmap (which is now correctly oriented)
        // Calculate target dimensions maintaining aspect ratio
        var scale = Math.Min((float)thumbnailSize / orientedBitmap.Width, (float)thumbnailSize / orientedBitmap.Height);
        var finalScaledWidth = (int)(orientedBitmap.Width * scale);
        var finalScaledHeight = (int)(orientedBitmap.Height * scale);
        
        using var resizedBitmap = orientedBitmap.Resize(new SKImageInfo(finalScaledWidth, finalScaledHeight), SKFilterQuality.Medium);
        if (resizedBitmap is null) return null;

        // 4. Encode the final resized SKBitmap directly to a MemoryStream for Avalonia
        using var image = SKImage.FromBitmap(resizedBitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 80); // PNG is lossless, 80 quality for JPEG is common
        await using var skBitmapStream = encoded.AsStream();
        return new Bitmap(skBitmapStream);
    }

    private static SKEncodedOrigin GetSkiaEncodedOrigin(Stream stream)
     {
         var metaData = ImageMetadataReader.ReadMetadata(stream);
         var subIfdDirectory = metaData.OfType<ExifIfd0Directory>().FirstOrDefault();
         if (subIfdDirectory is null) return SKEncodedOrigin.Default;
         
         return subIfdDirectory.GetInt32(ExifDirectoryBase.TagOrientation) switch
         {
             1 => SKEncodedOrigin.TopLeft,      // 0 degrees, normal
             2 => SKEncodedOrigin.TopRight,     // Horizontal flip
             3 => SKEncodedOrigin.BottomRight,  // 180 degrees
             4 => SKEncodedOrigin.BottomLeft,   // Vertical flip
             5 => SKEncodedOrigin.LeftTop,      // Transpose (flip across diagonal + 90 deg)
             6 => SKEncodedOrigin.RightTop,     // 90 degrees clockwise
             7 => SKEncodedOrigin.RightBottom,  // Transverse (flip across other diagonal + 270 deg)
             8 => SKEncodedOrigin.LeftBottom,   // 270 degrees clockwise
             _ => SKEncodedOrigin.Default       // Fallback to default if unknown or 0
         };
     }
    
    private static SKMatrix GetOrientationMatrix(SKEncodedOrigin origin, int width, int height)
    {
        var matrix = SKMatrix.Identity;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // horizontal flip
                matrix = SKMatrix.CreateScale(-1.0f, 1.0f, width / 2f, height / 2f);
                break;
            case SKEncodedOrigin.BottomRight: // 180 degrees
                matrix = SKMatrix.CreateRotationDegrees(180, width / 2f, height / 2f);
                break;
            case SKEncodedOrigin.BottomLeft: // vertical flip
                matrix = SKMatrix.CreateScale(1.0f, -1.0f, width / 2f, height / 2f);
                break;
            case SKEncodedOrigin.TopLeft: // no rotation or flip (identity)
                break;
            case SKEncodedOrigin.LeftTop: // rotate 90 counter-clockwise + transpose
                matrix = SKMatrix.CreateRotationDegrees(90);
                matrix = matrix.PostConcat(SKMatrix.CreateTranslation(height, 0)); // Adjust translation based on new dimensions
                break;
            case SKEncodedOrigin.RightTop: // rotate 90 clockwise
                matrix = SKMatrix.CreateRotationDegrees(90);
                matrix = matrix.PostConcat(SKMatrix.CreateTranslation(0, width)); // Adjust translation based on new dimensions
                break;
            case SKEncodedOrigin.RightBottom: // rotate 270 counter-clockwise
                matrix = SKMatrix.CreateRotationDegrees(270);
                matrix = matrix.PostConcat(SKMatrix.CreateTranslation(height, width)); // Adjust translation based on new dimensions
                break;
            case SKEncodedOrigin.LeftBottom: // rotate 270 clockwise + transpose
                matrix = SKMatrix.CreateRotationDegrees(270);
                matrix = matrix.PostConcat(SKMatrix.CreateTranslation(0, width)); // Adjust translation based on new dimensions
                break;
            default: break;
        }

        return matrix;
    }
}