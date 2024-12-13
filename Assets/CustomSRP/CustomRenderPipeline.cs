﻿using System.Collections.Generic;
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
		Portal.HACK_Validate();

		foreach (var camera in cameras)
		{
			RenderCamera(context, camera);
			// orientation gizmo breaks unless we do this
			camera.ResetProjectionMatrix();
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
		var sampleName = $"render portal {portal} depth {currentDepth}";
		ctx.cmd.BeginSample(sampleName);

		Matrix4x4 localToWorld = default;
		Matrix4x4 proj = default;
		if (portal)
		{
			PunchHole(ctx, portal, ref currentDepth);

			localToWorld = ctx.cam.transform.localToWorldMatrix;
			proj = ctx.cam.projectionMatrix;
			SetupCamera(ctx, portal);
		}

		CullingResults cullingResults;
		{
			var valid = ctx.cam.TryGetCullingParameters(out var cullingParameters);
			if (!valid) return;
			cullingResults = ctx.ctx.Cull(ref cullingParameters);

			ctx.ctx.SetupCameraProperties(ctx.cam);
		}

		DrawGeometry(ctx, cullingResults, true, currentDepth);

		if (currentDepth < _asset.MaxDepth)
		{
			// DFS traverse of portals
			foreach (var innerPortal in GetInnerPortals(ctx, portal))
			{
				RenderPortal(ctx, innerPortal, currentDepth);
			}
		}

		DrawGeometry(ctx, cullingResults, false, currentDepth);

		if (portal)
		{
			UnsetupCamera(ctx, localToWorld, proj);

			UnpunchHole(ctx, portal, ref currentDepth);
		}

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
		var sampleName = $"punch hole";
		ctx.cmd.BeginSample(sampleName);

		// read and incr
		ctx.cmd.SetGlobalInt("_StencilRef", currentDepth);
		ctx.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 0);
		currentDepth++;

		// ovewrite depth
		ctx.cmd.SetGlobalInt("_StencilRef", currentDepth);
		ctx.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 1);

		ctx.cmd.EndSample(sampleName);
	}

	/// <summary>
	/// stencil read currentDepth, write currentDepth - 1
	/// writes depth = portal quad depth
	/// </summary>
	private void UnpunchHole(RenderContext ctx, Portal portal, ref int currentDepth)
	{
		var sampleName = $"unpunch hole";
		ctx.cmd.BeginSample(sampleName);

		// read and decr
		// write quad depth
		ctx.cmd.SetGlobalInt("_StencilRef", currentDepth);
		ctx.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 2);
		currentDepth--;

		ctx.cmd.EndSample(sampleName);
	}

	/// <summary>
	/// setup camera matrices and viewport
	/// </summary>
	private void SetupCamera(RenderContext ctx, Portal portal)
	{
		// if (!fromPortal || !toPortal) return;

		var fromPortal = portal;
		var toPortal = portal.LinkedPortal;

		var sampleName = $"setup camera from {fromPortal} to {toPortal}";
		ctx.cmd.BeginSample(sampleName);

		{
			var p2pMatrix = toPortal.transform.localToWorldMatrix * Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0)) * fromPortal.transform.worldToLocalMatrix;

			var newCamMatrix = p2pMatrix * ctx.cam.transform.localToWorldMatrix;
			ctx.cam.transform.SetPositionAndRotation(
				newCamMatrix.GetPosition(),
				newCamMatrix.rotation
			);
			ctx.cmd.SetViewMatrix(ctx.cam.worldToCameraMatrix);
		}

		// set near plane
		// stolen from sebastian. rewrite at some point
		{
			Transform clipPlane = toPortal.transform;
			int dot = System.Math.Sign(Vector3.Dot(clipPlane.forward, toPortal.transform.position - ctx.cam.transform.position));

			Vector3 camSpacePos = ctx.cam.worldToCameraMatrix.MultiplyPoint(clipPlane.position);
			Vector3 camSpaceNormal = ctx.cam.worldToCameraMatrix.MultiplyVector(clipPlane.forward) * dot;
			float camSpaceDst = -Vector3.Dot(camSpacePos, camSpaceNormal) + 0;

			// Don't use oblique clip plane if very close to portal as it seems this can cause some visual artifacts
			{
				Vector4 clipPlaneCameraSpace = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDst);

				// Update projection based on new clip plane
				// Calculate matrix with player cam so that player camera settings (fov, etc) are used
				ctx.cam.projectionMatrix = ctx.cam.CalculateObliqueMatrix(clipPlaneCameraSpace);
			}
			ctx.cmd.SetProjectionMatrix(ctx.cam.projectionMatrix);
		}

		// TODO: confine frustum to portal using viewport etc

		ctx.cmd.EndSample(sampleName);
	}

	private void UnsetupCamera(RenderContext ctx, Matrix4x4 localToWorld, Matrix4x4 proj)
	{
		var sampleName = $"unsetup camera";
		ctx.cmd.BeginSample(sampleName);

		ctx.cam.transform.SetPositionAndRotation(
			localToWorld.GetPosition(),
			localToWorld.rotation
		);
		ctx.cmd.SetViewMatrix(ctx.cam.worldToCameraMatrix);
		ctx.cam.projectionMatrix = proj;
		ctx.cmd.SetProjectionMatrix(ctx.cam.projectionMatrix);

		ctx.cmd.EndSample(sampleName);
	}


	private void DrawGeometry(RenderContext ctx, CullingResults cullingResults, bool opaque, int currentDepth)
	{
		var sampleName = $"draw geometry {(opaque ? "opaque" : "transparent")}";
		ctx.cmd.BeginSample(sampleName);

		var rendererListDesc = new RendererListDesc(new ShaderTagId("CustomUnlit"), cullingResults, ctx.cam)
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
