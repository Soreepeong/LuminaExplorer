namespace LuminaExplorer.Controls.DirectXStuff.Shaders.GameShaderAdapter;

public class ShaderSet {
    public ShaderSet(GameVertexShaderSm5 vs, GamePixelShaderSm5 ps) {
        Vs = vs;
        Ps = ps;
    }

    public GameVertexShaderSm5 Vs { get; }

    public GamePixelShaderSm5 Ps { get; }
}
