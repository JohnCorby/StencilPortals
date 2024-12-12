using System.Collections.Generic;
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

	private void RenderCamera(ScriptableRenderContext context, Camera camera)
	{
		var cmd = CommandBufferPool.Get();
		var sampleName = $"render camera {camera}";
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

	/// <summary>
	/// render a portal. null = render initial camera
	/// </summary>
	private void RenderPortal(RenderContext ctx, Portal portal, int currentDepth = 0)
	{
		var sampleName = $"render portal {portal}";
		ctx.cmd.BeginSample(sampleName);

		var valid = ctx.cam.TryGetCullingParameters(out var cullingParameters);
		if (!valid) return;
		var cullingResults = ctx.ctx.Cull(ref cullingParameters);

		ctx.ctx.SetupCameraProperties(ctx.cam);

		DrawGeometry(ctx, cullingResults, true, currentDepth);

		if (currentDepth < 4)
		{
			// DFS traverse of portals
			foreach (var innerPortal in GetInnerPortals(ctx, portal))
			{
				PunchHole(ctx, innerPortal, ref currentDepth);

				SetupCamera(ctx, portal, innerPortal);

				RenderPortal(ctx, innerPortal, currentDepth);

				SetupCamera(ctx, innerPortal, portal);

				UnpunchHole(ctx, innerPortal, ref currentDepth);
			}
		}

		DrawGeometry(ctx, cullingResults, false, currentDepth);

		ctx.cmd.EndSample(sampleName);
	}

	private IEnumerable<Portal> GetInnerPortals(RenderContext ctx, Portal portal)
	{
		return portal ? portal.InnerPortals : Portal.AllPortals;
	}

	/// <summary>
	/// stencil read currentDepth, write currentDepth + 1
	/// writes depth = far
	/// </summary>
	private void PunchHole(RenderContext ctx, Portal portal, ref int currentDepth)
	{
		var sampleName = $"punch hole for {portal}";
		ctx.cmd.BeginSample(sampleName);

		// read and incr
		_asset.PortalPassesMaterial.SetInt("_StencilRef", currentDepth);
		ctx.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 0);
		currentDepth++;

		// ovewrite depth
		_asset.PortalPassesMaterial.SetInt("_StencilRef", currentDepth);
		ctx.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 1);

		ctx.cmd.EndSample(sampleName);
	}

	/// <summary>
	/// stencil read currentDepth, write currentDepth - 1
	/// writes depth = portal quad depth
	/// </summary>
	private void UnpunchHole(RenderContext ctx, Portal portal, ref int currentDepth)
	{
		var sampleName = $"unpunch hole for {portal}";
		ctx.cmd.BeginSample(sampleName);

		// read and decr
		_asset.PortalPassesMaterial.SetInt("_StencilRef", currentDepth);
		ctx.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 2);
		currentDepth--;

		ctx.cmd.EndSample(sampleName);
	}

	/// <summary>
	/// setup camera matrices and viewport
	/// </summary>
	private void SetupCamera(RenderContext ctx, Portal fromPortal, Portal toPortal)
	{
		var sampleName = $"setup camera from {fromPortal} to {toPortal}";
		ctx.cmd.BeginSample(sampleName);

		var p2pMatrix = toPortal.transform.localToWorldMatrix * fromPortal.transform.worldToLocalMatrix;

		var newCamMatrix = p2pMatrix * ctx.cam.transform.localToWorldMatrix;
		ctx.cam.transform.SetPositionAndRotation(
			newCamMatrix.GetPosition(),
			newCamMatrix.rotation
		);

		// TODO: set near plane
		// TODO: confine frustum to portal using viewport etc

		ctx.cmd.EndSample(sampleName);
	}

	private void DrawGeometry(RenderContext ctx, CullingResults cullingResults, bool opaque, int currentDepth)
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
