Texture2D GTexture : register(t0);
SamplerState GSampler : register(s0);

cbuffer Tex2DConstantbuffer : register(b0) {
	float rotation;
	float transparencyCellSize;
	float2 pan;
	float2 effectiveSize;
	float2 clientSize;
	float4 cellRectScale;
	float4 transparencyCellColor1;
	float4 transparencyCellColor2;
	int channelFilter;
	bool disableAlphaChannel;
}

struct VertexShaderInput {
	float2 xy : POSITION0;
	float2 uv : TEXCOORD;
};

struct PixelShaderInput {
	float4 pos : SV_POSITION;
	float2 xy : POSITION0;
	float2 uv : TEXCOORD;
};

PixelShaderInput main_vs(VertexShaderInput input) {
	PixelShaderInput vertexShaderOutput;

	const float2 pos = cellRectScale.xy + lerp(0, cellRectScale.zw, input.xy) - 0.5;
	const float2 scaled = pos * effectiveSize;
	float s, c;
	sincos(rotation, s, c);
	const float2 rotated = float2(
		scaled.x * c - scaled.y * s,
		scaled.x * s + scaled.y * c);
	const float2 translated = rotated + pan;
	const float2 rangeAdjusted = translated * 2 / clientSize;
	vertexShaderOutput.pos = float4(rangeAdjusted.x, -rangeAdjusted.y, 0.5, 1);
	vertexShaderOutput.xy = input.xy;
	vertexShaderOutput.uv = input.uv;

	return vertexShaderOutput;
}

float4 main_ps(PixelShaderInput input) : SV_TARGET {
	float4 fg = GTexture.Sample(GSampler, input.uv);
	if (channelFilter == 4) {
		return float4(fg.w, fg.w, fg.w, 1);
	} else if (disableAlphaChannel) {
		if (channelFilter == 1)
			return float4(fg.x, fg.x, fg.x, 1);
		else if (channelFilter == 2)
			return float4(fg.y, fg.y, fg.y, 1);
		else if (channelFilter == 3)
			return float4(fg.z, fg.z, fg.z, 1);
		else
			return float4(fg.xyz, 1);
	} else {
		if (channelFilter == 1)
			fg.y = fg.z = fg.x;
		else if (channelFilter == 2)
			fg.x = fg.z = fg.y;
		else if (channelFilter == 3)
			fg.x = fg.y = fg.z;

		const float2 unitOffset = floor(input.xy * effectiveSize / transparencyCellSize);
		const float gridColorChoice = (unitOffset.x + unitOffset.y) % 2;
		const float4 bg = lerp(transparencyCellColor1, transparencyCellColor2, gridColorChoice);
		const float newA = (1 - fg.w) * bg.w + fg.w;
		return float4(((1 - fg.w) * bg.w * bg.xyz + fg.w * fg.xyz) / newA, newA);
	}
}
