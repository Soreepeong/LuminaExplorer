SamplerState g_diffuseSampler : register(s0);
SamplerState g_normalSampler : register(s0);
SamplerState g_specularSampler : register(s0);
SamplerState g_maskSampler : register(s0);

Texture2D g_diffuseTexture : register(t0);
Texture2D g_normalTexture : register(t1);
Texture2D g_specularTexture : register(t2);
Texture2D g_maskTexture : register(t3);

cbuffer g_cameraParameters : register(b0) {
	// System.Numerics.Matrix4x4 is in row major.
	row_major float4x4 g_viewProjection;
}

struct VSInput {
	float4 position : POSITION;
	float4 blendWeight : BLENDWEIGHT;
	int4 blendIndices : BLENDINDICES;
	float3 normal : NORMAL;
	float4 uv : TEXCOORD;
	float4 tangent2 : TANGENT1;
	float4 tangent1 : TANGENT0;
	float4 color : COLOR;
};

struct VSOutput {
	float4 position : SV_POSITION;
	float4 uv : TEXCOORD;
};

VSOutput main_vs(VSInput input) {
	VSOutput output;
	output.position = mul(input.position, g_viewProjection);
	output.uv = input.uv;
	return output;
}

float4 main_ps(VSOutput input) : SV_TARGET {
	return g_diffuseTexture.Sample(g_diffuseSampler, input.uv);
}
