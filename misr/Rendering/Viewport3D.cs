using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;
using Misr.Rendering;
using Misr.Core;
using Misr.UI;

namespace Misr.Rendering;

public class Viewport3D : IDisposable
{
    private readonly GL _gl;
    private readonly TextureAtlas _textureAtlas;
    
    // 3D Rendering state
    private uint _cubeVAO;
    private uint _cubeVBO;
    private uint _cubeEBO;
    private uint _cube3DShaderProgram;
    private int _cubeModelLocation;
    private int _cubeViewLocation;
    private int _cubeProjectionLocation;
    private int _cubeTextureLocation;
    private int _cubeUseTextureLocation;
    private int _cubeTileUVBoundsLocation;

    // Floor mesh state
    private uint _floorVAO;
    private uint _floorVBO;
    private uint _floorEBO;
    private int _floorVertexCount;

    // Wireframe highlight state
    private uint _wireframeVAO;
    private uint _wireframeVBO;
    private uint _wireframeEBO;
    private uint _wireframeShaderProgram;
    private int _wireframeModelLocation;
    private int _wireframeViewLocation;
    private int _wireframeProjectionLocation;
    private int _wireframeColorLocation;

    // Transform gizmo state
    private uint _gizmoVAO;
    private uint _gizmoVBO;
    private uint _gizmoEBO;
    private uint _gizmoShaderProgram;
    private int _gizmoModelLocation;
    private int _gizmoViewLocation;
    private int _gizmoProjectionLocation;
    private int _gizmoVertexCount;

    // Gizmo interaction state
    private bool _isDraggingGizmo = false;
    private int _draggedAxis = -1; // 0=X, 1=Y, 2=Z
    private int _hoveredAxis = -1; // 0=X, 1=Y, 2=Z, -1=none
    private Vector2 _lastMousePos = Vector2.Zero;
    private Vector3 _dragStartObjectPos = Vector3.Zero;

    // Matrix state
    private Matrix4x4 _viewMatrix;
    private Matrix4x4 _projectionMatrix;
    private Matrix4x4 _modelMatrix;

    // Camera state
    private Vector3 _cameraPosition = new Vector3(3.0f, 3.0f, 3.0f);
    private Vector3 _cameraTarget = Vector3.Zero;
    
    // Free-fly camera state
    private float _cameraYaw = -135.0f;   // Horizontal rotation (degrees) - facing origin from (3,3,3)
    private float _cameraPitch = -35.26f; // Vertical rotation (degrees) - facing origin from (3,3,3)

    // Public properties for camera control
    public Vector3 CameraPosition 
    { 
        get => _cameraPosition; 
        set 
        { 
            _cameraPosition = value;
            UpdateCamera();
        } 
    }
    
    public float CameraYaw 
    { 
        get => _cameraYaw; 
        set 
        { 
            _cameraYaw = value;
            UpdateCamera();
        } 
    }
    
    public float CameraPitch 
    { 
        get => _cameraPitch; 
        set 
        { 
            _cameraPitch = value;
            UpdateCamera();
        } 
    }

    public List<SceneObject> SceneObjects { get; set; } = new List<SceneObject>();
    public int SelectedObjectIndex { get; set; } = -1;
    public bool ObjectVisible { get; set; } = true;
    
    // Timeline reference for keyframe creation
    private Timeline? _timeline;
    
    // Current selected object helper
    public SceneObject? CurrentObject => 
        SelectedObjectIndex >= 0 && SelectedObjectIndex < SceneObjects.Count 
            ? SceneObjects[SelectedObjectIndex] 
            : null;
    
    // Legacy properties for backwards compatibility
    public Vector3 ObjectPosition 
    { 
        get => CurrentObject?.Position ?? Vector3.Zero; 
        set { if (CurrentObject != null) CurrentObject.Position = value; }
    }
    public Vector3 ObjectRotation 
    { 
        get => CurrentObject?.Rotation ?? Vector3.Zero; 
        set { if (CurrentObject != null) CurrentObject.Rotation = value; }
    }
    public Vector3 ObjectScale 
    { 
        get => CurrentObject?.Scale ?? Vector3.One; 
        set { if (CurrentObject != null) CurrentObject.Scale = value; }
    }

    public Viewport3D(GL gl, TextureAtlas textureAtlas)
    {
        _gl = gl;
        _textureAtlas = textureAtlas;
    }

    public void SetTimeline(Timeline timeline)
    {
        _timeline = timeline;
    }

    public unsafe void Initialize()
    {
        Create3DObjects();
        CreateWireframeObjects();
        CreateGizmoObjects();
        LoadFloorMesh();
        UpdateCamera();
    }

    private string LoadShaderFromResource(string resourceName)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"misr.assets.shaders.{resourceName}");
        if (stream == null)
            throw new FileNotFoundException($"Shader resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private unsafe void Create3DObjects()
    {
        // Cube vertices (position + color + UV)
        float[] cubeVertices = {
            // Front face
            -0.5f, -0.5f,  0.5f,   1.0f, 0.0f, 0.0f,  0.0f, 0.0f, // Bottom-left (red)
             0.5f, -0.5f,  0.5f,   0.0f, 1.0f, 0.0f,  1.0f, 0.0f, // Bottom-right (green)
             0.5f,  0.5f,  0.5f,   0.0f, 0.0f, 1.0f,  1.0f, 1.0f, // Top-right (blue)
            -0.5f,  0.5f,  0.5f,   1.0f, 1.0f, 0.0f,  0.0f, 1.0f, // Top-left (yellow)
            
            // Back face
            -0.5f, -0.5f, -0.5f,   1.0f, 0.0f, 1.0f,  0.0f, 0.0f, // Bottom-left (magenta)
             0.5f, -0.5f, -0.5f,   0.0f, 1.0f, 1.0f,  1.0f, 0.0f, // Bottom-right (cyan)
             0.5f,  0.5f, -0.5f,   1.0f, 1.0f, 1.0f,  1.0f, 1.0f, // Top-right (white)
            -0.5f,  0.5f, -0.5f,   0.5f, 0.5f, 0.5f,  0.0f, 1.0f  // Top-left (gray)
        };

        uint[] cubeIndices = {
            // Front face
            0, 1, 2,   2, 3, 0,
            // Back face
            4, 5, 6,   6, 7, 4,
            // Left face
            7, 3, 0,   0, 4, 7,
            // Right face
            1, 5, 6,   6, 2, 1,
            // Top face
            3, 2, 6,   6, 7, 3,
            // Bottom face
            0, 1, 5,   5, 4, 0
        };

        // Create VAO, VBO, and EBO for cube
        _cubeVAO = _gl.GenVertexArray();
        _cubeVBO = _gl.GenBuffer();
        _cubeEBO = _gl.GenBuffer();

        _gl.BindVertexArray(_cubeVAO);

        // Bind and fill VBO
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _cubeVBO);
        fixed (float* verticesPtr = cubeVertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(cubeVertices.Length * sizeof(float)), verticesPtr, BufferUsageARB.StaticDraw);
        }

        // Bind and fill EBO
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _cubeEBO);
        fixed (uint* indicesPtr = cubeIndices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(cubeIndices.Length * sizeof(uint)), indicesPtr, BufferUsageARB.StaticDraw);
        }

        // Set up vertex attributes
        // Position attribute (location 0)
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);

        // Color attribute (location 1)
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);

        // Texture coordinate attribute (location 2)
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);

        _gl.BindVertexArray(0);

        // Create 3D shader program
        var vertex3DShader = LoadShaderFromResource("basic3d.vert");
        var fragment3DShader = LoadShaderFromResource("basic3d.frag");

        // Create and compile 3D shaders
        var vertexShader3D = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader3D, vertex3DShader);
        _gl.CompileShader(vertexShader3D);

        var fragmentShader3D = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader3D, fragment3DShader);
        _gl.CompileShader(fragmentShader3D);

        // Create 3D shader program
        _cube3DShaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_cube3DShaderProgram, vertexShader3D);
        _gl.AttachShader(_cube3DShaderProgram, fragmentShader3D);
        _gl.LinkProgram(_cube3DShaderProgram);

        // Clean up shaders
        _gl.DeleteShader(vertexShader3D);
        _gl.DeleteShader(fragmentShader3D);

        // Get uniform locations
        _cubeModelLocation = _gl.GetUniformLocation(_cube3DShaderProgram, "model");
        _cubeViewLocation = _gl.GetUniformLocation(_cube3DShaderProgram, "view");
        _cubeProjectionLocation = _gl.GetUniformLocation(_cube3DShaderProgram, "projection");
        _cubeTextureLocation = _gl.GetUniformLocation(_cube3DShaderProgram, "terrainTexture");
        _cubeUseTextureLocation = _gl.GetUniformLocation(_cube3DShaderProgram, "useTexture");
        _cubeTileUVBoundsLocation = _gl.GetUniformLocation(_cube3DShaderProgram, "tileUVBounds");

        // Initialize matrices - 1x1x1 meter cube
        _modelMatrix = Matrix4x4.Identity;
        UpdateCamera();
    }

    private unsafe void CreateWireframeObjects()
    {
        // Wireframe cube vertices (just positions for lines)
        float[] wireframeVertices = {
            // Front face corners
            -0.5f, -0.5f,  0.5f,   // 0: Bottom-left front
             0.5f, -0.5f,  0.5f,   // 1: Bottom-right front
             0.5f,  0.5f,  0.5f,   // 2: Top-right front
            -0.5f,  0.5f,  0.5f,   // 3: Top-left front
            
            // Back face corners
            -0.5f, -0.5f, -0.5f,   // 4: Bottom-left back
             0.5f, -0.5f, -0.5f,   // 5: Bottom-right back
             0.5f,  0.5f, -0.5f,   // 6: Top-right back
            -0.5f,  0.5f, -0.5f    // 7: Top-left back
        };

        // Wireframe line indices (12 edges of a cube)
        uint[] wireframeIndices = {
            // Front face edges
            0, 1,   1, 2,   2, 3,   3, 0,
            // Back face edges  
            4, 5,   5, 6,   6, 7,   7, 4,
            // Connecting edges
            0, 4,   1, 5,   2, 6,   3, 7
        };

        // Create VAO, VBO, and EBO for wireframe
        _wireframeVAO = _gl.GenVertexArray();
        _wireframeVBO = _gl.GenBuffer();
        _wireframeEBO = _gl.GenBuffer();

        _gl.BindVertexArray(_wireframeVAO);

        // Bind and fill VBO
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _wireframeVBO);
        fixed (float* verticesPtr = wireframeVertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(wireframeVertices.Length * sizeof(float)), verticesPtr, BufferUsageARB.StaticDraw);
        }

        // Bind and fill EBO
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _wireframeEBO);
        fixed (uint* indicesPtr = wireframeIndices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(wireframeIndices.Length * sizeof(uint)), indicesPtr, BufferUsageARB.StaticDraw);
        }

        // Set up vertex attributes - only position for wireframe
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);

        _gl.BindVertexArray(0);

        // Create wireframe shader program
        var wireframeVertShader = @"#version 330 core
layout (location = 0) in vec3 aPos;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

void main()
{
    gl_Position = projection * view * model * vec4(aPos, 1.0);
}";

        var wireframeFragShader = @"#version 330 core
out vec4 FragColor;

uniform vec3 color;

void main()
{
    FragColor = vec4(color, 1.0);
}";

        // Create and compile wireframe shaders
        var vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, wireframeVertShader);
        _gl.CompileShader(vertexShader);

        var fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, wireframeFragShader);
        _gl.CompileShader(fragmentShader);

        // Create wireframe shader program
        _wireframeShaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_wireframeShaderProgram, vertexShader);
        _gl.AttachShader(_wireframeShaderProgram, fragmentShader);
        _gl.LinkProgram(_wireframeShaderProgram);

        // Clean up shaders
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        // Get uniform locations
        _wireframeModelLocation = _gl.GetUniformLocation(_wireframeShaderProgram, "model");
        _wireframeViewLocation = _gl.GetUniformLocation(_wireframeShaderProgram, "view");
        _wireframeProjectionLocation = _gl.GetUniformLocation(_wireframeShaderProgram, "projection");
        _wireframeColorLocation = _gl.GetUniformLocation(_wireframeShaderProgram, "color");
    }

    private unsafe void CreateGizmoObjects()
    {
        // Create VAO, VBO, and EBO for gizmo (buffers will be updated dynamically)
        _gizmoVAO = _gl.GenVertexArray();
        _gizmoVBO = _gl.GenBuffer();
        _gizmoEBO = _gl.GenBuffer();

        _gl.BindVertexArray(_gizmoVAO);

        // Set up vertex attributes: position (3) + color (3)
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gizmoVBO);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);

        _gl.BindVertexArray(0);

        // Create gizmo shader program
        var gizmoVertShader = @"#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec3 aColor;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

out vec3 vertexColor;

void main()
{
    gl_Position = projection * view * model * vec4(aPos, 1.0);
    vertexColor = aColor;
}";

        var gizmoFragShader = @"#version 330 core
in vec3 vertexColor;
out vec4 FragColor;

void main()
{
    FragColor = vec4(vertexColor, 1.0);
}";

        // Create and compile gizmo shaders
        var vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, gizmoVertShader);
        _gl.CompileShader(vertexShader);

        var fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, gizmoFragShader);
        _gl.CompileShader(fragmentShader);

        // Create gizmo shader program
        _gizmoShaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_gizmoShaderProgram, vertexShader);
        _gl.AttachShader(_gizmoShaderProgram, fragmentShader);
        _gl.LinkProgram(_gizmoShaderProgram);

        // Clean up shaders
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        // Get uniform locations
        _gizmoModelLocation = _gl.GetUniformLocation(_gizmoShaderProgram, "model");
        _gizmoViewLocation = _gl.GetUniformLocation(_gizmoShaderProgram, "view");
        _gizmoProjectionLocation = _gl.GetUniformLocation(_gizmoShaderProgram, "projection");
    }

    private unsafe void UpdateGizmoBuffers()
    {
        // Create transform gizmo with 3 arrows (X, Y, Z axes) with current hover state
        var gizmoVertices = new List<float>();
        var gizmoIndices = new List<uint>();
        uint indexOffset = 0;

        // X-axis arrow (Red or White if hovered) - points along positive X (longer, skinnier)
        var xColor = _hoveredAxis == 0 ? new Vector3(1, 1, 1) : (Vector3?)null; // White if hovered
        AddArrowToGizmo(gizmoVertices, gizmoIndices, ref indexOffset, 
            Vector3.Zero, Vector3.UnitX, 1.2f, 0.025f, 0.2f, 0.08f, xColor);

        // Y-axis arrow (Green or White if hovered) - points along positive Y (longer, skinnier)
        var yColor = _hoveredAxis == 1 ? new Vector3(1, 1, 1) : (Vector3?)null; // White if hovered
        AddArrowToGizmo(gizmoVertices, gizmoIndices, ref indexOffset,
            Vector3.Zero, Vector3.UnitY, 1.2f, 0.025f, 0.2f, 0.08f, yColor);

        // Z-axis arrow (Blue or White if hovered) - points along positive Z (longer, skinnier)
        var zColor = _hoveredAxis == 2 ? new Vector3(1, 1, 1) : (Vector3?)null; // White if hovered
        AddArrowToGizmo(gizmoVertices, gizmoIndices, ref indexOffset,
            Vector3.Zero, Vector3.UnitZ, 1.2f, 0.025f, 0.2f, 0.08f, zColor);

        var vertexArray = gizmoVertices.ToArray();
        var indexArray = gizmoIndices.ToArray();
        _gizmoVertexCount = indexArray.Length;

        // Update VBO and EBO with new data
        _gl.BindVertexArray(_gizmoVAO);

        // Update VBO
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gizmoVBO);
        fixed (float* verticesPtr = vertexArray)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertexArray.Length * sizeof(float)), verticesPtr, BufferUsageARB.DynamicDraw);
        }

        // Update EBO
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _gizmoEBO);
        fixed (uint* indicesPtr = indexArray)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indexArray.Length * sizeof(uint)), indicesPtr, BufferUsageARB.DynamicDraw);
        }

        _gl.BindVertexArray(0);
    }

    private void AddArrowToGizmo(List<float> vertices, List<uint> indices, ref uint indexOffset, 
        Vector3 origin, Vector3 direction, float length, float shaftRadius, float headLength, float headRadius, Vector3? overrideColor = null)
    {
        var color = overrideColor ?? (direction.X > 0.5f ? new Vector3(1, 0, 0) :  // Red for X
                                     direction.Y > 0.5f ? new Vector3(0, 1, 0) :  // Green for Y
                                                         new Vector3(0, 0, 1));   // Blue for Z

        var shaftEnd = origin + direction * (length - headLength);
        var arrowTip = origin + direction * length;

        // Create rounded shaft using cylindrical geometry
        var perpendicular = Math.Abs(direction.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var right = Vector3.Normalize(Vector3.Cross(direction, perpendicular));
        var up = Vector3.Normalize(Vector3.Cross(right, direction));

        const int cylinderSegments = 8; // More segments for rounder appearance
        var shaftVerticesList = new List<Vector3>();
        
        // Generate cylinder vertices
        for (int ring = 0; ring < 2; ring++) // 2 rings: start and end
        {
            var center = ring == 0 ? origin : shaftEnd;
            for (int seg = 0; seg < cylinderSegments; seg++)
            {
                var angle = (float)(seg * 2 * Math.PI / cylinderSegments);
                var localPos = right * (MathF.Cos(angle) * shaftRadius) + up * (MathF.Sin(angle) * shaftRadius);
                shaftVerticesList.Add(center + localPos);
            }
        }

        // Add shaft vertices
        foreach (var vertex in shaftVerticesList)
        {
            vertices.AddRange(new[] { vertex.X, vertex.Y, vertex.Z, color.X, color.Y, color.Z });
        }

        // Generate cylinder indices
        for (int seg = 0; seg < cylinderSegments; seg++)
        {
            var next = (seg + 1) % cylinderSegments;
            
            // Two triangles per segment
            indices.Add(indexOffset + (uint)seg);
            indices.Add(indexOffset + (uint)(seg + cylinderSegments));
            indices.Add(indexOffset + (uint)next);
            
            indices.Add(indexOffset + (uint)next);
            indices.Add(indexOffset + (uint)(seg + cylinderSegments));
            indices.Add(indexOffset + (uint)(next + cylinderSegments));
        }
        indexOffset += (uint)(cylinderSegments * 2);

        // Arrow head (cone with circular base)
        var headVerticesList = new List<Vector3>();
        
        // Add cone base vertices (circle at shaft end)
        for (int seg = 0; seg < cylinderSegments; seg++)
        {
            var angle = (float)(seg * 2 * Math.PI / cylinderSegments);
            var localPos = right * (MathF.Cos(angle) * headRadius) + up * (MathF.Sin(angle) * headRadius);
            headVerticesList.Add(shaftEnd + localPos);
        }
        
        // Add cone tip
        headVerticesList.Add(arrowTip);

        // Add head vertices
        foreach (var vertex in headVerticesList)
        {
            vertices.AddRange(new[] { vertex.X, vertex.Y, vertex.Z, color.X, color.Y, color.Z });
        }

        // Generate cone indices
        for (int seg = 0; seg < cylinderSegments; seg++)
        {
            var next = (seg + 1) % cylinderSegments;
            
            // Triangle from base edge to tip
            indices.Add(indexOffset + (uint)seg);
            indices.Add(indexOffset + (uint)next);
            indices.Add(indexOffset + (uint)cylinderSegments); // tip vertex
        }
        indexOffset += (uint)(cylinderSegments + 1);
    }

    private unsafe void LoadFloorMesh()
    {
        // Get tile040 UV coordinates from atlas
        var tile040UV = _textureAtlas.AtlasMapping.ContainsKey("tile040") ? _textureAtlas.AtlasMapping["tile040"] : new AtlasRect { U1 = 0, V1 = 0, U2 = 1, V2 = 1 };
        
        // For 1 tile per meter on a 64x64 meter floor, we need 64 repetitions in each direction
        // But we must keep UV coordinates within tile040's atlas boundaries
        
        // Floor vertices from OBJ file (position + color + UV coordinates)
        // Use simple 0-64 UV coordinates that will be remapped in shader or by texture wrapping
        float[] floorVertices = {
            // Position                    Color (white)           UV coordinates (0-64 range for 64 repetitions)
            -32.0f, 0.0f,  32.0f,       1.0f, 1.0f, 1.0f,       0.0f, 0.0f,   // Top-left
             32.0f, 0.0f,  32.0f,       1.0f, 1.0f, 1.0f,       64.0f, 0.0f,  // Top-right
             32.0f, 0.0f, -32.0f,       1.0f, 1.0f, 1.0f,       64.0f, 64.0f, // Bottom-right
            -32.0f, 0.0f, -32.0f,       1.0f, 1.0f, 1.0f,       0.0f, 64.0f   // Bottom-left
        };

        uint[] floorIndices = {
            0, 1, 2,   // First triangle
            2, 3, 0    // Second triangle
        };

        _floorVertexCount = floorIndices.Length;

        // Create VAO, VBO, and EBO for floor
        _floorVAO = _gl.GenVertexArray();
        _floorVBO = _gl.GenBuffer();
        _floorEBO = _gl.GenBuffer();

        _gl.BindVertexArray(_floorVAO);

        // Bind and fill VBO
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _floorVBO);
        fixed (float* verticesPtr = floorVertices)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(floorVertices.Length * sizeof(float)), verticesPtr, BufferUsageARB.StaticDraw);
        }

        // Bind and fill EBO
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _floorEBO);
        fixed (uint* indicesPtr = floorIndices)
        {
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(floorIndices.Length * sizeof(uint)), indicesPtr, BufferUsageARB.StaticDraw);
        }

        // Set up vertex attributes (same layout as cube)
        // Position attribute (location 0)
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)0);
        _gl.EnableVertexAttribArray(0);

        // Color attribute (location 1)
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(3 * sizeof(float)));
        _gl.EnableVertexAttribArray(1);

        // Texture coordinate attribute (location 2)
        _gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), (void*)(6 * sizeof(float)));
        _gl.EnableVertexAttribArray(2);

        _gl.BindVertexArray(0);
    }

    private void UpdateCamera()
    {
        // Calculate forward direction from yaw and pitch
        float yawRad = _cameraYaw * MathF.PI / 180.0f;
        float pitchRad = _cameraPitch * MathF.PI / 180.0f;
        
        Vector3 forward = new Vector3(
            MathF.Cos(pitchRad) * MathF.Cos(yawRad),
            MathF.Sin(pitchRad),
            MathF.Cos(pitchRad) * MathF.Sin(yawRad)
        );
        
        _cameraTarget = _cameraPosition + forward;
        _viewMatrix = Matrix4x4.CreateLookAt(_cameraPosition, _cameraTarget, Vector3.UnitY);
    }

    public void UpdateProjection(float aspectRatio)
    {
        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4.0f, aspectRatio, 0.1f, 100.0f);
    }

    public void UpdateObjectTransform()
    {
        // Convert degrees to radians
        var rotationRadians = ObjectRotation * (MathF.PI / 180.0f);
        
        // Create transformation matrices (SRT order: Scale -> Rotation -> Translation)
        var scaleMatrix = Matrix4x4.CreateScale(ObjectScale);
        var rotationMatrix = Matrix4x4.CreateRotationX(rotationRadians.X) *
                           Matrix4x4.CreateRotationY(rotationRadians.Y) *
                           Matrix4x4.CreateRotationZ(rotationRadians.Z);
        var translationMatrix = Matrix4x4.CreateTranslation(ObjectPosition);
        
        // Combine transformations in SRT order
        _modelMatrix = scaleMatrix * rotationMatrix * translationMatrix;
    }

    public unsafe void Render(Vector2 viewportPos, Vector2 viewportSize, int windowHeight)
    {
        // Skip rendering if viewport is too small
        if (viewportSize.X <= 0 || viewportSize.Y <= 0) return;
        
        // Set viewport for 3D rendering (flip Y coordinate for OpenGL)
        int viewportX = (int)viewportPos.X;
        int viewportY = (int)(windowHeight - viewportPos.Y - viewportSize.Y);
        _gl.Viewport(viewportX, viewportY, (uint)viewportSize.X, (uint)viewportSize.Y);
        
        // Disable ImGui's blend state for 3D rendering
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        
        // Enable depth testing for 3D
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        
        // Clear color and depth buffers for this viewport
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(viewportX, viewportY, (uint)viewportSize.X, (uint)viewportSize.Y);
        _gl.ClearColor(0.576471f, 0.576471f, 1.0f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Disable(EnableCap.ScissorTest);
        
        // Use 3D shader
        _gl.UseProgram(_cube3DShaderProgram);
        
        // Update camera projection with current aspect ratio
        float aspectRatio = viewportSize.X / viewportSize.Y;
        UpdateProjection(aspectRatio);
        
        // Set matrices
        Span<float> modelArray = stackalloc float[16];
        Span<float> viewArray = stackalloc float[16];
        Span<float> projectionArray = stackalloc float[16];
        
        MatrixToArray(_viewMatrix, viewArray);
        MatrixToArray(_projectionMatrix, projectionArray);
        
        _gl.UniformMatrix4(_cubeViewLocation, 1, false, viewArray);
        _gl.UniformMatrix4(_cubeProjectionLocation, 1, false, projectionArray);
        
        // Bind terrain texture (use first atlas for now)
        if (_textureAtlas.AtlasTextures.Count > 0)
        {
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, _textureAtlas.AtlasTextures[0]);
            _gl.Uniform1(_cubeTextureLocation, 0);
        }
        
        // Render floor (no animation - always at origin) with texture
        if (_textureAtlas.AtlasMapping.ContainsKey("tile040"))
        {
            var tile040 = _textureAtlas.AtlasMapping["tile040"];
            
            // Bind the correct atlas for tile040
            if (tile040.AtlasIndex < _textureAtlas.AtlasTextures.Count)
            {
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, _textureAtlas.AtlasTextures[tile040.AtlasIndex]);
                _gl.Uniform1(_cubeTextureLocation, 0);
                
                // Set tile UV bounds for tile040
                _gl.Uniform4(_cubeTileUVBoundsLocation, tile040.U1, tile040.V1, tile040.U2, tile040.V2);
            }
        }
        
        var floorModelMatrix = Matrix4x4.Identity;
        Span<float> floorModelArray = stackalloc float[16];
        MatrixToArray(floorModelMatrix, floorModelArray);
        _gl.UniformMatrix4(_cubeModelLocation, 1, false, floorModelArray);
        _gl.Uniform1(_cubeUseTextureLocation, 1); // Use texture for floor

        _gl.BindVertexArray(_floorVAO);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)_floorVertexCount, DrawElementsType.UnsignedInt, null);

        // Render all objects with meshes
        if (ObjectVisible)
        {
            foreach (var obj in SceneObjects)
            {
                if (obj.HasMesh)
                {
                    // Update model matrix for this object
                    var rotationRadians = obj.Rotation * (MathF.PI / 180.0f);
                    var rotationMatrix = Matrix4x4.CreateRotationX(rotationRadians.X) *
                                       Matrix4x4.CreateRotationY(rotationRadians.Y) *
                                       Matrix4x4.CreateRotationZ(rotationRadians.Z);
                    var scaleMatrix = Matrix4x4.CreateScale(obj.Scale);
                    var rotationScaleMatrix = scaleMatrix * rotationMatrix;
                    var translationMatrix = Matrix4x4.CreateTranslation(obj.Position);
                    _modelMatrix = rotationScaleMatrix * translationMatrix;
                    
                    MatrixToArray(_modelMatrix, modelArray);
                    _gl.UniformMatrix4(_cubeModelLocation, 1, false, modelArray);
                    _gl.Uniform1(_cubeUseTextureLocation, 0); // Don't use texture for cube

                    _gl.BindVertexArray(_cubeVAO);
                    _gl.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, null);
                }
            }
        }

        // Render wireframe highlight for selected object
        if (SelectedObjectIndex >= 0 && SelectedObjectIndex < SceneObjects.Count)
        {
            var selectedObj = SceneObjects[SelectedObjectIndex];
            if (selectedObj.HasMesh)
            {
                // Use wireframe shader
                _gl.UseProgram(_wireframeShaderProgram);
                
                // Set matrices for wireframe
                _gl.UniformMatrix4(_wireframeViewLocation, 1, false, viewArray);
                _gl.UniformMatrix4(_wireframeProjectionLocation, 1, false, projectionArray);
                
                // Set white color for highlight
                _gl.Uniform3(_wireframeColorLocation, 1.0f, 1.0f, 1.0f);
                
                // Scale the wireframe slightly larger than the object for better visibility
                var rotationRadians = selectedObj.Rotation * (MathF.PI / 180.0f);
                var rotationMatrix = Matrix4x4.CreateRotationX(rotationRadians.X) *
                                   Matrix4x4.CreateRotationY(rotationRadians.Y) *
                                   Matrix4x4.CreateRotationZ(rotationRadians.Z);
                var highlightScale = selectedObj.Scale * 1.05f; // 5% larger for highlight
                var scaleMatrix = Matrix4x4.CreateScale(highlightScale);
                var rotationScaleMatrix = scaleMatrix * rotationMatrix;
                var translationMatrix = Matrix4x4.CreateTranslation(selectedObj.Position);
                var wireframeModelMatrix = rotationScaleMatrix * translationMatrix;
                
                Span<float> wireframeModelArray = stackalloc float[16];
                MatrixToArray(wireframeModelMatrix, wireframeModelArray);
                _gl.UniformMatrix4(_wireframeModelLocation, 1, false, wireframeModelArray);
                
                // Disable depth testing temporarily to ensure wireframe is always visible
                _gl.Disable(EnableCap.DepthTest);
                
                // Enable line width (may not work on all systems)
                _gl.LineWidth(2.0f);
                
                _gl.BindVertexArray(_wireframeVAO);
                _gl.DrawElements(PrimitiveType.Lines, 24, DrawElementsType.UnsignedInt, null);
                
                // Restore depth testing and line width
                _gl.Enable(EnableCap.DepthTest);
                _gl.LineWidth(1.0f);
            }
        }

        // Render transform gizmo for selected object
        if (SelectedObjectIndex >= 0 && SelectedObjectIndex < SceneObjects.Count)
        {
            var selectedObj = SceneObjects[SelectedObjectIndex];
            if (selectedObj.HasMesh)
            {
                // Update gizmo buffers with current hover state
                UpdateGizmoBuffers();
                
                // Use gizmo shader
                _gl.UseProgram(_gizmoShaderProgram);
                
                // Set matrices for gizmo
                _gl.UniformMatrix4(_gizmoViewLocation, 1, false, viewArray);
                _gl.UniformMatrix4(_gizmoProjectionLocation, 1, false, projectionArray);
                
                // Calculate gizmo size based on distance to camera for consistent screen size
                var distanceToCamera = Vector3.Distance(_cameraPosition, selectedObj.Position);
                var gizmoScale = distanceToCamera * 0.1f; // Scale factor for consistent size
                
                // Create gizmo transform at object position
                var gizmoMatrix = Matrix4x4.CreateScale(gizmoScale) * Matrix4x4.CreateTranslation(selectedObj.Position);
                
                Span<float> gizmoModelArray = stackalloc float[16];
                MatrixToArray(gizmoMatrix, gizmoModelArray);
                _gl.UniformMatrix4(_gizmoModelLocation, 1, false, gizmoModelArray);
                
                // Disable depth testing to ensure gizmo is always visible
                _gl.Disable(EnableCap.DepthTest);
                
                _gl.BindVertexArray(_gizmoVAO);
                _gl.DrawElements(PrimitiveType.Triangles, (uint)_gizmoVertexCount, DrawElementsType.UnsignedInt, null);
                
                // Restore depth testing
                _gl.Enable(EnableCap.DepthTest);
            }
        }
        
        // Restore ImGui state
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.DepthTest);
    }

    private static void MatrixToArray(Matrix4x4 matrix, Span<float> array)
    {
        array[0] = matrix.M11; array[1] = matrix.M12; array[2] = matrix.M13; array[3] = matrix.M14;
        array[4] = matrix.M21; array[5] = matrix.M22; array[6] = matrix.M23; array[7] = matrix.M24;
        array[8] = matrix.M31; array[9] = matrix.M32; array[10] = matrix.M33; array[11] = matrix.M34;
        array[12] = matrix.M41; array[13] = matrix.M42; array[14] = matrix.M43; array[15] = matrix.M44;
    }

    public void Dispose()
    {
        // Clean up cube resources
        _gl.DeleteVertexArray(_cubeVAO);
        _gl.DeleteBuffer(_cubeVBO);
        _gl.DeleteBuffer(_cubeEBO);
        _gl.DeleteProgram(_cube3DShaderProgram);
        
        // Clean up wireframe resources
        _gl.DeleteVertexArray(_wireframeVAO);
        _gl.DeleteBuffer(_wireframeVBO);
        _gl.DeleteBuffer(_wireframeEBO);
        _gl.DeleteProgram(_wireframeShaderProgram);
        
        // Clean up gizmo resources
        _gl.DeleteVertexArray(_gizmoVAO);
        _gl.DeleteBuffer(_gizmoVBO);
        _gl.DeleteBuffer(_gizmoEBO);
        _gl.DeleteProgram(_gizmoShaderProgram);
        
        // Clean up floor resources
        _gl.DeleteVertexArray(_floorVAO);
        _gl.DeleteBuffer(_floorVBO);
        _gl.DeleteBuffer(_floorEBO);
    }

    // Object selection methods - returns -2 for gizmo hit, -1 for empty space, or object index
    public int GetObjectAtScreenPoint(Vector2 screenPos, Vector2 viewportPos, Vector2 viewportSize)
    {
        // Convert screen coordinates to normalized device coordinates (-1 to 1)
        var normalizedX = (screenPos.X - viewportPos.X) / viewportSize.X * 2.0f - 1.0f;
        var normalizedY = 1.0f - (screenPos.Y - viewportPos.Y) / viewportSize.Y * 2.0f;

        // Create ray from camera through the clicked point
        var ray = ScreenPointToRay(new Vector2(normalizedX, normalizedY), viewportSize);
        
        // First check if we're clicking on the gizmo (if there's a selected object)
        if (SelectedObjectIndex >= 0 && SelectedObjectIndex < SceneObjects.Count)
        {
            var selectedObj = SceneObjects[SelectedObjectIndex];
            if (selectedObj.HasMesh)
            {
                var gizmoAxis = GetGizmoAxisAtRay(ray, selectedObj.Position);
                if (gizmoAxis >= 0)
                {
                    // Start gizmo dragging
                    _isDraggingGizmo = true;
                    _draggedAxis = gizmoAxis;
                    _lastMousePos = screenPos;
                    _dragStartObjectPos = selectedObj.Position;
                    return -2; // Special value indicating gizmo was clicked
                }
            }
        }
        
        // Find closest intersected object
        float closestDistance = float.MaxValue;
        int closestObjectIndex = -1;
        
        for (int i = 0; i < SceneObjects.Count; i++)
        {
            var obj = SceneObjects[i];
            if (!obj.HasMesh) continue;
            
            var distance = RayIntersectCube(ray.Origin, ray.Direction, obj.Position, obj.Scale);
            
            if (distance.HasValue && distance.Value < closestDistance)
            {
                closestDistance = distance.Value;
                closestObjectIndex = i;
            }
        }
        
        return closestObjectIndex; // Returns -1 if no object hit (empty space)
    }

    public void HandleMouseDrag(Vector2 currentMousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        if (!_isDraggingGizmo || _draggedAxis < 0 || SelectedObjectIndex < 0) return;

        var selectedObj = SceneObjects[SelectedObjectIndex];
        if (!selectedObj.HasMesh) return;

        // Calculate mouse movement
        var mouseDelta = currentMousePos - _lastMousePos;
        _lastMousePos = currentMousePos;

        // Convert mouse movement to world space movement along the dragged axis
        var moveScale = 0.01f; // Sensitivity factor
        var worldMovement = Vector3.Zero;

        switch (_draggedAxis)
        {
            case 0: // X axis
                worldMovement = Vector3.UnitX * mouseDelta.X * moveScale;
                break;
            case 1: // Y axis  
                worldMovement = Vector3.UnitY * -mouseDelta.Y * moveScale; // Invert Y for screen coordinates
                break;
            case 2: // Z axis
                worldMovement = Vector3.UnitZ * -mouseDelta.X * moveScale; // Use X movement for Z axis
                break;
        }

        // Apply the movement to the object
        selectedObj.Position += worldMovement;
    }

    public void EndGizmoDrag()
    {
        // Add keyframes for the position change if we were dragging
        if (_isDraggingGizmo && _timeline != null && SelectedObjectIndex >= 0 && SelectedObjectIndex < SceneObjects.Count)
        {
            var selectedObj = SceneObjects[SelectedObjectIndex];
            if (selectedObj.HasMesh)
            {
                // Check if position actually changed
                var positionChanged = Vector3.Distance(_dragStartObjectPos, selectedObj.Position) > 0.001f;
                
                if (positionChanged)
                {
                    // Add keyframes for all three position components at current timeline frame
                    _timeline.AddKeyframe("position.x", _timeline.CurrentFrame, selectedObj.Position.X);
                    _timeline.AddKeyframe("position.y", _timeline.CurrentFrame, selectedObj.Position.Y);
                    _timeline.AddKeyframe("position.z", _timeline.CurrentFrame, selectedObj.Position.Z);
                }
            }
        }
        
        _isDraggingGizmo = false;
        _draggedAxis = -1;
    }

    public void UpdateGizmoHover(Vector2 mousePos, Vector2 viewportPos, Vector2 viewportSize)
    {
        // Only update hover if we have a selected object and are not currently dragging
        if (_isDraggingGizmo || SelectedObjectIndex < 0 || SelectedObjectIndex >= SceneObjects.Count)
        {
            _hoveredAxis = -1;
            return;
        }

        var selectedObj = SceneObjects[SelectedObjectIndex];
        if (!selectedObj.HasMesh)
        {
            _hoveredAxis = -1;
            return;
        }

        // Convert screen coordinates to normalized device coordinates (-1 to 1)
        var normalizedX = (mousePos.X - viewportPos.X) / viewportSize.X * 2.0f - 1.0f;
        var normalizedY = 1.0f - (mousePos.Y - viewportPos.Y) / viewportSize.Y * 2.0f;

        // Create ray from camera through the mouse position
        var ray = ScreenPointToRay(new Vector2(normalizedX, normalizedY), viewportSize);
        
        // Check if we're hovering over the gizmo
        _hoveredAxis = GetGizmoAxisAtRay(ray, selectedObj.Position);
    }

    private (Vector3 Origin, Vector3 Direction) ScreenPointToRay(Vector2 normalizedScreenPoint, Vector2 viewportSize)
    {
        // Calculate aspect ratio
        float aspectRatio = viewportSize.X / viewportSize.Y;
        
        // Create inverse matrices
        Matrix4x4.Invert(_projectionMatrix, out var invProjection);
        Matrix4x4.Invert(_viewMatrix, out var invView);
        
        // Convert normalized screen coordinates to view space
        var nearPoint = new Vector4(normalizedScreenPoint.X, normalizedScreenPoint.Y, -1.0f, 1.0f);
        var farPoint = new Vector4(normalizedScreenPoint.X, normalizedScreenPoint.Y, 1.0f, 1.0f);
        
        // Transform to view space
        var nearView = Vector4.Transform(nearPoint, invProjection);
        var farView = Vector4.Transform(farPoint, invProjection);
        
        // Perspective divide
        nearView /= nearView.W;
        farView /= farView.W;
        
        // Transform to world space
        var nearWorld = Vector4.Transform(nearView, invView);
        var farWorld = Vector4.Transform(farView, invView);
        
        var rayOrigin = new Vector3(nearWorld.X, nearWorld.Y, nearWorld.Z);
        var rayEnd = new Vector3(farWorld.X, farWorld.Y, farWorld.Z);
        var rayDirection = Vector3.Normalize(rayEnd - rayOrigin);
        
        return (rayOrigin, rayDirection);
    }

    private float? RayIntersectCube(Vector3 rayOrigin, Vector3 rayDirection, Vector3 cubePosition, Vector3 cubeScale)
    {
        // Calculate cube bounds
        var halfSize = cubeScale * 0.5f;
        var minBounds = cubePosition - halfSize;
        var maxBounds = cubePosition + halfSize;
        
        // Ray-box intersection using slab method
        var invDir = new Vector3(
            Math.Abs(rayDirection.X) > 1e-6f ? 1.0f / rayDirection.X : float.MaxValue,
            Math.Abs(rayDirection.Y) > 1e-6f ? 1.0f / rayDirection.Y : float.MaxValue,
            Math.Abs(rayDirection.Z) > 1e-6f ? 1.0f / rayDirection.Z : float.MaxValue
        );
        
        var t1 = (minBounds.X - rayOrigin.X) * invDir.X;
        var t2 = (maxBounds.X - rayOrigin.X) * invDir.X;
        var t3 = (minBounds.Y - rayOrigin.Y) * invDir.Y;
        var t4 = (maxBounds.Y - rayOrigin.Y) * invDir.Y;
        var t5 = (minBounds.Z - rayOrigin.Z) * invDir.Z;
        var t6 = (maxBounds.Z - rayOrigin.Z) * invDir.Z;
        
        var tmin = Math.Max(Math.Max(Math.Min(t1, t2), Math.Min(t3, t4)), Math.Min(t5, t6));
        var tmax = Math.Min(Math.Min(Math.Max(t1, t2), Math.Max(t3, t4)), Math.Max(t5, t6));
        
        // If tmax < 0, ray is intersecting AABB, but whole AABB is behind us
        if (tmax < 0)
            return null;
        
        // If tmin > tmax, ray doesn't intersect AABB
        if (tmin > tmax)
            return null;
        
        // Return the closest intersection distance
        return tmin > 0 ? tmin : tmax;
    }

    private int GetGizmoAxisAtRay((Vector3 Origin, Vector3 Direction) ray, Vector3 gizmoPosition)
    {
        var distanceToCamera = Vector3.Distance(_cameraPosition, gizmoPosition);
        var gizmoScale = distanceToCamera * 0.1f;
        var arrowLength = 1.2f * gizmoScale;
        
        var axisDirections = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        
        float bestScore = float.MaxValue;
        int closestAxis = -1;
        var hitThreshold = 0.001f; // Much tighter threshold for precision
        
        for (int axis = 0; axis < 3; axis++)
        {
            var axisDirection = axisDirections[axis];
            var axisEnd = gizmoPosition + axisDirection * arrowLength;
            
            // Calculate vectors from ray origin to gizmo center and axis end
            var toCenter = gizmoPosition - ray.Origin;
            var toEnd = axisEnd - ray.Origin;
            
            // Check alignment with both center and end of axis
            var distToCenter = Vector3.Distance(ray.Origin, gizmoPosition);
            var distToEnd = Vector3.Distance(ray.Origin, axisEnd);
            
            // Normalize vectors for dot product
            var dirToCenter = Vector3.Normalize(toCenter);
            var dirToEnd = Vector3.Normalize(toEnd);
            
            // Calculate alignment scores (higher = better alignment)
            var alignmentToCenter = Vector3.Dot(ray.Direction, dirToCenter);
            var alignmentToEnd = Vector3.Dot(ray.Direction, dirToEnd);
            
            // Use the best alignment and weight by distance
            var bestAlignment = Math.Max(alignmentToCenter, alignmentToEnd);
            var avgDistance = (distToCenter + distToEnd) * 0.5f;
            
            // Create a score that combines alignment and distance proximity
            // Lower score is better
            var score = (1.0f - bestAlignment) + (avgDistance * 0.01f); // Distance weight is small
            
            // Only consider if alignment is reasonable
            if (bestAlignment > (1.0f - hitThreshold) && score < bestScore)
            {
                bestScore = score;
                closestAxis = axis;
            }
        }
        
        return closestAxis;
    }
}
