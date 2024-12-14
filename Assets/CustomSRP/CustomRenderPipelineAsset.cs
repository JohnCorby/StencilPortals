using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/CustomRenderPipelineAsset", fileName = "CustomRenderPipelineAsset")]
public class CustomRenderPipelineAsset : RenderPipelineAsset<CustomRenderPipeline>
{
	public Material PortalPassesMaterial;
	[Range(0, 10)]
	public int MaxDepth = 1;

	protected override RenderPipeline CreatePipeline()
	{
		return new CustomRenderPipeline(this);
	}
}
