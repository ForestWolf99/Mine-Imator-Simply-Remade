using ImGuiNET;
using Silk.NET.Windowing;

namespace Misr.UI;

public class MenuBar
{
    private readonly IWindow _window;
    private bool _shouldExit = false;

    public MenuBar(IWindow window)
    {
        _window = window;
    }

    public bool ShouldExit => _shouldExit;

    public void Render()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("New", "Ctrl+N"))
                {
                    // TODO: Implement new project
                }
                if (ImGui.MenuItem("Open", "Ctrl+O"))
                {
                    // TODO: Implement open project
                }
                if (ImGui.MenuItem("Save", "Ctrl+S"))
                {
                    // TODO: Implement save project
                }
                if (ImGui.MenuItem("Save As", "Ctrl+Shift+S"))
                {
                    // TODO: Implement save project as
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Import"))
                {
                    // TODO: Implement import assets
                }
                if (ImGui.MenuItem("Export"))
                {
                    // TODO: Implement export assets
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Exit", "Alt+F4"))
                {
                    _shouldExit = true;
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Undo", "Ctrl+Z"))
                {
                    // TODO: Implement undo
                }
                if (ImGui.MenuItem("Redo", "Ctrl+Y"))
                {
                    // TODO: Implement redo
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Cut", "Ctrl+X"))
                {
                    // TODO: Implement cut
                }
                if (ImGui.MenuItem("Copy", "Ctrl+C"))
                {
                    // TODO: Implement copy
                }
                if (ImGui.MenuItem("Paste", "Ctrl+V"))
                {
                    // TODO: Implement paste
                }
                if (ImGui.MenuItem("Delete", "Del"))
                {
                    // TODO: Implement delete selected object
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Render"))
            {
                if (ImGui.MenuItem("Render Frame", "F12"))
                {
                    // TODO: Implement render current frame
                }
                if (ImGui.MenuItem("Render Animation", "Ctrl+F12"))
                {
                    // TODO: Implement render animation
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Render Settings"))
                {
                    // TODO: Open render settings dialog
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Help"))
            {
                if (ImGui.MenuItem("About"))
                {
                    // TODO: Show about dialog
                }
                if (ImGui.MenuItem("Documentation"))
                {
                    // TODO: Open documentation
                }
                if (ImGui.MenuItem("Shortcuts"))
                {
                    // TODO: Show keyboard shortcuts dialog
                }
                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }
    }
}
