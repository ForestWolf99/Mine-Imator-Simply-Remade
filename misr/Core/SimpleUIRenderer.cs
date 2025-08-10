using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImGuiNET;
using System.Numerics;
using System.Runtime.InteropServices;
using StbImageSharp;
using System.Reflection;
using Misr.UI;
using Misr.Rendering;
using Misr.Core;

namespace Misr;





public class SimpleUIRenderer : IDisposable
{
    private IWindow _window;
    private GL _gl = null!;
    private IInputContext _inputContext = null!;
    private bool _initialized = false;
    
    // ImGui rendering
    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _elementBuffer;
    private uint _shaderProgram;
    private uint _fontTexture;
    private int _attribLocationTex;
    private int _attribLocationProjMtx;
    private int _attribLocationVtxPos;
    private int _attribLocationVtxUV;
    private int _attribLocationVtxColor;


    
    // Store viewport position for 3D rendering
    private Vector2 _viewportPosition = Vector2.Zero;
    private Vector2 _viewportSize = Vector2.Zero;
    
    // 3D Viewport
    private Viewport3D _viewport3D = null!;
    
    // Texture atlas
    private TextureAtlas _textureAtlas = null!;
    
    // Timeline
    private Timeline _timeline = null!;
    
    // Properties panel
    private PropertiesPanel _propertiesPanel = null!;
    
    // Scene tree panel
    private SceneTreePanel _sceneTreePanel = null!;
    
    // Scene objects
    private List<SceneObject> _sceneObjects = new List<SceneObject>();
    private int _selectedObjectIndex = -1;
    private int _lastKnownSceneTreeSelection = -1;
    private int _lastKnownPropertiesSelection = -1;
    
    // Mouse capture state for camera control
    private bool _mouseCaptured = false;
    private Vector2 _lastMousePos = Vector2.Zero;
    
    // Keyboard state
    private bool _wPressed = false;
    private bool _sPressed = false;
    private bool _aPressed = false;
    private bool _dPressed = false;
    private bool _ePressed = false;
    private bool _qPressed = false;
    

    


    public SimpleUIRenderer(object? vk, IWindow window)
    {
        _window = window;
    }

    public unsafe void Initialize()
    {
        try
        {
            _gl = GL.GetApi(_window);
            _inputContext = _window.CreateInput();
            _textureAtlas = new TextureAtlas(_gl);
        _viewport3D = new Viewport3D(_gl, _textureAtlas);
            _timeline = new Timeline();
            _propertiesPanel = new PropertiesPanel();
            _sceneTreePanel = new SceneTreePanel();
            _propertiesPanel.SetViewport3D(_viewport3D);
            _propertiesPanel.SetTimeline(_timeline);
            _viewport3D.SetTimeline(_timeline);
            
            // Link the same object lists
            _propertiesPanel.SceneObjects = _sceneObjects;
            _sceneTreePanel.SceneObjects = _sceneObjects;
            _viewport3D.SceneObjects = _sceneObjects;
            _timeline.SceneObjects = _sceneObjects;
            
            // Create ImGui context
            ImGui.CreateContext();
            var io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            
            // Setup input callbacks
            SetupInput();
            
            // Create device objects
            CreateDeviceObjects();
            CreateFontsTexture();
            _textureAtlas.CreateTerrainAtlases();
            _viewport3D.Initialize();
            
            _initialized = true;
            Console.WriteLine("ImGui UI Renderer initialized successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ImGui initialization error: {ex.Message}");
        }
    }

    private void SetupInput()
    {
        var io = ImGui.GetIO();
        
        // Setup mouse
        foreach (var mouse in _inputContext.Mice)
        {
            mouse.MouseMove += (mouse, position) =>
            {
                io.MousePos = new Vector2(position.X, position.Y);
            };
            
            mouse.MouseDown += (mouse, button) =>
            {
                if (button == MouseButton.Left) io.MouseDown[0] = true;
                if (button == MouseButton.Right) io.MouseDown[1] = true;
                if (button == MouseButton.Middle) io.MouseDown[2] = true;
            };
            
            mouse.MouseUp += (mouse, button) =>
            {
                if (button == MouseButton.Left) io.MouseDown[0] = false;
                if (button == MouseButton.Right) io.MouseDown[1] = false;
                if (button == MouseButton.Middle) io.MouseDown[2] = false;
            };
            
            mouse.Scroll += (mouse, scroll) =>
            {
                // Always do normal scrolling - zoom is handled elsewhere
                io.MouseWheel = scroll.Y;
            };
        }
        
        // Setup keyboard
        foreach (var keyboard in _inputContext.Keyboards)
        {
            keyboard.KeyChar += (keyboard, character) =>
            {
                io.AddInputCharacter(character);
            };
            
            keyboard.KeyDown += (keyboard, key, scancode) =>
            {
                if (key == Key.W) _wPressed = true;
                if (key == Key.S) _sPressed = true;
                if (key == Key.A) _aPressed = true;
                if (key == Key.D) _dPressed = true;
                if (key == Key.E) _ePressed = true;
                if (key == Key.Q) _qPressed = true;
                if (key == Key.F7) SpawnCube();
            };
            
            keyboard.KeyUp += (keyboard, key, scancode) =>
            {
                if (key == Key.W) _wPressed = false;
                if (key == Key.S) _sPressed = false;
                if (key == Key.A) _aPressed = false;
                if (key == Key.D) _dPressed = false;
                if (key == Key.E) _ePressed = false;
                if (key == Key.Q) _qPressed = false;
            };
        }
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

    private unsafe void CreateDeviceObjects()
    {
        // Load shaders from files
        var vertexShaderSource = LoadShaderFromResource("imgui.vert");
        var fragmentShaderSource = LoadShaderFromResource("imgui.frag");

        // Create shaders
        var vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexShaderSource);
        _gl.CompileShader(vertexShader);

        var fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentShaderSource);
        _gl.CompileShader(fragmentShader);

        _shaderProgram = _gl.CreateProgram();
        _gl.AttachShader(_shaderProgram, vertexShader);
        _gl.AttachShader(_shaderProgram, fragmentShader);
        _gl.LinkProgram(_shaderProgram);

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _attribLocationTex = _gl.GetUniformLocation(_shaderProgram, "Texture");
        _attribLocationProjMtx = _gl.GetUniformLocation(_shaderProgram, "ProjMtx");
        _attribLocationVtxPos = _gl.GetAttribLocation(_shaderProgram, "Position");
        _attribLocationVtxUV = _gl.GetAttribLocation(_shaderProgram, "UV");
        _attribLocationVtxColor = _gl.GetAttribLocation(_shaderProgram, "Color");

        // Create buffers
        _vertexArray = _gl.GenVertexArray();
        _vertexBuffer = _gl.GenBuffer();
        _elementBuffer = _gl.GenBuffer();
    }

    private unsafe void CreateFontsTexture()
    {
        var io = ImGui.GetIO();
        
        // Build texture atlas
        io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);

        // Create OpenGL texture
        _fontTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _fontTexture);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels.ToPointer());

        // Store the texture identifier
        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }











    public void Update(float deltaTime)
    {
        if (!_initialized) return;

        // Sync selection between components - detect actual user changes
        var previousSelection = _selectedObjectIndex;
        
        // Check if scene tree selection changed by user interaction
        if (_sceneTreePanel.SelectedObjectIndex != _lastKnownSceneTreeSelection && 
            _sceneTreePanel.SelectedObjectIndex != _selectedObjectIndex)
        {
            _selectedObjectIndex = _sceneTreePanel.SelectedObjectIndex;
        }
        // Check if properties panel selection changed by user interaction
        else if (_propertiesPanel.SelectedObjectIndex != _lastKnownPropertiesSelection && 
                 _propertiesPanel.SelectedObjectIndex != _selectedObjectIndex)
        {
            _selectedObjectIndex = _propertiesPanel.SelectedObjectIndex;
        }
        
        // Update all panels to match current selection
        _sceneTreePanel.SelectedObjectIndex = _selectedObjectIndex;
        _propertiesPanel.SelectedObjectIndex = _selectedObjectIndex;
        _viewport3D.SelectedObjectIndex = _selectedObjectIndex;
        _timeline.SelectedObjectIndex = _selectedObjectIndex;
        
        // Remember the current state for next frame
        _lastKnownSceneTreeSelection = _selectedObjectIndex;
        _lastKnownPropertiesSelection = _selectedObjectIndex;
        
        // Update timeline
        _timeline.Update(deltaTime);
        
        // Update all objects' transforms from keyframes only when playing or scrubbing
        if (_timeline.IsPlaying || _timeline.IsScrubbing)
        {
            foreach (var obj in _sceneObjects)
            {
                if (_timeline.HasKeyframes(obj))
                {
                    var animatedPosition = _timeline.GetAnimatedPosition(obj);
                    var animatedRotation = _timeline.GetAnimatedRotation(obj);
                    var animatedScale = _timeline.GetAnimatedScale(obj);
                    
                    obj.Position = animatedPosition;
                    obj.Rotation = animatedRotation;
                    obj.Scale = animatedScale;
                }
            }
        }
        
        // Update selected object's properties panel display only when playing or scrubbing
        if (_timeline.HasKeyframes() && (_timeline.IsPlaying || _timeline.IsScrubbing))
        {
            var keyframePosition = _timeline.GetAnimatedPosition();
            var keyframeRotation = _timeline.GetAnimatedRotation();
            var keyframeScale = _timeline.GetAnimatedScale();
            _propertiesPanel.ObjectPosition = keyframePosition;
            _propertiesPanel.ObjectRotation = keyframeRotation;
            _propertiesPanel.ObjectScale = keyframeScale;
        }
        
        // Update 3D scene - pass object transform to viewport
        _viewport3D.ObjectPosition = _propertiesPanel.ObjectPosition;
        _viewport3D.ObjectRotation = _propertiesPanel.ObjectRotation;
        _viewport3D.ObjectScale = _propertiesPanel.ObjectScale;

        // Setup ImGui frame
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(_window.Size.X, _window.Size.Y);
        io.DeltaTime = deltaTime;



        ImGui.NewFrame();
    }

    public void DrawFrame()
    {
        if (!_initialized) return;

        // Update window title
        var propertiesInfo = _propertiesPanel.GetPropertiesInfo();
        var timelineInfo = $"Frame:{_timeline.CurrentFrame}/{_timeline.TotalFrames} {(_timeline.IsPlaying ? "[PLAYING]" : "[STOPPED]")} Zoom:{_timeline.TimelineZoom:F1}x";
        _window.Title = $"Misr - ImGui UI - {propertiesInfo} | {timelineInfo}";

        // Clear background
        _gl.ClearColor(0.15f, 0.15f, 0.20f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Draw ImGui UI
        DrawImGuiUI();

        // Render ImGui
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
        
        // Render 3D scene on top of ImGui in the viewport area  
        _viewport3D.Render(_viewportPosition, _viewportSize, _window.Size.Y);
    }

    private void DrawImGuiUI()
    {
        var windowSize = _window.Size;
        
        // Render scene tree panel (takes up 1/3 of vertical space on the right)
        _sceneTreePanel.Render(new Vector2(windowSize.X, windowSize.Y));
        
        // Render properties panel (takes up 2/3 of vertical space on the right, below scene tree)
        _propertiesPanel.Render(new Vector2(windowSize.X, windowSize.Y));

        // Render timeline
        _timeline.Render(new Vector2(windowSize.X, windowSize.Y));

        // 3D Viewport (Main area not occupied by properties or timeline)
        ImGui.SetNextWindowPos(new Vector2(0, 0));
        ImGui.SetNextWindowSize(new Vector2(windowSize.X - 280, windowSize.Y - 200));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
        
        if (ImGui.Begin("3D Viewport", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse))
        {
            // Get the viewport area within the window
            var viewportPos = ImGui.GetCursorScreenPos();
            var viewportSize = ImGui.GetContentRegionAvail();
            
            // Store viewport info for later 3D rendering
            _viewportPosition = viewportPos;
            _viewportSize = viewportSize;
            
            // Add invisible button to capture the full viewport area (no background, 3D will render here)
            ImGui.InvisibleButton("##3DViewport", viewportSize);
            
            // Handle mouse interactions in viewport
            if (ImGui.IsItemHovered())
            {
                // Update gizmo hover state on mouse movement
                var mousePos = ImGui.GetMousePos();
                _viewport3D.UpdateGizmoHover(mousePos, _viewportPosition, _viewportSize);
                
                // Handle mouse interactions for selection and gizmo dragging
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_mouseCaptured)
                {
                    var clickResult = _viewport3D.GetObjectAtScreenPoint(mousePos, _viewportPosition, _viewportSize);
                    
                    if (clickResult == -2)
                    {
                        // Clicked on gizmo - don't change selection, gizmo interaction already started
                    }
                    else if (clickResult >= 0)
                    {
                        // Clicked on an object - select it
                        _selectedObjectIndex = clickResult;
                    }
                    else
                    {
                        // Clicked on empty space - deselect all
                        _selectedObjectIndex = -1;
                    }
                }
                
                // Handle mouse dragging for gizmo
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) && !_mouseCaptured)
                {
                    _viewport3D.HandleMouseDrag(mousePos, _viewportPosition, _viewportSize);
                }
                
                // End gizmo drag on mouse release
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !_mouseCaptured)
                {
                    _viewport3D.EndGizmoDrag();
                }
                
                // Right click for camera control
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    _mouseCaptured = true;
                    _lastMousePos = ImGui.GetMousePos();
                    // Hide cursor and prevent it from leaving window
                    foreach (var mouse in _inputContext.Mice)
                    {
                        mouse.Cursor.CursorMode = CursorMode.Disabled;
                    }
                }
            }
            
            if (_mouseCaptured)
            {
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
                {
                    _mouseCaptured = false;
                    // Restore cursor
                    foreach (var mouse in _inputContext.Mice)
                    {
                        mouse.Cursor.CursorMode = CursorMode.Normal;
                    }
                }
                else
                {
                    // Handle mouse delta for camera rotation
                    var currentMousePos = ImGui.GetMousePos();
                    var mouseDelta = currentMousePos - _lastMousePos;
                    
                    // Apply rotation (sensitivity can be adjusted)
                    float sensitivity = 0.1f;
                    _viewport3D.CameraYaw += mouseDelta.X * sensitivity;
                    _viewport3D.CameraPitch -= mouseDelta.Y * sensitivity;
                    
                    // Clamp pitch to prevent camera flipping
                    _viewport3D.CameraPitch = Math.Max(-89.0f, Math.Min(89.0f, _viewport3D.CameraPitch));
                    
                    _lastMousePos = currentMousePos;
                }
                
                // Handle WASD movement when mouse is captured
                float moveSpeed = 5.0f * (1.0f / 60.0f); // Assume ~60 FPS for now
                var currentPos = _viewport3D.CameraPosition;
                
                // Calculate movement vectors based on current camera orientation
                float yawRad = _viewport3D.CameraYaw * MathF.PI / 180.0f;
                float pitchRad = _viewport3D.CameraPitch * MathF.PI / 180.0f;
                
                Vector3 forward = new Vector3(
                    MathF.Cos(pitchRad) * MathF.Cos(yawRad),
                    MathF.Sin(pitchRad),
                    MathF.Cos(pitchRad) * MathF.Sin(yawRad)
                );
                Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
                Vector3 up = Vector3.UnitY;
                
                if (_wPressed) currentPos += forward * moveSpeed;
                if (_sPressed) currentPos -= forward * moveSpeed;
                if (_aPressed) currentPos -= right * moveSpeed;
                if (_dPressed) currentPos += right * moveSpeed;
                if (_ePressed) currentPos += up * moveSpeed;
                if (_qPressed) currentPos -= up * moveSpeed;
                
                _viewport3D.CameraPosition = currentPos;
            }
        }
        ImGui.End();
        ImGui.PopStyleColor();
    }







    private unsafe void RenderDrawData(ImDrawDataPtr drawData)
    {
        int fbWidth = (int)(drawData.DisplaySize.X * drawData.FramebufferScale.X);
        int fbHeight = (int)(drawData.DisplaySize.Y * drawData.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0) return;

        // Setup render state
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Enable(EnableCap.ScissorTest);

        // Setup viewport and projection matrix
        _gl.Viewport(0, 0, (uint)fbWidth, (uint)fbHeight);
        float L = drawData.DisplayPos.X;
        float R = drawData.DisplayPos.X + drawData.DisplaySize.X;
        float T = drawData.DisplayPos.Y;
        float B = drawData.DisplayPos.Y + drawData.DisplaySize.Y;

        Span<float> orthoProjection = stackalloc float[16] {
            2.0f / (R - L), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (T - B), 0.0f, 0.0f,
            0.0f, 0.0f, -1.0f, 0.0f,
            (R + L) / (L - R), (T + B) / (B - T), 0.0f, 1.0f
        };

        _gl.UseProgram(_shaderProgram);
        _gl.Uniform1(_attribLocationTex, 0);
        _gl.UniformMatrix4(_attribLocationProjMtx, 1, false, orthoProjection);

        _gl.BindVertexArray(_vertexArray);

        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            // Upload vertex/index buffers
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(cmdList.VtxBuffer.Size * sizeof(ImDrawVert)), (void*)cmdList.VtxBuffer.Data, BufferUsageARB.StreamDraw);

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _elementBuffer);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(cmdList.IdxBuffer.Size * sizeof(ushort)), (void*)cmdList.IdxBuffer.Data, BufferUsageARB.StreamDraw);

            // Setup vertex attributes
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxPos);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxUV);
            _gl.EnableVertexAttribArray((uint)_attribLocationVtxColor);

            _gl.VertexAttribPointer((uint)_attribLocationVtxPos, 2, VertexAttribPointerType.Float, false, (uint)sizeof(ImDrawVert), (void*)0);
            _gl.VertexAttribPointer((uint)_attribLocationVtxUV, 2, VertexAttribPointerType.Float, false, (uint)sizeof(ImDrawVert), (void*)8);
            _gl.VertexAttribPointer((uint)_attribLocationVtxColor, 4, VertexAttribPointerType.UnsignedByte, true, (uint)sizeof(ImDrawVert), (void*)16);

            for (int cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                var pcmd = cmdList.CmdBuffer[cmdI];
                if (pcmd.UserCallback != IntPtr.Zero)
                {
                    continue;
                }
                else
                {
                    Vector4 clipRect;
                    clipRect.X = pcmd.ClipRect.X - drawData.DisplayPos.X;
                    clipRect.Y = pcmd.ClipRect.Y - drawData.DisplayPos.Y;
                    clipRect.Z = pcmd.ClipRect.Z - drawData.DisplayPos.X;
                    clipRect.W = pcmd.ClipRect.W - drawData.DisplayPos.Y;

                    if (clipRect.X < fbWidth && clipRect.Y < fbHeight && clipRect.Z >= 0.0f && clipRect.W >= 0.0f)
                    {
                        _gl.Scissor((int)clipRect.X, (int)(fbHeight - clipRect.W), (uint)(clipRect.Z - clipRect.X), (uint)(clipRect.W - clipRect.Y));

                        _gl.BindTexture(TextureTarget.Texture2D, (uint)pcmd.TextureId);
                        _gl.DrawElementsBaseVertex(PrimitiveType.Triangles, pcmd.ElemCount, DrawElementsType.UnsignedShort, (void*)(pcmd.IdxOffset * sizeof(ushort)), (int)pcmd.VtxOffset);
                    }
                }
            }
        }

        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
    }

    public unsafe void Dispose()
    {
        if (_initialized)
        {
            _gl.DeleteVertexArray(_vertexArray);
            _gl.DeleteBuffer(_vertexBuffer);
            _gl.DeleteBuffer(_elementBuffer);
            _gl.DeleteProgram(_shaderProgram);
            _gl.DeleteTexture(_fontTexture);
            
            // Clean up 3D viewport resources
            _viewport3D?.Dispose();
            
            // Clean up texture atlas resources
            _textureAtlas?.Dispose();
            
            // Clean up timeline resources
            _timeline?.Dispose();
            
            // Clean up properties panel resources
            _propertiesPanel?.Dispose();
        }
        
        ImGui.DestroyContext();
        _inputContext?.Dispose();
        _gl?.Dispose();
    }

    private void SpawnCube()
    {
        // Create new cube object with unique name
        var cubeNumber = _sceneObjects.Count(obj => obj.Name.StartsWith("Cube")) + 1;
        var newCube = SceneObject.CreateCube($"Cube.{cubeNumber:D3}");
        
        // Add to scene objects
        _sceneObjects.Add(newCube);
        
        // Select the new object
        _selectedObjectIndex = _sceneObjects.Count - 1;
        _propertiesPanel.SelectedObjectIndex = _selectedObjectIndex;
        _viewport3D.SelectedObjectIndex = _selectedObjectIndex;
        _timeline.SelectedObjectIndex = _selectedObjectIndex;
        
        // Reset timeline frame for new object
        _timeline.CurrentFrame = 0;
    }
}
