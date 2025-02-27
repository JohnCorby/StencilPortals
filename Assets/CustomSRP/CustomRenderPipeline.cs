﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
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
		var sampleName = $"render camera \"{camera.name}\"";
		Profiler.BeginSample(sampleName);

		// hacky, but no need to render past fog
		camera.farClipPlane = RenderSettings.fogEndDistance * _asset.EdgeFadeMultiplier;

		// only do this once. inner portals have matrices changed manually
		context.SetupCameraProperties(camera);

		var cmd = CommandBufferPool.Get();
		cmd.BeginSample(sampleName);

		{
			cmd.SetGlobalVector("_FogParams", new Vector4(
				RenderSettings.fogStartDistance, RenderSettings.fogEndDistance,
				RenderSettings.fogStartDistance, RenderSettings.fogEndDistance * _asset.EdgeFadeMultiplier
			));
			cmd.SetGlobalColor("_FogColor", RenderSettings.fogColor * _asset.FogColorMultiplier);
			cmd.SetGlobalVector("_AmbientLightColor", RenderSettings.ambientLight);

			var light = RenderSettings.sun;
			cmd.SetGlobalVector("_DirectionalLightColor", light.color * light.intensity);
			cmd.SetGlobalVector("_DirectionalLightDirection", -light.transform.forward);
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
		cmd.SetRenderTarget(new RenderTargetIdentifier[] { rt0, rt1, rt2 }, rt0);

		cmd.ClearRenderTarget(RTClearFlags.All, new Color[] { RenderSettings.fogColor * _asset.FogColorMultiplier, new Vector4(0, 0, 1), new Vector4(RenderSettings.fogEndDistance * _asset.EdgeFadeMultiplier, 0) });

		var rc = new RenderContext
		{
			cmd = cmd,
			ctx = context,
			cam = camera,
			viewport = new Rect(0, 0, camera.pixelWidth, camera.pixelHeight),
		};
		RenderPortal(rc, null, 0);

#if UNITY_EDITOR
		// cant render this per portal, it doesnt move for some reason
		if (Handles.ShouldRenderGizmos())
		{
			cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PreImageEffects));
			cmd.DrawRendererList(context.CreateGizmoRendererList(camera, GizmoSubset.PostImageEffects));
		}
#endif

		{
			// TL, TR, BL, BR
			// need big z or it distorts. why.
			cmd.SetGlobalVectorArray("_CameraCorners", new Vector4[]
			{
				camera.ViewportToWorldPoint(new Vector3(0, 1, 99999)),
				camera.ViewportToWorldPoint(new Vector3(1, 1, 99999)),
				camera.ViewportToWorldPoint(new Vector3(0, 0, 99999)),
				camera.ViewportToWorldPoint(new Vector3(1, 0, 99999)),
			});
			// blit changes unity matrices so lol
			cmd.SetGlobalMatrix("_ViewMatrix", rc.cam.worldToCameraMatrix);
			cmd.Blit(BuiltinRenderTextureType.None, BuiltinRenderTextureType.CameraTarget, _asset.PostProcessMaterial);
		}
		cmd.ReleaseTemporaryRT(rt0);
		cmd.ReleaseTemporaryRT(rt1);
		cmd.ReleaseTemporaryRT(rt2);

		cmd.EndSample(sampleName);

		context.ExecuteCommandBuffer(cmd);
		CommandBufferPool.Release(cmd);

		context.Submit();

		// orientation gizmo breaks unless we do this
		camera.ResetProjectionMatrix();

		Profiler.EndSample();
	}

	/// <summary>
	/// render a portal. null = render initial camera
	/// </summary>
	private void RenderPortal(RenderContext rc, Portal portal, int currentDepth)
	{
		rc.cam.TryGetCullingParameters(out var cullingParameters);
		cullingParameters.shadowDistance = Mathf.Min(_asset.MaxShadowDistance, RenderSettings.fogEndDistance);
		var cullingResults = rc.ctx.Cull(ref cullingParameters);

		DrawShadows(rc, cullingResults);

		// set target back to main render guy. this really needs to be cleaned up
		var rt0 = Shader.PropertyToID("_ColorBuffer");
		var rt1 = Shader.PropertyToID("_NormalBuffer");
		var rt2 = Shader.PropertyToID("_DistanceBuffer");
		rc.cmd.SetRenderTarget(new RenderTargetIdentifier[] { rt0, rt1, rt2 }, rt0);
		rc.cmd.SetViewport(rc.viewport);

		DrawGeometry(rc, cullingResults, true, currentDepth);

		// release shadow buffer before recursion so we reuse one rt instead of creating a new one each time
		// this means transparent geometry cant read shadows. MG has this limitation too
		rc.cmd.ReleaseTemporaryRT(Shader.PropertyToID("_ShadowBuffer"));

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
				Profiler.BeginSample(sampleName);
				rc.cmd.BeginSample(sampleName);

				PunchHole(rc, innerPortal, ref currentDepth);

				SetupCamera(ref rc, innerPortal);

				RenderPortal(rc, innerPortal, currentDepth);

				UnsetupCamera(ref rc, localToWorld, proj, viewport);

				UnpunchHole(rc, innerPortal, ref currentDepth);

				rc.cmd.EndSample(sampleName);
				Profiler.EndSample();
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
		Profiler.BeginSample(sampleName);
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
		Profiler.EndSample();
	}

	/// <summary>
	/// stencil read currentDepth, write currentDepth - 1
	/// writes depth = portal quad depth
	/// </summary>
	private void UnpunchHole(RenderContext rc, Portal portal, ref int currentDepth)
	{
		var sampleName = $"unpunch hole";
		Profiler.BeginSample(sampleName);
		rc.cmd.BeginSample(sampleName);

		// read stencil and decr
		// write quad depth
		// dont care about depth cuz we check stencil
		rc.cmd.SetGlobalInt("_StencilRef", currentDepth);
		rc.cmd.DrawRenderer(portal.Renderer, _asset.PortalPassesMaterial, 0, 2);
		currentDepth--;

		rc.cmd.EndSample(sampleName);
		Profiler.EndSample();
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
		if (screenCorners.Any(x => x.z < 1))
			return new Rect(0, 0, rc.cam.pixelWidth, rc.cam.pixelHeight);

		return new Rect
		{
			xMin = screenCorners.Min(x => x.x),
			yMin = screenCorners.Min(x => x.y),
			xMax = screenCorners.Max(x => x.x),
			yMax = screenCorners.Max(x => x.y),
		};
	}

	/// <summary>
	/// setup camera matrices and viewport
	/// </summary>
	private void SetupCamera(ref RenderContext rc, Portal portal)
	{
		var fromPortal = portal;
		var toPortal = portal.LinkedPortal;

		var sampleName = $"setup camera from \"{fromPortal.name}\" to \"{toPortal.name}\"";
		Profiler.BeginSample(sampleName);
		rc.cmd.BeginSample(sampleName);

		// confine frustum to fromPortal
		// https://github.com/MagnusCaligo/Outer_Portals/blob/master/Outer_Portals/PortalController.cs#L143-L157
		// TODO use cleaner https://discussions.unity.com/t/scissor-rectangle/404230
		{
			// want to use original proj
			rc.cam.ResetProjectionMatrix();

			var newViewport = GetBoundingRectangle(rc, fromPortal);
			// confine new viewport to old viewport
			// also, viewport expects whole numbers
			newViewport.xMin = (int)Mathf.Max(newViewport.xMin, rc.viewport.xMin);
			newViewport.yMin = (int)Mathf.Max(newViewport.yMin, rc.viewport.yMin);
			newViewport.xMax = (int)Mathf.Min(newViewport.xMax, rc.viewport.xMax);
			newViewport.yMax = (int)Mathf.Min(newViewport.yMax, rc.viewport.yMax);
			rc.viewport = newViewport;
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

			var localToWorld = p2pMatrix * rc.cam.transform.localToWorldMatrix;
			// actually move camera so culling happens. could edit cullingMatrix instead but whatever
			rc.cam.transform.SetPositionAndRotation(localToWorld.GetPosition(), localToWorld.rotation);
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
		Profiler.EndSample();
	}

	/// <summary>
	/// undo matrices and viewport
	/// </summary>
	private void UnsetupCamera(ref RenderContext rc, Matrix4x4 localToWorld, Matrix4x4 proj, Rect viewport)
	{
		var sampleName = $"unsetup camera";
		Profiler.BeginSample(sampleName);
		rc.cmd.BeginSample(sampleName);

		rc.cam.transform.SetPositionAndRotation(localToWorld.GetPosition(), localToWorld.rotation);
		rc.cmd.SetViewMatrix(rc.cam.worldToCameraMatrix);
		rc.cam.projectionMatrix = proj;
		rc.cmd.SetProjectionMatrix(rc.cam.projectionMatrix);
		rc.viewport = viewport;
		rc.cmd.SetViewport(rc.viewport);

		rc.cmd.EndSample(sampleName);
		Profiler.EndSample();
	}


	private void DrawGeometry(RenderContext rc, CullingResults cullingResults, bool opaque, int currentDepth)
	{
		var sampleName = $"draw geometry {(opaque ? "opaque" : "transparent")}";
		Profiler.BeginSample(sampleName);
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
		Profiler.EndSample();
	}

	private void DrawShadows(RenderContext rc, CullingResults cullingResults)
	{
		const int lightIndex = 0;

		if (!cullingResults.GetShadowCasterBounds(lightIndex, out _)) return;

		var sampleName = $"draw shadows";
		Profiler.BeginSample(sampleName);
		rc.cmd.BeginSample(sampleName);

		var shadowRt = Shader.PropertyToID("_ShadowBuffer");

		var atlasSize = _asset.ShadowAtlasSize;
		// TODO only create shadow texture once please
		rc.cmd.GetTemporaryRT(shadowRt, atlasSize, atlasSize, 32, FilterMode.Bilinear, RenderTextureFormat.Shadowmap);
		rc.cmd.SetRenderTarget(shadowRt);
		rc.cmd.ClearRenderTarget(RTClearFlags.All, Color.clear);

		var light = RenderSettings.sun;
		// this still uses old deprecated thing. i dont care, i dont even know if its needed
		var shadowSettings = new ShadowDrawingSettings(cullingResults, lightIndex, BatchCullingProjectionType.Orthographic);
		cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
			lightIndex, 0, 1, Vector3.zero, atlasSize, light.shadowNearPlane,
			out var view, out var proj, out var splitData
		);
		shadowSettings.splitData = splitData;

		{
			var m = proj * view;
			if (SystemInfo.usesReversedZBuffer)
			{
				m.m20 = -m.m20;
				m.m21 = -m.m21;
				m.m22 = -m.m22;
				m.m23 = -m.m23;
			}
			m.m00 = 0.5f * (m.m00 + m.m30);
			m.m01 = 0.5f * (m.m01 + m.m31);
			m.m02 = 0.5f * (m.m02 + m.m32);
			m.m03 = 0.5f * (m.m03 + m.m33);
			m.m10 = 0.5f * (m.m10 + m.m30);
			m.m11 = 0.5f * (m.m11 + m.m31);
			m.m12 = 0.5f * (m.m12 + m.m32);
			m.m13 = 0.5f * (m.m13 + m.m33);
			m.m20 = 0.5f * (m.m20 + m.m30);
			m.m21 = 0.5f * (m.m21 + m.m31);
			m.m22 = 0.5f * (m.m22 + m.m32);
			m.m23 = 0.5f * (m.m23 + m.m33);
			rc.cmd.SetGlobalMatrix("_ShadowMatrix", m);
		}
		rc.cmd.SetViewProjectionMatrices(view, proj);

		rc.cmd.SetGlobalDepthBias(0, light.shadowBias); // doesnt seem to do anything :(
		rc.cmd.DrawRendererList(rc.ctx.CreateShadowRendererList(ref shadowSettings));
		rc.cmd.SetGlobalDepthBias(0, 0);

		rc.cmd.SetViewProjectionMatrices(rc.cam.worldToCameraMatrix, rc.cam.projectionMatrix);

		// cause shadow culling now by submitting
		rc.ctx.ExecuteCommandBuffer(rc.cmd);
		rc.cmd.Clear();
		rc.ctx.Submit();

		rc.cmd.EndSample(sampleName);
		Profiler.EndSample();
	}
}
