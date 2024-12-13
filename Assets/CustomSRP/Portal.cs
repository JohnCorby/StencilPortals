using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class Portal : MonoBehaviour
{
	public static Portal[] AllPortals { get; private set; }

	/// <summary>
	/// PVS of portals this portal can see
	/// </summary>
	public List<Portal> InnerPortals { get; private set; }

	public Renderer Renderer { get; private set; }
	public Portal LinkedPortal;

	private void Awake()
	{
		Renderer = GetComponent<Renderer>();

		if (AllPortals == null) AllPortals = FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		InnerPortals = AllPortals.ToList();
		InnerPortals.Remove(LinkedPortal);

		Debug.Log($"inner portals = {string.Join(",", InnerPortals)}", this);
	}


	[MenuItem("Portals/(DEBUG) Rebuild")]
	public static void DEBUG_Rebuild()
	{
		AllPortals = FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		foreach (var portal in AllPortals)
		{
			portal.Renderer = portal.GetComponent<Renderer>();
			portal.InnerPortals = AllPortals.ToList();
			portal.InnerPortals.Remove(portal.LinkedPortal);
		}
	}

	// stupid and should be removed at some point
	public static void HACK_Validate()
	{
		if (AllPortals == null) AllPortals = FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		foreach (var portal in AllPortals)
		{
			if (!portal.Renderer) portal.Renderer = portal.GetComponent<Renderer>();
			if (portal.InnerPortals == null)
			{
				portal.InnerPortals = AllPortals.ToList();
				portal.InnerPortals.Remove(portal.LinkedPortal);
			}
		}
	}
}
