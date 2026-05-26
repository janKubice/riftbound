using System.Linq;
using System.Text;
using UnityEngine;

public class PhysicsSceneAudit : MonoBehaviour
{
    [ContextMenu("Print Physics Audit")]
    private void PrintPhysicsAudit()
    {
#if UNITY_2022_2_OR_NEWER
        Collider[] colliders = FindObjectsByType<Collider>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        Rigidbody[] rigidbodies = FindObjectsByType<Rigidbody>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
#else
        Collider[] colliders = FindObjectsOfType<Collider>(true);
        Rigidbody[] rigidbodies = FindObjectsOfType<Rigidbody>(true);
#endif

        MeshCollider[] meshColliders = colliders.OfType<MeshCollider>().ToArray();

        int enabledColliders = colliders.Count(c => c.enabled && c.gameObject.activeInHierarchy);
        int triggers = colliders.Count(c => c.isTrigger);
        int staticColliders = colliders.Count(c => c.attachedRigidbody == null);
        int dynamicColliders = colliders.Count(c => c.attachedRigidbody != null);

        var sb = new StringBuilder();

        sb.AppendLine("=== PHYSICS AUDIT ===");
        sb.AppendLine($"Total Colliders: {colliders.Length}");
        sb.AppendLine($"Enabled + Active Colliders: {enabledColliders}");
        sb.AppendLine($"Trigger Colliders: {triggers}");
        sb.AppendLine($"Static Colliders without Rigidbody: {staticColliders}");
        sb.AppendLine($"Colliders with Rigidbody: {dynamicColliders}");
        sb.AppendLine($"Rigidbodies: {rigidbodies.Length}");
        sb.AppendLine($"MeshColliders: {meshColliders.Length}");
        sb.AppendLine();

        sb.AppendLine("=== COLLIDERS BY TYPE ===");

        foreach (var group in colliders.GroupBy(c => c.GetType().Name).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"{group.Key}: {group.Count()}");
        }

        sb.AppendLine();
        sb.AppendLine("=== COLLIDERS BY LAYER ===");

        foreach (var group in colliders.GroupBy(c => LayerMask.LayerToName(c.gameObject.layer)).OrderByDescending(g => g.Count()))
        {
            string layerName = string.IsNullOrWhiteSpace(group.Key) ? "Unnamed Layer" : group.Key;
            sb.AppendLine($"{layerName}: {group.Count()}");
        }

        sb.AppendLine();
        sb.AppendLine("=== TOP MESH COLLIDERS BY TRIANGLE COUNT ===");

        foreach (MeshCollider mc in meshColliders
                     .Where(mc => mc.sharedMesh != null)
                     .OrderByDescending(mc => mc.sharedMesh.triangles.Length)
                     .Take(30))
        {
            int triangles = mc.sharedMesh.triangles.Length / 3;

            sb.AppendLine(
                $"{GetFullPath(mc.transform)} | Layer: {LayerMask.LayerToName(mc.gameObject.layer)} | Convex: {mc.convex} | Tris: {triangles}"
            );
        }

        Debug.Log(sb.ToString());
    }

    private static string GetFullPath(Transform t)
    {
        string path = t.name;

        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}