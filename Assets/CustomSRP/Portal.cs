using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class Portal : MonoBehaviour
{
	public static Portal[] AllPortals { get; private set; }

	public List<Portal> InnerPortals { get; private set; }

	public Renderer Renderer { get; private set; }

	private void Awake()
	{
		Renderer = GetComponent<Renderer>();

		if (AllPortals == null) AllPortals = FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		InnerPortals = AllPortals.ToList();
		InnerPortals.Remove(this);

		Debug.Log($"inner portals = {string.Join(",", InnerPortals)}", this);
	}

	[MenuItem("Tools/Init Portals")]
	public static void InitPortals()
	{
		foreach (var portal in FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
		{
			portal.Awake();
		}
	}
}
