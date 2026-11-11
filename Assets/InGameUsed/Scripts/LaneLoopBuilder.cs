using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a one-way lane (a chain of LaneNodes) that runs through a list of corner points.
/// It offsets the lane to the correct side of the road automatically, rounds the corners,
/// and links every node to the next one in the direction of travel.
///
/// Right-click this component's header -> "Build Lane" (or "Clear Lane").
/// </summary>
public class LaneLoopBuilder : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Road corners / intersections, IN THE ORDER the car should drive through them.")]
    public Transform[] corners;
    [Tooltip("Tick = last corner connects back to the first.")]
    public bool closedLoop = true;

    [Header("Rules of the road")]
    [Tooltip("Off = drive on the right (the lane is on the right-hand side of the direction of travel). On = drive on the left.")]
    public bool driveOnLeft = false;
    [Tooltip("Distance from the road's centre line to the middle of the lane. Road Lane_01 is 14 wide, so 3.5 is the middle of one lane.")]
    public float laneOffset = 3.5f;

    [Header("Shape")]
    public float cornerRadius = 7f;
    public int cornerSteps = 4;
    [Tooltip("Gap between nodes on straight sections.")]
    public float nodeSpacing = 10f;
    [Tooltip("Cars are capped to this speed (m/s) on the corner nodes. 0 = no cap.")]
    public float cornerSpeedLimit = 5f;
    [Tooltip("Lifts nodes slightly so the gizmos sit above the road surface.")]
    public float heightOffset = 0.05f;

    [ContextMenu("Build Lane")]
    public void Build()
    {
        if (corners == null || corners.Length < 2)
        {
            Debug.LogWarning("LaneLoopBuilder: add at least 2 corners.", this);
            return;
        }
        foreach (var c in corners)
        {
            if (c == null) { Debug.LogWarning("LaneLoopBuilder: a corner slot is empty.", this); return; }
        }

        Clear();

        int n = corners.Length;
        float side = driveOnLeft ? -1f : 1f;

        // ---- Pass 1: for every corner work out the lane points that go around it. ----
        var cornerPts = new List<List<Vector3>>();
        var cornerIsArc = new List<bool>();

        for (int i = 0; i < n; i++)
        {
            bool hasPrev = closedLoop || i > 0;
            bool hasNext = closedLoop || i < n - 1;

            Vector3 c = P(i, n);
            var pts = new List<Vector3>();
            bool arc = false;

            if (!hasPrev || !hasNext)
            {
                // Open end of the route: just one point, pushed to the correct side.
                Vector3 d = hasNext ? Flat(P(i + 1, n) - c) : Flat(c - P(i - 1, n));
                pts.Add(c + side * Right(d.normalized) * laneOffset);
            }
            else
            {
                Vector3 prev = P(i - 1, n), next = P(i + 1, n);
                Vector3 dIn = Flat(c - prev).normalized;
                Vector3 dOut = Flat(next - c).normalized;

                Vector3 rIn = side * Right(dIn);
                Vector3 rOut = side * Right(dOut);

                if (Vector3.Dot(dIn, dOut) > 0.999f)
                {
                    pts.Add(c + rIn * laneOffset); // not really a corner
                }
                else
                {
                    // Where the two offset lane lines meet.
                    Vector3 miter = c + (rIn + rOut) / (1f + Vector3.Dot(rIn, rOut)) * laneOffset;

                    float r = Mathf.Min(cornerRadius,
                                        0.5f * Flat(c - prev).magnitude,
                                        0.5f * Flat(next - c).magnitude);

                    Vector3 s = miter - dIn * r;
                    Vector3 e = miter + dOut * r;

                    int steps = Mathf.Max(1, cornerSteps);
                    for (int k = 0; k <= steps; k++)
                    {
                        float t = k / (float)steps;
                        // Quadratic Bezier: start -> miter -> end gives a smooth rounded corner.
                        Vector3 p = (1 - t) * (1 - t) * s + 2 * (1 - t) * t * miter + t * t * e;
                        pts.Add(p);
                    }
                    arc = true;
                }
            }

            cornerPts.Add(pts);
            cornerIsArc.Add(arc);
        }

        // ---- Pass 2: stitch corners together, adding evenly spaced nodes on the straights. ----
        var positions = new List<Vector3>();
        var limits = new List<float>();

        for (int i = 0; i < n; i++)
        {
            foreach (var p in cornerPts[i])
            {
                positions.Add(p);
                limits.Add(cornerIsArc[i] ? cornerSpeedLimit : 0f);
            }

            bool hasNext = closedLoop || i < n - 1;
            if (!hasNext) continue;

            Vector3 a = cornerPts[i][cornerPts[i].Count - 1];
            Vector3 b = cornerPts[(i + 1) % n][0];
            float dist = Vector3.Distance(a, b);
            int segs = Mathf.Max(1, Mathf.RoundToInt(dist / Mathf.Max(1f, nodeSpacing)));
            for (int k = 1; k < segs; k++)
            {
                positions.Add(Vector3.Lerp(a, b, k / (float)segs));
                limits.Add(0f);
            }
        }

        // ---- Create the nodes and link them one-way in order. ----
        var nodes = new List<LaneNode>();
        for (int i = 0; i < positions.Count; i++)
        {
            var go = new GameObject($"Lane_{i:000}");
            go.transform.position = positions[i];
            go.transform.SetParent(transform, true);

            var node = go.AddComponent<LaneNode>();
            node.speedLimit = limits[i];
            nodes.Add(node);

#if UNITY_EDITOR
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Build Lane");
#endif
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            bool last = i == nodes.Count - 1;
            if (last && !closedLoop) { nodes[i].next = new LaneNode[0]; continue; }
            nodes[i].next = new[] { nodes[(i + 1) % nodes.Count] };

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(nodes[i]);
#endif
        }

        Debug.Log($"LaneLoopBuilder: built {nodes.Count} lane nodes.", this);
    }

    [ContextMenu("Clear Lane")]
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
}
