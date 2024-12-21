using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
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

	private struct RenderContext
	{
		public CommandBuffer cmd;
		public ScriptableRenderContext ctx;
		public Camera cam;
		public Rect viewport;
	}

	private void RenderCamera(ScriptableRenderContext context, Camera camera)
	{
		RenderSettings.fogColor *= _asset.FogColorMultipler;

		// apparently we only need to do this once and not per portal
		context.SetupCameraProperties(camera);

		var cmd = CommandBufferPool.Get();
		var sampleName = $"render camera \"{camera.name}\"";
		cmd.BeginSample(sampleName);

		{
			cmd.SetGlobalVector("_FogParams", new Vector4(
				RenderSettings.fogStartDistance, RenderSettings.fogEndDistance,
				RenderSettings.fogStartDistance, RenderSettings.fogEndDistance * 2
			));
			cmd.SetGlobalVector("_AmbientLightColor", RenderSettings.ambientLight);

			var light = RenderSettings.sun;
			cmd.SetGlobalVector("_DirectionalLightColor", light.color * light.intensity);
			cmd.SetGlobalVector("_DirectionalLightDirection", -light.transform.forward);
		}

		// temp
		{
			camera.TryGetCullingParameters(out var cullingParameters);
			cullingParameters.shadowDistance = 10;
			var cullingResults = context.Cull(ref cullingParameters);
			DrawShadows(new RenderContext
			{
				cmd = cmd,
				ctx = context,
				cam = camera,
			}, cullingResults);
		}

		var rt0 = Shader.PropertyToID("_ColorBuffer");
		var rt1 = Shader.PropertyToID("_NormalBuffer");
		var rt2 = Shader.PropertyToID("_DistanceBuffer");
		cmd.GetTemporaryRT(rt0, camera.pixelWidth, camera.pixelHeight, 32, FilterMode.Point, GraphicsFormat.R16G16B16A16_SFloat, 8);
		cmd.GetTemporaryRT(rt1, new RenderTextureDescriptor
		{
			width = camera.pixelWidth,
			height = camera.pixelHeight,
			msaaSamples = 8,
			graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
			depthBufferBits = 0,
			dimension = TextureDimension.Tex2D,
			bindMS = true,
		});
		cmd.GetTemporaryRT(rt2, new RenderTextureDescriptor
		{
			width = camera.pixelWidth,
			height = camera.pixelHeight,
			msaaSamples = 8,
			graphicsFormat = GraphicsFormat.R32_SFloat,
			depthBufferBits = 0,
			dimension = TextureDimension.Tex2D,
			bindMS = true,
		});
		cmd.SetRenderTarget(colors: new RenderTargetIdentifier[] { rt0, rt1, rt2 }, depth: rt0);

		cmd.ClearRenderTarget(RTClearFlags.All, new[] { RenderSettings.fogColor, new Color(0, 1, 0, 0), new Color(RenderSettings.fogEndDistance * 2, 0, 0, 0) });

		var rc = new RenderContext
		{
			cmd = cmd,
			ctx = context,
			cam = camera,
			viewport = new Rect(0, 0, camera.pixelWidth, camera.pixelHeight)
		};
		RenderPortal(rc, null, 0);

		cmd.ReleaseTemporaryRT(Shader.PropertyToID("_ShadowBuffer"));

		// cant render this per portal, it doesnt move for some reason
		if (Handles.ShouldRenderGizmos())
		{
			cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PreImageEffects));
			cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects));
		}

		{
			// TL, TR, BL, BR
			cmd.SetGlobalVectorArray("_CameraCorners", new Vector4[]
			{
				camera.ViewportToWorldPoint(new Vector3(0, 1, camera.farClipPlane)),
				camera.ViewportToWorldPoint(new Vector3(1, 1, camera.farClipPlane)),
				camera.ViewportToWorldPoint(new Vector3(0, 0, camera.farClipPlane)),
				camera.ViewportToWorldPoint(new Vector3(1, 0, camera.farClipPlane)),
			});
		}

		cmd.Blit(BuiltinRenderTextureType.None, BuiltinRenderTextureType.CameraTarget, _asset.PostProcessMaterial);
		cmd.ReleaseTemporaryRT(rt0);
		cmd.ReleaseTemporaryRT(rt1);
		cmd.ReleaseTemporaryRT(rt2);

		cmd.EndSample(sampleName);

		context.ExecuteCommandBuffer(cmd);
		context.Submit();

		CommandBufferPool.Release(cmd);

		// orientation gizmo breaks unless we do this
		camera.ResetProjectionMatrix();

		RenderSettings.fogColor /= _asset.FogColorMultipler;
	}

	/// <summary>
	/// render a portal. null = render initial camera
	/// </summary>
	private void RenderPortal(RenderContext rc, Portal portal, int currentDepth)
	{
		rc.cam.TryGetCullingParameters(out var cullingParameters);
		var cullingResults = rc.ctx.Cull(ref cullingParameters);

		DrawGeometry(rc, cullingResults, true, currentDepth);

		if (currentDepth < _asset.MaxDepth)
		{
			// get camera state before changing it
			var localToWorld = rc.cam.transform.localToWorldMatrix;
			var proj = rc.cam.projectionMatrix;
			var viewport = rc.viewport;

			// DFS traverse of portals
			foreach (var innerPortal in GetInnerPortals(rc, portal))
			{
				// could be moved to start/end of RenderPortal, but the code reads nicer like this
				var sampleName = $"render portal \"{innerPortal.name}\" depth {currentDepth}";
				rc.cmd.BeginSample(sampleName);

				PunchHole(rc, innerPortal, ref currentDepth);

				SetupCamera(ref rc, innerPortal);

				RenderPortal(rc, innerPortal, currentDepth);

				UnsetupCamera(ref rc, localToWorld, proj, viewport);

				UnpunchHole(rc, innerPortal, ref currentDepth);

				rc.cmd.EndSample(sampleName);
			}
		}

		DrawGeometry(rc, cullingResults, false, currentDepth);

		// cant check stencil without making new skybox material
		// its okay because the correct skybox gets drawn over everything last
		// BUG: gets shifted around by viewport projection
		// rc.cmd.DrawRendererList(rc.ctx.CreateSkyboxRendererList(rc.cam));
	}

	private IEnumerable<Portal> GetInnerPortals(RenderContext rc, Portal portal)
	{
		IEnumerable<Portal> portals = portal ? portal.InnerPortals : Portal.AllPortals;

		// cull based on direction
		portals = portals.Where(x => Vector3.Dot(x.transform.forward, x.transform.position - rc.cam.transform.position) > 0);
		// cull based on frustum
		var planes = new Plane[6];
		GeometryUtility.CalculateFrustumPlanes(rc.cam, planes);
		portals = portals.Where(x => GeometryUtility.TestPlanesAABB(planes, x.Renderer.bounds));

		return portals;
	}

	/// <summary>
	/// stencil read currentDepth, write currentDepth + 1
	/// writes depth = far
	/// </summary>
	private void PunchHole(RenderContext rc, Portal portal, ref int currentDepth)
	{
		var sampleName = $"punch hole";
		rc.cmd.BeginSample(sampleName);

		// read stencil and incr
		// reads from depth here so things in front stay in front
		rc.cmd.SetGlobalInt("_StencilRef", currentDepth);
		rc.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 0);
		currentDepth++;

		// read stencil and ovewrite depth
		// dont care about depth cuz we check stencil
		rc.cmd.SetGlobalInt("_StencilRef", currentDepth);
		rc.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 1);

		rc.cmd.EndSample(sampleName);
	}

	/// <summary>
	/// stencil read currentDepth, write currentDepth - 1
	/// writes depth = portal quad depth
	/// </summary>
	private void UnpunchHole(RenderContext rc, Portal portal, ref int currentDepth)
	{
		var sampleName = $"unpunch hole";
		rc.cmd.BeginSample(sampleName);

		// read stencil and decr
		// write quad depth
		// dont care about depth cuz we check stencil
		rc.cmd.SetGlobalInt("_StencilRef", currentDepth);
		rc.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 2);
		currentDepth--;

		rc.cmd.EndSample(sampleName);
	}

	/// <summary>
	/// get viewport for bounding rect of portal
	/// </summary>
	private Rect GetBoundingRectangle(RenderContext rc, Portal portal)
	{
		// var screenPoint = ctx.cam.WorldToScreenPoint(portal.transform.position);
		// return new Rect(screenPoint.x - 100, screenPoint.y - 100, 200, 200);

		// bad
		// TODO https://discussions.unity.com/t/draw-bounding-rectangle-screen-space-around-a-game-object-with-a-renderer-world-space/821680/4
		// BUG: breaks when intersecting sometimes lol
		var worldCorners = new[]
		{
			portal.transform.position + (portal.transform.up + portal.transform.right) * 1.5f,
			portal.transform.position + (-portal.transform.up - portal.transform.right) * 1.5f,
			portal.transform.position + (-portal.transform.up + portal.transform.right) * 1.5f,
			portal.transform.position + (portal.transform.up - portal.transform.right) * 1.5f,
		};
		var screenCorners = worldCorners.Select(x => rc.cam.WorldToScreenPoint(x));

		// if any of the corners are behind the camera, just set viewport to full screen lol
		// this is terrible, i dont know why i have to do 1 instead of 0 :(
		if (screenCorners.Any(x => x.z < 1)) return new Rect(0, 0, rc.cam.pixelWidth, rc.cam.pixelHeight);

		// viewport expects whole numbers
		var left = (int)screenCorners.Min(x => x.x);
		var right = (int)screenCorners.Max(x => x.x);
		var bottom = (int)screenCorners.Min(x => x.y);
		var top = (int)screenCorners.Max(x => x.y);

		return new Rect(left, bottom, right - left, top - bottom);
	}

	/// <summary>
	/// setup camera matrices and viewport
	/// </summary>
	private void SetupCamera(ref RenderContext rc, Portal portal)
	{
		var fromPortal = portal;
		var toPortal = portal.LinkedPortal;

		var sampleName = $"setup camera from \"{fromPortal.name}\" to \"{toPortal.name}\"";
		rc.cmd.BeginSample(sampleName);

		// confine frustum to fromPortal
		// https://github.com/MagnusCaligo/Outer_Portals/blob/master/Outer_Portals/PortalController.cs#L143-L157
		// TODO use cleaner https://discussions.unity.com/t/scissor-rectangle/404230
		{
			// want to use original proj
			rc.cam.ResetProjectionMatrix();

			rc.viewport = GetBoundingRectangle(rc, fromPortal);
			rc.cmd.SetViewport(rc.viewport);

			// make matrix to go from original proj to new viewport proj
			// this expects 0-1, so divide
			var r = new Rect(rc.viewport.x / rc.cam.pixelWidth, rc.viewport.y / rc.cam.pixelHeight, rc.viewport.width / rc.cam.pixelWidth, rc.viewport.height / rc.cam.pixelHeight);
			// reverse effects of viewport
			Matrix4x4 m = rc.cam.projectionMatrix;
			Matrix4x4 m2 = Matrix4x4.TRS(new Vector3((1 / r.width - 1), (1 / r.height - 1), 0), Quaternion.identity, new Vector3(1 / r.width, 1 / r.height, 1));
			Matrix4x4 m3 = Matrix4x4.TRS(new Vector3(-r.x * 2 / r.width, -r.y * 2 / r.height, 0), Quaternion.identity, Vector3.one);
			rc.cam.projectionMatrix = m3 * m2 * m;
			rc.cmd.SetProjectionMatrix(rc.cam.projectionMatrix);
		}

		// move camera to position to toPortal
		{
			var p2pMatrix = toPortal.transform.localToWorldMatrix * Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0)) * fromPortal.transform.worldToLocalMatrix;

			var newCamMatrix = p2pMatrix * rc.cam.transform.localToWorldMatrix;
			// actually move camera so culling happens. could edit cullingMatrix instead but whatever
			rc.cam.transform.SetPositionAndRotation(newCamMatrix.GetPosition(), newCamMatrix.rotation);
			rc.cmd.SetViewMatrix(rc.cam.worldToCameraMatrix);
		}

		// set near plane to toPortal
		// https://github.com/SebLague/Portals/blob/master/Assets/Scripts/Core/Portal.cs#L250-L272
		{
			Transform clipPlane = toPortal.transform;
			int dot = Math.Sign(Vector3.Dot(clipPlane.forward, toPortal.transform.position - rc.cam.transform.position));

			Vector3 camSpacePos = rc.cam.worldToCameraMatrix.MultiplyPoint(clipPlane.position);
			Vector3 camSpaceNormal = rc.cam.worldToCameraMatrix.MultiplyVector(clipPlane.forward) * dot;
			float camSpaceDst = -Vector3.Dot(camSpacePos, camSpaceNormal) + 0;

			// Don't use oblique clip plane if very close to portal as it seems this can cause some visual artifacts
			{
				Vector4 clipPlaneCameraSpace = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDst);

				// Update projection based on new clip plane
				// Calculate matrix with player cam so that player camera settings (fov, etc) are used
				rc.cam.projectionMatrix = rc.cam.CalculateObliqueMatrix(clipPlaneCameraSpace);
				rc.cmd.SetProjectionMatrix(rc.cam.projectionMatrix);
			}
		}

		rc.cmd.EndSample(sampleName);
	}

	/// <summary>
	/// undo matrices and viewport
	/// </summary>
	private void UnsetupCamera(ref RenderContext rc, Matrix4x4 localToWorld, Matrix4x4 proj, Rect viewport)
	{
		var sampleName = $"unsetup camera";
		rc.cmd.BeginSample(sampleName);

		rc.cam.transform.SetPositionAndRotation(localToWorld.GetPosition(), localToWorld.rotation);
		rc.cmd.SetViewMatrix(rc.cam.worldToCameraMatrix);
		rc.cam.projectionMatrix = proj;
		rc.cmd.SetProjectionMatrix(rc.cam.projectionMatrix);
		rc.viewport = viewport;
		rc.cmd.SetViewport(rc.viewport);

		rc.cmd.EndSample(sampleName);
	}


	private void DrawGeometry(RenderContext rc, CullingResults cullingResults, bool opaque, int currentDepth)
	{
		var sampleName = $"draw geometry {(opaque ? "opaque" : "transparent")}";
		rc.cmd.BeginSample(sampleName);

		var rendererListDesc = new RendererListDesc(new ShaderTagId("CustomLit"), cullingResults, rc.cam)
		{
			sortingCriteria = opaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent,
			renderQueueRange = opaque ? RenderQueueRange.opaque : RenderQueueRange.transparent,
			stateBlock = new RenderStateBlock(RenderStateMask.Stencil)
			{
				stencilState = new StencilState(compareFunction: CompareFunction.Equal),
				stencilReference = currentDepth
			}
		};
		rc.cmd.DrawRendererList(rc.ctx.CreateRendererList(rendererListDesc));

		rc.cmd.EndSample(sampleName);
	}

	private void DrawShadows(RenderContext rc, CullingResults cullingResults)
	{
		const int lightIndex = 0;

		if (!cullingResults.GetShadowCasterBounds(lightIndex, out _)) return;

		var sampleName = $"draw shadows";
		rc.cmd.BeginSample(sampleName);

		var shadowRt = Shader.PropertyToID("_ShadowBuffer");

		const int atlasSize = 1024;
		rc.cmd.GetTemporaryRT(shadowRt, atlasSize, atlasSize, 32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);
		rc.cmd.SetRenderTarget(shadowRt);
		rc.cmd.ClearRenderTarget(RTClearFlags.All, Color.clear);

		var shadowSettings = new ShadowDrawingSettings(cullingResults, lightIndex, BatchCullingProjectionType.Orthographic);
		cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
			lightIndex, 0, 1, Vector3.zero, atlasSize, 0,
			out var view, out var proj, out var splitData
		);
		shadowSettings.splitData = splitData;

		rc.cmd.SetGlobalMatrix("_ShadowMatrix", proj * view);
		rc.cmd.SetViewProjectionMatrices(view, proj);

		rc.cmd.DrawRendererList(rc.ctx.CreateShadowRendererList(ref shadowSettings));

		rc.cmd.SetViewProjectionMatrices(rc.cam.worldToCameraMatrix, rc.cam.projectionMatrix);

		rc.cmd.EndSample(sampleName);
	}
}
