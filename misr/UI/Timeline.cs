using ImGuiNET;
using System.Numerics;
using System.Reflection;
using Misr.Core;
using Silk.NET.OpenGL;
using StbImageSharp;
using Svg;
using System.Drawing;
using System.Drawing.Imaging;

namespace Misr.UI;

public class Timeline : IDisposable
{
    // Timeline state
    public float TimelineZoom { get; set; } = 1.0f; // 1.0 = normal zoom (95 frames), 2.0 = 2x zoom, etc.
    public int TimelineStart { get; set; } = 0; // Starting frame of visible timeline
    public int TotalFrames { get; set; } = 2000; // Total frames in the animation - calculated dynamically
    public int CurrentFrame { get; set; } = 0;
    public float CurrentFrameFloat { get; private set; } = 0.0f;
    public bool IsPlaying { get; set; } = false;
    public bool IsScrubbing { get; private set; } = false;
    public bool IsHovered { get; private set; } = false;
    public bool IsDraggingKeyframes => _isDraggingKeyframe;
    
    // Animation framerate (frames per second)
    public float FrameRate { get; set; } = 30.0f;
    
    // Store position when animation starts playing
    private int _playStartFrame = 0;
    private float _playStartFrameFloat = 0.0f;
    
    // Store scrubber position for playhead synchronization and 3D viewport
    private Vector2 _scrubberPosition = Vector2.Zero;
    private float _scrubberWidth = 0.0f;
    
    // Icon textures for buttons
    private GL? _gl;
    private uint _playIconTexture = 0;
    private uint _pauseIconTexture = 0;
    private uint _stopIconTexture = 0;
    private uint _resetIconTexture = 0;
    
    // Reference to scene objects and selected object
    public List<SceneObject>? SceneObjects { get; set; }
    public int SelectedObjectIndex { get; set; } = -1;
    
    // Keyframe selection state
    public class SelectedKeyframe
    {
        public string Property { get; set; } = "";
        public int Frame { get; set; } = -1;
        public int ObjectIndex { get; set; } = -1;
        
        public override bool Equals(object? obj)
        {
            return obj is SelectedKeyframe other &&
                   Property == other.Property &&
                   Frame == other.Frame &&
                   ObjectIndex == other.ObjectIndex;
        }
        
        public override int GetHashCode()
        {
            return HashCode.Combine(Property, Frame, ObjectIndex);
        }
    }
    
    // Multi-selection support
    public HashSet<SelectedKeyframe> SelectedKeyframes { get; private set; } = new HashSet<SelectedKeyframe>();
    
    // Legacy single selection for backward compatibility
    public SelectedKeyframe? SelectedKeyframeInfo => SelectedKeyframes.FirstOrDefault();
    
    // Keyframe dragging state
    private bool _isDraggingKeyframe = false;
    private SelectedKeyframe? _draggingKeyframeInfo = null;
    private Vector2 _dragStartMousePos = Vector2.Zero;
    private int _dragStartFrame = -1;
    
    // Selection box dragging state
    private bool _isDragSelecting = false;
    private Vector2 _dragSelectionStart = Vector2.Zero;
    private Vector2 _dragSelectionEnd = Vector2.Zero;
    
    // Current selected object helper
    public SceneObject? CurrentObject => 
        SceneObjects != null && SelectedObjectIndex >= 0 && SelectedObjectIndex < SceneObjects.Count 
            ? SceneObjects[SelectedObjectIndex] 
            : null;
    
    // Track previous object index to detect changes
    private int _previousSelectedObjectIndex = -1;
    
    public Timeline(GL gl)
    {
        _gl = gl;
        CalculateTotalFrames();
        LoadIconTextures();
    }
    
    public void Update(float deltaTime)
    {
        // Clear keyframe selection if object changed
        if (_previousSelectedObjectIndex != SelectedObjectIndex)
        {
            SelectedKeyframes.Clear();
            _isDraggingKeyframe = false;
            _draggingKeyframeInfo = null;
            _previousSelectedObjectIndex = SelectedObjectIndex;
        }
        
        // Stop dragging if mouse button is released
        if (_isDraggingKeyframe && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _isDraggingKeyframe = false;
            _draggingKeyframeInfo = null;
        }
        
        // Stop drag selection if mouse button is released
        if (_isDragSelecting && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _isDragSelecting = false;
        }
        
        // Update animation at specified frame rate
        if (IsPlaying)
        {
            // Smoothly advance fractional frame
            CurrentFrameFloat += FrameRate * deltaTime;
            
            // Wrap around if we exceed total frames
            if (CurrentFrameFloat > TotalFrames) 
                CurrentFrameFloat = 0.0f;
            
            // Update integer frame for UI display
            CurrentFrame = (int)CurrentFrameFloat;
        }
    }
    
    public void Render(Vector2 windowSize)
    {
        // Timeline Panel (Green - Bottom, 200px tall)
        ImGui.SetNextWindowPos(new Vector2(0, windowSize.Y - 200));
        ImGui.SetNextWindowSize(new Vector2(windowSize.X - 280, 200));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.2f, 0.8f, 0.4f, 1.0f));
        
        if (ImGui.Begin("Timeline", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysVerticalScrollbar))
        {
            IsHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
            HandleTimelineZoom();
            
            // Calculate visible frame range
            int visibleFrames = (int)(95 / TimelineZoom);
            int timelineEnd = Math.Min(TimelineStart + visibleFrames, TotalFrames);
            
            RenderTimelineControls(visibleFrames, timelineEnd);
            RenderTimelineContent(windowSize, visibleFrames);
        }
        else
        {
            IsHovered = false;
        }
        ImGui.End();
        ImGui.PopStyleColor();
    }
    
    private void HandleTimelineZoom()
    {
        // Handle timeline zoom with Shift + scroll wheel
        var io = ImGui.GetIO();
        if (ImGui.IsWindowHovered() && io.KeyShift && io.MouseWheel != 0)
        {
            float zoomDelta = io.MouseWheel * 0.1f;
            TimelineZoom = Math.Max(0.1f, Math.Min(10.0f, TimelineZoom + zoomDelta));
            
            // Recalculate timeline bounds
            int tempVisibleFrames = (int)(95 / TimelineZoom);
            if (TimelineStart + tempVisibleFrames > TotalFrames)
                TimelineStart = Math.Max(0, TotalFrames - tempVisibleFrames);
            
            // Consume the mouse wheel to prevent vertical scrolling
            io.MouseWheel = 0;
        }
    }
    
    private void RenderTimelineControls(int visibleFrames, int timelineEnd)
    {
        // Top row: Zoom controls and playback controls
        ImGui.Text($"Visible: {TimelineStart} - {timelineEnd} ({visibleFrames} frames)");
        ImGui.SameLine();
        if (ImGui.Button("Fit All"))
        {
            TimelineZoom = 95.0f / TotalFrames;
            TimelineStart = 0;
        }
        
        // Zoom slider
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        float zoom = TimelineZoom;
        if (ImGui.SliderFloat("Zoom", ref zoom, 0.1f, 10.0f, "%.1fx"))
        {
            // Clamp zoom
            TimelineZoom = Math.Max(0.1f, Math.Min(10.0f, zoom));
            // Recalculate visible frames after zoom change
            visibleFrames = (int)(95 / TimelineZoom);
            if (TimelineStart + visibleFrames > TotalFrames)
                TimelineStart = Math.Max(0, TotalFrames - visibleFrames);
        }
        
        // Scroll controls
        ImGui.SameLine();
        if (ImGui.Button("◀"))
        {
            TimelineStart = Math.Max(0, TimelineStart - (int)(visibleFrames * 0.1f));
        }
        ImGui.SameLine();
        if (ImGui.Button("▶"))
        {
            TimelineStart = Math.Min(TotalFrames - visibleFrames, TimelineStart + (int)(visibleFrames * 0.1f));
        }
        
        // Playback controls on the same row
        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();
        if (ImGui.ImageButton("resetButton", (nint)_resetIconTexture, new Vector2(16, 16)))
        {
            CurrentFrame = 0;
            CurrentFrameFloat = 0.0f;
        }
        
        ImGui.SameLine();
        var iconTexture = IsPlaying ? _pauseIconTexture : _playIconTexture;
        if (ImGui.ImageButton("playPauseButton", (nint)iconTexture, new Vector2(16, 16)))
        {
            if (!IsPlaying)
            {
                // Store current position when starting to play
                _playStartFrame = CurrentFrame;
                _playStartFrameFloat = CurrentFrameFloat;
            }
            IsPlaying = !IsPlaying;
        }
        
        ImGui.SameLine();
        if (ImGui.ImageButton("stopButton", (nint)_stopIconTexture, new Vector2(16, 16)))
        {
            IsPlaying = false;
            CurrentFrame = _playStartFrame;
            CurrentFrameFloat = _playStartFrameFloat;
        }
        
        ImGui.Spacing();
    }
    
    private void RenderTimelineContent(Vector2 windowSize, int visibleFrames)
    {
        // Create horizontal layout: track labels on left, timeline tracks on right
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var labelWidth = 150.0f; // Fixed width for label column
        var trackWidth = availableWidth - labelWidth;
        
        // Left vertical box - Track names
        ImGui.BeginGroup();
        RenderTrackNamesBox(labelWidth, availableHeight);
        ImGui.EndGroup();
        
        // Right vertical box - Timeline tracks with scrubber on top
        ImGui.SameLine(0, 0); // No spacing between the boxes
        ImGui.BeginGroup();
        RenderTimelineTracksBox(trackWidth, availableHeight, visibleFrames);
        ImGui.EndGroup();
        
        // Draw playhead across all tracks
        DrawPlayhead(visibleFrames);
        
        // Add dedicated horizontal scrollbar at bottom
        RenderTimelineHorizontalScrollbar(visibleFrames);
    }
    
    private void RenderTimelineHorizontalScrollbar(int visibleFrames)
    {
        // Calculate scrollbar parameters
        var scrollbarHeight = ImGui.GetStyle().ScrollbarSize;
        var verticalScrollbarWidth = ImGui.GetStyle().ScrollbarSize;
        float scrollMax = Math.Max(0.0f, TotalFrames - visibleFrames);
        
        // Get timeline window position and size
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        
        // Calculate scrollbar position - fixed to bottom of timeline window
        var scrollbarPos = new Vector2(windowPos.X, windowPos.Y + windowSize.Y - scrollbarHeight);
        var scrollbarWidth = windowSize.X - verticalScrollbarWidth; // Full width minus vertical scrollbar
        
        // Only show scrollbar if there's content to scroll
        if (scrollMax > 0)
        {
            // Position cursor at the bottom of the timeline window
            ImGui.SetCursorScreenPos(scrollbarPos);
            
            // Add dummy item to validate cursor position
            ImGui.Dummy(Vector2.Zero);
            
            // Create child window with horizontal scrollbar for timeline control
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.2f, 0.2f, 0.2f, 1.0f)); // Dark background
            ImGui.BeginChild("##TimelineHorizontalScrollbar", new Vector2(scrollbarWidth, scrollbarHeight), 
                ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
            
            // Calculate the zoomed timeline width for scrollbar range
            float zoomedTimelineWidth = TotalFrames * TimelineZoom;
            
            // Create invisible content spanning the zoomed timeline
            ImGui.InvisibleButton("##ScrollContent", new Vector2(zoomedTimelineWidth, 1));
            
            // Get the current scroll position and convert to timeline start
            float currentScrollX = ImGui.GetScrollX();
            int newTimelineStart = (int)(currentScrollX / TimelineZoom);
            
            // Update timeline start if scroll position changed
            if (newTimelineStart != TimelineStart)
            {
                TimelineStart = Math.Max(0, Math.Min(TotalFrames - visibleFrames, newTimelineStart));
            }
            
            // Sync scroll position with timeline start (for zoom controls etc.)
            float expectedScrollX = TimelineStart * TimelineZoom;
            if (Math.Abs(currentScrollX - expectedScrollX) > 1.0f)
            {
                ImGui.SetScrollX(expectedScrollX);
            }
            
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
        // Note: If no scrolling is needed, we simply don't show any scrollbar
    }
    
    private void RenderTrackNamesBox(float labelWidth, float availableHeight)
    {
        // Track names vertical box with background color
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.3f, 0.3f, 0.3f, 1.0f)); // Dark gray background
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3, 3)); // Match timeline tracks padding
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing); // Ensure consistent item spacing
        ImGui.BeginChild("##TrackNames", new Vector2(labelWidth, 0), ImGuiChildFlags.AutoResizeY);
        
        // Header with same spacing settings as scrubber
        var barSize = new Vector2(-1, 18);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.4f, 0.4f, 0.4f, 1.0f)); // Gray background for header
        ImGui.ProgressBar(0.0f, barSize, "Keyframe Tracks");
        ImGui.PopStyleColor();
        
        // Track labels with colored backgrounds - only show tracks with keyframes
        if (CurrentObject?.PosXKeyframes.Count > 0)
            RenderTrackLabel("Cube.Position X:", new Vector4(0.4f, 0.2f, 0.2f, 1.0f));
        if (CurrentObject?.PosYKeyframes.Count > 0)
            RenderTrackLabel("Cube.Position Y:", new Vector4(0.2f, 0.4f, 0.2f, 1.0f));
        if (CurrentObject?.PosZKeyframes.Count > 0)
            RenderTrackLabel("Cube.Position Z:", new Vector4(0.2f, 0.2f, 0.4f, 1.0f));
        if (CurrentObject?.RotXKeyframes.Count > 0)
            RenderTrackLabel("Cube.Rotation X:", new Vector4(0.6f, 0.3f, 0.3f, 1.0f));
        if (CurrentObject?.RotYKeyframes.Count > 0)
            RenderTrackLabel("Cube.Rotation Y:", new Vector4(0.3f, 0.6f, 0.3f, 1.0f));
        if (CurrentObject?.RotZKeyframes.Count > 0)
            RenderTrackLabel("Cube.Rotation Z:", new Vector4(0.3f, 0.3f, 0.6f, 1.0f));
        if (CurrentObject?.ScaleXKeyframes.Count > 0)
            RenderTrackLabel("Cube.Scale X:", new Vector4(0.8f, 0.4f, 0.4f, 1.0f));
        if (CurrentObject?.ScaleYKeyframes.Count > 0)
            RenderTrackLabel("Cube.Scale Y:", new Vector4(0.4f, 0.8f, 0.4f, 1.0f));
        if (CurrentObject?.ScaleZKeyframes.Count > 0)
            RenderTrackLabel("Cube.Scale Z:", new Vector4(0.4f, 0.4f, 0.8f, 1.0f));
        
        ImGui.EndChild();
        ImGui.PopStyleVar(2); // Pop WindowPadding and ItemSpacing
        ImGui.PopStyleColor();
    }
    
    private void RenderTrackLabel(string labelText, Vector4 labelColor)
    {
        var barSize = new Vector2(-1, 18);
        var darkerColor = new Vector4(labelColor.X * 0.7f, labelColor.Y * 0.7f, labelColor.Z * 0.7f, labelColor.W);
        var lighterColor = new Vector4(Math.Min(1.0f, labelColor.X * 1.3f), Math.Min(1.0f, labelColor.Y * 1.3f), Math.Min(1.0f, labelColor.Z * 1.3f), labelColor.W);
        
        ImGui.PushStyleColor(ImGuiCol.Button, labelColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, lighterColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, darkerColor);
        ImGui.Button(labelText, barSize);
        ImGui.PopStyleColor(3);
    }
    
    private void RenderTimelineTracksBox(float trackWidth, float availableHeight, int visibleFrames)
    {
        // Timeline tracks vertical box with background color and horizontal scrolling
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.2f, 1.0f)); // Dark blue background
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3, 3)); // Match track names padding
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ImGui.GetStyle().ItemSpacing); // Ensure consistent item spacing
        ImGui.BeginChild("##TimelineTracks", new Vector2(trackWidth, 0), ImGuiChildFlags.AutoResizeY);
        
        // Scrubber at the top
        RenderFrameScrubber(visibleFrames);
        
        // Timeline tracks - only show tracks with keyframes
        if (CurrentObject?.PosXKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.PosXKeyframes.Keys.ToList(), "position.x", visibleFrames, trackWidth);
        if (CurrentObject?.PosYKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.PosYKeyframes.Keys.ToList(), "position.y", visibleFrames, trackWidth);
        if (CurrentObject?.PosZKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.PosZKeyframes.Keys.ToList(), "position.z", visibleFrames, trackWidth);
        if (CurrentObject?.RotXKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.RotXKeyframes.Keys.ToList(), "rotation.x", visibleFrames, trackWidth);
        if (CurrentObject?.RotYKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.RotYKeyframes.Keys.ToList(), "rotation.y", visibleFrames, trackWidth);
        if (CurrentObject?.RotZKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.RotZKeyframes.Keys.ToList(), "rotation.z", visibleFrames, trackWidth);
        if (CurrentObject?.ScaleXKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.ScaleXKeyframes.Keys.ToList(), "scale.x", visibleFrames, trackWidth);
        if (CurrentObject?.ScaleYKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.ScaleYKeyframes.Keys.ToList(), "scale.y", visibleFrames, trackWidth);
        if (CurrentObject?.ScaleZKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.ScaleZKeyframes.Keys.ToList(), "scale.z", visibleFrames, trackWidth);
        
        ImGui.EndChild();
        ImGui.PopStyleVar(2); // Pop WindowPadding and ItemSpacing
        ImGui.PopStyleColor();
    }

    private void RenderTimelineTrack(List<int> keyframeFrames, string property, int visibleFrames, float trackWidth)
    {
        var drawList = ImGui.GetWindowDrawList();
        var barSize = new Vector2(-1, 18);
        
        // Draw timeline track
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.1f, 0.1f, 0.15f, 1.0f)); // Darker background for tracks
        ImGui.ProgressBar(0.0f, barSize, "");
        ImGui.PopStyleColor();
        
        var trackRect = ImGui.GetItemRectMin();
        var trackSize = ImGui.GetItemRectSize();
        
        // Check for mouse interaction on this track
        bool isTrackHovered = ImGui.IsItemHovered();
        
        // Handle drag selection start on empty track space
        if (isTrackHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_isDraggingKeyframe)
        {
            var mousePos = ImGui.GetMousePos();
            bool clickedOnKeyframe = false;
            
            // Check if we clicked on any keyframe
            foreach (var frame in keyframeFrames)
            {
                if (frame < TimelineStart || frame > TimelineStart + visibleFrames)
                    continue;
                    
                float normalizedPos = (frame - TimelineStart) / (float)visibleFrames;
                float markerX = trackRect.X + (normalizedPos * trackSize.X);
                float markerY = trackRect.Y + (trackSize.Y * 0.5f);
                float distance = Vector2.Distance(mousePos, new Vector2(markerX, markerY));
                
                if (distance <= 6.0f)
                {
                    clickedOnKeyframe = true;
                    break;
                }
            }
            
            // If we didn't click on a keyframe, start drag selection
            if (!clickedOnKeyframe)
            {
                var io = ImGui.GetIO();
                if (!io.KeyCtrl)
                {
                    SelectedKeyframes.Clear(); // Clear selection unless Ctrl is held
                }
                _isDragSelecting = true;
                _dragSelectionStart = mousePos;
                _dragSelectionEnd = mousePos;
            }
        }
        
        // Update drag selection
        if (_isDragSelecting && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _dragSelectionEnd = ImGui.GetMousePos();
        }
        
        // Handle keyframe dragging globally (outside of keyframe loop)
        if (_isDraggingKeyframe && _draggingKeyframeInfo != null && 
            _draggingKeyframeInfo.Property == property && 
            _draggingKeyframeInfo.ObjectIndex == SelectedObjectIndex)
        {
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                // Calculate new frame position based on mouse drag
                var currentMousePos = ImGui.GetMousePos();
                float deltaX = currentMousePos.X - _dragStartMousePos.X;
                float framePixelWidth = trackSize.X / visibleFrames;
                int frameDelta = (int)(deltaX / framePixelWidth);
                int newFrame = Math.Max(0, _dragStartFrame + frameDelta);
                
                // Only update if frame changed significantly (prevent excessive updates)
                if (newFrame != _draggingKeyframeInfo.Frame && newFrame >= 0)
                {
                    // Move all selected keyframes by the same delta
                    int actualFrameDelta = newFrame - _draggingKeyframeInfo.Frame;
                    MoveSelectedKeyframes(actualFrameDelta);
                    
                    // Update the dragging keyframe reference
                    _draggingKeyframeInfo.Frame = newFrame;
                }
            }
            else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                // Mouse released, stop dragging
                _isDraggingKeyframe = false;
                _draggingKeyframeInfo = null;
            }
        }
        
        // Draw keyframe markers only if they're in the visible range
        foreach (var frame in keyframeFrames)
        {
            if (frame < TimelineStart || frame > TimelineStart + visibleFrames)
                continue; // Skip keyframes outside visible range
            
            float normalizedPos = (frame - TimelineStart) / (float)visibleFrames;
            float markerX = trackRect.X + (normalizedPos * trackSize.X);
            float markerY = trackRect.Y + (trackSize.Y * 0.5f);
            
            // Create keyframe identifier for this keyframe
            var keyframeId = new SelectedKeyframe
            {
                Property = property,
                Frame = frame,
                ObjectIndex = SelectedObjectIndex
            };
            
            // Check if this keyframe is selected
            bool isSelected = SelectedKeyframes.Contains(keyframeId);
            
            // Check if mouse is over this keyframe
            var mousePos = ImGui.GetMousePos();
            float distance = Vector2.Distance(mousePos, new Vector2(markerX, markerY));
            bool isHovered = distance <= 6.0f && isTrackHovered;
            
            // Handle keyframe selection (only if not currently dragging)
            if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_isDraggingKeyframe)
            {
                var io = ImGui.GetIO();
                if (io.KeyCtrl)
                {
                    // Ctrl+click: toggle selection
                    if (isSelected)
                    {
                        SelectedKeyframes.Remove(keyframeId);
                    }
                    else
                    {
                        SelectedKeyframes.Add(keyframeId);
                    }
                }
                else
                {
                    // Normal click: if clicking on already selected keyframe, start dragging all selected
                    if (isSelected && SelectedKeyframes.Count > 1)
                    {
                        // Start dragging all selected keyframes
                        _isDraggingKeyframe = true;
                        _draggingKeyframeInfo = keyframeId;
                        _dragStartMousePos = mousePos;
                        _dragStartFrame = frame;
                    }
                    else
                    {
                        // Clear selection and select this keyframe
                        SelectedKeyframes.Clear();
                        SelectedKeyframes.Add(keyframeId);
                        
                        // Initialize drag state
                        _isDraggingKeyframe = true;
                        _draggingKeyframeInfo = keyframeId;
                        _dragStartMousePos = mousePos;
                        _dragStartFrame = frame;
                    }
                }
            }
            
            // Draw keyframe marker with appropriate color
            uint color;
            if (isSelected)
                color = 0xFF00FF00; // Green for selected
            else if (isHovered)
                color = 0xFFFFFFFF; // White for hovered
            else
                color = 0xFF00FFFF; // Yellow for normal
            
            drawList.AddCircleFilled(new Vector2(markerX, markerY), isSelected ? 5.0f : 4.0f, color);
            
            // Add outline for selected keyframes
            if (isSelected)
            {
                drawList.AddCircle(new Vector2(markerX, markerY), 5.0f, 0xFF000000, 0, 1.5f); // Black outline
            }
            
            // Handle drag selection for this keyframe
            if (_isDragSelecting)
            {
                // Check if keyframe is inside selection rectangle
                var selectionMin = new Vector2(Math.Min(_dragSelectionStart.X, _dragSelectionEnd.X), 
                                             Math.Min(_dragSelectionStart.Y, _dragSelectionEnd.Y));
                var selectionMax = new Vector2(Math.Max(_dragSelectionStart.X, _dragSelectionEnd.X), 
                                             Math.Max(_dragSelectionStart.Y, _dragSelectionEnd.Y));
                
                if (markerX >= selectionMin.X && markerX <= selectionMax.X &&
                    markerY >= selectionMin.Y && markerY <= selectionMax.Y)
                {
                    // Add to selection if not already selected
                    SelectedKeyframes.Add(keyframeId);
                }
            }
        }
        
        // Draw selection rectangle if drag selecting
        if (_isDragSelecting)
        {
            var selectionMin = new Vector2(Math.Min(_dragSelectionStart.X, _dragSelectionEnd.X), 
                                         Math.Min(_dragSelectionStart.Y, _dragSelectionEnd.Y));
            var selectionMax = new Vector2(Math.Max(_dragSelectionStart.X, _dragSelectionEnd.X), 
                                         Math.Max(_dragSelectionStart.Y, _dragSelectionEnd.Y));
            
            drawList.AddRect(selectionMin, selectionMax, 0xFF00FFFF, 0.0f, ImDrawFlags.None, 1.5f); // Yellow selection box
            drawList.AddRectFilled(selectionMin, selectionMax, 0x2200FFFF); // Semi-transparent yellow fill
        }
    }
    

    
    private void RenderFrameScrubber(int visibleFrames)
    {
        // Frame scrubber - renders exactly like progress bars with same zoom level
        int scrubberEnd = Math.Min(TimelineStart + visibleFrames, TotalFrames);
        
        // Draw empty progress bar (same as keyframe tracks)
        var barSize = new Vector2(-1, 18);
        ImGui.ProgressBar(0.0f, barSize, "");
        
        // Get the progress bar's screen position and size for mouse interaction
        var itemRect = ImGui.GetItemRectMin();
        var itemSize = ImGui.GetItemRectSize();
        
        // Store scrubber position for playhead synchronization
        _scrubberPosition = itemRect;
        _scrubberWidth = itemSize.X;
        
        // Handle mouse interaction on the scrubber (relative to visible range)
        IsScrubbing = ImGui.IsItemHovered() && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        if (IsScrubbing)
        {
            var mousePos = ImGui.GetMousePos();
            float relativeX = (mousePos.X - itemRect.X) / itemSize.X;
            relativeX = Math.Max(0.0f, Math.Min(1.0f, relativeX)); // Clamp to 0-1
            
            // Calculate frame within the visible range
            CurrentFrame = TimelineStart + (int)(relativeX * visibleFrames);
            CurrentFrame = Math.Max(0, Math.Min(TotalFrames, CurrentFrame)); // Clamp to total range
            CurrentFrameFloat = CurrentFrame; // Keep fractional frame in sync
        }
        
        RenderFrameLabelsAndGrid(itemRect, itemSize, visibleFrames, scrubberEnd);
        RenderPlayheadOnScrubber(itemRect, itemSize, visibleFrames, scrubberEnd);
    }
    
    private void RenderFrameLabelsAndGrid(Vector2 itemRect, Vector2 itemSize, int visibleFrames, int scrubberEnd)
    {
        // Draw frame labels on the scrubber based on zoom level
        var drawList = ImGui.GetWindowDrawList();
        var textColor = 0xFFFFFFFF; // White color
        
        // Calculate label interval based on zoom level (adjusted for 95-frame base)
        int labelInterval;
        if (TimelineZoom >= 8.0f) labelInterval = 1;   // 8x+ zoom: every 1 frame (~12 frames visible)
        else if (TimelineZoom >= 4.0f) labelInterval = 2;   // 4x-8x zoom: every 2 frames (~24 frames visible)
        else if (TimelineZoom >= 2.0f) labelInterval = 5;   // 2x-4x zoom: every 5 frames (~48 frames visible)
        else if (TimelineZoom >= 1.0f) labelInterval = 10;  // 1x-2x zoom: every 10 frames (~95 frames visible)
        else if (TimelineZoom >= 0.5f) labelInterval = 20;  // 0.5x-1x zoom: every 20 frames (~190 frames visible)
        else labelInterval = 50; // <0.5x zoom: every 50 frames (380+ frames visible)
        
        // Calculate which frame numbers to show
        int startFrame = (TimelineStart / labelInterval) * labelInterval; // Round down to nearest interval
        for (int frame = startFrame; frame <= scrubberEnd; frame += labelInterval)
        {
            if (frame < TimelineStart) continue;
            
            float progress = (frame - TimelineStart) / (float)visibleFrames;
            float labelX = itemRect.X + (progress * itemSize.X);
            float labelY = itemRect.Y + (itemSize.Y * 0.5f);
            
            // Draw frame number on the scrubber
            drawList.AddText(new Vector2(labelX - 10, labelY - 8), textColor, frame.ToString());
            
            // Draw vertical grid line from top of scrubber to bottom of tracks
            var gridLineColor = 0x33FFFFFFu; // Semi-transparent white
            float lineTopY = itemRect.Y;
            float lineBottomY = itemRect.Y + itemSize.Y + (9 * (18 + ImGui.GetStyle().ItemSpacing.Y)); // Scrubber + 9 tracks (3 position + 3 rotation + 3 scale)
            drawList.AddLine(new Vector2(labelX, lineTopY), new Vector2(labelX, lineBottomY), gridLineColor, 1.0f);
        }
    }
    
    private void RenderPlayheadOnScrubber(Vector2 itemRect, Vector2 itemSize, int visibleFrames, int scrubberEnd)
    {
        // Draw red playhead indicator on the scrubber (only if current frame is visible)
        if (CurrentFrame >= TimelineStart && CurrentFrame <= scrubberEnd)
        {
            var drawList = ImGui.GetWindowDrawList();
            var textColor = 0xFFFFFFFF; // White color
            
            float progress = (CurrentFrame - TimelineStart) / (float)visibleFrames;
            float playheadX = itemRect.X + (progress * itemSize.X);
            float playheadY = itemRect.Y + (itemSize.Y * 0.5f);
            
            // Draw red triangle arrow at playhead position
            var playheadColor = 0xFF0000FF; // Red color
            var trianglePoints = new Vector2[]
            {
                new Vector2(playheadX, playheadY + 9), // Bottom point (pointing down)
                new Vector2(playheadX - 6, playheadY - 9), // Top left
                new Vector2(playheadX + 6, playheadY - 9)  // Top right
            };
            drawList.AddTriangleFilled(trianglePoints[0], trianglePoints[1], trianglePoints[2], playheadColor);
            
            // Draw frame number above playhead
            drawList.AddText(new Vector2(playheadX - 15, playheadY - 20), textColor, CurrentFrame.ToString());
        }
    }
    
    private void DrawPlayhead(int visibleFrames)
    {
        // Only draw playhead if current frame is within visible range
        if (CurrentFrame < TimelineStart || CurrentFrame > TimelineStart + visibleFrames)
            return;

        var drawList = ImGui.GetWindowDrawList();
        var currentWindowPos = ImGui.GetWindowPos();
        var currentWindowSize = ImGui.GetWindowSize();
        
        // Calculate playhead position using the exact scrubber coordinates
        float normalizedPos = (CurrentFrame - TimelineStart) / (float)visibleFrames;
        float playheadX = _scrubberPosition.X + (normalizedPos * _scrubberWidth);
        
        // Draw vertical line from top of scrubber to bottom of timeline window
        float topY = _scrubberPosition.Y; // Start at scrubber top
        float bottomY = currentWindowPos.Y + currentWindowSize.Y - ImGui.GetStyle().ScrollbarSize; // End at bottom of timeline window (minus scrollbar)
        
        // Draw playhead line (red vertical line)
        var playheadColor = 0xFF0000FFu; // Red color
        drawList.AddLine(new Vector2(playheadX, topY), new Vector2(playheadX, bottomY), playheadColor, 2.0f);
    }
    
    public void CalculateTotalFrames()
    {
        // Calculate dynamic timeline length based on keyframes from all objects
        int maxKeyframe = 0;
        if (SceneObjects != null)
        {
            foreach (var obj in SceneObjects)
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
        }
        
        // If no keyframes exist, use minimum timeline length
        if (maxKeyframe == 0)
            TotalFrames = 500; // Default minimum
        else
            TotalFrames = Math.Max(maxKeyframe + 100, 500); // Minimum 500 frames
    }
    
    // Methods for keyframe management
    public void AddKeyframe(string property, int frame, float value)
    {
        if (CurrentObject == null) return;
        
        Dictionary<int, float> keyframes;
        switch (property.ToLower())
        {
            case "x":
            case "position.x":
                keyframes = CurrentObject.PosXKeyframes;
                break;
            case "y":
            case "position.y":
                keyframes = CurrentObject.PosYKeyframes;
                break;
            case "z":
            case "position.z":
                keyframes = CurrentObject.PosZKeyframes;
                break;
            case "rotation.x":
                keyframes = CurrentObject.RotXKeyframes;
                break;
            case "rotation.y":
                keyframes = CurrentObject.RotYKeyframes;
                break;
            case "rotation.z":
                keyframes = CurrentObject.RotZKeyframes;
                break;
            case "scale.x":
                keyframes = CurrentObject.ScaleXKeyframes;
                break;
            case "scale.y":
                keyframes = CurrentObject.ScaleYKeyframes;
                break;
            case "scale.z":
                keyframes = CurrentObject.ScaleZKeyframes;
                break;
            default:
                return; // Unknown property
        }
        
        // Add or update keyframe
        keyframes[frame] = value;
        CalculateTotalFrames(); // Recalculate timeline length
    }
    
    public void RemoveKeyframe(string property, int frame)
    {
        if (CurrentObject == null) return;
        
        Dictionary<int, float> keyframes;
        switch (property.ToLower())
        {
            case "x":
            case "position.x":
                keyframes = CurrentObject.PosXKeyframes;
                break;
            case "y":
            case "position.y":
                keyframes = CurrentObject.PosYKeyframes;
                break;
            case "z":
            case "position.z":
                keyframes = CurrentObject.PosZKeyframes;
                break;
            case "rotation.x":
                keyframes = CurrentObject.RotXKeyframes;
                break;
            case "rotation.y":
                keyframes = CurrentObject.RotYKeyframes;
                break;
            case "rotation.z":
                keyframes = CurrentObject.RotZKeyframes;
                break;
            case "scale.x":
                keyframes = CurrentObject.ScaleXKeyframes;
                break;
            case "scale.y":
                keyframes = CurrentObject.ScaleYKeyframes;
                break;
            case "scale.z":
                keyframes = CurrentObject.ScaleZKeyframes;
                break;
            default:
                return; // Unknown property
        }
        
        keyframes.Remove(frame);
        CalculateTotalFrames(); // Recalculate timeline length
    }
    
    public void MoveKeyframe(string property, int oldFrame, int newFrame)
    {
        if (CurrentObject == null || oldFrame == newFrame) return;
        
        Dictionary<int, float> keyframes;
        switch (property.ToLower())
        {
            case "x":
            case "position.x":
                keyframes = CurrentObject.PosXKeyframes;
                break;
            case "y":
            case "position.y":
                keyframes = CurrentObject.PosYKeyframes;
                break;
            case "z":
            case "position.z":
                keyframes = CurrentObject.PosZKeyframes;
                break;
            case "rotation.x":
                keyframes = CurrentObject.RotXKeyframes;
                break;
            case "rotation.y":
                keyframes = CurrentObject.RotYKeyframes;
                break;
            case "rotation.z":
                keyframes = CurrentObject.RotZKeyframes;
                break;
            case "scale.x":
                keyframes = CurrentObject.ScaleXKeyframes;
                break;
            case "scale.y":
                keyframes = CurrentObject.ScaleYKeyframes;
                break;
            case "scale.z":
                keyframes = CurrentObject.ScaleZKeyframes;
                break;
            default:
                return; // Unknown property
        }
        
        // Get the value at the old frame
        if (keyframes.ContainsKey(oldFrame))
        {
            float value = keyframes[oldFrame];
            keyframes.Remove(oldFrame); // Remove from old position
            
            // Find a suitable target frame if the desired frame is occupied
            int targetFrame = FindAvailableFrame(keyframes, newFrame, oldFrame);
            keyframes[targetFrame] = value; // Add at target position
            CalculateTotalFrames(); // Recalculate timeline length
        }
    }
    
    private int FindAvailableFrame(Dictionary<int, float> keyframes, int preferredFrame, int excludeFrame)
    {
        // If preferred frame is available (not occupied by a different keyframe), use it
        if (!keyframes.ContainsKey(preferredFrame) || preferredFrame == excludeFrame)
        {
            return preferredFrame;
        }
        
        // Find the next available frame by searching in both directions
        // This allows dragging "through" existing keyframes to find open spots
        for (int offset = 1; offset <= 10; offset++) // Search up to 10 frames away
        {
            // Try frame to the right first (positive direction)
            int rightFrame = preferredFrame + offset;
            if (!keyframes.ContainsKey(rightFrame) && rightFrame != excludeFrame)
            {
                return rightFrame;
            }
            
            // Try frame to the left (negative direction)
            int leftFrame = preferredFrame - offset;
            if (leftFrame >= 0 && !keyframes.ContainsKey(leftFrame) && leftFrame != excludeFrame)
            {
                return leftFrame;
            }
        }
        
        // If no free frame found nearby, just use the preferred frame (this shouldn't normally happen)
        return preferredFrame;
    }
    
    private Dictionary<int, float>? GetKeyframeDictionary(string property)
    {
        if (CurrentObject == null) return null;
        
        switch (property.ToLower())
        {
            case "x":
            case "position.x":
                return CurrentObject.PosXKeyframes;
            case "y":
            case "position.y":
                return CurrentObject.PosYKeyframes;
            case "z":
            case "position.z":
                return CurrentObject.PosZKeyframes;
            case "rotation.x":
                return CurrentObject.RotXKeyframes;
            case "rotation.y":
                return CurrentObject.RotYKeyframes;
            case "rotation.z":
                return CurrentObject.RotZKeyframes;
            case "scale.x":
                return CurrentObject.ScaleXKeyframes;
            case "scale.y":
                return CurrentObject.ScaleYKeyframes;
            case "scale.z":
                return CurrentObject.ScaleZKeyframes;
            default:
                return null;
        }
    }
    
    public void MoveSelectedKeyframes(int frameDelta)
    {
        if (frameDelta == 0 || SelectedKeyframes.Count == 0) return;
        
        // Create a list of keyframes to update (to avoid modifying collection during iteration)
        var keyframesToUpdate = new List<SelectedKeyframe>(SelectedKeyframes);
        
        // First pass: collect all keyframes to move and validate the move is possible
        var moveOperations = new List<(SelectedKeyframe keyframe, int newFrame)>();
        foreach (var keyframe in keyframesToUpdate)
        {
            int newFrame = Math.Max(0, keyframe.Frame + frameDelta);
            moveOperations.Add((keyframe, newFrame));
        }
        
        // Clear current selection to update with new positions
        SelectedKeyframes.Clear();
        
        // Second pass: perform the moves and track actual final positions
        foreach (var (keyframe, newFrame) in moveOperations)
        {
            // Get the dictionary for this property to check final position
            var keyframes = GetKeyframeDictionary(keyframe.Property);
            if (keyframes != null)
            {
                // Store value before moving
                float value = keyframes.ContainsKey(keyframe.Frame) ? keyframes[keyframe.Frame] : 0.0f;
                
                // Move the keyframe (this handles collision detection)
                MoveKeyframe(keyframe.Property, keyframe.Frame, newFrame);
                
                // Find where it actually ended up (might be different due to collision avoidance)
                int actualFrame = newFrame;
                if (keyframes.ContainsKey(newFrame) && Math.Abs(keyframes[newFrame] - value) < 0.001f)
                {
                    actualFrame = newFrame; // It went where we wanted
                }
                else
                {
                    // Find where it actually went
                    foreach (var kvp in keyframes)
                    {
                        if (Math.Abs(kvp.Value - value) < 0.001f)
                        {
                            actualFrame = kvp.Key;
                            break;
                        }
                    }
                }
                
                // Add updated keyframe to selection with actual position
                SelectedKeyframes.Add(new SelectedKeyframe
                {
                    Property = keyframe.Property,
                    Frame = actualFrame,
                    ObjectIndex = keyframe.ObjectIndex
                });
            }
        }
    }
    
    // Evaluate keyframe value at current frame with interpolation
    public float EvaluateKeyframes(string property, int frame)
    {
        Dictionary<int, float> keyframes;
        switch (property.ToLower())
        {
            case "x":
            case "position.x":
                keyframes = CurrentObject.PosXKeyframes;
                break;
            case "y":
            case "position.y":
                keyframes = CurrentObject.PosYKeyframes;
                break;
            case "z":
            case "position.z":
                keyframes = CurrentObject.PosZKeyframes;
                break;
            case "rotation.x":
                keyframes = CurrentObject.RotXKeyframes;
                break;
            case "rotation.y":
                keyframes = CurrentObject.RotYKeyframes;
                break;
            case "rotation.z":
                keyframes = CurrentObject.RotZKeyframes;
                break;
            case "scale.x":
                keyframes = CurrentObject.ScaleXKeyframes;
                break;
            case "scale.y":
                keyframes = CurrentObject.ScaleYKeyframes;
                break;
            case "scale.z":
                keyframes = CurrentObject.ScaleZKeyframes;
                break;
            default:
                // Return default values based on property type
                if (property.StartsWith("scale"))
                    return 1.0f; // Default scale is 1.0
                return 0.0f; // Default position/rotation is 0.0
        }
        
        if (keyframes.Count == 0)
        {
            // Return default values based on property type
            if (property.StartsWith("scale"))
                return 1.0f; // Default scale is 1.0
            return 0.0f; // Default position/rotation is 0.0
        }
            
        // If exact frame exists, return it
        if (keyframes.ContainsKey(frame))
            return keyframes[frame];
            
        // Find surrounding keyframes for interpolation
        var sortedFrames = keyframes.Keys.OrderBy(k => k).ToList();
        
        // Before first keyframe
        if (frame < sortedFrames[0])
            return keyframes[sortedFrames[0]];
            
        // After last keyframe
        if (frame > sortedFrames[sortedFrames.Count - 1])
            return keyframes[sortedFrames[sortedFrames.Count - 1]];
            
        // Find the two keyframes to interpolate between
        int leftFrame = -1, rightFrame = -1;
        for (int i = 0; i < sortedFrames.Count - 1; i++)
        {
            if (frame > sortedFrames[i] && frame < sortedFrames[i + 1])
            {
                leftFrame = sortedFrames[i];
                rightFrame = sortedFrames[i + 1];
                break;
            }
        }
        
        if (leftFrame == -1 || rightFrame == -1)
            return 0.0f; // Shouldn't happen
            
        // Linear interpolation
        float leftValue = keyframes[leftFrame];
        float rightValue = keyframes[rightFrame];
        float t = (float)(frame - leftFrame) / (rightFrame - leftFrame);
        return leftValue + (rightValue - leftValue) * t;
    }
    
    // Evaluate keyframes for any keyframe dictionary with specified default
    public float EvaluateKeyframesWithDefault(Dictionary<int, float> keyframes, float frame, float defaultValue)
    {
        if (keyframes.Count == 0)
            return defaultValue; // Return the specified default for empty keyframes
            
        // Find surrounding keyframes for interpolation
        var sortedFrames = keyframes.Keys.OrderBy(k => k).ToList();
        
        // Before first keyframe
        if (frame < sortedFrames[0])
            return keyframes[sortedFrames[0]];
            
        // After last keyframe
        if (frame > sortedFrames[sortedFrames.Count - 1])
            return keyframes[sortedFrames[sortedFrames.Count - 1]];
            
        // Find the two keyframes to interpolate between
        int leftFrame = -1, rightFrame = -1;
        for (int i = 0; i < sortedFrames.Count - 1; i++)
        {
            if (frame >= sortedFrames[i] && frame <= sortedFrames[i + 1])
            {
                leftFrame = sortedFrames[i];
                rightFrame = sortedFrames[i + 1];
                break;
            }
        }
        
        if (leftFrame == -1 || rightFrame == -1)
            return keyframes[sortedFrames[0]]; // Fallback
            
        // If we're exactly on a keyframe, return its value
        if (Math.Abs(frame - leftFrame) < 0.001f)
            return keyframes[leftFrame];
        if (Math.Abs(frame - rightFrame) < 0.001f)
            return keyframes[rightFrame];
            
        // Linear interpolation with fractional frame
        float leftValue = keyframes[leftFrame];
        float rightValue = keyframes[rightFrame];
        float t = (frame - leftFrame) / (rightFrame - leftFrame);
        
        return leftValue + (rightValue - leftValue) * t;
    }

    public float EvaluateKeyframesWithDefault(Dictionary<int, float> keyframes, int frame, float defaultValue)
    {
        if (keyframes.Count == 0)
            return defaultValue; // Return the specified default for empty keyframes
            
        // If exact frame exists, return it
        if (keyframes.ContainsKey(frame))
            return keyframes[frame];
            
        // Find surrounding keyframes for interpolation
        var sortedFrames = keyframes.Keys.OrderBy(k => k).ToList();
        
        // Before first keyframe
        if (frame < sortedFrames[0])
            return keyframes[sortedFrames[0]];
            
        // After last keyframe
        if (frame > sortedFrames[sortedFrames.Count - 1])
            return keyframes[sortedFrames[sortedFrames.Count - 1]];
            
        // Find the two keyframes to interpolate between
        int leftFrame = -1, rightFrame = -1;
        for (int i = 0; i < sortedFrames.Count - 1; i++)
        {
            if (frame > sortedFrames[i] && frame < sortedFrames[i + 1])
            {
                leftFrame = sortedFrames[i];
                rightFrame = sortedFrames[i + 1];
                break;
            }
        }
        
        if (leftFrame == -1 || rightFrame == -1)
            return keyframes[sortedFrames[0]]; // Fallback
            
        // Linear interpolation
        float leftValue = keyframes[leftFrame];
        float rightValue = keyframes[rightFrame];
        float t = (float)(frame - leftFrame) / (rightFrame - leftFrame);
        
        return leftValue + (rightValue - leftValue) * t;
    }
    
    // Get current animated position based on keyframes
    public Vector3 GetAnimatedPosition()
    {
        var x = EvaluateKeyframes("position.x", CurrentFrame);
        var y = EvaluateKeyframes("position.y", CurrentFrame);
        var z = EvaluateKeyframes("position.z", CurrentFrame);
        
        return new Vector3(x, y, z);
    }
    
    // Get current animated rotation based on keyframes
    public Vector3 GetAnimatedRotation()
    {
        var x = EvaluateKeyframes("rotation.x", CurrentFrame);
        var y = EvaluateKeyframes("rotation.y", CurrentFrame);
        var z = EvaluateKeyframes("rotation.z", CurrentFrame);
        
        return new Vector3(x, y, z);
    }
    
    // Get current animated scale based on keyframes
    public Vector3 GetAnimatedScale()
    {
        var x = EvaluateKeyframes("scale.x", CurrentFrame);
        var y = EvaluateKeyframes("scale.y", CurrentFrame);
        var z = EvaluateKeyframes("scale.z", CurrentFrame);
        
        return new Vector3(x, y, z);
    }
    
    // Get animated position for a specific object at current frame
    public Vector3 GetAnimatedPosition(SceneObject obj)
    {
        // Use fractional frame for smooth interpolation when playing, integer frame when scrubbing
        float frameToUse = IsPlaying ? CurrentFrameFloat : CurrentFrame;
        var x = EvaluateKeyframesWithDefault(obj.PosXKeyframes, frameToUse, 0.0f);
        var y = EvaluateKeyframesWithDefault(obj.PosYKeyframes, frameToUse, 0.0f);
        var z = EvaluateKeyframesWithDefault(obj.PosZKeyframes, frameToUse, 0.0f);
        
        return new Vector3(x, y, z);
    }
    
    // Get animated rotation for a specific object at current frame
    public Vector3 GetAnimatedRotation(SceneObject obj)
    {
        // Use fractional frame for smooth interpolation when playing, integer frame when scrubbing
        float frameToUse = IsPlaying ? CurrentFrameFloat : CurrentFrame;
        var x = EvaluateKeyframesWithDefault(obj.RotXKeyframes, frameToUse, 0.0f);
        var y = EvaluateKeyframesWithDefault(obj.RotYKeyframes, frameToUse, 0.0f);
        var z = EvaluateKeyframesWithDefault(obj.RotZKeyframes, frameToUse, 0.0f);
        
        return new Vector3(x, y, z);
    }
    
    // Get animated scale for a specific object at current frame
    public Vector3 GetAnimatedScale(SceneObject obj)
    {
        // Use fractional frame for smooth interpolation when playing, integer frame when scrubbing
        float frameToUse = IsPlaying ? CurrentFrameFloat : CurrentFrame;
        var x = EvaluateKeyframesWithDefault(obj.ScaleXKeyframes, frameToUse, 1.0f);
        var y = EvaluateKeyframesWithDefault(obj.ScaleYKeyframes, frameToUse, 1.0f);
        var z = EvaluateKeyframesWithDefault(obj.ScaleZKeyframes, frameToUse, 1.0f);
        
        return new Vector3(x, y, z);
    }
    
    // Check if a specific object has any keyframes
    public bool HasKeyframes(SceneObject obj)
    {
        return obj.PosXKeyframes.Count > 0 || obj.PosYKeyframes.Count > 0 || obj.PosZKeyframes.Count > 0 ||
               obj.RotXKeyframes.Count > 0 || obj.RotYKeyframes.Count > 0 || obj.RotZKeyframes.Count > 0 ||
               obj.ScaleXKeyframes.Count > 0 || obj.ScaleYKeyframes.Count > 0 || obj.ScaleZKeyframes.Count > 0;
    }
    
    // Check if there are any keyframes for the current object
    public bool HasKeyframes()
    {
        if (CurrentObject == null) return false;
        
        return CurrentObject.PosXKeyframes.Count > 0 || CurrentObject.PosYKeyframes.Count > 0 || CurrentObject.PosZKeyframes.Count > 0 ||
               CurrentObject.RotXKeyframes.Count > 0 || CurrentObject.RotYKeyframes.Count > 0 || CurrentObject.RotZKeyframes.Count > 0 ||
               CurrentObject.ScaleXKeyframes.Count > 0 || CurrentObject.ScaleYKeyframes.Count > 0 || CurrentObject.ScaleZKeyframes.Count > 0;
    }
    
    // Properties to expose scrubber position for 3D viewport synchronization
    public Vector2 ScrubberPosition => _scrubberPosition;
    public float ScrubberWidth => _scrubberWidth;

    public void ClearAllKeyframes()
    {
        if (CurrentObject == null) return;
        
        CurrentObject.PosXKeyframes.Clear();
        CurrentObject.PosYKeyframes.Clear();
        CurrentObject.PosZKeyframes.Clear();
        CurrentObject.RotXKeyframes.Clear();
        CurrentObject.RotYKeyframes.Clear();
        CurrentObject.RotZKeyframes.Clear();
        CurrentObject.ScaleXKeyframes.Clear();
        CurrentObject.ScaleYKeyframes.Clear();
        CurrentObject.ScaleZKeyframes.Clear();
        CalculateTotalFrames();
    }
    
    // Keyframe selection management methods
    public void ClearKeyframeSelection()
    {
        SelectedKeyframes.Clear();
    }
    
    public bool HasSelectedKeyframe()
    {
        return SelectedKeyframes.Count > 0;
    }
    
    public int GetSelectedKeyframeCount()
    {
        return SelectedKeyframes.Count;
    }
    
    public void DeleteSelectedKeyframe()
    {
        if (SelectedKeyframes.Count > 0 && CurrentObject != null)
        {
            // Delete all selected keyframes
            var keyframesToDelete = new List<SelectedKeyframe>(SelectedKeyframes);
            foreach (var keyframe in keyframesToDelete)
            {
                RemoveKeyframe(keyframe.Property, keyframe.Frame);
            }
            SelectedKeyframes.Clear();
        }
    }
    
    public void SetSelectedKeyframe(string property, int frame)
    {
        if (SelectedObjectIndex >= 0)
        {
            SelectedKeyframes.Clear();
            SelectedKeyframes.Add(new SelectedKeyframe
            {
                Property = property,
                Frame = frame,
                ObjectIndex = SelectedObjectIndex
            });
        }
    }

    private unsafe void LoadIconTextures()
    {
        if (_gl == null) return;
        
        // Load icons from embedded SVG resources
        _playIconTexture = LoadIconFromResource("play.svg") ?? 0;
        _pauseIconTexture = LoadIconFromResource("pause.svg") ?? 0;
        _stopIconTexture = LoadIconFromResource("stop.svg") ?? 0;
        _resetIconTexture = LoadIconFromResource("chevron-double-left.svg") ?? 0;
    }
    
    private unsafe uint? LoadIconFromResource(string iconFileName)
    {
        if (_gl == null) return null;
        
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"misr.assets.icons.{iconFileName}");
            
            if (stream == null) return null;
            
            byte[] imageData;
            
            if (iconFileName.EndsWith(".svg"))
            {
                // Convert SVG to PNG bytes
                imageData = ConvertSvgToPngBytes(stream);
                if (imageData == null) return null;
                
                // Load the PNG data with StbImageSharp
                var imageResult = ImageResult.FromMemory(imageData, ColorComponents.RedGreenBlueAlpha);
                return CreateTextureFromImageResult(imageResult);
            }
            else
            {
                // Direct PNG/image loading
                var imageResult = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                return CreateTextureFromImageResult(imageResult);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load icon {iconFileName}: {ex.Message}");
            return null;
        }
    }
    
    private byte[]? ConvertSvgToPngBytes(Stream svgStream)
    {
        try
        {
            // Load SVG document
            var svgDocument = SvgDocument.Open<SvgDocument>(svgStream);
            
            // Set size to 16x16 for icons
            svgDocument.Width = 16;
            svgDocument.Height = 16;
            
            // Create bitmap and render SVG to it with white color
            using var bitmap = new Bitmap(16, 16);
            using var graphics = Graphics.FromImage(bitmap);
            
            // Clear background to transparent
            graphics.Clear(Color.Transparent);
            
            // Set the current color to white for SVG rendering
            // This affects elements with fill="currentColor"
            svgDocument.Fill = new SvgColourServer(Color.White);
            
            // Render the SVG
            svgDocument.Draw(graphics);
            
            // Convert bitmap to PNG bytes
            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream, ImageFormat.Png);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to convert SVG to PNG: {ex.Message}");
            return null;
        }
    }
    
    private unsafe uint CreateTextureFromImageResult(ImageResult imageResult)
    {
        var texture = _gl!.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        
        fixed (byte* dataPtr = imageResult.Data)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)imageResult.Width, (uint)imageResult.Height, 0, Silk.NET.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, dataPtr);
        }
        
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return texture;
    }
    


    public void Dispose()
    {
        if (_gl != null)
        {
            if (_playIconTexture != 0) _gl.DeleteTexture(_playIconTexture);
            if (_pauseIconTexture != 0) _gl.DeleteTexture(_pauseIconTexture);
            if (_stopIconTexture != 0) _gl.DeleteTexture(_stopIconTexture);
            if (_resetIconTexture != 0) _gl.DeleteTexture(_resetIconTexture);
        }
    }
}
