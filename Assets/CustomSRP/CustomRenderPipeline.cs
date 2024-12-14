using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

/// <summary>
/// based on paper and https://catlikecoding.com/unity/tutorials/custom-srp/custom-render-pipeline/
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
#if UNITY_EDITOR
		Portal.HACK_Validate();
#endif

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
		// apparently we only need to do this once and not per portal
		context.SetupCameraProperties(camera);

		var cmd = CommandBufferPool.Get();
		var sampleName = $"render camera {camera}";
		cmd.BeginSample(sampleName);

		cmd.ClearRenderTarget(true, true, Color.clear);

		var ctx = new RenderContext
		{
			cmd = cmd,
			ctx = context,
			cam = camera,
		};
		RenderPortal(ctx, null);

		// cant render this per portal, it doesnt move for some reason
		cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PreImageEffects));
		cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects));

		cmd.EndSample(sampleName);

		context.ExecuteCommandBuffer(cmd);
		context.Submit();

		CommandBufferPool.Release(cmd);

		// orientation gizmo breaks unless we do this
		camera.ResetProjectionMatrix();
	}

	/// <summary>
	/// render a portal. null = render initial camera
	/// </summary>
	private void RenderPortal(RenderContext ctx, Portal portal, int currentDepth = 0)
	{
		ctx.cam.TryGetCullingParameters(out var cullingParameters);
		var cullingResults = ctx.ctx.Cull(ref cullingParameters);

		DrawGeometry(ctx, cullingResults, true, currentDepth);

		if (currentDepth < _asset.MaxDepth)
		{
			// DFS traverse of portals
			foreach (var innerPortal in GetInnerPortals(ctx, portal))
			{
				// could be moved to start/end of RenderPortal, but the code reads nicer like this
				var sampleName = $"render portal {innerPortal} depth {currentDepth}";
				ctx.cmd.BeginSample(sampleName);

				PunchHole(ctx, innerPortal, ref currentDepth);

				var localToWorld = ctx.cam.transform.localToWorldMatrix;
				var proj = ctx.cam.projectionMatrix;
				var viewport = GetBoundingRectangle(ctx, portal);
				SetupCamera(ctx, innerPortal);

				RenderPortal(ctx, innerPortal, currentDepth);

				UnsetupCamera(ctx, localToWorld, proj, viewport);

				UnpunchHole(ctx, innerPortal, ref currentDepth);

				ctx.cmd.EndSample(sampleName);
			}
		}

		DrawGeometry(ctx, cullingResults, false, currentDepth);

		// cant check stencil without making new skybox material
		// its okay because the correct skybox gets drawn over everything last
		// BUG: gets shifted around by viewport projection
		ctx.cmd.DrawRendererList(ctx.ctx.CreateSkyboxRendererList(ctx.cam));
	}

	private IEnumerable<Portal> GetInnerPortals(RenderContext ctx, Portal portal)
	{
		IEnumerable<Portal> portals = portal ? portal.InnerPortals : Portal.AllPortals;

		// cull based on frustum
		var planes = new Plane[6];
		GeometryUtility.CalculateFrustumPlanes(ctx.cam, planes);
		portals = portals.Where(x => GeometryUtility.TestPlanesAABB(planes, x.Renderer.bounds));

		return portals;
	}

	/// <summary>
	/// stencil read currentDepth, write currentDepth + 1
	/// writes depth = far
	/// </summary>
	private void PunchHole(RenderContext ctx, Portal portal, ref int currentDepth)
	{
		var sampleName = $"punch hole";
		ctx.cmd.BeginSample(sampleName);

		// read stencil and incr
		// reads from depth here so things in front stay in front
		ctx.cmd.SetGlobalInt("_StencilRef", currentDepth);
		ctx.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 0);
		currentDepth++;

		// read stencil and ovewrite depth
		// dont care about depth cuz we check stencil
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

		// read stencil and decr
		// write quad depth
		// dont care about depth cuz we check stencil
		ctx.cmd.SetGlobalInt("_StencilRef", currentDepth);
		ctx.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 2);
		currentDepth--;

		ctx.cmd.EndSample(sampleName);
	}

	public Rect GetBoundingRectangle(RenderContext ctx, Portal portal)
	{
		if (!portal) return new Rect(0, 0, ctx.cam.pixelWidth, ctx.cam.pixelHeight);

		// var screenPoint = ctx.cam.WorldToScreenPoint(portal.transform.position);
		// return new Rect(screenPoint.x - 100, screenPoint.y - 100, 200, 200);

		// bad
		var worldCorners = new[]
		{
			portal.transform.position + portal.transform.up + portal.transform.right,
			portal.transform.position - portal.transform.up - portal.transform.right,
			portal.transform.position - portal.transform.up + portal.transform.right,
			portal.transform.position + portal.transform.up - portal.transform.right,
		};
		var screenCorners = worldCorners.Select(x => ctx.cam.WorldToScreenPoint(x));

		var left = screenCorners.Select(x => x.x).Min();
		var right = screenCorners.Select(x => x.x).Max();
		var bottom = screenCorners.Select(x => x.y).Min();
		var top = screenCorners.Select(x => x.y).Max();

		return new Rect(left, bottom, right - left, top - bottom);
	}

	/// <summary>
	/// setup camera matrices and viewport
	/// </summary>
	private void SetupCamera(RenderContext ctx, Portal portal)
	{
		var fromPortal = portal;
		var toPortal = portal.LinkedPortal;

		var sampleName = $"setup camera from {fromPortal} to {toPortal}";
		ctx.cmd.BeginSample(sampleName);

		// confine frustum to fromPortal
		// https://github.com/MagnusCaligo/Outer_Portals/blob/master/Outer_Portals/PortalController.cs#L143-L157
		{
			var viewport = GetBoundingRectangle(ctx, fromPortal);
			// viewport.x = Mathf.Round(viewport.x);
			// viewport.y = Mathf.Round(viewport.y);
			// viewport.width = Mathf.Clamp(Mathf.Round(viewport.width), 1, ctx.cam.pixelWidth);
			// viewport.height = Mathf.Clamp(Mathf.Round(viewport.height), 1, ctx.cam.pixelHeight);

			//Matrix4x4 m = Locator.GetPlayerCamera().mainCamera.projectionMatrix;
			// ctx.cam.ResetProjectionMatrix();
			Matrix4x4 m = ctx.cam.projectionMatrix;
			// if (cam.rect.size != r.size) NHLogger.Log($"changing {this} rect from {cam.rect} to {r}");
			ctx.cmd.SetViewport(viewport);
			// cam.rect = r;
			// cam.aspect = playerCamera.aspect; // does this need to be set here?

			// this expects 0-1, so divide
			var r = new Rect(viewport.x / ctx.cam.pixelWidth, viewport.y / ctx.cam.pixelHeight, viewport.width / ctx.cam.pixelWidth, viewport.height / ctx.cam.pixelHeight);
			// reverse effects of viewport
			Matrix4x4 m2 = Matrix4x4.TRS(new Vector3((1 / r.width - 1), (1 / r.height - 1), 0), Quaternion.identity, new Vector3(1 / r.width, 1 / r.height, 1));
			Matrix4x4 m3 = Matrix4x4.TRS(new Vector3(-r.x * 2 / r.width, -r.y * 2 / r.height, 0), Quaternion.identity, Vector3.one);
			ctx.cam.projectionMatrix = m3 * m2 * m;
			ctx.cmd.SetProjectionMatrix(ctx.cam.projectionMatrix);
		}

		// move camera to position to toPortal
		{
			var p2pMatrix = toPortal.transform.localToWorldMatrix * Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0)) * fromPortal.transform.worldToLocalMatrix;

			var newCamMatrix = p2pMatrix * ctx.cam.transform.localToWorldMatrix;
			// actually move camera so culling happens. could edit cullingMatrix instead but whatever
			ctx.cam.transform.SetPositionAndRotation(
				newCamMatrix.GetPosition(),
				newCamMatrix.rotation
			);
			ctx.cmd.SetViewMatrix(ctx.cam.worldToCameraMatrix);
		}

		// set near plane to toPortal
		// https://github.com/SebLague/Portals/blob/master/Assets/Scripts/Core/Portal.cs#L250-L272
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
				ctx.cmd.SetProjectionMatrix(ctx.cam.projectionMatrix);
			}
		}

		ctx.cmd.EndSample(sampleName);
	}

	/// <summary>
	/// undo matrices and viewport
	/// </summary>
	private void UnsetupCamera(RenderContext ctx, Matrix4x4 localToWorld, Matrix4x4 proj, Rect viewport)
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
		ctx.cmd.SetViewport(viewport);

		ctx.cmd.EndSample(sampleName);
	}


	private void DrawGeometry(RenderContext ctx, CullingResults cullingResults, bool opaque, int currentDepth)
	{
		var sampleName = $"draw geometry {(opaque ? "opaque" : "transparent")}";
		ctx.cmd.BeginSample(sampleName);

		var rendererListDesc = new RendererListDesc(new ShaderTagId("CustomLit"), cullingResults, ctx.cam)
		{
			sortingCriteria = opaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent,
			renderQueueRange = opaque ? RenderQueueRange.opaque : RenderQueueRange.transparent,
			stateBlock = new RenderStateBlock(RenderStateMask.Stencil)
			{
				stencilState = new StencilState(compareFunction: CompareFunction.Equal),
				stencilReference = currentDepth
			}
		};
		ctx.cmd.DrawRendererList(ctx.ctx.CreateRendererList(rendererListDesc));

		ctx.cmd.EndSample(sampleName);
	}
}
