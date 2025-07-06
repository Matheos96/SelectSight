# SelectSight

SelectSight is a cross-platform desktop application built with Avalonia UI and C# that allows users to easily browse image files within a selected directory, manage a selection of these files, and copy their paths to the clipboard. It also includes basic persistence for selected files across application sessions.

## Features

* **Folder Selection:** Open a native file dialog to select any directory on your system.
* **Thumbnail View:** Displays image files from the selected directory as a grid of thumbnails.
* **Interactive Selection:**
    * Click on a thumbnail to toggle its selection.
    * Currently selected files are highlighted in the thumbnail view.
* **Selected Files List:** A separate panel lists the names of all currently selected files.
* **"Select All" Functionality:** A button to quickly select all images in the current view (with confirmation for non-empty selections).
* **"Clear Selected" Functionality:** A button to clear the current selection (with confirmation).
* **"Copy Selected to Clipboard" Functionality:** Copies the full paths of all selected images to the system clipboard, supporting both Windows file paths and Linux `text/uri-list` format.
* **Selection Persistence:** Remembers your selected files across application launches by saving their paths to a temporary user-specific directory.
* **Cross-Platform:** Built with Avalonia UI, designed to run on Windows, macOS, and Linux.

## Attributions

"No photo" icon by [pocike](https://www.flaticon.com/authors/pocike) from [Flaticon](https://www.flaticon.com/).