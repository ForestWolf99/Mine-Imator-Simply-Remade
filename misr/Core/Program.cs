
using Silk.NET.Windowing;
using Silk.NET.Maths;
using System.Numerics;
using System.Reflection;
using StbImageSharp;
using Silk.NET.Core;

namespace Misr;

class Program
{
    private static IWindow? _window;
    private static SimpleUIRenderer? _renderer;


    static void Main(string[] args)
    {
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

        // Set window icon
        SetWindowIcon();

        _renderer = new SimpleUIRenderer(null, _window);
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
                
                // Create icon using the Silk.NET API
                var iconData = new RawImage(imageResult.Width, imageResult.Height, new Memory<byte>(imageResult.Data));
                
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
