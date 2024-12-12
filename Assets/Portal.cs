using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
public class Portal : MonoBehaviour
{
	public static Portal[] AllPortals { get; private set; }

	public List<Portal> InnerPortals { get; private set; }

	private void Awake()
	{
		if (AllPortals == null) AllPortals = FindObjectsByType<Portal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
		InnerPortals = AllPortals.ToList();
		InnerPortals.Remove(this);
	}
}
