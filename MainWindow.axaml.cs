using System.Text;

namespace SelectSight;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<FileItem> _allFiles = [];
    private readonly ObservableCollection<FileItem> _selectedFiles = [];
    
    private Point _dragStartPosition;
    private bool _isDragging;
    private const double DragThreshold = 5.0;
    private ListBoxItem? _pressedListBoxItem;
    
    public MainWindow()
    {
        InitializeComponent();

        InitConditionalUiElements();
        
        AllFilesListBox.ItemsSource = _allFiles;
        SelectedFilesListBox.ItemsSource = _selectedFiles;
        AllFilesListBox.SelectedItems = _selectedFiles;
        
        _ = Initialize(); // Start the initialization process asynchronously
    }
    
    private void InitConditionalUiElements()
    {
        // Show/hide the selected files list based on app settings
        if (Program.AppSettings.ShowSelectedFilesList) return;
        SelectedFilesBorder.IsVisible = false;
        FilesGrid.ColumnDefinitions = new ColumnDefinitions("*, Auto");
    }

    private async Task Initialize()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null)
        {
            // Handle shutdown if no top-level window -- Should not happen in normal use
            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            });
            return;
        }
        
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a folder",
            AllowMultiple = false
        });

        SetupDragAndDrop();

        await Task.Run(async () =>
        {
            if (folders.Count > 0)
            {
                var directoryPath = folders[0].Path.LocalPath;
                if (!Directory.Exists(directoryPath))
                {
                    Console.WriteLine($"Directory not found: {directoryPath}");
                    return;
                }
                
                const string selectSightTempFolder = "SelectSightData";
                const string selectSightSelectedFilesFile = "SelectedFiles.ss";
                
                var selectSightTemp = Path.Combine(Path.GetTempPath(), selectSightTempFolder);
                if (!Directory.Exists(selectSightTemp)) Directory.CreateDirectory(selectSightTemp);
                var selectedFilesFile = Path.Combine(selectSightTemp, selectSightSelectedFilesFile);
            
                var oldSelections = File.Exists(selectedFilesFile)
                    ? (await File.ReadAllLinesAsync(selectedFilesFile)).ToHashSet()
                    : [];
            
                _selectedFiles.CollectionChanged += SelectedFilesOnCollectionChanged;
                _allFiles.CollectionChanged += AllFilesOnCollectionChanged;
                
                var directoryInfo = new DirectoryInfo(directoryPath);
                var files = directoryInfo.GetFiles().OrderBy(p => p.CreationTime).ToArray();
                var totalFiles = files.Length;
                var currentFileIndex = 0;
                foreach (var fileInfo in files)
                {
                    var filePath = fileInfo.FullName;
                    var fileItem = new FileItem(filePath);
                    _allFiles.Add(fileItem);
                    
                    // If the file was previously selected, add it to the selected files
                    // (Queue on UI Thread to avoid issues with collection modification due to modification above that also affects AllFilesListBox)
                    if (oldSelections.Contains(filePath)) Dispatcher.UIThread.Post(() => _selectedFiles.Add(fileItem)); 
                    
                    await fileItem.LoadThumbnailAsync();
                    currentFileIndex++;
                    ShowFeedback($"Loading files and creating thumbnails {currentFileIndex/(float)totalFiles:P} ({currentFileIndex}/{totalFiles})", -1, false);
                }
                ShowFeedback("Files loaded successfully", 4, false);
                return;
                
                void SelectedFilesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
                {
                    File.WriteAllLines(selectedFilesFile, _selectedFiles.Select(f => f.FullPath));
                    RefreshUiButtonStates(); // Ensure the UI reflects the current state of selected files
                }
                void AllFilesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshFilesInfoText();
            }
            
            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            });
        });
    }
    
    private void SetupDragAndDrop()
    {
        AllFilesListBox.AddHandler(PointerPressedEvent, OnAllFilesListBoxClick, RoutingStrategies.Tunnel);
        AllFilesListBox.AddHandler(PointerMovedEvent, OnAllFilesListBoxPointerMoved, RoutingStrategies.Tunnel);
        AllFilesListBox.AddHandler(PointerReleasedEvent, OnAllFilesListBoxPointerReleased, RoutingStrategies.Tunnel);
    }

    #region Refreshing UI

    private void ToggleFileSelection(FileItem fileItem)
    {
        if (!_selectedFiles.Remove(fileItem)) _selectedFiles.Add(fileItem);
    }
    
    private void ShowFeedback(string message, long durationSeconds = -1, bool resetTextAfterTimeout = true) => Task.Run(async () =>
    {
        var currentMessage = string.Empty;
        Dispatcher.UIThread.Post(() =>
        {
            currentMessage = FeedbackText.Text;
            FeedbackText.Text = message;
        });
        if (durationSeconds <= 0) return; // Don't reset feedback if duration is 0 or negative
        
        await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
        
        Dispatcher.UIThread.Post(() => FeedbackText.Text = resetTextAfterTimeout ? currentMessage : string.Empty);
    });
    
    private void RefreshUiButtonStates() => Dispatcher.UIThread.Post(() =>
    {
        var hasSelections = _selectedFiles.Count > 0;
        CopyButton.IsEnabled = hasSelections;
        ClearButton.IsEnabled = hasSelections;
    });
    
    private void RefreshFilesInfoText()
    {
        var sb = new StringBuilder($"{_allFiles.Count} files | ");
        if (_selectedFiles.Count == 0) sb.Append("No files selected");
        else sb.Append($"{_selectedFiles.Count} file{(_selectedFiles.Count == 1 ? string.Empty : "s")} selected");
        var newText = sb.ToString();
        Dispatcher.UIThread.Post(() => FilesInfoText.Text = newText);
    }

    #endregion
    
    #region Eventhandlers

    #region AllFile

    private void OnAllFilesListBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Release pointer capture if we were still holding it
        if (ReferenceEquals(e.Pointer.Captured, _pressedListBoxItem)) e.Pointer.Capture(null);

        // If a drag was NOT initiated (i.e., mouse released before threshold) --> It was a click
        if (!_isDragging && _pressedListBoxItem is { DataContext: FileItem clickedFileItem }) ToggleFileSelection(clickedFileItem);

        // Reset state regardless of whether it was a click or drag
        _isDragging = false;
        _pressedListBoxItem = null;
        e.Handled = true;
    }

    private async void OnAllFilesListBoxPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedListBoxItem == null || !ReferenceEquals(e.Pointer.Captured, _pressedListBoxItem)) return;
        
        // Calculate the distance moved
        var currentPosition = e.GetPosition(this);
        Vector delta = currentPosition - _dragStartPosition;
        var distance = delta.Length;

        // If we haven't started dragging yet, and the mouse has moved beyond the threshold
        if (_isDragging || !(distance > DragThreshold)) return;
        
        _isDragging = true;

        // Release pointer capture as DragDrop.DoDragDrop will handle it
        e.Pointer.Capture(null);
        
        if (_pressedListBoxItem.DataContext is FileItem clickedFileItem)
        {
            if (!_selectedFiles.Contains(clickedFileItem)) _selectedFiles.Add(clickedFileItem); // Ensure the clicked file is selected
            
            var filePaths = _selectedFiles.Select(f => f.FullPath).ToList();
            if (filePaths.Count != 0)
            {
                var data = await CreateFilesDataObject(filePaths);
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Link);
            }
        }
        
        // Reset state after drag/drop finishes
        _isDragging = false;
        _pressedListBoxItem = null;
    }

    private void OnAllFilesListBoxClick(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragStartPosition = e.GetPosition(this);
        _pressedListBoxItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (_pressedListBoxItem is null) return;
        e.Pointer.Capture(_pressedListBoxItem);
        _isDragging = false;
        e.Handled = true;
    }

    #endregion

    #region Buttons

    private async void CopySelectedBtnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedFiles.Count == 0) return;

        // Get the top-level window/control to access the clipboard
        var topLevel = GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            Console.WriteLine("Clipboard not available.");
            return;
        }
        
        try
        {
            var filePaths = _selectedFiles.Select(f => f.FullPath);
            var dataObject = await CreateFilesDataObject(filePaths);
  
            await topLevel.Clipboard.SetDataObjectAsync(dataObject);
            ShowFeedback($"{_selectedFiles.Count} {(_selectedFiles.Count == 1 ? "file was" : "files were")} copied to the clipboard", 3);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error copying files to clipboard: {ex.Message}");
        }
    }
    
    private async void SelectAllBtnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedFiles.Count > 0)
        {
            var box = MessageBoxManager
                .GetMessageBoxStandard("Confirm Select All", "Are you sure you want to select all files?",
                    ButtonEnum.YesNo);

            if (await box.ShowAsync() != ButtonResult.Yes) return;
        }
        
        foreach (var fileItem in _allFiles) _selectedFiles.Add(fileItem);
    }
    
    private async void ClearSelectedBtnClick(object? sender, RoutedEventArgs e)
    {
        if (_selectedFiles.Count == 0) return;
        var box = MessageBoxManager
            .GetMessageBoxStandard("Confirm Clear", "Are you sure you want to clear the selected files?",
                ButtonEnum.YesNo);

        if (await box.ShowAsync() != ButtonResult.Yes) return;
        
        _selectedFiles.Clear();
        ShowFeedback("Cleared all selected files", 3);
    }

    #endregion

    #endregion
    
    private async Task<DataObject> CreateFilesDataObject(IEnumerable<string> filePaths)
    {
        var data = new DataObject();
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return data;
            var storageFiles = new List<IStorageFile>();
            foreach (var filePath in filePaths)
            {
                var storageFile = await topLevel.StorageProvider.TryGetFileFromPathAsync(filePath);
                if (storageFile is not null) storageFiles.Add(storageFile);
            }
            data.Set(DataFormats.Files, storageFiles);
        }
        else
            data.Set("text/uri-list", string.Join(Environment.NewLine, filePaths.Select(f => new Uri(f).AbsoluteUri)));

        return data;
    }
}