Texture2D GTexture : register(t0);
SamplerState GSampler : register(s0);

cbuffer Tex2DConstantbuffer : register(b0) {
float2x2 rotate;
float2 imageSize;
float2 pan;
float2 clientSize;
}

struct VertexShaderInput {
	float2 xy : POSITION;
	float2 uv : TEXCOORD;
};

struct PixelShaderInput {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD;
};

PixelShaderInput main_vs(VertexShaderInput input) {
	PixelShaderInput vertexShaderOutput;

	vertexShaderOutput.pos = float4(
		(mul(input.xy * imageSize / 2, rotate) + pan) / clientSize,
		// input.xy + pan / clientSize,
		0.5,
		1);
	vertexShaderOutput.uv = input.uv;

	return vertexShaderOutput;
}

float4 main_ps(PixelShaderInput input) : SV_TARGET {
	return GTexture.Sample(GSampler, input.uv);
}
