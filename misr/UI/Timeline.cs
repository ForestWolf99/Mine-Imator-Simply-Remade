using ImGuiNET;
using System.Numerics;
using Misr.Core;

namespace Misr.UI;

public class Timeline : IDisposable
{
    // Timeline state
    public float TimelineZoom { get; set; } = 1.0f; // 1.0 = normal zoom (95 frames), 2.0 = 2x zoom, etc.
    public int TimelineStart { get; set; } = 0; // Starting frame of visible timeline
    public int TotalFrames { get; set; } = 2000; // Total frames in the animation - calculated dynamically
    public int CurrentFrame { get; set; } = 0;
    public bool IsPlaying { get; set; } = false;
    public bool IsScrubbing { get; private set; } = false;
    
    // Animation framerate (frames per second)
    public float FrameRate { get; set; } = 30.0f;
    private float _frameTimer = 0.0f;
    
    // Store scrubber position for playhead synchronization and 3D viewport
    private Vector2 _scrubberPosition = Vector2.Zero;
    private float _scrubberWidth = 0.0f;
    
    // Reference to scene objects and selected object
    public List<SceneObject>? SceneObjects { get; set; }
    public int SelectedObjectIndex { get; set; } = -1;
    
    // Current selected object helper
    public SceneObject? CurrentObject => 
        SceneObjects != null && SelectedObjectIndex >= 0 && SelectedObjectIndex < SceneObjects.Count 
            ? SceneObjects[SelectedObjectIndex] 
            : null;
    
    public Timeline()
    {
        CalculateTotalFrames();
    }
    
    public void Update(float deltaTime)
    {
        // Update animation at specified frame rate
        if (IsPlaying)
        {
            _frameTimer += deltaTime;
            float frameInterval = 1.0f / FrameRate;
            
            while (_frameTimer >= frameInterval)
            {
                _frameTimer -= frameInterval;
                CurrentFrame++;
                if (CurrentFrame > TotalFrames) CurrentFrame = 0;
            }
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
            HandleTimelineZoom();
            
            // Calculate visible frame range
            int visibleFrames = (int)(95 / TimelineZoom);
            int timelineEnd = Math.Min(TimelineStart + visibleFrames, TotalFrames);
            
            RenderTimelineControls(visibleFrames, timelineEnd);
            RenderTimelineContent(windowSize, visibleFrames);
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
        if (ImGui.Button(IsPlaying ? "⏸ Pause" : "▶ Play"))
        {
            IsPlaying = !IsPlaying;
        }
        
        ImGui.SameLine();
        if (ImGui.Button("⏹ Stop"))
        {
            IsPlaying = false;
            CurrentFrame = 0;
        }
        
        ImGui.SameLine();
        if (ImGui.Button("⏮ Reset"))
        {
            CurrentFrame = 0;
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
            RenderTimelineTrack(CurrentObject.PosXKeyframes.Keys.ToList(), visibleFrames, trackWidth);
        if (CurrentObject?.PosYKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.PosYKeyframes.Keys.ToList(), visibleFrames, trackWidth);
        if (CurrentObject?.PosZKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.PosZKeyframes.Keys.ToList(), visibleFrames, trackWidth);
        if (CurrentObject?.RotXKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.RotXKeyframes.Keys.ToList(), visibleFrames, trackWidth);
        if (CurrentObject?.RotYKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.RotYKeyframes.Keys.ToList(), visibleFrames, trackWidth);
        if (CurrentObject?.RotZKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.RotZKeyframes.Keys.ToList(), visibleFrames, trackWidth);
        if (CurrentObject?.ScaleXKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.ScaleXKeyframes.Keys.ToList(), visibleFrames, trackWidth);
        if (CurrentObject?.ScaleYKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.ScaleYKeyframes.Keys.ToList(), visibleFrames, trackWidth);
        if (CurrentObject?.ScaleZKeyframes.Count > 0)
            RenderTimelineTrack(CurrentObject.ScaleZKeyframes.Keys.ToList(), visibleFrames, trackWidth);
        
        ImGui.EndChild();
        ImGui.PopStyleVar(2); // Pop WindowPadding and ItemSpacing
        ImGui.PopStyleColor();
    }
    

    
    private void RenderTimelineTrack(List<int> keyframeFrames, int visibleFrames, float trackWidth)
    {
        var drawList = ImGui.GetWindowDrawList();
        var barSize = new Vector2(-1, 18);
        
        // Draw timeline track
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Vector4(0.1f, 0.1f, 0.15f, 1.0f)); // Darker background for tracks
        ImGui.ProgressBar(0.0f, barSize, "");
        ImGui.PopStyleColor();
        
        var trackRect = ImGui.GetItemRectMin();
        var trackSize = ImGui.GetItemRectSize();
        
        // Draw keyframe markers only if they're in the visible range
        foreach (var frame in keyframeFrames)
        {
            if (frame < TimelineStart || frame > TimelineStart + visibleFrames)
                continue; // Skip keyframes outside visible range
            
            float normalizedPos = (frame - TimelineStart) / (float)visibleFrames;
            float markerX = trackRect.X + (normalizedPos * trackSize.X);
            float markerY = trackRect.Y + (trackSize.Y * 0.5f);
            
            // Draw keyframe marker
            var color = 0xFF00FFFF; // Yellow color
            drawList.AddCircleFilled(new Vector2(markerX, markerY), 4.0f, color);
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
    
    private void CalculateTotalFrames()
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
        var x = EvaluateKeyframesWithDefault(obj.PosXKeyframes, CurrentFrame, 0.0f);
        var y = EvaluateKeyframesWithDefault(obj.PosYKeyframes, CurrentFrame, 0.0f);
        var z = EvaluateKeyframesWithDefault(obj.PosZKeyframes, CurrentFrame, 0.0f);
        
        return new Vector3(x, y, z);
    }
    
    // Get animated rotation for a specific object at current frame
    public Vector3 GetAnimatedRotation(SceneObject obj)
    {
        var x = EvaluateKeyframesWithDefault(obj.RotXKeyframes, CurrentFrame, 0.0f);
        var y = EvaluateKeyframesWithDefault(obj.RotYKeyframes, CurrentFrame, 0.0f);
        var z = EvaluateKeyframesWithDefault(obj.RotZKeyframes, CurrentFrame, 0.0f);
        
        return new Vector3(x, y, z);
    }
    
    // Get animated scale for a specific object at current frame
    public Vector3 GetAnimatedScale(SceneObject obj)
    {
        var x = EvaluateKeyframesWithDefault(obj.ScaleXKeyframes, CurrentFrame, 1.0f);
        var y = EvaluateKeyframesWithDefault(obj.ScaleYKeyframes, CurrentFrame, 1.0f);
        var z = EvaluateKeyframesWithDefault(obj.ScaleZKeyframes, CurrentFrame, 1.0f);
        
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

    public void Dispose()
    {
        // No resources to dispose currently
    }
}
