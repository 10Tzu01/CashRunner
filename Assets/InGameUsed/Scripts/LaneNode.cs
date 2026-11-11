using UnityEngine;

/// <summary>
/// One point on a lane. Lanes are ONE-WAY: a car only ever drives from a node to the
/// nodes listed in 'next', so a car can never go the wrong way along a lane.
/// (Separate from PathPoint, which your cops use.)
/// </summary>
public class LaneNode : MonoBehaviour
{
    [Tooltip("Nodes a car may drive to after this one. More than one entry = a junction; the car picks one at random.")]
    public LaneNode[] next;

    [Tooltip("Speed cap (m/s) while heading toward this node. 0 = no cap. Used to slow cars for corners.")]
    public float speedLimit = 0f;

    /// <summary>Direction of travel through this node (flat on the ground plane).</summary>
    public Vector3 Direction()
    {
        if (next != null)
        {
            foreach (var n in next)
            {
                if (n == null) continue;
                Vector3 d = n.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.0001f) return d.normalized;
            }
        }
        return transform.forward;
    }

    private void OnDrawGizmos()
    {
        Vector3 p = transform.position + Vector3.up * 0.4f;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(p, 0.35f);

        if (next == null) return;
        Gizmos.color = Color.cyan;
        foreach (var n in next)
        {
            if (n == null) continue;
            DrawArrow(p, n.transform.position + Vector3.up * 0.4f);
        }
    }

    private static void DrawArrow(Vector3 a, Vector3 b)
    {
        Gizmos.DrawLine(a, b);
        Vector3 dir = b - a;
        if (dir.sqrMagnitude < 0.01f) return;
        dir.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        Vector3 tip = Vector3.Lerp(a, b, 0.6f);
        Gizmos.DrawLine(tip, tip - dir * 1.2f + side * 0.6f);
        Gizmos.DrawLine(tip, tip - dir * 1.2f - side * 0.6f);
    }
}
