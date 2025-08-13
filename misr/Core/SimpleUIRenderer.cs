using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImGuiNET;
using System.Numerics;
using StbImageSharp;
using System.Reflection;
using Misr.UI;
using Misr.Rendering;
using Misr.Core;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;

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
    
    // Framebuffer for 3D rendering
    private uint _viewportFramebuffer;
    private uint _viewportTexture;
    private uint _viewportDepthBuffer;
    
    // Texture atlas
    private TextureAtlas _textureAtlas = null!;
    
    // Bench button texture
    private uint _benchTexture = 0;
    
    // Timeline
    private Timeline _timeline = null!;
    
    // Properties panel
    private PropertiesPanel _propertiesPanel = null!;
    
    // Scene tree panel
    private SceneTreePanel _sceneTreePanel = null!;
    
    // Menu bar
    private MenuBar _menuBar = null!;
    
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
    
    // Dialog system
    private Dialog? _currentDialog = null;
    private Dialog? _progressDialog = null;
    private bool _viewportWasHovered = false;
    
    // Render to file system
    private bool _shouldRenderToFile = false;
    private int _renderWidth = 0;
    private int _renderHeight = 0;
    private string _renderFilePath = "";
    
    // Animation rendering system
    private bool _shouldRenderAnimation = false;
    private int _animationWidth = 0;
    private int _animationHeight = 0;
    private int _animationFramerate = 30;
    private int _animationBitrate = 10000;
    private string _animationFormat = "";
    private string _animationFilePath = "";
    private bool _cancelAnimation = false;


    public SimpleUIRenderer(IWindow window)
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
            _timeline = new Timeline(_gl);
            _propertiesPanel = new PropertiesPanel();
            _sceneTreePanel = new SceneTreePanel();
            _menuBar = new MenuBar(_window);
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
            CreateViewportFramebuffer();
            _textureAtlas.CreateTerrainAtlases();
            LoadBenchTexture();
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
                if (key == Key.F12) 
                {
                    var currentIo = ImGui.GetIO();
                    if (currentIo.KeyCtrl)
                        ShowRenderAnimationDialog();
                    else
                        ShowRenderSettingsDialog();
                }
                if (key == Key.Delete) 
                {
                    if (_timeline.HasSelectedKeyframe() && _timeline.IsHovered)
                    {
                        _timeline.DeleteSelectedKeyframe();
                    }
                }
                if (key == Key.X)
                {
                    // Show delete confirmation if viewport was hovered and object is selected
                    if (_viewportWasHovered && _selectedObjectIndex >= 0 && _selectedObjectIndex < _sceneObjects.Count)
                    {
                        var selectedObject = _sceneObjects[_selectedObjectIndex];
                        var io = ImGui.GetIO();
                        var dialogPosition = new Vector2(io.MousePos.X, io.MousePos.Y);
                        
                        _currentDialog = new Dialog(
                            DialogType.DeleteConfirmation,
                            "DELETE OBJECT",
                            $"Are you sure you want to delete '{selectedObject.Name}'?",
                            dialogPosition,
                            () => DeleteSelectedObject(),
                            () => { /* No action needed */ }
                        );
                    }
                }
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
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixels.ToPointer());

        // Store the texture identifier
        io.Fonts.SetTexID((IntPtr)_fontTexture);
        io.Fonts.ClearTexData();
    }

    private unsafe void CreateViewportFramebuffer()
    {
        // Create framebuffer for 3D viewport rendering
        _viewportFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _viewportFramebuffer);
        
        // Create color texture
        _viewportTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _viewportTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb, 1024, 768, 0, Silk.NET.OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, (void*)0);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _viewportTexture, 0);
        
        // Create depth buffer
        _viewportDepthBuffer = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _viewportDepthBuffer);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent, 1024, 768);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _viewportDepthBuffer);
        
        // Check framebuffer completeness
        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
        {
            Console.WriteLine("Viewport framebuffer not complete!");
        }
        
        // Unbind framebuffer
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private unsafe void Render3DToTexture()
    {
        if (_viewportSize.X <= 0 || _viewportSize.Y <= 0) return;
        
        // Bind viewport framebuffer
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _viewportFramebuffer);
        
        // Update framebuffer size if needed
        var newWidth = (uint)_viewportSize.X;
        var newHeight = (uint)_viewportSize.Y;
        
        // Resize texture if needed
        _gl.BindTexture(TextureTarget.Texture2D, _viewportTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb, newWidth, newHeight, 0, Silk.NET.OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, (void*)0);
        
        // Resize depth buffer if needed
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _viewportDepthBuffer);
        _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent, newWidth, newHeight);
        
        // Set viewport to full framebuffer size
        _gl.Viewport(0, 0, newWidth, newHeight);
        
        // Render 3D scene to framebuffer
        _viewport3D.Render(Vector2.Zero, _viewportSize, (int)newHeight);
        
        // Restore default framebuffer
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        
        // Restore main window viewport
        _gl.Viewport(0, 0, (uint)_window.Size.X, (uint)_window.Size.Y);
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
        
        // Update all objects' transforms from keyframes when playing, scrubbing, or dragging keyframes
        if (_timeline.IsPlaying || _timeline.IsScrubbing || _timeline.IsDraggingKeyframes)
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
        
        // Update selected object's properties panel display when playing, scrubbing, or dragging keyframes
        if (_timeline.HasKeyframes() && (_timeline.IsPlaying || _timeline.IsScrubbing || _timeline.IsDraggingKeyframes))
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
        _window.Title = "Mine Imator Simply Remade";

        // Clear background
        _gl.ClearColor(0.15f, 0.15f, 0.20f, 1.0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Draw ImGui UI
        DrawImGuiUI();

        // Render main ImGui
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
        
        // Render 3D scene to framebuffer texture
        Render3DToTexture();
        
        // Check if we should render to file
        if (_shouldRenderToFile)
        {
            _shouldRenderToFile = false;
            RenderSceneToFile(_renderWidth, _renderHeight, _renderFilePath);
        }
        
        // Check if we should render animation
        if (_shouldRenderAnimation)
        {
            _shouldRenderAnimation = false;
            RenderAnimationToFile(_animationWidth, _animationHeight, _animationFramerate, _animationBitrate, _animationFormat, _animationFilePath);
        }
        
        // Check if application should exit after all rendering is complete
        if (_menuBar.ShouldExit)
        {
            _window.Close();
        }
    }

    private void DrawImGuiUI()
    {
        var windowSize = _window.Size;
        
        // Reset viewport hover state at start of frame
        _viewportWasHovered = false;
        
        // Render scene tree panel (takes up 1/3 of vertical space on the right)
        _sceneTreePanel.Render(new Vector2(windowSize.X, windowSize.Y));
        
        // Render properties panel (takes up 2/3 of vertical space on the right, below scene tree)
        _propertiesPanel.Render(new Vector2(windowSize.X, windowSize.Y));

        // Render timeline
        _timeline.Render(new Vector2(windowSize.X, windowSize.Y));

        // 3D Viewport (Main area not occupied by properties or timeline)
        var menuBarHeight = ImGui.GetFrameHeight();
        ImGui.SetNextWindowPos(new Vector2(0, menuBarHeight));
        ImGui.SetNextWindowSize(new Vector2(windowSize.X - 280, windowSize.Y - 200 - menuBarHeight));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.1f, 0.1f, 0.1f, 1.0f));
        
        if (ImGui.Begin("3D Viewport", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus))
        {
            // Get the viewport area within the window
            var viewportPos = ImGui.GetCursorScreenPos();
            var viewportSize = ImGui.GetContentRegionAvail();
            
            // Store viewport info for later 3D rendering
            _viewportPosition = viewportPos;
            _viewportSize = viewportSize;
            
            // Display 3D scene texture in the viewport (flip Y to correct orientation)
            ImGui.Image((IntPtr)_viewportTexture, viewportSize, new Vector2(0, 1), new Vector2(1, 0));
            
            // Handle mouse interactions in viewport
            if (ImGui.IsItemHovered())
            {
                // Track that viewport was hovered for X key deletion
                _viewportWasHovered = true;
                
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
            
            // Add bench button overlay in top-left corner
            if (_benchTexture != 0)
            {
                // Save current cursor position
                var currentCursor = ImGui.GetCursorScreenPos();
                
                // Position button in top-left corner of viewport (with some padding)
                var buttonPos = new Vector2(_viewportPosition.X + 10, _viewportPosition.Y + 10);
                var buttonSize = new Vector2(64, 64); // Made larger
                
                // Set cursor to button position
                ImGui.SetCursorScreenPos(buttonPos);
                ImGui.Dummy(Vector2.Zero); // Validate cursor position
                
                // Check if mouse is hovering over button
                var mousePos = ImGui.GetMousePos();
                bool isHovered = mousePos.X >= buttonPos.X && mousePos.X <= buttonPos.X + buttonSize.X &&
                                mousePos.Y >= buttonPos.Y && mousePos.Y <= buttonPos.Y + buttonSize.Y;
                
                // Add invisible button to check click state without affecting visual positioning
                if (ImGui.InvisibleButton("##benchButtonHitbox", buttonSize))
                {
                    Console.WriteLine("Bench button clicked!");
                }
                
                // Manually render the texture at the button position with flipped UV coordinates
                var drawList = ImGui.GetWindowDrawList();
                var tintColor = isHovered ? 0xFFFFFFFF : 0x4DFFFFFF; // Full opacity when hovered, 30% opacity otherwise
                drawList.AddImage((IntPtr)_benchTexture, buttonPos, 
                    new Vector2(buttonPos.X + buttonSize.X, buttonPos.Y + buttonSize.Y),
                    new Vector2(0, 1), new Vector2(1, 0), tintColor); // Flip Y coordinates and apply tint
                
                // Restore cursor position
                ImGui.SetCursorScreenPos(currentCursor);
                ImGui.Dummy(Vector2.Zero); // Validate cursor position
            }
        }
        ImGui.End();
        ImGui.PopStyleColor();
        
        // Render menu bar normally in main UI pass (3D is now a texture)
        _menuBar.Render();
        
        // Check if render settings dialog should be shown
        if (_menuBar.ShouldShowRenderSettings)
        {
            RenderDialogInputBlocker(new Vector2(windowSize.X, windowSize.Y));
            ShowRenderSettingsDialog();
        }
        
        // Check if render animation dialog should be shown
        if (_menuBar.ShouldShowRenderAnimation)
        {
            ShowRenderAnimationDialog();
        }
        
        // Render dialog overlay and current dialog in main UI pass
        if (_currentDialog != null)
        {
            // Render overlay first
            RenderDialogOverlay(new Vector2(windowSize.X, windowSize.Y));
            
            // Finally render the dialog on top
            _currentDialog.Render(new Vector2(windowSize.X, windowSize.Y));
            
            // Check for outside clicks and close dialog if needed
            if (_currentDialog.CheckOutsideClick())
            {
                _currentDialog = null;
            }
            // Remove dialog if it's no longer visible (only check if dialog still exists)
            else if (!_currentDialog.IsVisible)
            {
                _currentDialog = null;
            }
        }
        
        // Render progress dialog if it exists
        if (_progressDialog != null)
        {
            // Render overlay first
            RenderDialogOverlay(new Vector2(windowSize.X, windowSize.Y));
            
            // Then render input blocker
            RenderDialogInputBlocker(new Vector2(windowSize.X, windowSize.Y));
            
            // Finally render the progress dialog on top
            _progressDialog.Render(new Vector2(windowSize.X, windowSize.Y));
            
            // Remove progress dialog if it's no longer visible
            if (!_progressDialog.IsVisible)
            {
                _progressDialog = null;
            }
        }
    }

    private void DeleteSelectedObject()
    {
        if (_selectedObjectIndex >= 0 && _selectedObjectIndex < _sceneObjects.Count)
        {
            // Remove the object from the scene
            _sceneObjects.RemoveAt(_selectedObjectIndex);
            
            // Adjust selection index
            if (_selectedObjectIndex >= _sceneObjects.Count)
            {
                _selectedObjectIndex = _sceneObjects.Count - 1; // Select last object if current was last
            }
            
            // If no objects left, deselect
            if (_sceneObjects.Count == 0)
            {
                _selectedObjectIndex = -1;
            }
            
            // Update all UI panels with new selection
            _propertiesPanel.SelectedObjectIndex = _selectedObjectIndex;
            _viewport3D.SelectedObjectIndex = _selectedObjectIndex;
            _timeline.SelectedObjectIndex = _selectedObjectIndex;
            _sceneTreePanel.SelectedObjectIndex = _selectedObjectIndex;
            
            // Update references to scene objects
            _propertiesPanel.SceneObjects = _sceneObjects;
            _viewport3D.SceneObjects = _sceneObjects;
            _timeline.SceneObjects = _sceneObjects;
            _sceneTreePanel.SceneObjects = _sceneObjects;
            
            // Recalculate timeline frames after object deletion
            _timeline.CalculateTotalFrames();
        }
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
            
            // Clean up bench texture
            if (_benchTexture != 0) _gl.DeleteTexture(_benchTexture);
            
            // Clean up viewport framebuffer resources
            _gl.DeleteFramebuffer(_viewportFramebuffer);
            _gl.DeleteTexture(_viewportTexture);
            _gl.DeleteRenderbuffer(_viewportDepthBuffer);
            
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

    private void ShowRenderSettingsDialog()
    {
        if (_currentDialog == null)
        {
            var io = ImGui.GetIO();
            var centerPos = new Vector2(io.DisplaySize.X * 0.5f - 150, io.DisplaySize.Y * 0.5f - 100);
            
            _currentDialog = new Dialog(
                DialogType.RenderSettings,
                "RENDER SETTINGS",
                "",
                centerPos,
                () => { Console.WriteLine("Render frame started with selected settings"); },
                () => { /* Cancel - no action needed */ },
                (width, height, filePath) => { 
                    Console.WriteLine($"[DEBUG] Setting render flags: {width}x{height} -> {filePath}");
                    _shouldRenderToFile = true;
                    _renderWidth = width;
                    _renderHeight = height;
                    _renderFilePath = filePath;
                }
            );
        }
    }

    private void ShowRenderAnimationDialog()
    {
        if (_currentDialog == null)
        {
            var io = ImGui.GetIO();
            var centerPos = new Vector2(io.DisplaySize.X * 0.5f - 200, io.DisplaySize.Y * 0.5f - 150);
            
            _currentDialog = new Dialog(
                DialogType.RenderAnimation,
                "RENDER ANIMATION",
                "",
                centerPos,
                () => { Console.WriteLine("Render animation started with selected settings"); },
                () => { /* Cancel - no action needed */ },
                null, // No single frame render callback for animation dialog
                (width, height, framerate, bitrate, format, filePath) => { 
                    Console.WriteLine($"[DEBUG] Setting animation render flags: {width}x{height}, {framerate}fps, {bitrate}kbps, {format} -> {filePath}");
                    _shouldRenderAnimation = true;
                    _animationWidth = width;
                    _animationHeight = height;
                    _animationFramerate = framerate;
                    _animationBitrate = bitrate;
                    _animationFormat = format;
                    _animationFilePath = filePath;
                }
            );
        }
    }

    private void RenderDialogOverlay(Vector2 windowSize)
    {
        // Create a fullscreen overlay - just provides the dark visual background
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(windowSize);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 0.5f)); // Semi-transparent black overlay
        
        if (ImGui.Begin("##DialogOverlay", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | 
                       ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | 
                       ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs))
        {
            // Overlay just provides visual background - no input blocking
        }
        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void RenderDialogInputBlocker(Vector2 windowSize)
    {
        // Create a fullscreen invisible input blocker
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(windowSize);
        if (ImGui.Begin("##DialogInputBlocker", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | 
                       ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | 
                       ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground))
        {
            // Invisible button that captures all input to block UI behind the dialog
            ImGui.InvisibleButton("##InputBlocker", windowSize);
        }
        ImGui.End();
    }

    private unsafe void RenderSceneToFile(int width, int height, string filePath)
    {
        try
        {
            // Create temporary framebuffer for high-res rendering
            uint renderFramebuffer = _gl.GenFramebuffer();
            uint renderTexture = _gl.GenTexture();
            uint renderDepthBuffer = _gl.GenRenderbuffer();
            
            if (renderFramebuffer == 0 || renderTexture == 0 || renderDepthBuffer == 0)
            {
                return;
            }
            
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, renderFramebuffer);
            
            // Create color texture
            _gl.BindTexture(TextureTarget.Texture2D, renderTexture);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgb, (uint)width, (uint)height, 0, Silk.NET.OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, (void*)0);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, renderTexture, 0);
            
            // Create depth buffer
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderDepthBuffer);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent, (uint)width, (uint)height);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, renderDepthBuffer);
            
            // Check framebuffer completeness
            var fbStatus = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (fbStatus != GLEnum.FramebufferComplete)
            {
                return;
            }
            
            // Set viewport and render the scene
            _gl.Viewport(0, 0, (uint)width, (uint)height);
            _viewport3D.Render(Vector2.Zero, new Vector2(width, height), height, false); // Disable UI elements for file render
            
            // Read pixels from framebuffer
            byte[] pixels = new byte[width * height * 3]; // RGB
            fixed (byte* pixelPtr = pixels)
            {
                _gl.ReadPixels(0, 0, (uint)width, (uint)height, Silk.NET.OpenGL.PixelFormat.Rgb, PixelType.UnsignedByte, pixelPtr);
            }
            
            // Save to file
            SaveImageToFile(pixels, width, height, filePath);
            
            // Clean up temporary framebuffer
            _gl.DeleteFramebuffer(renderFramebuffer);
            _gl.DeleteTexture(renderTexture);
            _gl.DeleteRenderbuffer(renderDepthBuffer);
            
            // Restore original viewport
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)_window.Size.X, (uint)_window.Size.Y);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during render: {ex.Message}");
        }
    }

    private void SaveImageToFile(byte[] pixels, int width, int height, string filePath)
    {
        try
        {
            // Flip the image vertically (OpenGL reads bottom-to-top, but images are top-to-bottom)
            byte[] flippedPixels = new byte[pixels.Length];
            int rowSize = width * 3;
            for (int y = 0; y < height; y++)
            {
                int srcRow = (height - 1 - y) * rowSize;
                int dstRow = y * rowSize;
                Array.Copy(pixels, srcRow, flippedPixels, dstRow, rowSize);
            }
            
            // Determine file format from extension
            string extension = Path.GetExtension(filePath).ToLower();
            
            using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, width, height), 
                ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            
            unsafe
            {
                byte* bmpPtr = (byte*)bitmapData.Scan0;
                for (int i = 0; i < flippedPixels.Length; i += 3)
                {
                    // Convert RGB to BGR for bitmap
                    bmpPtr[i] = flippedPixels[i + 2];     // B
                    bmpPtr[i + 1] = flippedPixels[i + 1]; // G
                    bmpPtr[i + 2] = flippedPixels[i];     // R
                }
            }
            
            bitmap.UnlockBits(bitmapData);
            
            // Save in appropriate format
            switch (extension)
            {
                case ".png":
                    bitmap.Save(filePath, ImageFormat.Png);
                    break;
                case ".jpg":
                case ".jpeg":
                    bitmap.Save(filePath, ImageFormat.Jpeg);
                    break;
                case ".bmp":
                    bitmap.Save(filePath, ImageFormat.Bmp);
                    break;
                default:
                    bitmap.Save(filePath, ImageFormat.Png); // Default to PNG
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving image: {ex.Message}");
        }
    }

    private async void RenderAnimationToFile(int width, int height, int framerate, int bitrate, string format, string filePath)
    {
        try
        {
            bool isPngSequence = format == "PNG Sequence";
            
            if (isPngSequence)
            {
                // For PNG sequence, just save the current frame with the selected name
                RenderSceneToFile(width, height, filePath);
            }
            else
            {
                // Show progress dialog for video rendering
                ShowProgressDialog();
                
                // For video formats, render all frames and use FFmpeg
                await RenderVideoWithFFmpeg(width, height, framerate, bitrate, format, filePath);
                
                // Hide progress dialog when complete
                HideProgressDialog();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during animation render: {ex.Message}");
            HideProgressDialog();
        }
    }

    private async Task RenderVideoWithFFmpeg(int width, int height, int framerate, int bitrate, string format, string filePath)
    {
        try
        {
            // Create temporary directory for frames
            string tempDir = Path.Combine(Path.GetTempPath(), $"misr_render_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(tempDir);
            
            // Get last keyframe position across all objects
            int lastKeyframe = GetLastKeyframePosition();
            if (lastKeyframe <= 0)
            {
                lastKeyframe = 0; // Render at least frame 0
            }
            
            int framesToRender = lastKeyframe + 1; // Include frame 0
            
            // Store current timeline state
            int originalFrame = _timeline.CurrentFrame;
            bool wasPlaying = _timeline.IsPlaying;
            if (wasPlaying) _timeline.IsPlaying = false;
            
            // Render each frame
            for (int frame = 0; frame < framesToRender; frame++)
            {
                // Check for cancellation
                if (_cancelAnimation)
                {
                    Console.WriteLine("Animation rendering cancelled by user");
                    return;
                }
                
                // Update progress
                _progressDialog?.UpdateFrameProgress(frame + 1, framesToRender);
                
                // Set timeline to this frame
                _timeline.CurrentFrame = frame;
                
                // Update object transforms for this frame
                foreach (var obj in _sceneObjects)
                {
                    if (_timeline.HasKeyframes(obj))
                    {
                        obj.Position = _timeline.GetAnimatedPosition(obj);
                        obj.Rotation = _timeline.GetAnimatedRotation(obj);
                        obj.Scale = _timeline.GetAnimatedScale(obj);
                    }
                }
                
                // Render frame to temporary file
                string frameFile = Path.Combine(tempDir, $"frame_{frame:D6}.png");
                RenderSceneToFile(width, height, frameFile);
            }
            
            // Restore timeline state
            _timeline.CurrentFrame = originalFrame;
            if (wasPlaying) _timeline.IsPlaying = true;
            
            // Verify frames were created before encoding
            var frameFiles = Directory.GetFiles(tempDir, "frame_*.png");
            if (frameFiles.Length == 0)
            {
                return;
            }
            
            // Update progress to encoding phase
            _progressDialog?.UpdateEncodingProgress(0.0f);
            
            // Use FFmpeg to encode video
            await EncodeVideoWithFFmpeg(tempDir, width, height, framerate, bitrate, format, filePath, framesToRender);
            
            // Clean up temporary directory
            Directory.Delete(tempDir, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in video render: {ex.Message}");
        }
    }

    private async Task EncodeVideoWithFFmpeg(string frameDir, int width, int height, int framerate, int bitrate, string format, string outputPath, int totalFrames)
    {
        try
        {
            // Get FFmpeg executable path from new location
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "lib", "ffmpeg", "ffmpeg.exe");
            
            if (!File.Exists(ffmpegPath))
            {
                Console.WriteLine($"ERROR: FFmpeg not found at {ffmpegPath}");
                return;
            }
            
            // Test FFmpeg first
            await TestFFmpegVersion(ffmpegPath);
            
            // Build FFmpeg command based on format
            string inputPattern = Path.Combine(frameDir, "frame_%06d.png").Replace("\\", "/"); // FFmpeg prefers forward slashes
            string codec, extension;
            
            switch (format.ToUpper())
            {
                case "MP4":
                    codec = "libx264";
                    extension = ".mp4";
                    break;
                case "MOV":
                    codec = "libx264";
                    extension = ".mov";
                    break;
                case "WMV":
                    codec = "wmv2";
                    extension = ".wmv";
                    break;
                default:
                    codec = "libx264";
                    extension = ".mp4";
                    break;
            }
            
            // Ensure output file has correct extension
            if (!outputPath.ToLower().EndsWith(extension))
            {
                outputPath = Path.ChangeExtension(outputPath, extension);
            }
            
            string ffmpegArgs = $"-y -framerate {framerate} -i \"{inputPattern}\" -c:v {codec} -b:v {bitrate}k -pix_fmt yuv420p \"{outputPath}\"";
            
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                WorkingDirectory = Path.GetDirectoryName(ffmpegPath), // Set working directory to FFmpeg folder with DLLs
                Arguments = ffmpegArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                // Parse FFmpeg progress from stderr in real-time
                var stderrTask = Task.Run(async () =>
                {
                    using var reader = process.StandardError;
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        ParseFFmpegProgress(line, totalFrames);
                    }
                });
                
                var output = await process.StandardOutput.ReadToEndAsync();
                await stderrTask;
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0)
                {
                    _progressDialog?.UpdateEncodingProgress(100.0f);
                }
                else
                {
                    Console.WriteLine($"Video encoding failed with exit code: {process.ExitCode}");
                    
                    // Try fallback command for compatibility
                    await TryFallbackFFmpegCommand(frameDir, framerate, outputPath);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error encoding video: {ex.Message}");
        }
    }

    private int GetLastKeyframePosition()
    {
        int maxKeyframe = 0;
        
        foreach (var obj in _sceneObjects)
        {
            foreach (var frame in obj.PosXKeyframes.Keys) maxKeyframe = Math.Max(maxKeyframe, frame);
            foreach (var frame in obj.PosYKeyframes.Keys) maxKeyframe = Math.Max(maxKeyframe, frame);
            foreach (var frame in obj.PosZKeyframes.Keys) maxKeyframe = Math.Max(maxKeyframe, frame);
            foreach (var frame in obj.RotXKeyframes.Keys) maxKeyframe = Math.Max(maxKeyframe, frame);
            foreach (var frame in obj.RotYKeyframes.Keys) maxKeyframe = Math.Max(maxKeyframe, frame);
            foreach (var frame in obj.RotZKeyframes.Keys) maxKeyframe = Math.Max(maxKeyframe, frame);
            foreach (var frame in obj.ScaleXKeyframes.Keys) maxKeyframe = Math.Max(maxKeyframe, frame);
            foreach (var frame in obj.ScaleYKeyframes.Keys) maxKeyframe = Math.Max(maxKeyframe, frame);
            foreach (var frame in obj.ScaleZKeyframes.Keys) maxKeyframe = Math.Max(maxKeyframe, frame);
        }
        
        return maxKeyframe;
    }

    private unsafe void LoadBenchTexture()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("misr.assets.sprite.bench.png");
            
            if (stream == null)
            {
                Console.WriteLine("Failed to load bench.png - resource not found");
                return;
            }
            
            // Load the PNG data with StbImageSharp
            var imageResult = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            
            // Create OpenGL texture
            _benchTexture = _gl.GenTexture();
            _gl.BindTexture(TextureTarget.Texture2D, _benchTexture);
            
            fixed (byte* dataPtr = imageResult.Data)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)imageResult.Width, (uint)imageResult.Height, 0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, dataPtr);
            }
            
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            
            _gl.BindTexture(TextureTarget.Texture2D, 0);
            
            Console.WriteLine("Bench texture loaded successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load bench texture: {ex.Message}");
        }
    }

    private async Task TryFallbackFFmpegCommand(string frameDir, int framerate, string outputPath)
    {
        try
        {
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "lib", "ffmpeg", "ffmpeg.exe");
            string inputPattern = Path.Combine(frameDir, "frame_%06d.png").Replace("\\", "/");
            
            // Simpler FFmpeg command without bitrate and specific codec settings
            string fallbackArgs = $"-y -framerate {framerate} -i \"{inputPattern}\" -vcodec libx264 -preset medium \"{outputPath}\"";
            
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                WorkingDirectory = Path.GetDirectoryName(ffmpegPath), // Set working directory to FFmpeg folder
                Arguments = fallbackArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"Fallback video encoding also failed with exit code: {process.ExitCode}");
                    if (!string.IsNullOrEmpty(error))
                        Console.WriteLine($"Fallback FFmpeg error: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in fallback FFmpeg: {ex.Message}");
        }
    }

    private async Task TestFFmpegVersion(string ffmpegPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                WorkingDirectory = Path.GetDirectoryName(ffmpegPath), // Set working directory to FFmpeg folder
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"FFmpeg version test failed: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error testing FFmpeg: {ex.Message}");
        }
    }

    private void ShowProgressDialog()
    {
        if (_progressDialog == null)
        {
            var io = ImGui.GetIO();
            var centerPos = new Vector2(io.DisplaySize.X * 0.5f - 200, io.DisplaySize.Y * 0.5f - 100);
            
            _progressDialog = new Dialog(
                DialogType.RenderProgress,
                "RENDER PROGRESS",
                "",
                centerPos,
                () => { /* Complete - no action needed */ },
                () => { _cancelAnimation = true; } // Cancel callback
            );
        }
    }

    private void HideProgressDialog()
    {
        _progressDialog = null;
        _cancelAnimation = false;
    }

    private void ParseFFmpegProgress(string line, int totalFrames)
    {
        try
        {
            // FFmpeg outputs progress like "frame= 123 fps=30 q=28.0 size= 1024kB time=00:00:04.10 bitrate=2048.0kbits/s speed=1.0x"
            if (line.Contains("frame="))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (part.StartsWith("frame="))
                    {
                        var frameStr = part.Substring(6);
                        if (int.TryParse(frameStr, out int currentFrame))
                        {
                            float progress = totalFrames > 0 ? (float)currentFrame / totalFrames * 100.0f : 0.0f;
                            _progressDialog?.UpdateEncodingProgress(Math.Min(progress, 100.0f));
                        }
                        break;
                    }
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }
    }
}
