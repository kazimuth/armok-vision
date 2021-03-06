﻿Shader "Lux/Self-Illumin/AlphaTest Bumped Specular" {

Properties {
	_Color ("Diffuse Color", Color) = (1,1,1,1)
	_MainTex ("Base (RGB) Alpha (A)", 2D) = "white" {}
	_SpecTex ("Specular Color (RGB) Roughness (A)", 2D) = "black" {}
	_BumpMap ("Normalmap", 2D) = "bump" {}
	
	_Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
	_Illum ("Illumin (RGB) Alpha (A)", 2D) = "black" {}
	_EmissionLM ("Emission (Lightmapper)", Float) = 1
	
	
	_DiffCubeIBL ("Custom Diffuse Cube", Cube) = "black" {}
	_SpecCubeIBL ("Custom Specular Cube", Cube) = "black" {}
	
	// _Shininess property is needed by the lightmapper - otherwise it throws errors
	[HideInInspector] _Shininess ("Shininess (only for Lightmapper)", Float) = 0.5
	[HideInInspector] _AO ("Ambient Occlusion Alpha (A)", 2D) = "white" {}
}

SubShader { 
	Tags {"Queue"="AlphaTest" "IgnoreProjector"="True" "RenderType"="TransparentCutout"}
	LOD 400
	
	CGPROGRAM
	#pragma surface surf LuxDirect noambient alphatest:_Cutoff
	#pragma glsl
	#pragma target 3.0

	#pragma multi_compile LUX_LIGHTING_BP LUX_LIGHTING_CT
	#pragma multi_compile LUX_LINEAR LUX_GAMMA
	#pragma multi_compile DIFFCUBE_ON DIFFCUBE_OFF
	#pragma multi_compile SPECCUBE_ON SPECCUBE_OFF
	#pragma multi_compile LUX_AO_OFF LUX_AO_ON

// #define LUX_LIGHTING_CT
// #define LUX_GAMMA
// #define DIFFCUBE_ON
// #define SPECCUBE_ON
// #define LUX_AO_ON

	// include should be called after all defines
	#include "../LuxCore/LuxLightingDirect.cginc"
	
	float4 _Color;
	sampler2D _MainTex;
	sampler2D _SpecTex;
	sampler2D _BumpMap;
	sampler2D _Illum;
	float _EmissionLM;
	#ifdef DIFFCUBE_ON
		samplerCUBE _DiffCubeIBL;
	#endif
	#ifdef SPECCUBE_ON
		samplerCUBE _SpecCubeIBL;
	#endif
	#ifdef LUX_AO_ON
		sampler2D _AO;
	#endif
	
	// Is set by script
	float4 ExposureIBL;

	struct Input {
		float2 uv_MainTex;
		float2 uv_BumpMap;
		#ifdef LUX_AO_ON
			float2 uv_AO;
		#endif
		float3 viewDir;
		float3 worldNormal;
		float3 worldRefl;
		INTERNAL_DATA
	};
	

	void surf (Input IN, inout SurfaceOutputLux o) {
		fixed4 diff_albedo = tex2D(_MainTex, IN.uv_MainTex);
		fixed4 spec_albedo = tex2D(_SpecTex, IN.uv_MainTex);
		fixed4 illumination = tex2D(_Illum, IN.uv_MainTex);
		// Diffuse Albedo
		o.Albedo = diff_albedo.rgb * _Color.rgb;
		o.Alpha = diff_albedo.a * _Color.a;
		o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
		// Specular Color
		o.SpecularColor = spec_albedo.rgb;
		// Roughness – gamma for BlinnPhong / linear for CookTorrence
		o.Specular = LuxAdjustSpecular(spec_albedo.a);
	
		#include "../LuxCore/LuxLightingAmbient.cginc"
		
		o.Emission += illumination.rgb * _EmissionLM * illumination.a;
		// should we use diff albedo here? * diff_albedo.rgb;
		// this would need the illumination color to be stored in diff_albedo -> so illum tex would only be alpha...
		
	}
ENDCG
}
FallBack "Transparent/Cutout/VertexLit"
CustomEditor "LuxMaterialInspector"
}
