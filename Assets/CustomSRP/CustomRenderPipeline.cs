using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

public class CustomRenderPipeline : RenderPipeline
{
	private readonly CustomRenderPipelineAsset _asset;

	public CustomRenderPipeline(CustomRenderPipelineAsset asset)
	{
		_asset = asset;
	}

	protected override void Render(ScriptableRenderContext context, Camera[] cameras)
	{
		foreach (var camera in cameras)
		{
			RenderCamera(context, camera);
		}
	}

	private void RenderCamera(ScriptableRenderContext context, Camera camera)
	{
		camera.TryGetCullingParameters(out var cullingParameters);
		var cullingResults = context.Cull(ref cullingParameters);

		var cmd = CommandBufferPool.Get();

		cmd.DrawRendererList(context.CreateRendererList(new RendererListDesc(new ShaderTagId("UniversalForward"), cullingResults, camera)));
		cmd.DrawRendererList(context.CreateSkyboxRendererList(camera));

		CommandBufferPool.Release(cmd);
	}
}
