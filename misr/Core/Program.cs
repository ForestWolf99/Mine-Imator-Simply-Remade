
using Silk.NET.Windowing;
using Silk.NET.Maths;
using System.Numerics;
using System.Reflection;
using StbImageSharp;
using Silk.NET.Core;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Misr;

class Program
{
    private static IWindow? _window;
    private static SimpleUIRenderer? _renderer;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    
    private static string GetFFmpegExecutableName()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
    }
    
    private static void ShowErrorDialog(string message, string title)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            MessageBox(IntPtr.Zero, message, title, 0x10); // MB_ICONERROR
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Try different Linux dialog tools in order of preference
            var dialogTools = new[] { "zenity", "kdialog", "xmessage" };
            
            foreach (var tool in dialogTools)
            {
                try
                {
                    if (IsCommandAvailable(tool))
                    {
                        var processInfo = tool switch
                        {
                            "zenity" => new ProcessStartInfo("zenity", $"--error --text=\"{message}\" --title=\"{title}\""),
                            "kdialog" => new ProcessStartInfo("kdialog", $"--error \"{message}\" --title \"{title}\""),
                            "xmessage" => new ProcessStartInfo("xmessage", $"-center \"{title}\\n\\n{message}\""),
                            _ => null
                        };
                        
                        if (processInfo != null)
                        {
                            Process.Start(processInfo)?.WaitForExit();
                            return;
                        }
                    }
                }
                catch { /* Continue to next tool */ }
            }
            
            // Fallback to console output
            Console.Error.WriteLine($"ERROR: {title}");
            Console.Error.WriteLine(message);
        }
        else
        {
            // Fallback for other platforms
            Console.Error.WriteLine($"ERROR: {title}");
            Console.Error.WriteLine(message);
        }
    }
    
    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo("which", command)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            var process = Process.Start(processInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }


    static void Main(string[] args)
    {
        // Check if ffmpeg binary exists in the application directory
        var ffmpegExecutable = GetFFmpegExecutableName();
        var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ffmpegExecutable);
        if (!File.Exists(ffmpegPath))
        {
            ShowErrorDialog(
                $"FFmpeg binary ({ffmpegExecutable}) not found in application directory.\n\nPlease ensure {ffmpegExecutable} is present alongside the application executable.", 
                "Mine Imator Simply Remade - Missing Dependency");
            Environment.Exit(1);
            return;
        }

        // Get primary monitor to calculate window size and position
        var monitor = Silk.NET.Windowing.Monitor.GetMainMonitor(null);
        var displaySize = monitor.Bounds.Size;
        
        var windowWidth = displaySize.X - 200;
        var windowHeight = displaySize.Y - 160;
        var windowX = (displaySize.X - windowWidth) / 2;
        var windowY = (displaySize.Y - windowHeight) / 2;

        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(windowWidth, windowHeight),
            Position = new Vector2D<int>(windowX, windowY),
            Title = "Mine Imator Simply Remade"
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;

        _window.Run();
    }

    private static void OnLoad()
    {
        if (_window == null) return;

        // Enable Y-flip for all image loading globally
        StbImage.stbi_set_flip_vertically_on_load(1);

        // Set window icon
        SetWindowIcon();

        _renderer = new SimpleUIRenderer(_window);
        _renderer.Initialize();

        Console.WriteLine("OpenGL and renderer initialized successfully!");
    }

    private static void SetWindowIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("misr.assets.appIcon01.png");
            
            if (stream != null)
            {
                var imageResult = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                
                // Flip the image vertically to fix upside-down display
                var flippedData = new byte[imageResult.Data.Length];
                int bytesPerPixel = 4; // RGBA
                int stride = imageResult.Width * bytesPerPixel;
                
                for (int y = 0; y < imageResult.Height; y++)
                {
                    int srcOffset = y * stride;
                    int dstOffset = (imageResult.Height - 1 - y) * stride;
                    Array.Copy(imageResult.Data, srcOffset, flippedData, dstOffset, stride);
                }
                
                // Create icon using the Silk.NET API
                var iconData = new RawImage(imageResult.Width, imageResult.Height, new Memory<byte>(flippedData));
                
                _window!.SetWindowIcon(new ReadOnlySpan<RawImage>(new[] { iconData }));
                Console.WriteLine("Window icon set successfully!");
            }
            else
            {
                Console.WriteLine("Warning: Could not load window icon from embedded resources");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to set window icon: {ex.Message}");
        }
    }

    private static void OnRender(double deltaTime)
    {
        if (_renderer == null) return;

        _renderer.Update((float)deltaTime);
        _renderer.DrawFrame();
    }

    private static void OnClosing()
    {
        _renderer?.Dispose();
        Console.WriteLine("Window closing...");
    }
}
