namespace SelectSight;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

public partial class MainWindow : Window
{
    private HashSet<FileItem> _allFiles = [];
    private readonly ObservableCollection<FileItem> _selectedFiles = [];
    
    public MainWindow()
    {
        InitializeComponent();
        Activated += OnActivated;
    }
    
    private async void OnActivated(object? sender, EventArgs e)
    {
        Activated -= OnActivated;
        
        var topLevel = GetTopLevel(this);
        if (topLevel is not null)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a folder",
                AllowMultiple = false
            });
            if (folders.Any()) LoadFilesFromDirectory(folders[0].Path.LocalPath);
            else Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            });
        }
        
        AllFilesListBox.ItemsSource = _allFiles;
        AllFilesListBox.SelectedItems = _selectedFiles;
        
        SelectedFilesListBox.ItemsSource = _selectedFiles;
        
        InitializeFilePersistence();
        UpdateButtonStates();
    }

    private void LoadFilesFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Console.WriteLine($"Directory not found: {directoryPath}");
            return;
        }

        var files = Directory.GetFiles(directoryPath);
        _allFiles = files.Select(p => new FileItem(p)).ToHashSet();
    }
    
    private void InitializeFilePersistence()
    {
        const string selectSightTempFolder = "SelectSightData";
        const string selectSightSelectedFilesFile = "SelectedFiles.ss";
        
        var selectSightTemp = Path.Combine(Path.GetTempPath(), selectSightTempFolder);
        if (!Directory.Exists(selectSightTemp)) Directory.CreateDirectory(selectSightTemp);
        var selectedFilesFile = Path.Combine(selectSightTemp, selectSightSelectedFilesFile);

        if (File.Exists(selectedFilesFile))
        {
            var oldSelections = File.ReadAllLines(selectedFilesFile).Select(p => new FileItem(p)).ToHashSet();
            foreach (var oldSelection in oldSelections) 
                if (_allFiles.TryGetValue(oldSelection, out var fileItem)) _selectedFiles.Add(fileItem);
        }
        
        _selectedFiles.CollectionChanged += SelectedFilesOnCollectionChanged;
        return;
        
        void SelectedFilesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            File.WriteAllLines(selectedFilesFile, _selectedFiles.Select(f => f.FullPath));
        }
    }
    
    private void UpdateButtonStates()
    {
        var hasSelections = _selectedFiles.Count > 0;
        CopyButton.IsEnabled = hasSelections;
        ClearButton.IsEnabled = hasSelections;
    }

    #region Eventhandlers

    private async void CopySelectedFiles(object? sender, RoutedEventArgs e)
    {
        if (_selectedFiles.Count < 1) return;

        // Get the top-level window/control to access the clipboard
        var topLevel = GetTopLevel(this);

        if (topLevel?.Clipboard is null)
        {
            Console.WriteLine("Clipboard not available.");
            return;
        }
        
        try
        {
            var dataObject = new DataObject();
            var filePaths = _selectedFiles.Select(f => f.FullPath).ToList();
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                dataObject.Set("text/uri-list", string.Join(Environment.NewLine, filePaths.Select(f => new Uri(f).AbsoluteUri)));
            else dataObject.Set(DataFormats.Files, filePaths);
            
            await topLevel.Clipboard.SetDataObjectAsync(dataObject);
            Console.WriteLine($"Copied {_selectedFiles.Count} files to clipboard.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error copying files to clipboard: {ex.Message}");
        }
    }
    
    private async void SelectAllFiles(object? sender, RoutedEventArgs e)
    {
        if (_selectedFiles.Count > 0)
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard("Confirm Select All", "Are you sure you want to select all files?",
                    ButtonEnum.YesNo);

            if (await box.ShowAsync() != ButtonResult.Yes) return;
        }
        
        foreach (var fileItem in _allFiles) _selectedFiles.Add(fileItem);
        UpdateButtonStates();
    }
    
    private async void ClearSelectedFiles(object? sender, RoutedEventArgs e)
    {
        if (_selectedFiles.Count < 1) return;
        var box = MessageBoxManager
            .GetMessageBoxStandard("Confirm Clear", "Are you sure you want to clear the selected files?",
                ButtonEnum.YesNo);

        if (await box.ShowAsync() != ButtonResult.Yes) return;
        
        _selectedFiles.Clear();
        UpdateButtonStates();
    }

    private void OnFileClicked(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is not StackPanel { DataContext: FileItem fileItem }) return;
        if (!_selectedFiles.Remove(fileItem)) _selectedFiles.Add(fileItem);
        UpdateButtonStates();
        e.Handled = true;
    }

    #endregion
}