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
		var sampleName = $"render camera {camera.name}";
		cmd.BeginSample(sampleName);

		// cmd.ClearRenderTarget(true, true, Color.clear);

		var ctx = new RenderContext
		{
			cmd = cmd,
			ctx = context,
			cam = camera,
		};
		RenderPortal(ctx, null);

		// just shove this at the end for now
		cmd.DrawRendererList(context.CreateSkyboxRendererList(camera));
		cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PreImageEffects));
		cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects));

		cmd.EndSample(sampleName);

		context.ExecuteCommandBuffer(cmd);
		context.Submit();

		CommandBufferPool.Release(cmd);
	}

	private static void RenderPortal(RenderContext ctx, Portal portal, int currentDepth = 0)
	{
		if (currentDepth != 0)
		{
			PunchHole(ctx, portal, currentDepth);
			currentDepth++;

			SetupCamera(ctx, portal);
		}

		ctx.cam.TryGetCullingParameters(out var cullingParameters);
		var cullingResults = ctx.ctx.Cull(ref cullingParameters);
		ctx.ctx.SetupCameraProperties(ctx.cam);

		DrawGeometry(ctx, cullingResults, true, currentDepth);

		if (currentDepth <= 3 && false)
		{
			// DFS traverse of portals
			foreach (var innerPortal in portal.InnerPortals)
			{
				RenderPortal(ctx, innerPortal, currentDepth);
			}
		}

		DrawGeometry(ctx, cullingResults, false, currentDepth);

		if (currentDepth != 0)
		{
			UnpunchHole(ctx, currentDepth);
			currentDepth--;

			SetupCamera(ctx, portal);
		}
	}

	/// <summary>
	/// stencil read currentDepth, write currentDepth + 1
	/// writes depth = far
	/// </summary>
	private static void PunchHole(RenderContext ctx, Portal portal, int currentDepth)
	{
		// render portal with stencil ref = currentDepth and incr
		// ctx.cmd.DrawRenderer(portal.Renderer, portal.Renderer.sharedMaterial);

		// stencil ref = currentDepth + 1, ovewrite depth
		// right now we just have this happen with a queue
	}

	/// <summary>
	/// stencil read currentDepth, write currentDepth - 1
	/// writes depth = portal quad depth
	/// </summary>
	private static void UnpunchHole(RenderContext ctx, int currentDepth) { }

	/// <summary>
	/// setup camera matrices and viewport
	/// </summary>
	private static void SetupCamera(RenderContext ctx, Portal portal)
	{
		// ctx.ctx.SetupCameraProperties(ctx.cam);
	}

	private static void DrawGeometry(RenderContext ctx, CullingResults cullingResults, bool opaque, int currentDepth)
	{
		var sampleName = $"draw geometry {(opaque ? "opaque" : "transparent")}";
		ctx.cmd.BeginSample(sampleName);

		var rendererListDesc = new RendererListDesc(new ShaderTagId("SRPDefaultUnlit"), cullingResults, ctx.cam)
		{
			sortingCriteria = opaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent,
			renderQueueRange = opaque ? RenderQueueRange.opaque : RenderQueueRange.transparent,
			stateBlock = new RenderStateBlock(RenderStateMask.Stencil)
			{
				stencilState = new StencilState(compareFunction: CompareFunction.Equal),
				stencilReference = currentDepth
			}
		};
		var rendererList = ctx.ctx.CreateRendererList(rendererListDesc);
		ctx.cmd.DrawRendererList(rendererList);

		ctx.cmd.EndSample(sampleName);
	}
}
