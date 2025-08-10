#version 330 core
in vec3 vertexColor;
in vec2 texCoord;
out vec4 FragColor;

uniform sampler2D terrainTexture;
uniform bool useTexture;
uniform vec4 tileUVBounds; // x=U1, y=V1, z=U2, w=V2 for the specific tile

void main()
{
    if (useTexture) {
        // Map the UV coordinates to repeat within the tile bounds
        vec2 tiledUV = fract(texCoord); // Get fractional part for tiling
        
        // Remap to tile bounds in atlas
        vec2 atlasUV = tileUVBounds.xy + tiledUV * (tileUVBounds.zw - tileUVBounds.xy);
        
        FragColor = texture(terrainTexture, atlasUV) * vec4(vertexColor, 1.0);
    } else {
        FragColor = vec4(vertexColor, 1.0);
    }
}
