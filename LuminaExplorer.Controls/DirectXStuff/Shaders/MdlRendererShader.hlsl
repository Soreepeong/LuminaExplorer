// see: SaintCoinach.Graphics.Viewer/Effects/HLSL/Structures.fxh
struct Lighting {
	float3 diffuse;
	float3 specular;
};

struct DirectionalLight {
	float3 direction;
	float4 diffuse;
	float4 specular;
};

// https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-semantics

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
	float4 positionPS : SV_POSITION; // [System Value] Vertex position in object space
	float3 positionWS : POSITIONT; // Transformed vertex position.	
	float3 normalWS : NORMAL;
	float4 uv : TEXCOORD0;
	float4 blendWeight : BLENDWEIGHT;
	int4 blendIndices : BLENDINDICES;
	float4 color : COLOR;
	float4 tangent1WS : TEXCOORD5;
	float4 tangent2WS : TEXCOORD6;
};

SamplerState g_diffuseSampler : register(s0);
SamplerState g_normalSampler : register(s1);
SamplerState g_specularSampler : register(s2);
SamplerState g_maskSampler : register(s3);

Texture2D g_diffuseTexture : register(t0);
Texture2D g_normalTexture : register(t1);
Texture2D g_specularTexture : register(t2);
Texture2D g_maskTexture : register(t3);

// see: SaintCoinach.Graphics.Viewer/Effects/HLSL/Common.fxh
cbuffer g_cameraParameters : register(b0) {
	// System.Numerics.Matrix4x4 is in row major.

	// The game defines these as camera parameters.	
	row_major float4x4 g_camera_view;  // 3x4 in game
	row_major float4x4 g_camera_inverseView;  // 4x3 in game
	row_major float4x4 g_camera_viewProjection;
	row_major float4x4 g_camera_inverseViewProjection;
	row_major float4x4 g_camera_projection;
	row_major float4x4 g_camera_inverseProjection;
	row_major float4x4 g_camera_mainViewToProjection;
	float3 g_eyePosition;
	float3 g_lookAtVector;

	// The game defines one world parameter, seemingly in a different register.
	// Not doing that here (for no special reason.)
	row_major float4x4 g_worldView;  // 3x4 in game

	// It's here, just because SaintCoinach.Graphics defines these.
	row_major float4x4 g_world;
	row_major float4x4 g_worldInverseTranspose;
	row_major float4x4 g_worldViewProjection;
}

cbuffer g_lightParameters : register(b1) {
	float4 g_light_diffuseColor : packoffset(c0);
	float3 g_light_emissiveColor : packoffset(c1);
	float3 g_light_ambientColor : packoffset(c2);
	float3 g_light_specularColor : packoffset(c3);
	float g_light_specularPower : packoffset(c3.w);

	DirectionalLight g_light0 : packoffset(c4);
	DirectionalLight g_light1 : packoffset(c7);
	DirectionalLight g_light2 : packoffset(c10);
}

// see: SaintCoinach.Graphics.Viewer/Effects/HLSL/Lightning.fxh
Lighting getLight(float3 pos3D, float3 eyeVector, float3 worldNormal) {
	float3x3 lightDirections = 0;
	float3x3 lightDiffuse = 0;
	float3x3 lightSpecular = 0;
	float3x3 halfVectors = 0;

	// is this transpose?
	[unroll]
	for (int i = 0; i < 3; i++) {
		lightDirections[i] = float3x3(
			g_light0.direction,
			g_light1.direction,
			g_light2.direction)[i];
		lightDiffuse[i] = float3x3(
			g_light0.diffuse.xyz,
			g_light1.diffuse.xyz,
			g_light2.diffuse.xyz)[i];
		lightSpecular[i] = float3x3(
			g_light0.specular.xyz,
			g_light1.specular.xyz,
			g_light2.specular.xyz)[i];
        
		halfVectors[i] = normalize(eyeVector - lightDirections[i]);
	}

	const float3 dotL = mul(-lightDirections, worldNormal);
	const float3 dotH = mul(halfVectors, worldNormal);
    
	const float3 zeroL = step(0, dotL);

	const float3 diffuse  = zeroL * dotL;
	const float3 specular = pow(max(dotH, 0) * zeroL, g_light_specularPower);

	Lighting result;
    
	result.diffuse  = mul(diffuse,  lightDiffuse)  * g_light_diffuseColor.rgb + g_light_emissiveColor;
	result.specular = mul(specular, lightSpecular) * g_light_specularColor;

	return result;
}

// see: SaintCoinach.Graphics.Viewer/Effects/HLSL/Common.fxh
VSOutput main_vs(VSInput input) {
	VSOutput output;

	output.positionPS = mul(input.position, g_worldViewProjection);
	output.positionWS = mul(input.position, g_world).xyz;
	output.normalWS = normalize(mul(float4(input.normal, 1), g_worldInverseTranspose)).xyz;

	output.uv = input.uv;
	output.blendWeight = input.blendWeight;
	output.blendIndices = input.blendIndices;
	output.color = input.color;

	// Going to pretend these are tangents
	float4 t1 = (input.tangent1 - 0.5) * 2.0;
	output.tangent1WS.xyz = normalize(mul(float4(t1.xyz, 1), g_worldInverseTranspose)).xyz;
	output.tangent1WS.w = t1.w;

	float4 t2 = (input.tangent2 - 0.5) * 2.0;
	output.tangent2WS.xyz = normalize(mul(float4(t2.xyz, 1), g_worldInverseTranspose)).xyz;
	output.tangent2WS.w = t2.w;

	return output;
}

float4 main_ps(VSOutput input) : SV_TARGET {
	float4 normal = g_normalTexture.Sample(g_normalSampler, input.uv.xy);
	float4 diffuse = g_diffuseTexture.Sample(g_diffuseSampler, input.uv.xy);
	float4 specular = g_specularTexture.Sample(g_specularSampler, input.uv.xy);

	float a = normal.b;
	clip(a <= 0.5 ? -1 : 1);
	const float3 bump = (normal.xyz - 0.5) * 2.0;

	const float3 binorm = cross(input.normalWS.xyz, input.tangent1WS.xyz);
	const float3 bumpnormal = normalize(bump.x * input.tangent1WS.xyz + bump.y * binorm + bump.z * input.normalWS);

	const float3 eyeVector = normalize(g_eyePosition - input.positionWS);
	const Lighting light = getLight(g_eyePosition, eyeVector, bumpnormal);

	float4 color = float4(diffuse.rgb, a);

	color.rgb *= light.diffuse.rgb;
	color.rgb += light.specular.rgb * specular.rgb * color.a;
	return color;
}
