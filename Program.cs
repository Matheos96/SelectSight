namespace SelectSight;

using System;
using Avalonia;
using Microsoft.Extensions.Configuration;

class Program
{
    public static AppSettings AppSettings { get; private set; } = null!;
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional:true)
            .Build();
        
        AppSettings = configuration.GetSection("AppSettings").Get<AppSettings>() ?? new AppSettings();
        
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal class AppSettings
{
    public bool ReadExif { get; init; } = true;
    public bool ShowSelectedFilesList { get; init; } = true;
    public int ThumbnailSize { get; init; } = 300; // Default thumbnail size is 300 pixels
}