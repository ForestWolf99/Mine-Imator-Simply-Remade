using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Misr.Utilities;

public static class FileDialog
{
    public static async Task<string?> ShowSaveDialogAsync(string defaultFileName = "render", string filter = "PNG Files (*.png)|*.png|JPEG Files (*.jpg)|*.jpg|BMP Files (*.bmp)|*.bmp")
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await ShowSaveDialogWindowsAsync(defaultFileName, filter);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await ShowSaveDialogLinuxAsync(defaultFileName);
        }
        else
        {
            // Fallback for unsupported platforms
            Console.WriteLine("File dialog not supported on this platform");
            return null;
        }
    }

    private static async Task<string?> ShowSaveDialogWindowsAsync(string defaultFileName, string filter)
    {
        try
        {
            // Convert filter format from .NET to Windows Forms format if needed
            string windowsFilter = filter.Replace("|", "|");  // Already correct format
            
            // Determine default extension from filter
            string defaultExt = "png";
            if (filter.Contains("*.mp4")) defaultExt = "mp4";
            else if (filter.Contains("*.mov")) defaultExt = "mov";
            else if (filter.Contains("*.wmv")) defaultExt = "wmv";
            else if (filter.Contains("*.avi")) defaultExt = "avi";
            else if (filter.Contains("*.webm")) defaultExt = "webm";
            
            // Use PowerShell to show Windows file dialog
            var script = $@"
Add-Type -AssemblyName System.Windows.Forms
$saveFileDialog = New-Object System.Windows.Forms.SaveFileDialog
$saveFileDialog.FileName = '{defaultFileName}'
$saveFileDialog.Filter = '{windowsFilter}'
$saveFileDialog.DefaultExt = '{defaultExt}'
$saveFileDialog.AddExtension = $true
$result = $saveFileDialog.ShowDialog()
if ($result -eq [System.Windows.Forms.DialogResult]::OK) {{
    Write-Output $saveFileDialog.FileName
}} else {{
    Write-Output ''
}}
";

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                var result = await process.StandardOutput.ReadToEndAsync();
                var trimmedResult = result.Trim();
                return string.IsNullOrEmpty(trimmedResult) ? null : trimmedResult;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error showing Windows file dialog: {ex.Message}");
        }
        
        return null;
    }

    private static async Task<string?> ShowSaveDialogLinuxAsync(string defaultFileName)
    {
        try
        {
            // Try zenity first (most common)
            var result = await TryLinuxDialogCommand("zenity", $"--file-selection --save --filename=\"{defaultFileName}.png\" --file-filter='PNG files (*.png) | *.png' --file-filter='JPEG files (*.jpg) | *.jpg' --file-filter='BMP files (*.bmp) | *.bmp' --file-filter='All files | *'");
            if (result != null) return result;

            // Try kdialog (KDE)
            result = await TryLinuxDialogCommand("kdialog", $"--getsavefilename \"{defaultFileName}.png\" \"*.png *.jpg *.bmp\"");
            if (result != null) return result;

            // Fallback to simple console prompt
            Console.WriteLine($"Native file dialog not available. Please specify full path for save file (default: {defaultFileName}.png):");
            // In a real implementation, you might want to handle this differently
            return $"{defaultFileName}.png";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error showing Linux file dialog: {ex.Message}");
        }

        return null;
    }

    private static async Task<string?> TryLinuxDialogCommand(string command, string args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    var result = await process.StandardOutput.ReadToEndAsync();
                    var trimmedResult = result.Trim();
                    return string.IsNullOrEmpty(trimmedResult) ? null : trimmedResult;
                }
            }
        }
        catch
        {
            // Command not found or failed
        }

        return null;
    }
}
