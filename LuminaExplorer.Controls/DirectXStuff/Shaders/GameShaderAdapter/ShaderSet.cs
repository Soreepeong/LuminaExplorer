namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

/// <summary>Shaders used for a pass of a shader node.</summary>
/// <remarks>Shaders are owned by <see cref="GameShaderPool"/>.</remarks>
public class ShaderSet {
    public ShaderSet(uint passId, GameVertexShaderSm5 vs, GamePixelShaderSm5 ps)
    {
        this.PassId = passId;
        this.Vs = vs;
        this.Ps = ps;
    }

    public uint PassId { get; }

    public GameVertexShaderSm5 Vs { get; }

    public GamePixelShaderSm5 Ps { get; }
}
