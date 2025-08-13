using ImGuiNET;
using System.Numerics;
using System;
using Misr.Utilities;

namespace Misr.UI;

public enum DialogType
{
    DeleteConfirmation,
    SystemMessage,
    YesNoConfirmation,
    Information,
    RenderSettings,
    RenderAnimation,
    RenderProgress
}

public class Dialog
{
    private readonly DialogType _type;
    private readonly string _title;
    private readonly string _message;
    private readonly Action? _onYes;
    private readonly Action? _onNo;
    private readonly Action<int, int, string>? _onRenderComplete;
    private readonly Action<int, int, int, int, string, string>? _onAnimationRenderComplete;
    private readonly Vector2 _position;
    
    private Vector2 _dialogPos = Vector2.Zero;
    private Vector2 _dialogSize = Vector2.Zero;
    
    // Render settings state
    private int _selectedResolutionIndex = 3; // Default to FHD 1080p
    private readonly string[] _resolutionOptions = { 
        "Avatar 512x512", 
        "VGA 640x480", 
        "HD 720p 1280x720", 
        "FHD 1080p 1920x1080", 
        "QHD 1440p 2560x1440", 
        "UHD 4K 3840x2160", 
        "HD Cinematic 1680x720", 
        "FHD Cinematic 2560x1080", 
        "QHD Cinematic 3440x1440", 
        "QHD+ Cinematic 3840x1600", 
        "UW4k Cinematic 4320x1800", 
        "UW5k Cinematic 5120x2160", 
        "Custom" 
    };
    private int _customWidth = 1920;
    private int _customHeight = 1080;
    private bool _keepAspectRatio = true;
    private float _aspectRatio = 16.0f / 9.0f;
    
    // Animation render settings state
    private int _selectedVideoFormatIndex = 0;
    private readonly string[] _videoFormatOptions = { "MP4", "MOV", "WMV", "PNG Sequence" };
    private int _bitrate = 10000; // kbps
    private int _framerate = 30;
    
    // Progress tracking state
    private int _currentFrame = 0;
    private int _totalFrames = 0;
    private float _encodingProgress = 0.0f;
    private string _progressText = "";
    private bool _isEncoding = false;

    public bool IsVisible { get; set; } = true;
    public DialogType Type => _type;

    public Dialog(DialogType type, string title, string message, Vector2 position, Action? onYes = null, Action? onNo = null, Action<int, int, string>? onRenderComplete = null, Action<int, int, int, int, string, string>? onAnimationRenderComplete = null)
    {
        _type = type;
        _title = title;
        _message = message;
        _position = position;
        _onYes = onYes;
        _onNo = onNo;
        _onRenderComplete = onRenderComplete;
        _onAnimationRenderComplete = onAnimationRenderComplete;
    }

    public void Render(Vector2 windowSize)
    {
        if (!IsVisible) return;

        // Create the dialog window
        ImGui.SetNextWindowPos(_position);
        
        // Make it modal-like with no close button and always on top
        var flags = ImGuiWindowFlags.NoMove | 
                   ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar;
        
        // For render settings, animation, and progress dialogs, use fixed size instead of auto-resize
        if (_type == DialogType.RenderSettings || _type == DialogType.RenderAnimation || _type == DialogType.RenderProgress)
        {
            ImGui.SetNextWindowSize(new Vector2(400, 0)); // Fixed width, auto height
            flags |= ImGuiWindowFlags.NoResize;
        }
        else
        {
            flags |= ImGuiWindowFlags.AlwaysAutoResize;
        }
                   
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.2f, 0.2f, 0.2f, 1.0f)); // Solid dark background
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 0.0f, 0.0f, 1.0f)); // Red border
        
        if (ImGui.Begin("##Dialog", flags))
        {
            // Adjust position to keep auto-sized dialog fully on screen
            var currentDialogSize = ImGui.GetWindowSize();
            var currentDialogPos = ImGui.GetWindowPos();
            
            var adjustedPos = currentDialogPos;
            if (currentDialogPos.X + currentDialogSize.X > windowSize.X)
                adjustedPos.X = windowSize.X - currentDialogSize.X - 10;
            if (currentDialogPos.Y + currentDialogSize.Y > windowSize.Y)
                adjustedPos.Y = windowSize.Y - currentDialogSize.Y - 10;
            if (adjustedPos.X < 10)
                adjustedPos.X = 10;
            if (adjustedPos.Y < 10)
                adjustedPos.Y = 10;
            
            if (adjustedPos.X != currentDialogPos.X || adjustedPos.Y != currentDialogPos.Y)
                ImGui.SetWindowPos(adjustedPos);
            
            // Store final dialog bounds for outside-click detection
            _dialogPos = ImGui.GetWindowPos();
            _dialogSize = ImGui.GetWindowSize();

            // Render content based on dialog type
            RenderDialogContent();
        }
        ImGui.End();
        ImGui.PopStyleColor(2);
    }

    public bool CheckOutsideClick()
    {
        if (!IsVisible) return false;

        // Render settings, animation, and progress dialogs should not close on outside clicks
        if (_type == DialogType.RenderSettings || _type == DialogType.RenderAnimation || _type == DialogType.RenderProgress) return false;

        // Check for clicks outside dialog bounds (with 5 pixel buffer)
        var mousePos = ImGui.GetMousePos();
        var dialogMinX = _dialogPos.X - 5;
        var dialogMaxX = _dialogPos.X + _dialogSize.X + 5;
        var dialogMinY = _dialogPos.Y - 5;
        var dialogMaxY = _dialogPos.Y + _dialogSize.Y + 5;
        
        // Return true if clicked outside bounds
        return ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
               (mousePos.X < dialogMinX || mousePos.X > dialogMaxX ||
                mousePos.Y < dialogMinY || mousePos.Y > dialogMaxY);
    }

    private void RenderDialogContent()
    {
        switch (_type)
        {
            case DialogType.DeleteConfirmation:
                RenderDeleteConfirmation();
                break;
            case DialogType.SystemMessage:
                RenderSystemMessage();
                break;
            case DialogType.YesNoConfirmation:
                RenderYesNoConfirmation();
                break;
            case DialogType.Information:
                RenderInformation();
                break;
            case DialogType.RenderSettings:
                RenderRenderSettings();
                break;
            case DialogType.RenderAnimation:
                RenderRenderAnimation();
                break;
            case DialogType.RenderProgress:
                RenderRenderProgress();
                break;
        }
    }

    private void RenderDeleteConfirmation()
    {
        ImGui.Text("DELETE OBJECT");
        ImGui.Separator();
        ImGui.Text(_message);
        ImGui.Spacing();
        
        // Center buttons
        float buttonWidth = 80.0f;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float totalWidth = buttonWidth * 2 + spacing;
        float windowWidth = ImGui.GetWindowSize().X;
        ImGui.SetCursorPosX((windowWidth - totalWidth) * 0.5f);
        
        // Yes button with red color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.1f, 0.1f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));
        
        if (ImGui.Button("Yes", new Vector2(buttonWidth, 0)))
        {
            _onYes?.Invoke();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
        ImGui.SameLine();
        
        // No button with gray color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        
        if (ImGui.Button("No", new Vector2(buttonWidth, 0)))
        {
            _onNo?.Invoke();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
    }

    private void RenderSystemMessage()
    {
        ImGui.Text(_title);
        ImGui.Separator();
        ImGui.Text(_message);
        ImGui.Spacing();
        
        float buttonWidth = 60.0f;
        float windowWidth = ImGui.GetWindowSize().X;
        ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);
        
        // OK button with neutral color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        
        if (ImGui.Button("OK", new Vector2(buttonWidth, 0)))
        {
            _onYes?.Invoke();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
    }

    private void RenderYesNoConfirmation()
    {
        ImGui.Text(_title);
        ImGui.Separator();
        ImGui.Text(_message);
        ImGui.Spacing();
        
        float buttonWidth = 80.0f;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float totalWidth = buttonWidth * 2 + spacing;
        float windowWidth = ImGui.GetWindowSize().X;
        ImGui.SetCursorPosX((windowWidth - totalWidth) * 0.5f);
        
        // Yes button with red color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.1f, 0.1f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));
        
        if (ImGui.Button("Yes", new Vector2(buttonWidth, 0)))
        {
            _onYes?.Invoke();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
        ImGui.SameLine();
        
        // No button with gray color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        
        if (ImGui.Button("No", new Vector2(buttonWidth, 0)))
        {
            _onNo?.Invoke();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
    }

    private void RenderInformation()
    {
        ImGui.Text(_title);
        ImGui.Separator();
        ImGui.Text(_message);
        ImGui.Spacing();
        
        float buttonWidth = 60.0f;
        float windowWidth = ImGui.GetWindowSize().X;
        ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);
        
        // OK button with neutral color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        
        if (ImGui.Button("OK", new Vector2(buttonWidth, 0)))
        {
            _onYes?.Invoke();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
    }

    private void RenderRenderSettings()
    {
        var windowWidth = ImGui.GetWindowSize().X;
        
        // Center the title
        var titleText = "RENDER SETTINGS";
        var titleWidth = ImGui.CalcTextSize(titleText).X;
        ImGui.SetCursorPosX((windowWidth - titleWidth) * 0.5f);
        ImGui.Text(titleText);
        ImGui.Separator();
        ImGui.Spacing();
        
        // Center "Image Size:" label
        var labelText = "Image Size:";
        var labelWidth = ImGui.CalcTextSize(labelText).X;
        ImGui.SetCursorPosX((windowWidth - labelWidth) * 0.5f);
        ImGui.Text(labelText);
        
        // Center the dropdown
        var dropdownWidth = 200f;
        ImGui.SetCursorPosX((windowWidth - dropdownWidth) * 0.5f);
        ImGui.SetNextItemWidth(dropdownWidth);
        if (ImGui.Combo("##ImageSize", ref _selectedResolutionIndex, _resolutionOptions, _resolutionOptions.Length))
        {
            // Resolution changed - update aspect ratio if switching to custom
            if (_selectedResolutionIndex == 12) // Custom
            {
                _aspectRatio = (float)_customWidth / _customHeight;
            }
        }
        
        // Show custom resolution controls if "Custom" is selected
        if (_selectedResolutionIndex == 12) // Custom option
        {
            ImGui.Spacing();
            
            // Calculate total width for the width/height controls
            var widthLabelSize = ImGui.CalcTextSize("Width:").X;
            var heightLabelSize = ImGui.CalcTextSize("Height:").X;
            var inputWidth = 80f;
            var itemSpacing = ImGui.GetStyle().ItemSpacing.X;
            var totalControlsWidth = widthLabelSize + itemSpacing + inputWidth + itemSpacing + heightLabelSize + itemSpacing + inputWidth;
            
            // Center the width/height controls
            ImGui.SetCursorPosX((windowWidth - totalControlsWidth) * 0.5f);
            
            // Width spinner
            ImGui.Text("Width:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(inputWidth);
            int oldWidth = _customWidth;
            if (ImGui.InputInt("##Width", ref _customWidth, 1, 10))
            {
                if (_customWidth < 1) _customWidth = 1;
                if (_keepAspectRatio && oldWidth != _customWidth)
                {
                    _customHeight = (int)(_customWidth / _aspectRatio);
                    if (_customHeight < 1) _customHeight = 1;
                }
            }
            
            ImGui.SameLine();
            ImGui.Text("Height:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(inputWidth);
            int oldHeight = _customHeight;
            if (ImGui.InputInt("##Height", ref _customHeight, 1, 10))
            {
                if (_customHeight < 1) _customHeight = 1;
                if (_keepAspectRatio && oldHeight != _customHeight)
                {
                    _customWidth = (int)(_customHeight * _aspectRatio);
                    if (_customWidth < 1) _customWidth = 1;
                }
            }
            
            ImGui.Spacing();
            
            // Center the checkbox
            var checkboxText = "Keep Aspect Ratio";
            var checkboxWidth = ImGui.CalcTextSize(checkboxText).X + ImGui.GetFrameHeight() + itemSpacing;
            ImGui.SetCursorPosX((windowWidth - checkboxWidth) * 0.5f);
            if (ImGui.Checkbox(checkboxText, ref _keepAspectRatio))
            {
                if (_keepAspectRatio)
                {
                    _aspectRatio = (float)_customWidth / _customHeight;
                }
            }
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        // Buttons
        float buttonWidth = 80.0f;
        float buttonSpacing = ImGui.GetStyle().ItemSpacing.X;
        float totalWidth = buttonWidth * 2 + buttonSpacing;
        ImGui.SetCursorPosX((windowWidth - totalWidth) * 0.5f);
        
        // Save button with green color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.1f, 0.6f, 0.1f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.9f, 0.3f, 1.0f));
        
        if (ImGui.Button("Save", new Vector2(buttonWidth, 0)))
        {
            ShowFileSaveDialog();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
        ImGui.SameLine();
        
        // Cancel button with gray color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        
        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
        {
            _onNo?.Invoke();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
    }
    
    private async void ShowFileSaveDialog()
    {
        try
        {
            var selectedPath = await FileDialog.ShowSaveDialogAsync("render");
            
            if (!string.IsNullOrEmpty(selectedPath))
            {
                // Get actual resolution values
                int width, height;
                if (_selectedResolutionIndex == 12) // Custom
                {
                    width = _customWidth;
                    height = _customHeight;
                }
                else
                {
                    // Parse resolution from preset options
                    (width, height) = ParseResolution(_resolutionOptions[_selectedResolutionIndex]);
                }
                
                // Trigger the render operation
                _onRenderComplete?.Invoke(width, height, selectedPath);
                _onYes?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error showing file save dialog: {ex.Message}");
        }
    }

    private (int width, int height) ParseResolution(string resolutionString)
    {
        // Extract resolution from strings like "FHD 1080p 1920x1080" or "Avatar 512x512"
        var parts = resolutionString.Split(' ');
        foreach (var part in parts)
        {
            if (part.Contains('x'))
            {
                var dimensions = part.Split('x');
                if (dimensions.Length == 2 && 
                    int.TryParse(dimensions[0], out int width) && 
                    int.TryParse(dimensions[1], out int height))
                {
                    return (width, height);
                }
            }
        }
        
        // Fallback to 1920x1080 if parsing fails
        return (1920, 1080);
    }

    private void RenderRenderAnimation()
    {
        var windowWidth = ImGui.GetWindowSize().X;
        
        // Center the title
        var titleText = "RENDER ANIMATION";
        var titleWidth = ImGui.CalcTextSize(titleText).X;
        ImGui.SetCursorPosX((windowWidth - titleWidth) * 0.5f);
        ImGui.Text(titleText);
        ImGui.Separator();
        ImGui.Spacing();
        
        // Center "Image Size:" label
        var imageSizeText = "Image Size:";
        var imageSizeWidth = ImGui.CalcTextSize(imageSizeText).X;
        ImGui.SetCursorPosX((windowWidth - imageSizeWidth) * 0.5f);
        ImGui.Text(imageSizeText);
        
        // Center the resolution dropdown
        var dropdownWidth = 200f;
        ImGui.SetCursorPosX((windowWidth - dropdownWidth) * 0.5f);
        ImGui.SetNextItemWidth(dropdownWidth);
        if (ImGui.Combo("##ImageSize", ref _selectedResolutionIndex, _resolutionOptions, _resolutionOptions.Length))
        {
            // Resolution changed - update aspect ratio if switching to custom
            if (_selectedResolutionIndex == 12) // Custom
            {
                _aspectRatio = (float)_customWidth / _customHeight;
            }
        }
        
        // Show custom resolution controls if "Custom" is selected
        if (_selectedResolutionIndex == 12) // Custom option
        {
            ImGui.Spacing();
            
            // Calculate total width for the width/height controls
            var widthLabelSize = ImGui.CalcTextSize("Width:").X;
            var heightLabelSize = ImGui.CalcTextSize("Height:").X;
            var inputWidth = 80f;
            var itemSpacing = ImGui.GetStyle().ItemSpacing.X;
            var totalControlsWidth = widthLabelSize + itemSpacing + inputWidth + itemSpacing + heightLabelSize + itemSpacing + inputWidth;
            
            // Center the width/height controls
            ImGui.SetCursorPosX((windowWidth - totalControlsWidth) * 0.5f);
            
            // Width spinner
            ImGui.Text("Width:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(inputWidth);
            int oldWidth = _customWidth;
            if (ImGui.InputInt("##Width", ref _customWidth, 1, 10))
            {
                if (_customWidth < 1) _customWidth = 1;
                if (_keepAspectRatio && oldWidth != _customWidth)
                {
                    _customHeight = (int)(_customWidth / _aspectRatio);
                    if (_customHeight < 1) _customHeight = 1;
                }
            }
            
            ImGui.SameLine();
            ImGui.Text("Height:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(inputWidth);
            int oldHeight = _customHeight;
            if (ImGui.InputInt("##Height", ref _customHeight, 1, 10))
            {
                if (_customHeight < 1) _customHeight = 1;
                if (_keepAspectRatio && oldHeight != _customHeight)
                {
                    _customWidth = (int)(_customHeight * _aspectRatio);
                    if (_customWidth < 1) _customWidth = 1;
                }
            }
            
            ImGui.Spacing();
            
            // Center the checkbox
            var checkboxText = "Keep Aspect Ratio";
            var checkboxWidth = ImGui.CalcTextSize(checkboxText).X + ImGui.GetFrameHeight() + itemSpacing;
            ImGui.SetCursorPosX((windowWidth - checkboxWidth) * 0.5f);
            if (ImGui.Checkbox(checkboxText, ref _keepAspectRatio))
            {
                if (_keepAspectRatio)
                {
                    _aspectRatio = (float)_customWidth / _customHeight;
                }
            }
        }
        
        ImGui.Spacing();
        
        // Center "Video Format:" label
        var formatText = "Video Format:";
        var formatWidth = ImGui.CalcTextSize(formatText).X;
        ImGui.SetCursorPosX((windowWidth - formatWidth) * 0.5f);
        ImGui.Text(formatText);
        
        // Center the video format dropdown
        ImGui.SetCursorPosX((windowWidth - dropdownWidth) * 0.5f);
        ImGui.SetNextItemWidth(dropdownWidth);
        ImGui.Combo("##VideoFormat", ref _selectedVideoFormatIndex, _videoFormatOptions, _videoFormatOptions.Length);
        
        ImGui.Spacing();
        
        // Center bitrate controls (disabled for PNG sequence)
        bool isPngSequence = _selectedVideoFormatIndex == 3; // PNG Sequence
        if (!isPngSequence)
        {
            var bitrateText = "Bitrate (kbps):";
            var bitrateWidth = ImGui.CalcTextSize(bitrateText).X;
            var bitrateInputWidth = 100f;
            var bitrateSpacing = ImGui.GetStyle().ItemSpacing.X;
            var totalBitrateWidth = bitrateWidth + bitrateSpacing + bitrateInputWidth;
            ImGui.SetCursorPosX((windowWidth - totalBitrateWidth) * 0.5f);
            ImGui.Text(bitrateText);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(bitrateInputWidth);
            if (ImGui.InputInt("##Bitrate", ref _bitrate, 100, 1000))
            {
                if (_bitrate < 100) _bitrate = 100;
                if (_bitrate > 100000) _bitrate = 100000;
            }
            ImGui.Spacing();
        }
        
        ImGui.Spacing();
        
        // Center framerate controls
        var framerateText = "Framerate (fps):";
        var framerateWidth = ImGui.CalcTextSize(framerateText).X;
        var framerateInputWidth = 80f;
        var framerateSpacing = ImGui.GetStyle().ItemSpacing.X;
        var totalFramerateWidth = framerateWidth + framerateSpacing + framerateInputWidth;
        ImGui.SetCursorPosX((windowWidth - totalFramerateWidth) * 0.5f);
        ImGui.Text(framerateText);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(framerateInputWidth);
        if (ImGui.InputInt("##Framerate", ref _framerate, 1, 10))
        {
            if (_framerate < 1) _framerate = 1;
            if (_framerate > 120) _framerate = 120;
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        // Buttons
        float buttonWidth = 80.0f;
        float buttonSpacing = ImGui.GetStyle().ItemSpacing.X;
        float totalWidth = buttonWidth * 2 + buttonSpacing;
        ImGui.SetCursorPosX((windowWidth - totalWidth) * 0.5f);
        
        // Save button with green color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.1f, 0.6f, 0.1f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.3f, 0.9f, 0.3f, 1.0f));
        
        if (ImGui.Button("Save", new Vector2(buttonWidth, 0)))
        {
            ShowAnimationSaveDialog();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
        ImGui.SameLine();
        
        // Cancel button with gray color scheme
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        
        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
        {
            _onNo?.Invoke();
            IsVisible = false;
        }
        
        ImGui.PopStyleColor(3);
    }

    private async void ShowAnimationSaveDialog()
    {
        try
        {
            // Determine file filter and default name based on format
            string filter;
            string defaultName;
            bool isPngSequence = _selectedVideoFormatIndex == 3;
            
            if (isPngSequence)
            {
                filter = "PNG Files (*.png)|*.png";
                defaultName = "frame_001"; // First frame of sequence
            }
            else
            {
                // Set filter and default name based on selected video format
                switch (_selectedVideoFormatIndex)
                {
                    case 0: // MP4
                        filter = "MP4 Files (*.mp4)|*.mp4";
                        defaultName = "animation.mp4";
                        break;
                    case 1: // MOV
                        filter = "MOV Files (*.mov)|*.mov";
                        defaultName = "animation.mov";
                        break;
                    case 2: // WMV
                        filter = "WMV Files (*.wmv)|*.wmv";
                        defaultName = "animation.wmv";
                        break;
                    default:
                        filter = "MP4 Files (*.mp4)|*.mp4|MOV Files (*.mov)|*.mov|WMV Files (*.wmv)|*.wmv";
                        defaultName = "animation.mp4";
                        break;
                }
            }
            
            var selectedPath = await FileDialog.ShowSaveDialogAsync(defaultName, filter);
            
            if (!string.IsNullOrEmpty(selectedPath))
            {
                var selectedFormat = _videoFormatOptions[_selectedVideoFormatIndex];
                
                // Get actual resolution values
                int width, height;
                if (_selectedResolutionIndex == 12) // Custom
                {
                    width = _customWidth;
                    height = _customHeight;
                }
                else
                {
                    // Parse resolution from preset options
                    (width, height) = ParseResolution(_resolutionOptions[_selectedResolutionIndex]);
                }
                
                // Trigger the animation render operation
                _onAnimationRenderComplete?.Invoke(width, height, _framerate, _bitrate, selectedFormat, selectedPath);
                _onYes?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error showing animation save dialog: {ex.Message}");
        }
    }

    public void UpdateFrameProgress(int currentFrame, int totalFrames)
    {
        _currentFrame = currentFrame;
        _totalFrames = totalFrames;
        _progressText = $"Rendering frame {currentFrame}/{totalFrames}";
        _isEncoding = false;
    }

    public void UpdateEncodingProgress(float progress)
    {
        _encodingProgress = progress;
        _progressText = $"Encoding video... {progress:F1}%";
        _isEncoding = true;
    }

    private void RenderRenderProgress()
    {
        var windowWidth = ImGui.GetWindowSize().X;
        
        // Center the title
        var titleText = _isEncoding ? "ENCODING VIDEO" : "RENDERING ANIMATION";
        var titleWidth = ImGui.CalcTextSize(titleText).X;
        ImGui.SetCursorPosX((windowWidth - titleWidth) * 0.5f);
        ImGui.Text(titleText);
        ImGui.Separator();
        ImGui.Spacing();
        
        // Center the progress text
        var progressTextWidth = ImGui.CalcTextSize(_progressText).X;
        ImGui.SetCursorPosX((windowWidth - progressTextWidth) * 0.5f);
        ImGui.Text(_progressText);
        ImGui.Spacing();
        
        // Progress bars
        if (_isEncoding)
        {
            // Encoding progress bar
            var encodingBarWidth = 300f;
            ImGui.SetCursorPosX((windowWidth - encodingBarWidth) * 0.5f);
            ImGui.ProgressBar(_encodingProgress / 100.0f, new Vector2(encodingBarWidth, 0), $"{_encodingProgress:F1}%");
        }
        else
        {
            // Frame rendering progress bar
            var frameProgress = _totalFrames > 0 ? (float)_currentFrame / _totalFrames : 0.0f;
            var frameBarWidth = 300f;
            ImGui.SetCursorPosX((windowWidth - frameBarWidth) * 0.5f);
            ImGui.ProgressBar(frameProgress, new Vector2(frameBarWidth, 0), $"{_currentFrame}/{_totalFrames}");
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        // Cancel button (only show if not encoding - can't safely cancel during FFmpeg)
        if (!_isEncoding)
        {
            float buttonWidth = 80.0f;
            ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);
            
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.1f, 0.1f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));
            
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
            {
                _onNo?.Invoke();
                IsVisible = false;
            }
            
            ImGui.PopStyleColor(3);
        }
    }
}
