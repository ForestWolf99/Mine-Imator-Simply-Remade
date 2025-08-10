using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Misr.Core;

namespace Misr.UI
{
    public class SceneTreePanel
    {
        public List<SceneObject> SceneObjects { get; set; } = new();
        public int SelectedObjectIndex { get; set; } = -1;
        
        public SceneObject? SelectedObject => SelectedObjectIndex >= 0 && SelectedObjectIndex < SceneObjects.Count ? SceneObjects[SelectedObjectIndex] : null;

        public void Render(Vector2 windowSize)
        {
            var panelHeight = windowSize.Y / 3.0f; // Take up 1/3 of vertical space
            
            ImGui.SetNextWindowPos(new Vector2(windowSize.X - 280, 0));
            ImGui.SetNextWindowSize(new Vector2(280, panelHeight));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.2f, 0.2f, 0.25f, 1.0f));
            
            if (ImGui.Begin("Scene Tree", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse))
            {
                ImGui.Text("Scene Objects:");
                ImGui.Separator();
                
                // Show all objects in the scene
                for (int i = 0; i < SceneObjects.Count; i++)
                {
                    var obj = SceneObjects[i];
                    bool isSelected = i == SelectedObjectIndex;
                    
                    // Use selectable for each object
                    if (ImGui.Selectable($"{obj.Name}##{i}", isSelected))
                    {
                        SelectedObjectIndex = i;
                    }
                    
                    // Optional: Add some visual indication of object type
                    if (obj.HasMesh)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), "[Mesh]");
                    }
                }
                
                if (SceneObjects.Count == 0)
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "No objects in scene");
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Press F7 to spawn a cube");
                }
            }
            
            ImGui.End();
            ImGui.PopStyleColor();
        }
    }
}
