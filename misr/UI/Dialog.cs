using ImGuiNET;
using System.Numerics;
using System;

namespace Misr.UI;

public enum DialogType
{
    DeleteConfirmation,
    SystemMessage,
    YesNoConfirmation,
    Information
}

public class Dialog
{
    private readonly DialogType _type;
    private readonly string _title;
    private readonly string _message;
    private readonly Action? _onYes;
    private readonly Action? _onNo;
    private readonly Vector2 _position;
    
    private Vector2 _dialogPos = Vector2.Zero;
    private Vector2 _dialogSize = Vector2.Zero;

    public bool IsVisible { get; set; } = true;

    public Dialog(DialogType type, string title, string message, Vector2 position, Action? onYes = null, Action? onNo = null)
    {
        _type = type;
        _title = title;
        _message = message;
        _position = position;
        _onYes = onYes;
        _onNo = onNo;
    }

    public void Render(Vector2 windowSize)
    {
        if (!IsVisible) return;

        // Create a fullscreen overlay first to make it modal-like
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(windowSize.X, windowSize.Y));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0f, 0.0f, 0.0f, 0.5f)); // Semi-transparent black overlay
        
        if (ImGui.Begin("##DialogOverlay", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | 
                       ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar | 
                       ImGuiWindowFlags.NoScrollbar))
        {
            // This creates a modal-like overlay that covers everything but doesn't block input
            // Input will be handled by the dialog window on top
        }
        ImGui.End();
        ImGui.PopStyleColor();
        
        // Now create the actual dialog on top of the overlay
        ImGui.SetNextWindowPos(_position);
        
        // Make it modal-like with no close button and always on top
        var flags = ImGuiWindowFlags.NoMove | 
                   ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar |
                   ImGuiWindowFlags.AlwaysAutoResize;
                   
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
}
