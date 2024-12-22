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
	public List<Portal> InnerPortals { get; private set; }

	public Renderer Renderer { get; private set; }
	public Portal LinkedPortal;

	private void OnEnable()
	{
		if (AllPortals == null) AllPortals = new();
		AllPortals.Add(this);
		if (Renderer == null) Renderer = GetComponent<Renderer>();

		// just rebuild for now
		InnerPortals = AllPortals.ToList();
		InnerPortals.Remove(LinkedPortal);
	}

	private void OnDisable()
	{
		if (AllPortals == null) AllPortals = new();
		AllPortals.Remove(this);

		// just rebuild for now
		InnerPortals = AllPortals.ToList();
		InnerPortals.Remove(LinkedPortal);
	}

#if UNITY_EDITOR
	[MenuItem("Portals/Screenshot")]
	public static void Screenshot()
	{
		ScreenCapture.CaptureScreenshot("cool image.png");
	}
#endif
}
