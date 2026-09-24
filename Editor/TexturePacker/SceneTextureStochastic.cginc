// Sampling math from Poi_Stochastic/VRLT_PoiStochastic.poiTemplateCollection.
// Keep these functions in sync with the material shader.
#ifndef THRY_SCENE_STOCHASTIC_INCLUDED
#define THRY_SCENE_STOCHASTIC_INCLUDED
#define glsl_mod(x, y) (((x) - (y) * floor((x) / (y))))
float _StochasticMode, _ThryInspectStochastic;
float _StochasticDeliotHeitzDensity, _StochasticHexGridDensity, _StochasticHexRotationStrength, _StochasticHexFallOffContrast, _StochasticHexFallOffPower;
// Classic Magic Numbers fracsin
	float2 StochasticHash2D2D(float2 s)
	{
		// return frac(sin(glsl_mod(float2(dot(s, float2(127.1, 311.7)), dot(s, float2(269.5, 183.3))), 3.14159)) * 43758.5453);
		return frac(sin(float2(dot(s, float2(127.1, 311.7)), dot(s, float2(269.5, 183.3)))) * 43758.5453);
	}

	// UV Offsets and blend weights
	// UVBW[0...2].xy = UV Offsets
	// UVBW[0...2].z = Blend Weights
	float3x3 DeliotHeitzStochasticUVBW(float2 uv)
	{
		// UV transformed into triangular grid space with UV scaled by approximation of 2*sqrt(3)
		const float2x2 stochasticSkewedGrid = float2x2(1.0, -0.57735027, 0.0, 1.15470054);
		float2 skewUV = mul(stochasticSkewedGrid, uv * 3.4641 * _StochasticDeliotHeitzDensity);

		// Vertex IDs and barycentric coords
		float2 vxID = floor(skewUV);
		float3 bary = float3(frac(skewUV), 0);
		bary.z = 1.0 - bary.x - bary.y;

		float3x3 pos = float3x3(
			float3(vxID, bary.z),
			float3(vxID + float2(0, 1), bary.y),
			float3(vxID + float2(1, 0), bary.x)
		);

		float3x3 neg = float3x3(
			float3(vxID + float2(1, 1), -bary.z),
			float3(vxID + float2(1, 0), 1.0 - bary.y),
			float3(vxID + float2(0, 1), 1.0 - bary.x)
		);

		return (bary.z > 0) ? pos : neg;
	}

	float4 DeliotHeitzSampleTexture(Texture2D tex, SamplerState texSampler, float2 uv, float2 dx, float2 dy)
	{
		// UVBW[0...2].xy = UV Offsets
		// UVBW[0...2].z = Blend Weights
		float3x3 UVBW = DeliotHeitzStochasticUVBW(uv);

		//blend samples with calculated weights
		return mul(tex.SampleGrad(texSampler, uv + StochasticHash2D2D(UVBW[0].xy), dx, dy), UVBW[0].z) +
		mul(tex.SampleGrad(texSampler, uv + StochasticHash2D2D(UVBW[1].xy), dx, dy), UVBW[1].z) +
		mul(tex.SampleGrad(texSampler, uv + StochasticHash2D2D(UVBW[2].xy), dx, dy), UVBW[2].z) ;
	}

	float4 DeliotHeitzSampleTexture(Texture2D tex, SamplerState texSampler, float2 uv)
	{
		float2 dx = ddx(uv), dy = ddy(uv);
		return DeliotHeitzSampleTexture(tex, texSampler, uv, dx, dy);
	}

	// HexTiling: Slower, but histogram-preserving
	// SPDX-License-Idenfitier: MIT
	// Copyright (c) 2022 mmikk
	// https://github.com/mmikk/hextile-demo
	float2 HextileMakeCenUV(float2 vertex)
	{
		// 0.288675 ~= 1/(2*sqrt(3))
		const float2x2 stochasticInverseSkewedGrid = float2x2(1.0, 0.5, 0.0, 1.0 / 1.15470054);
		return mul(stochasticInverseSkewedGrid, vertex) * 0.288675;
	}

	float2x2 HextileLoadRot2x2(float2 idx, float rotStrength)
	{
		float angle = abs(idx.x * idx.y) + abs(idx.x + idx.y) + UNITY_PI;

		// remap to +/-pi
		angle = glsl_mod(angle, 2 * UNITY_PI);
		if (angle < 0)  angle += 2 * UNITY_PI;
		if (angle > UNITY_PI) angle -= 2 * UNITY_PI;

		angle *= rotStrength;

		float cs, si; sincos(angle, si, cs);
		return float2x2(cs, -si, si, cs);
	}

	// UV Offsets and base blend weights
	// UVBWR[0...2].xy = UV Offsets
	// UVBWR[0...2].zw = rotation costh/sinth -> reconstruct rotation matrix with float2x2(UVBWR[n].z, -UVBWR[n].w, UVBWR[n].w, UVBWR[n].z)
	// UVBWR[3].xyz = Blend Weights (w unused) - needs luminance weighting
	float4x4 HextileUVBWR(float2 uv)
	{
		// Create Triangle Grid
		// Skew input space into simplex triangle grid (3.4641 ~= 2*sqrt(3))
		const float2x2 stochasticSkewedGrid = float2x2(1.0, -0.57735027, 0.0, 1.15470054);
		float2 skewedCoord = mul(stochasticSkewedGrid, uv * 3.4641 * _StochasticHexGridDensity);

		float2 baseId = float2(floor(skewedCoord));
		float3 temp = float3(frac(skewedCoord), 0);
		temp.z = 1 - temp.x - temp.y;

		float s = step(0.0, -temp.z);
		float s2 = 2 * s - 1;

		half3 weights = float3(-temp.z * s2, s - temp.y * s2, s - temp.x * s2);

		float2 vertex0 = baseId + float2(s, s);
		float2 vertex1 = baseId + float2(s, 1 - s);
		float2 vertex2 = baseId + float2(1 - s, s);

		float2 cen0 = HextileMakeCenUV(vertex0), cen1 = HextileMakeCenUV(vertex1), cen2 = HextileMakeCenUV(vertex2);
		float2x2 rot0 = float2x2(1, 0, 0, 1), rot1 = float2x2(1, 0, 0, 1), rot2 = float2x2(1, 0, 0, 1);

		if (_StochasticHexRotationStrength > 0)
		{
			rot0 = HextileLoadRot2x2(vertex0, _StochasticHexRotationStrength);
			rot1 = HextileLoadRot2x2(vertex1, _StochasticHexRotationStrength);
			rot2 = HextileLoadRot2x2(vertex2, _StochasticHexRotationStrength);
		}

		return float4x4(
			float4(mul(uv - cen0, rot0) + cen0 + StochasticHash2D2D(vertex0), rot0[0].x, -rot0[0].y),
			float4(mul(uv - cen1, rot1) + cen1 + StochasticHash2D2D(vertex1), rot1[0].x, -rot1[0].y),
			float4(mul(uv - cen2, rot2) + cen2 + StochasticHash2D2D(vertex2), rot2[0].x, -rot2[0].y),
			float4(weights, 0)
		);
	}

	float4 HextileSampleTexture(Texture2D tex, SamplerState texSampler, float2 uv, bool isNormalMap, float2 dUVdx, float2 dUVdy)
	{
		// For some reason doing this instead of just calculating it directly prevents it from \
			// breaking after a certain number of textures use it. I don't understand why yet
		float4x4 UVBWR = HextileUVBWR(uv);

		// 2D Rotation Matrices for dUVdx/dy
		// Not sure if this constant folds during compiling when rot is locked at 0, so force it
		float2x2 rot0 = float2x2(1, 0, 0, 1), rot1 = float2x2(1, 0, 0, 1), rot2 = float2x2(1, 0, 0, 1);

		if (_StochasticHexRotationStrength > 0)
		{
			rot0 = float2x2(UVBWR[0].z, -UVBWR[0].w, UVBWR[0].w, UVBWR[0].z);
			rot1 = float2x2(UVBWR[1].z, -UVBWR[1].w, UVBWR[1].w, UVBWR[1].z);
			rot2 = float2x2(UVBWR[2].z, -UVBWR[2].w, UVBWR[2].w, UVBWR[2].z);
		}

		// Weights
		float3 W = UVBWR[3].xyz;

		// Sample texture
		// float3x4 c = float3x4(
		// 	tex.SampleGrad(texSampler, UVBWR[0].xy, mul(dUVdx, rot0), mul(dUVdy, rot0)),
		// 	tex.SampleGrad(texSampler, UVBWR[1].xy, mul(dUVdx, rot1), mul(dUVdy, rot1)),
		// 	tex.SampleGrad(texSampler, UVBWR[2].xy, mul(dUVdx, rot2), mul(dUVdy, rot2))
		// );

		float4 c0 = tex.SampleGrad(texSampler, UVBWR[0].xy, mul(dUVdx, rot0), mul(dUVdy, rot0));
		float4 c1 = tex.SampleGrad(texSampler, UVBWR[1].xy, mul(dUVdx, rot1), mul(dUVdy, rot1));
		float4 c2 = tex.SampleGrad(texSampler, UVBWR[2].xy, mul(dUVdx, rot2), mul(dUVdy, rot2));

		// Blend samples using luminance
		// This is technically incorrect for normal maps, but produces very similar
		// results to blending using normal map gradients (steepness)
		const float3 Lw = float3(0.299, 0.587, 0.114);
		float3 Dw = float3(dot(c0.xyz, Lw), dot(c1.xyz, Lw), dot(c2.xyz, Lw));

		Dw = lerp(1.0, Dw, _StochasticHexFallOffContrast);
		W = Dw * pow(W, _StochasticHexFallOffPower);
		// In the original hextiling there's a Gain3 step here, but it seems to slow things down \
			// and cause the UVs to break, so I've omitted it. Looks fine without

		W /= (W.x + W.y + W.z);
		return W.x * c0 + W.y * c1 + W.z * c2;
	}

	float4 HextileSampleTexture(Texture2D tex, SamplerState texSampler, float2 uv, bool isNormalMap)
	{
		return HextileSampleTexture(tex, texSampler, uv, isNormalMap, ddx(uv), ddy(uv));
	}

#undef glsl_mod
#endif
