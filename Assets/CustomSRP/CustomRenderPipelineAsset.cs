using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/CustomRenderPipelineAsset", fileName = "CustomRenderPipelineAsset")]
public class CustomRenderPipelineAsset : RenderPipelineAsset<CustomRenderPipeline>
{
	public Material PortalPassesMaterial;
	public int MaxDepth = 1;
	[Space]
	public Material PostProcessMaterial;
	[Space]
	public float FogColorMultiplier = 1;
	public float EdgeFadeMultiplier = 2;
	[Space]
	public float MaxShadowDistance = 10;
	public int ShadowAtlasSize = 2048;

	protected override RenderPipeline CreatePipeline()
	{
		return new CustomRenderPipeline(this);
	}
}
