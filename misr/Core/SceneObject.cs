using System.Numerics;

namespace Misr.Core;

public class SceneObject
{
    public string Name { get; set; } = "Empty";
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 Rotation { get; set; } = Vector3.Zero; // Euler angles in degrees
    public Vector3 Scale { get; set; } = Vector3.One; // Scale factors (1.0 = normal size)
    public bool HasMesh { get; set; } = false;
    
    // Keyframe data - using Dictionary to store frame -> value pairs
    public Dictionary<int, float> PosXKeyframes { get; set; } = new Dictionary<int, float>();
    public Dictionary<int, float> PosYKeyframes { get; set; } = new Dictionary<int, float>();
    public Dictionary<int, float> PosZKeyframes { get; set; } = new Dictionary<int, float>();
    public Dictionary<int, float> RotXKeyframes { get; set; } = new Dictionary<int, float>();
    public Dictionary<int, float> RotYKeyframes { get; set; } = new Dictionary<int, float>();
    public Dictionary<int, float> RotZKeyframes { get; set; } = new Dictionary<int, float>();
    public Dictionary<int, float> ScaleXKeyframes { get; set; } = new Dictionary<int, float>();
    public Dictionary<int, float> ScaleYKeyframes { get; set; } = new Dictionary<int, float>();
    public Dictionary<int, float> ScaleZKeyframes { get; set; } = new Dictionary<int, float>();
    
    public SceneObject()
    {
    }
    
    public SceneObject(string name)
    {
        Name = name;
    }
    
    /// <summary>
    /// Creates a scene object with a cube mesh
    /// </summary>
    /// <param name="name">Name of the object (defaults to "Cube")</param>
    /// <returns>A scene object with cube mesh enabled</returns>
    public static SceneObject CreateCube(string name = "Cube")
    {
        return new SceneObject(name)
        {
            HasMesh = true
        };
    }
}
