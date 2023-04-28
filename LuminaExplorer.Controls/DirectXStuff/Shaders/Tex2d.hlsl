Texture2D GTexture : register(t0);
SamplerState GSampler : register(s0);

cbuffer Tex2DConstantbuffer : register(b0) {
	float2x2 rotate;
	float2 pan;
	float2 effectiveSize;
	float2 clientSize;
	float4 cellRectScale;
	float4 transparencyCellColor1;
	float4 transparencyCellColor2;
	float transparencyCellSize;
}

struct VertexShaderInput {
	float2 xy : POSITION0;
	float2 uv : TEXCOORD;
};

struct PixelShaderInput {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD;
};

PixelShaderInput main_vs(VertexShaderInput input) {
	PixelShaderInput vertexShaderOutput;

	const float2 xy = mul(input.xy, rotate);
	const float2 rel = 2 * (effectiveSize * (cellRectScale.xy + lerp(0, cellRectScale.zw, xy) - 0.5) + pan) / clientSize;
	vertexShaderOutput.pos = float4(rel.x, -rel.y, 0.5, 1);
	vertexShaderOutput.uv = input.uv;

	return vertexShaderOutput;
}

float4 main_ps(PixelShaderInput input) : SV_TARGET {
	const float gridColorChoice = (
		floor((input.pos.x + pan.x) * clientSize.x / transparencyCellSize) +
		floor((input.pos.y + pan.y) * clientSize.y / transparencyCellSize)
	) % 2;
	const float4 bg = lerp(transparencyCellColor1, transparencyCellColor2, gridColorChoice);
	const float4 fg = GTexture.Sample(GSampler, input.uv);
	float4 newW = float4(0, 0, 0, (1 - fg.w) * bg.w + fg.w);
	newW.xyz = ((1 - fg.w) * bg.w * bg.xyz + fg.w * fg.xyz) / newW.w;
	newW = float4(0.5, 0.5, 0.5, 1);
	return newW;
}
