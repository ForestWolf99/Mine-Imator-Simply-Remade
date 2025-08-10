using ImGuiNET;
using System.Numerics;
using Misr.Rendering;
using Misr.Core;

namespace Misr.UI;

public class PropertiesPanel : IDisposable
{
    // Scene objects and selection
    public List<SceneObject> SceneObjects { get; set; } = new List<SceneObject>();
    public int SelectedObjectIndex { get; set; } = -1;
    
    // Current selected object helper
    public SceneObject? SelectedObject => 
        SelectedObjectIndex >= 0 && SelectedObjectIndex < SceneObjects.Count 
            ? SceneObjects[SelectedObjectIndex] 
            : null;
    private Vector3 _lastObjectPosition = Vector3.Zero;
    
    // Legacy properties for backwards compatibility - these will delegate to SelectedObject
    public Vector3 ObjectPosition 
    { 
        get => SelectedObject?.Position ?? Vector3.Zero; 
        set { if (SelectedObject != null) SelectedObject.Position = value; }
    }
    public Vector3 ObjectRotation 
    { 
        get => SelectedObject?.Rotation ?? Vector3.Zero; 
        set { if (SelectedObject != null) SelectedObject.Rotation = value; }
    }
    public Vector3 ObjectScale 
    { 
        get => SelectedObject?.Scale ?? Vector3.One; 
        set { if (SelectedObject != null) SelectedObject.Scale = value; }
    }
    
    // References to external components
    private Viewport3D? _viewport3D;
    private Timeline? _timeline;
    
    public PropertiesPanel()
    {
    }
    
    public void SetViewport3D(Viewport3D viewport3D)
    {
        _viewport3D = viewport3D;
    }
    
    public void SetTimeline(Timeline timeline)
    {
        _timeline = timeline;
    }
    
    public void Render(Vector2 windowSize)
    {
        // Properties Panel (Blue - Right side, 280px wide, 2/3 of height below scene tree)
        var sceneTreeHeight = windowSize.Y / 3.0f;
        var propertiesHeight = windowSize.Y - sceneTreeHeight;
        
        ImGui.SetNextWindowPos(new Vector2(windowSize.X - 280, sceneTreeHeight));
        ImGui.SetNextWindowSize(new Vector2(280, propertiesHeight));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.2f, 0.4f, 0.8f, 1.0f));
        
        if (ImGui.Begin("Properties", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse))
        {
            RenderObjectProperties();
            RenderTransformControls();
            RenderOptionsControls();
        }
        ImGui.End();
        ImGui.PopStyleColor();
    }
    
    private void RenderObjectProperties()
    {
        
        // Object name
        if (SelectedObject != null)
        {
            ImGui.Text("Name");
            var name = SelectedObject.Name;
            if (ImGui.InputText("##Name", ref name, 256))
            {
                SelectedObject.Name = name;
            }
            
            // Object type/mesh status
            ImGui.Text($"Mesh: {(SelectedObject.HasMesh ? "Yes" : "None")}");
            ImGui.Separator();
        }
        else
        {
            ImGui.Text(SceneObjects.Count == 0 ? "No objects in scene" : "No object selected");
            ImGui.Separator();
            return; // Don't show transform controls if no object
        }
    }
    
    private void RenderTransformControls()
    {
        ImGui.Separator();
        ImGui.Text("Position");
        
        // Store previous values for change detection
        var previousPos = ObjectPosition;
        var previousRot = ObjectRotation;
        var previousScale = ObjectScale;
        
        // Position spinboxes - display values multiplied by 16
        var scaledPos = ObjectPosition * 16;
        if (ImGui.DragFloat("X", ref scaledPos.X, 0.1f))
        {
            ObjectPosition = scaledPos / 16;
            if (_timeline != null && Math.Abs(ObjectPosition.X - previousPos.X) > 0.001f)
                _timeline.AddKeyframe("position.x", _timeline.CurrentFrame, ObjectPosition.X);
        }
        if (ImGui.DragFloat("Y", ref scaledPos.Y, 0.1f))
        {
            ObjectPosition = scaledPos / 16;
            if (_timeline != null && Math.Abs(ObjectPosition.Y - previousPos.Y) > 0.001f)
                _timeline.AddKeyframe("position.y", _timeline.CurrentFrame, ObjectPosition.Y);
        }
        if (ImGui.DragFloat("Z", ref scaledPos.Z, 0.1f))
        {
            ObjectPosition = scaledPos / 16;
            if (_timeline != null && Math.Abs(ObjectPosition.Z - previousPos.Z) > 0.001f)
                _timeline.AddKeyframe("position.z", _timeline.CurrentFrame, ObjectPosition.Z);
        }
        
        ImGui.Separator();
        ImGui.Text("Rotation");
        
        // Rotation spinboxes in degrees
        var rotation = ObjectRotation;
        if (ImGui.DragFloat("Pitch (X)", ref rotation.X, 1.0f))
        {
            ObjectRotation = rotation;
            if (_timeline != null && Math.Abs(ObjectRotation.X - previousRot.X) > 0.1f)
                _timeline.AddKeyframe("rotation.x", _timeline.CurrentFrame, ObjectRotation.X);
        }
        if (ImGui.DragFloat("Yaw (Y)", ref rotation.Y, 1.0f))
        {
            ObjectRotation = rotation;
            if (_timeline != null && Math.Abs(ObjectRotation.Y - previousRot.Y) > 0.1f)
                _timeline.AddKeyframe("rotation.y", _timeline.CurrentFrame, ObjectRotation.Y);
        }
        if (ImGui.DragFloat("Roll (Z)", ref rotation.Z, 1.0f))
        {
            ObjectRotation = rotation;
            if (_timeline != null && Math.Abs(ObjectRotation.Z - previousRot.Z) > 0.1f)
                _timeline.AddKeyframe("rotation.z", _timeline.CurrentFrame, ObjectRotation.Z);
        }
        
        ImGui.Separator();
        ImGui.Text("Scale");
        
        // Scale spinboxes
        var scale = ObjectScale;
        if (ImGui.DragFloat("Scale X", ref scale.X, 0.01f))
        {
            ObjectScale = scale;
            if (_timeline != null && Math.Abs(ObjectScale.X - previousScale.X) > 0.01f)
                _timeline.AddKeyframe("scale.x", _timeline.CurrentFrame, ObjectScale.X);
        }
        if (ImGui.DragFloat("Scale Y", ref scale.Y, 0.01f))
        {
            ObjectScale = scale;
            if (_timeline != null && Math.Abs(ObjectScale.Y - previousScale.Y) > 0.01f)
                _timeline.AddKeyframe("scale.y", _timeline.CurrentFrame, ObjectScale.Y);
        }
        if (ImGui.DragFloat("Scale Z", ref scale.Z, 0.01f))
        {
            ObjectScale = scale;
            if (_timeline != null && Math.Abs(ObjectScale.Z - previousScale.Z) > 0.01f)
                _timeline.AddKeyframe("scale.z", _timeline.CurrentFrame, ObjectScale.Z);
        }
        
        // Uniform scale spinbox
        ImGui.Separator();
        var uniformScale = (ObjectScale.X + ObjectScale.Y + ObjectScale.Z) / 3.0f;
        var previousUniformScale = uniformScale;
        if (ImGui.DragFloat("Uniform Scale", ref uniformScale, 0.01f))
        {
            var scaleDelta = uniformScale - previousUniformScale;
            var newScale = new Vector3(ObjectScale.X + scaleDelta, ObjectScale.Y + scaleDelta, ObjectScale.Z + scaleDelta);
            if (_timeline != null && Math.Abs(scaleDelta) > 0.01f)
            {
                _timeline.AddKeyframe("scale.x", _timeline.CurrentFrame, newScale.X);
                _timeline.AddKeyframe("scale.y", _timeline.CurrentFrame, newScale.Y);
                _timeline.AddKeyframe("scale.z", _timeline.CurrentFrame, newScale.Z);
            }
            ObjectScale = newScale;
        }
    }
    
    private void RenderOptionsControls()
    {
        if (_viewport3D == null) return;
        
        ImGui.Separator();
        ImGui.Text("Options");
        var visible = _viewport3D.ObjectVisible;
        if (ImGui.Checkbox("Visible", ref visible))
            _viewport3D.ObjectVisible = visible;
    }
    
    public string GetPropertiesInfo()
    {
        return $"Pos:{ObjectPosition.X * 16:F2},{ObjectPosition.Y * 16:F2},{ObjectPosition.Z * 16:F2} Rot:{ObjectRotation.X:F1}°,{ObjectRotation.Y:F1}°,{ObjectRotation.Z:F1}° Scale:{ObjectScale.X:F2},{ObjectScale.Y:F2},{ObjectScale.Z:F2}";
    }
    
    public void Dispose()
    {
        // No resources to dispose currently
    }
}
