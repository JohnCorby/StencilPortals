using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/CustomRenderPipelineAsset", fileName = "CustomRenderPipelineAsset")]
public class CustomRenderPipelineAsset : RenderPipelineAsset<CustomRenderPipeline>
{
	public Material PortalPassesMaterial;
	[Range(0, 10)]
	public int MaxDepth = 1;
	public Material PostProcessMaterial;
	public float FogColorMultipler = 1;
	public ShadowSettings ShadowSettings;

	protected override RenderPipeline CreatePipeline()
	{
		return new CustomRenderPipeline(this);
	}
}

[Serializable]
public class ShadowSettings
{
	public float MaxDistance = 10;
	public int AtlasSize = 4096;
}
