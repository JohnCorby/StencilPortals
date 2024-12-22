using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class Portal : MonoBehaviour
{
	public static List<Portal> AllPortals { get; private set; }

	/// <summary>
	/// PVS of portals this portal can see
	/// </summary>
	public List<Portal> InnerPortals => AllPortals;

	public Renderer Renderer { get; private set; }
	public Portal LinkedPortal;

	private void OnEnable()
	{
		if (AllPortals == null) AllPortals = new();
		AllPortals.Add(this);
		if (Renderer == null) Renderer = GetComponent<Renderer>();
	}

	private void OnDisable()
	{
		if (AllPortals == null) AllPortals = new();
		AllPortals.Remove(this);
	}

#if UNITY_EDITOR
	[MenuItem("Portals/Screenshot")]
	public static void Screenshot()
	{
		ScreenCapture.CaptureScreenshot("cool image.png");
	}
#endif
}
