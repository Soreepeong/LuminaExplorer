SamplerState g_sampler : register(s0);

Texture2D g_texture : register(t0);

cbuffer g_parameters : register(b0) {
	float g_rotation;
	float g_transparencyCellSize;
	float2 g_pan;
	float2 g_effectiveSize;
	float2 g_clientSize;
	float4 g_cellRectScale;
	float4 g_transparencyCellColor1;
	float4 g_transparencyCellColor2;
	float4 g_pixelGridColor;
	float2 g_cellSourceSize;
	int g_channelFilter;
	bool g_useAlphaChannel;
}

struct VSInput {
	float2 xy : POSITION0;
	float2 uv : TEXCOORD;
};

struct VSOutput {
	float4 position : SV_POSITION;
	float2 uv : TEXCOORD;
};

VSOutput main_vs(VSInput input) {
	VSOutput output;

	const float2 pos = g_cellRectScale.xy + lerp(0, g_cellRectScale.zw, input.xy) - 0.5;
	const float2 scaled = pos * g_effectiveSize;
	float s, c;
	sincos(g_rotation, s, c);
	const float2 rotated = float2(
		scaled.x * c - scaled.y * s,
		scaled.x * s + scaled.y * c);
	const float2 translated = rotated + g_pan;
	const float2 rangeAdjusted = translated * 2 / g_clientSize;
	output.position = float4(rangeAdjusted.x, -rangeAdjusted.y, 0.5, 1);
	output.uv = input.uv;

	return output;
}

float4 blend_colors(float4 bg, float4 fg) {
	const float newA = (1 - fg.w) * bg.w + fg.w;
	return float4(((1 - fg.w) * bg.w * bg.xyz + fg.w * fg.xyz) / newA, newA);
}

float4 main_ps(VSOutput input) : SV_TARGET {
	float4 fg = g_texture.Sample(g_sampler, input.uv);
	float4 color;
	
	if (g_channelFilter == 4) {
		color = float4(fg.w, fg.w, fg.w, 1);
		
	} else if (!g_useAlphaChannel) {
		if (g_channelFilter == 1)
			color = float4(fg.x, fg.x, fg.x, 1);
		else if (g_channelFilter == 2)
			color = float4(fg.y, fg.y, fg.y, 1);
		else if (g_channelFilter == 3)
			color = float4(fg.z, fg.z, fg.z, 1);
		else
			color = float4(fg.xyz, 1);
		
	} else {
		if (g_channelFilter == 1)
			fg.y = fg.z = fg.x;
		else if (g_channelFilter == 2)
			fg.x = fg.z = fg.y;
		else if (g_channelFilter == 3)
			fg.x = fg.y = fg.z;

		const float2 unitOffset = floor((g_cellRectScale.xy + input.uv * g_cellRectScale.zw) * g_effectiveSize / g_transparencyCellSize);
		const float gridColorChoice = (unitOffset.x + unitOffset.y) % 2;
		const float4 bg = lerp(g_transparencyCellColor1, g_transparencyCellColor2, gridColorChoice);
		color = blend_colors(bg, fg);
	}

	const float2 screenPixelSize = 1 / g_effectiveSize / g_cellRectScale.zw;
	const float2 srcPos = input.uv * g_cellSourceSize;
	const float2 diagPos = (input.uv + screenPixelSize) * g_cellSourceSize;
	if ((floor(srcPos.x) != floor(diagPos.x) && input.uv.x + screenPixelSize.x < 1) ||
		(floor(srcPos.y) != floor(diagPos.y) && input.uv.y + screenPixelSize.y < 1)) {
		color = blend_colors(color, g_pixelGridColor);
	}

	return color;
}
