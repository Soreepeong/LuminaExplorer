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

// Note that System.Numerics.Matrix4x4 uses row major.
// HLSL defaults to column major.
// The game also uses row major.

// InputId = 0xF0BAD919u
cbuffer g_CameraParameters : register(b0) {
	row_major float3x4 m_View;
	row_major float3x4 m_InverseView;
	row_major float4x4 m_ViewProjection;
	row_major float4x4 m_InverseViewProjection;
	row_major float4x4 m_Projection;
	row_major float4x4 m_InverseProjection;
	row_major float4x4 m_MainViewToProjection;
	float3 m_EyePosition;
	float3 m_LookAtVector;
}

// InputId = 0x76BB3DC0u
cbuffer m_WorldViewMatrix : register(b1) {
	row_major float3x4 m_WorldView;
}

// InputId = 0x88AA546Au
cbuffer m_JointMatrixArray : register(b2) {
	row_major float3x4 m_JointMatrixArray[64];
}

// Following two are used by shaders from SaintCoinach.
cbuffer g_MiscWorldCameraParameters : register(b3) {
	row_major float4x4 m_World;
	row_major float4x4 m_WorldInverseTranspose;
	row_major float4x4 m_WorldViewProjection;
}

cbuffer g_LightParameters : register(b4) {
	float4 m_DiffuseColor : packoffset(c0);
	float3 m_EmissiveColor : packoffset(c1);
	float3 m_AmbientColor : packoffset(c2);
	float3 m_SpecularColor : packoffset(c3);
	float m_SpecularPower : packoffset(c3.w);

	DirectionalLight g_Light0 : packoffset(c4);
	DirectionalLight g_Light1 : packoffset(c7);
	DirectionalLight g_Light2 : packoffset(c10);
}

// see: SaintCoinach.Graphics.Viewer/Effects/HLSL/Lightning.fxh
Lighting GetLight(float3 pos3D, float3 eyeVector, float3 worldNormal) {
	float3x3 lightDirections = 0;
	float3x3 lightDiffuse = 0;
	float3x3 lightSpecular = 0;
	float3x3 halfVectors = 0;

	// is this transpose?
	[unroll]
	for (int i = 0; i < 3; i++) {
		lightDirections[i] = float3x3(
			g_Light0.direction,
			g_Light1.direction,
			g_Light2.direction)[i];
		lightDiffuse[i] = float3x3(
			g_Light0.diffuse.xyz,
			g_Light1.diffuse.xyz,
			g_Light2.diffuse.xyz)[i];
		lightSpecular[i] = float3x3(
			g_Light0.specular.xyz,
			g_Light1.specular.xyz,
			g_Light2.specular.xyz)[i];
        
		halfVectors[i] = normalize(eyeVector - lightDirections[i]);
	}

	const float3 dotL = mul(-lightDirections, worldNormal);
	const float3 dotH = mul(halfVectors, worldNormal);
    
	const float3 zeroL = step(0, dotL);

	const float3 diffuse  = zeroL * dotL;
	const float3 specular = pow(max(dotH, 0) * zeroL, m_SpecularPower);

	Lighting result;
    
	result.diffuse  = mul(diffuse,  lightDiffuse)  * m_DiffuseColor.rgb + m_EmissiveColor;
	result.specular = mul(specular, lightSpecular) * m_SpecularColor;

	return result;
}

void ApplySkinning(inout VSInput input) {
	float4 pos = 0;
	float3 norm = 0;
	float3 t1 = 0;
	float3 t2 = 0;

	[unroll]
	for(int i = 0; i < 4; i++) {
		const float3x4 joint = m_JointMatrixArray[input.blendIndices[i]];
		const float w = input.blendWeight[i];

		pos.xyz += mul(input.position, joint) * w;
		norm += mul(input.normal, joint) * w;
		t1 += mul(input.tangent1, joint) * w;
		t2 += mul(input.tangent2, joint) * w;
	}

	input.position.xyz = pos.xyz;

	input.normal = normalize(norm);
	input.tangent1.xyz = normalize(t1);
	input.tangent2.xyz = normalize(t2);
}

// see: SaintCoinach.Graphics.Viewer/Effects/HLSL/Common.fxh
VSOutput main_vs(VSInput input) {
	VSOutput output;

	output.positionPS = mul(input.position, m_WorldViewProjection);
	output.positionWS = mul(input.position, m_World).xyz;
	output.normalWS = normalize(mul(float4(input.normal, 1), m_WorldInverseTranspose)).xyz;

	input.tangent1 = (input.tangent1 - 0.5) * 2.0;
	input.tangent2 = (input.tangent2 - 0.5) * 2.0;

	ApplySkinning(input);

	output.uv = input.uv;
	output.blendWeight = input.blendWeight;
	output.blendIndices = input.blendIndices;
	output.color = input.color;

	// Going to pretend these are tangents
	float4 t1 = (input.tangent1 - 0.5) * 2.0;
	output.tangent1WS.xyz = normalize(mul(float4(t1.xyz, 1), m_WorldInverseTranspose)).xyz;
	output.tangent1WS.w = t1.w;

	float4 t2 = (input.tangent2 - 0.5) * 2.0;
	output.tangent2WS.xyz = normalize(mul(float4(t2.xyz, 1), m_WorldInverseTranspose)).xyz;
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

	const float3 eyeVector = normalize(m_EyePosition - input.positionWS);
	const Lighting light = GetLight(m_EyePosition, eyeVector, bumpnormal);

	float4 color = float4(diffuse.rgb, a);

	color.rgb *= light.diffuse.rgb;
	color.rgb += light.specular.rgb * specular.rgb * color.a;
	return color;
}
