namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public class ShaderSet {
    public ShaderSet(GameVertexShaderSm5 vs, GamePixelShaderSm5 ps)
    {
        this.Vs = vs;
        this.Ps = ps;
    }

    public GameVertexShaderSm5 Vs { get; }

    public GamePixelShaderSm5 Ps { get; }
}
