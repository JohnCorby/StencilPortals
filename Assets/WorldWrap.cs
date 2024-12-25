using UnityEngine;

public class WorldWrap : MonoBehaviour
{
	public float Distance = 100;
	public int Copies = 1;

	private void Start()
	{
		for (var i = -Copies; i <= Copies; i++)
		{
			for (var j = -Copies; j <= Copies; j++)
			{
				for (var k = -Copies; k <= Copies; k++)
				{
					var offset = transform.forward * i + transform.right * j + transform.up * k;
					if (offset == Vector3.zero) continue;
					DestroyImmediate(Instantiate(this, transform.position + offset * Distance, transform.rotation, transform.parent));
				}
			}
		}
	}
}
