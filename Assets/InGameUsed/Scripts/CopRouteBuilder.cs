using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Builds a cop patrol route (a list of waypoint children) that follows the SIDEWALK
/// along a list of road corners / intersections.
///
/// It creates the exact structure CopAIWithAnimation already expects:
///     routesRoot
///       └─ this object (one route)
///            ├─ Point_000
///            ├─ Point_001
///            └─ ...
///
/// Right-click this component's header -> "Build Route" (or "Clear Route").
/// </summary>
public class CopRouteBuilder : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Road corners / intersections, IN THE ORDER the cop should walk through them.")]
    public Transform[] corners;
    [Tooltip("Tick = the corners form a loop, so the first and last corners are rounded off like the others.")]
    public bool closedLoop = true;

    [Header("Sidewalk")]
    [Tooltip("Off = walk on the sidewalk on the RIGHT of the direction of travel. On = the left sidewalk.")]
    public bool leftSidewalk = false;
    [Tooltip("Distance from the road's centre line to the middle of the sidewalk. Road Lane_01 has its sidewalk between 7 and 10, so 8.5 is the middle.")]
    public float sidewalkOffset = 8.5f;

    [Header("Waypoints")]
    [Tooltip("Distance between waypoints along the straights (metres). Smaller = more waypoints, so after a chase " +
             "the cop rejoins its route closer to where it left it. 0 = corners only. " +
             "Set the cop's 'Wait At Point' to 0, otherwise it pauses at EVERY waypoint.")]
    public float pointSpacing = 8f;
    [Tooltip("Lifts waypoints to the sidewalk surface (Road Lane_01's sidewalk is 0.2 high).")]
    public float heightOffset = 0.2f;

    [Header("NavMesh")]
    [Tooltip("Moves each waypoint onto the nearest baked NavMesh point so the cop can always reach it.")]
    public bool snapToNavMesh = true;
    public float snapDistance = 3f;

    [Header("Gizmos")]
    public Color gizmoColor = new Color(0.2f, 0.6f, 1f);

    [ContextMenu("Build Route")]
    public void Build()
    {
        if (corners == null || corners.Length < 2)
        {
            Debug.LogWarning("CopRouteBuilder: add at least 2 corners.", this);
            return;
        }
        foreach (var c in corners)
        {
            if (c == null) { Debug.LogWarning("CopRouteBuilder: a corner slot is empty.", this); return; }
        }

        Clear();

        int n = corners.Length;
        float side = leftSidewalk ? -1f : 1f;

        // ---- Pass 1: one sidewalk point per corner. ----
        var keyPts = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            bool hasPrev = closedLoop || i > 0;
            bool hasNext = closedLoop || i < n - 1;
            Vector3 c = P(i, n);

            if (!hasPrev || !hasNext)
            {
                // Open end of the route: push the point to the correct side.
                Vector3 d = hasNext ? Flat(P(i + 1, n) - c) : Flat(c - P(i - 1, n));
                keyPts.Add(c + side * Right(d.normalized) * sidewalkOffset);
                continue;
            }

            Vector3 dIn = Flat(c - P(i - 1, n)).normalized;
            Vector3 dOut = Flat(P(i + 1, n) - c).normalized;
            Vector3 rIn = side * Right(dIn);
            Vector3 rOut = side * Right(dOut);

            if (Vector3.Dot(dIn, dOut) > 0.999f)
                keyPts.Add(c + rIn * sidewalkOffset);   // straight through, not really a corner
            else
                keyPts.Add(c + (rIn + rOut) / (1f + Vector3.Dot(rIn, rOut)) * sidewalkOffset); // where both sidewalks meet
        }

        // ---- Pass 2: optional extra points along the straights. ----
        var positions = new List<Vector3>();
        for (int i = 0; i < keyPts.Count; i++)
        {
            positions.Add(keyPts[i]);

            bool hasNext = closedLoop || i < keyPts.Count - 1;
            if (!hasNext || pointSpacing <= 0f) continue;

            Vector3 a = keyPts[i];
            Vector3 b = keyPts[(i + 1) % keyPts.Count];
            int segs = Mathf.Max(1, Mathf.RoundToInt(Vector3.Distance(a, b) / pointSpacing));
            for (int k = 1; k < segs; k++)
                positions.Add(Vector3.Lerp(a, b, k / (float)segs));
        }

        // ---- Create the waypoints as children, in order. ----
        int missed = 0;
        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 p = positions[i];

            if (snapToNavMesh)
            {
                NavMeshHit hit;
                if (NavMesh.SamplePosition(p, out hit, snapDistance, NavMesh.AllAreas)) p = hit.position;
                else missed++;
            }

            var go = new GameObject($"Point_{i:000}");
            go.transform.position = p;
            go.transform.SetParent(transform, true);

#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Build Cop Route");
#endif
        }

        Debug.Log($"CopRouteBuilder: built {positions.Count} waypoints." +
                  (missed > 0 ? $" {missed} were not near the NavMesh (bake it, or raise Snap Distance)." : ""), this);
    }

    [ContextMenu("Clear Route")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
#if UNITY_EDITOR
            if (!Application.isPlaying) { UnityEditor.Undo.DestroyObjectImmediate(child); continue; }
#endif
            Destroy(child);
        }
    }

    private Vector3 P(int i, int n)
    {
        Vector3 p = corners[((i % n) + n) % n].position;
        p.y += heightOffset;
        return p;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // Right-hand side when facing 'forward' (Unity: up x forward).
    private static Vector3 Right(Vector3 forward) { return Vector3.Cross(Vector3.up, forward); }

    private void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        int count = transform.childCount;
        for (int i = 0; i < count; i++)
        {
            Vector3 a = transform.GetChild(i).position + Vector3.up * 0.3f;
            Gizmos.DrawSphere(a, 0.4f);

            bool hasNext = closedLoop || i < count - 1;
            if (!hasNext || count < 2) continue;
            Vector3 b = transform.GetChild((i + 1) % count).position + Vector3.up * 0.3f;
            Gizmos.DrawLine(a, b);
        }
    }
}