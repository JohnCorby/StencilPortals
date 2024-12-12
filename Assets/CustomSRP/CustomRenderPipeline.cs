using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

/// <summary>
/// based on https://docs.google.com/document/d/1LHPYsLO8YfwPQLLgAokjVDdtufuwKa7nnPRX3bM35zA/edit?usp=sharing
/// </summary>
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

	public struct RenderContext
	{
		public CommandBuffer cmd;
		public ScriptableRenderContext ctx;
		public Camera cam;
	}

	private static void RenderCamera(ScriptableRenderContext context, Camera camera)
	{
		var cmd = CommandBufferPool.Get();

		var ctx = new RenderContext
		{
			cmd = cmd,
			ctx = context,
			cam = camera,
		};
		RenderPortal(null, ctx);

		// just shove this at the end for now
		cmd.DrawRendererList(context.CreateSkyboxRendererList(camera));
		cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PreImageEffects));
		cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects));

		context.ExecuteCommandBuffer(cmd);
		context.Submit();

		CommandBufferPool.Release(cmd);
	}

	private static void RenderPortal(Portal portal, RenderContext ctx, int currentDepth = 0)
	{
		PunchHole(ctx, currentDepth);

		SetupCamera(ctx, portal);

		ctx.cam.TryGetCullingParameters(out var cullingParameters);
		var cullingResults = ctx.ctx.Cull(ref cullingParameters);
		ctx.ctx.SetupCameraProperties(ctx.cam);

		DrawGeometry(ctx, cullingResults, true, currentDepth);

		/*
		// DFS traverse of portals
		foreach (var innerPortal in portal.InnerPortals)
		{
			RenderPortal(innerPortal, ctx, currentDepth + 1);
		}
		*/

		UnpunchHole(ctx, currentDepth);

		SetupCamera(ctx, portal);

		DrawGeometry(ctx, cullingResults, false, currentDepth);
	}

	/// <summary>
	/// writes depth = far
	/// increments stencil buffer
	/// </summary>
	private static void PunchHole(RenderContext ctx, int currentDepth) { }

	/// <summary>
	/// writes depth = wall depth
	/// decrements stencil buffer
	/// </summary>
	private static void UnpunchHole(RenderContext ctx, int currentDepth) { }

	/// <summary>
	/// setup camera matrices and viewport
	/// </summary>
	private static void SetupCamera(RenderContext ctx, Portal portal)
	{
		ctx.ctx.SetupCameraProperties(ctx.cam);
	}

	private static void DrawGeometry(RenderContext ctx, CullingResults cullingResults, bool opaque, int currentDepth)
	{
		var sampleName = $"draw geometry {(opaque ? "opaque" : "transparent")}";
		ctx.cmd.BeginSample(sampleName);

		var rendererListDesc = new RendererListDesc(new ShaderTagId("UniversalForward"), cullingResults, ctx.cam)
		{
			sortingCriteria = opaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent,
			renderQueueRange = opaque ? RenderQueueRange.opaque : RenderQueueRange.transparent
		};
		var rendererList = ctx.ctx.CreateRendererList(rendererListDesc);
		ctx.cmd.DrawRendererList(rendererList);

		ctx.cmd.EndSample(sampleName);
	}
}
