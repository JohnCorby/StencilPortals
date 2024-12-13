using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class Portal : MonoBehaviour
{
	public static Portal[] AllPortals { get; private set; }

	public List<Portal> InnerPortals { get; private set; }

	public Renderer Renderer { get; private set; }
	public Portal LinkedPortal;

	private void Awake()
	{
		Renderer = GetComponent<Renderer>();

		if (AllPortals == null) AllPortals = FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		InnerPortals = AllPortals.ToList();
		InnerPortals.Remove(this);
		// InnerPortals.Remove(LinkedPortal);

		Debug.Log($"inner portals = {string.Join(",", InnerPortals)}", this);
	}


	[MenuItem("Portals/HACK_Validate_Button")]
	public static void HACK_Validate_Button()
	{
		AllPortals = FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		foreach (var portal in AllPortals)
		{
			portal.Renderer = portal.GetComponent<Renderer>();
			portal.InnerPortals = AllPortals.ToList();
			portal.InnerPortals.Remove(portal);
		}
	}

	public static void HACK_Validate()
	{
		if (AllPortals == null) AllPortals = FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		foreach (var portal in AllPortals)
		{
			if (!portal.Renderer) portal.Renderer = portal.GetComponent<Renderer>();
			if (portal.InnerPortals == null)
			{
				portal.InnerPortals = AllPortals.ToList();
				portal.InnerPortals.Remove(portal);
			}
		}
	}
}
