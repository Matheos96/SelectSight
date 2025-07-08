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
    private HashSet<FileItem> _allFiles = [];
    private readonly ObservableCollection<FileItem> _selectedFiles = [];
    
    private Point _dragStartPosition;
    private bool _isDragging;
    private const double DragThreshold = 5.0;
    private ListBoxItem? _pressedListBoxItem;
    
    public MainWindow()
    {
        InitializeComponent();
        Activated += OnActivated;
        SetupDragAndDrop();
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
            if (folders.Any()) await LoadFilesFromDirectory(folders[0].Path.LocalPath);
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
        UpdateUiControlStates();
    }
    
    private void SetupDragAndDrop()
    {
        AllFilesListBox.AddHandler(PointerPressedEvent, OnListBoxClick, RoutingStrategies.Tunnel);
        AllFilesListBox.AddHandler(PointerMovedEvent, OnListBoxPointerMoved, RoutingStrategies.Tunnel);
        AllFilesListBox.AddHandler(PointerReleasedEvent, SelectedFilesListBox_PointerReleased, RoutingStrategies.Tunnel);
    }

    private async Task LoadFilesFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Console.WriteLine($"Directory not found: {directoryPath}");
            return;
        }

        var files = Directory.GetFiles(directoryPath);
        ShowFeedback("Loading files. Thumbnails may take a moment to appear...");
        await Task.Delay(5); // Allow UI to update before loading thumbnails
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
            
            UpdateUiControlStates(); // Ensure UI reflects the current state of selected files
        }
    }
    
    private void ToggleFileSelection(FileItem fileItem)
    {
        if (!_selectedFiles.Remove(fileItem)) _selectedFiles.Add(fileItem);
    }
    
    private void ShowFeedback(string message) => Task.Run(async () =>
    {
        Dispatcher.UIThread.Post(() => FeedbackText.Text = message);
        await Task.Delay(TimeSpan.FromSeconds(3));
        Dispatcher.UIThread.Post(() => FeedbackText.Text = string.Empty);
    });
    
    private void UpdateUiControlStates()
    {
        var hasSelections = _selectedFiles.Count > 0;
        CopyButton.IsEnabled = hasSelections;
        ClearButton.IsEnabled = hasSelections;

        var sb = new StringBuilder($"{_allFiles.Count} files | ");
        if (_selectedFiles.Count == 0) sb.Append("No files selected");
        else sb.Append($"{_selectedFiles.Count} file{(_selectedFiles.Count == 1 ? string.Empty : "s")} selected");
        SelectedFilesText.Text = sb.ToString();
    }

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

    #region Eventhandlers
    
    private void SelectedFilesListBox_PointerReleased(object? sender, PointerReleasedEventArgs e)
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

    private async void OnListBoxPointerMoved(object? sender, PointerEventArgs e)
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

    private void OnListBoxClick(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragStartPosition = e.GetPosition(this);
        _pressedListBoxItem = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (_pressedListBoxItem is null) return;
        e.Pointer.Capture(_pressedListBoxItem);
        _isDragging = false;
        e.Handled = true;
    }

    private async void CopySelectedFiles(object? sender, RoutedEventArgs e)
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
            ShowFeedback($"{_selectedFiles.Count} {(_selectedFiles.Count == 1 ? "file was" : "files were")} copied to the clipboard");
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
    }
    
    private async void ClearSelectedFiles(object? sender, RoutedEventArgs e)
    {
        if (_selectedFiles.Count == 0) return;
        var box = MessageBoxManager
            .GetMessageBoxStandard("Confirm Clear", "Are you sure you want to clear the selected files?",
                ButtonEnum.YesNo);

        if (await box.ShowAsync() != ButtonResult.Yes) return;
        
        _selectedFiles.Clear();
        ShowFeedback("Cleared all selected files");
    }

    #endregion
}