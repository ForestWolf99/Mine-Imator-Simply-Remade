using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Misr.Rendering;

public class MaxRectsBinPack
{
    private List<Rectangle> freeRectangles = new List<Rectangle>();
    private int binWidth = 0;
    private int binHeight = 0;

    public struct Rectangle
    {
        public int X, Y, Width, Height;
        
        public Rectangle(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }
    }

    public void Init(int width, int height)
    {
        binWidth = width;
        binHeight = height;
        freeRectangles.Clear();
        freeRectangles.Add(new Rectangle(0, 0, width, height));
    }

    public Rectangle Insert(int width, int height)
    {
        Rectangle bestNode = new Rectangle();
        int bestShortSideFit = int.MaxValue;
        int bestAreaFit = int.MaxValue;

        for (int i = 0; i < freeRectangles.Count; ++i)
        {
            var rect = freeRectangles[i];
            
            // Try to place the rectangle in leftbottom-right fashion.
            if (rect.Width >= width && rect.Height >= height)
            {
                int leftoverHoriz = rect.Width - width;
                int leftoverVert = rect.Height - height;
                int shortSideFit = Math.Min(leftoverHoriz, leftoverVert);
                int areaFit = leftoverHoriz * leftoverVert;

                if (areaFit < bestAreaFit || (areaFit == bestAreaFit && shortSideFit < bestShortSideFit))
                {
                    bestNode.X = rect.X;
                    bestNode.Y = rect.Y;
                    bestNode.Width = width;
                    bestNode.Height = height;
                    bestShortSideFit = shortSideFit;
                    bestAreaFit = areaFit;
                }
            }
        }

        if (bestNode.Width > 0)
        {
            SplitFreeNode(bestNode);
        }

        return bestNode;
    }

    private void SplitFreeNode(Rectangle usedNode)
    {
        for (int i = 0; i < freeRectangles.Count; )
        {
            var rect = freeRectangles[i];
            
            if (RectangleIntersect(usedNode, rect))
            {
                freeRectangles.RemoveAt(i);
                
                if (rect.X < usedNode.X + usedNode.Width && rect.X + rect.Width > usedNode.X + usedNode.Width)
                {
                    // New node at the right side of the used node.
                    Rectangle newNode = new Rectangle(usedNode.X + usedNode.Width, rect.Y, rect.X + rect.Width - (usedNode.X + usedNode.Width), rect.Height);
                    freeRectangles.Add(newNode);
                }
                if (rect.Y < usedNode.Y + usedNode.Height && rect.Y + rect.Height > usedNode.Y + usedNode.Height)
                {
                    // New node at the top side of the used node.
                    Rectangle newNode = new Rectangle(rect.X, usedNode.Y + usedNode.Height, rect.Width, rect.Y + rect.Height - (usedNode.Y + usedNode.Height));
                    freeRectangles.Add(newNode);
                }
                if (rect.X + rect.Width > usedNode.X && rect.X < usedNode.X)
                {
                    // New node at the left side of the used node.
                    Rectangle newNode = new Rectangle(rect.X, rect.Y, usedNode.X - rect.X, rect.Height);
                    freeRectangles.Add(newNode);
                }
                if (rect.Y + rect.Height > usedNode.Y && rect.Y < usedNode.Y)
                {
                    // New node at the bottom side of the used node.
                    Rectangle newNode = new Rectangle(rect.X, rect.Y, rect.Width, usedNode.Y - rect.Y);
                    freeRectangles.Add(newNode);
                }
            }
            else
            {
                ++i;
            }
        }
    }

    private bool RectangleIntersect(Rectangle r1, Rectangle r2)
    {
        return r1.X < r2.X + r2.Width && r1.X + r1.Width > r2.X && r1.Y < r2.Y + r2.Height && r1.Y + r1.Height > r2.Y;
    }
}

public struct AtlasRect
{
    public int X, Y, Width, Height;
    public float U1, V1, U2, V2; // Normalized texture coordinates
    public int AtlasIndex; // Which atlas this tile belongs to
}

public class TextureAtlas
{
    private readonly GL _gl;
    private List<uint> _terrainAtlasTextures = new List<uint>();
    private Dictionary<string, AtlasRect> _atlasMapping = new Dictionary<string, AtlasRect>();
    private List<(int width, int height)> _atlasDimensions = new List<(int, int)>();

    public IReadOnlyList<uint> AtlasTextures => _terrainAtlasTextures;
    public IReadOnlyDictionary<string, AtlasRect> AtlasMapping => _atlasMapping;
    public IReadOnlyList<(int width, int height)> AtlasDimensions => _atlasDimensions;

    public TextureAtlas(GL gl)
    {
        _gl = gl;
    }

    public void CreateTerrainAtlases()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var allTerrainResources = assembly.GetManifestResourceNames()
                .Where(name => name.Contains("assets.sprite.terrain") && name.EndsWith(".png"))
                .OrderBy(name => name)
                .ToArray();
                
            Console.WriteLine($"Creating terrain atlases for {allTerrainResources.Length} tiles");
            
            if (allTerrainResources.Length == 0)
            {
                Console.WriteLine("No embedded terrain tiles found - creating fallback texture");
                CreateFallbackTexture();
                return;
            }
            
            // Create multiple atlases, each with 64 tiles (8x8 grid) for optimal performance
            const int tilesPerAtlas = 64;
            int atlasCount = (int)Math.Ceiling((double)allTerrainResources.Length / tilesPerAtlas);
            
            for (int atlasIndex = 0; atlasIndex < atlasCount; atlasIndex++)
            {
                var resourcesForThisAtlas = allTerrainResources
                    .Skip(atlasIndex * tilesPerAtlas)
                    .Take(tilesPerAtlas)
                    .ToArray();
                    
                CreateSingleAtlas(assembly, resourcesForThisAtlas, atlasIndex);
            }
            
            Console.WriteLine($"Created {_terrainAtlasTextures.Count} terrain atlases with {allTerrainResources.Length} total tiles");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating terrain atlas: {ex.Message}");
            Console.WriteLine("Creating fallback texture instead");
            CreateFallbackTexture();
        }
    }

    private unsafe void CreateSingleAtlas(Assembly assembly, string[] resourceNames, int atlasIndex)
    {
        // Load all images and get their dimensions
        var images = new List<(string name, ImageResult image)>();
        int tileWidth = 0, tileHeight = 0;
        
        foreach (var resourceName in resourceNames)
        {
            // Extract tile name from resource name (e.g., "misr.assets.sprite.terrain.tile040.png" -> "tile040")
            var parts = resourceName.Split('.');
            var fileName = parts[parts.Length - 2]; // Get the part before ".png"
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;
            
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            var imageBytes = memoryStream.ToArray();
            
            var image = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);
            
            if (tileWidth == 0)
            {
                tileWidth = image.Width;
                tileHeight = image.Height;
            }
            
            images.Add((fileName, image));
        }
        
        // Calculate atlas dimensions (assume all tiles are the same size)
        int tilesPerRow = (int)Math.Ceiling(Math.Sqrt(images.Count));
        int atlasWidth = tilesPerRow * tileWidth;
        int atlasHeight = tilesPerRow * tileHeight;
        
        // Store atlas dimensions
        _atlasDimensions.Add((atlasWidth, atlasHeight));
        
        // Create atlas using MaxRectsBinPack
        var packer = new MaxRectsBinPack();
        packer.Init(atlasWidth, atlasHeight);
        
        // Create atlas texture data
        var atlasData = new byte[atlasWidth * atlasHeight * 4]; // RGBA
        
        // Pack images into atlas
        foreach (var (name, image) in images)
        {
            var rect = packer.Insert(image.Width, image.Height);
            
            if (rect.Width > 0) // Successfully packed
            {
                // Copy image data to atlas
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        int srcIndex = (y * image.Width + x) * 4;
                        int dstIndex = ((rect.Y + y) * atlasWidth + (rect.X + x)) * 4;
                        
                        atlasData[dstIndex] = image.Data[srcIndex];     // R
                        atlasData[dstIndex + 1] = image.Data[srcIndex + 1]; // G
                        atlasData[dstIndex + 2] = image.Data[srcIndex + 2]; // B
                        atlasData[dstIndex + 3] = image.Data[srcIndex + 3]; // A
                    }
                }
                
                // Store atlas mapping with normalized coordinates
                var atlasRect = new AtlasRect
                {
                    X = rect.X,
                    Y = rect.Y,
                    Width = rect.Width,
                    Height = rect.Height,
                    U1 = (float)rect.X / atlasWidth,
                    V1 = (float)rect.Y / atlasHeight,
                    U2 = (float)(rect.X + rect.Width) / atlasWidth,
                    V2 = (float)(rect.Y + rect.Height) / atlasHeight,
                    AtlasIndex = atlasIndex
                };
                
                _atlasMapping[name] = atlasRect;
            }
        }
        
        // Create OpenGL texture
        var atlasTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, atlasTexture);
        
        fixed (byte* dataPtr = atlasData)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)atlasWidth, (uint)atlasHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, dataPtr);
        }
        
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.NearestMipmapNearest);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        
        // Store the atlas texture
        _terrainAtlasTextures.Add(atlasTexture);
        
        Console.WriteLine($"Created atlas {atlasIndex}: {atlasWidth}x{atlasHeight} with {images.Count} tiles");
    }

    private unsafe void CreateFallbackTexture()
    {
        // Create a simple 64x64 checkerboard pattern as fallback
        int atlasWidth = 64;
        int atlasHeight = 64;
        var textureData = new byte[atlasWidth * atlasHeight * 4];
        
        for (int y = 0; y < atlasHeight; y++)
        {
            for (int x = 0; x < atlasWidth; x++)
            {
                int index = (y * atlasWidth + x) * 4;
                bool isBlack = ((x / 8) + (y / 8)) % 2 == 0;
                byte color = (byte)(isBlack ? 64 : 128);
                
                textureData[index] = color;     // R
                textureData[index + 1] = color; // G
                textureData[index + 2] = color; // B
                textureData[index + 3] = 255;   // A
            }
        }
        
        // Create OpenGL texture
        var fallbackTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, fallbackTexture);
        
        fixed (byte* dataPtr = textureData)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)atlasWidth, (uint)atlasHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, dataPtr);
        }
        
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        
        // Store fallback texture
        _terrainAtlasTextures.Add(fallbackTexture);
        _atlasDimensions.Add((atlasWidth, atlasHeight));
        
        // Add fallback tile040 mapping
        _atlasMapping["tile040"] = new AtlasRect
        {
            X = 0, Y = 0, Width = atlasWidth, Height = atlasHeight,
            U1 = 0.0f, V1 = 0.0f, U2 = 1.0f, V2 = 1.0f,
            AtlasIndex = 0
        };
        
        Console.WriteLine("Created fallback checkerboard texture");
    }

    public void Dispose()
    {
        // Clean up OpenGL textures
        foreach (var texture in _terrainAtlasTextures)
        {
            _gl.DeleteTexture(texture);
        }
        _terrainAtlasTextures.Clear();
        _atlasMapping.Clear();
        _atlasDimensions.Clear();
    }
}
