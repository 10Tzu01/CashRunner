using System.Collections.Generic;
using UnityEngine;

public class PathPoint : MonoBehaviour
{
    [Header("Reach")]
    [Tooltip("How close an enemy needs to be to consider this point reached")]
    public float reachRadius = 0.5f;

    [Header("Graph (optional)")]
    [Tooltip("Points this node connects to. Leave empty if you just want 'walk to closest point'.")]
    public PathPoint[] neighbors;

    // Static registry of every PathPoint currently in the scene
    public static readonly List<PathPoint> All = new List<PathPoint>();

    private void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    /// <summary>
    /// Returns the closest PathPoint to a given world position, or null if none exist.
    /// </summary>
    public static PathPoint FindClosest(Vector3 worldPos)
    {
        PathPoint best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < All.Count; i++)
        {
            var p = All[i];
            if (p == null) continue;

            float d = (p.transform.position - worldPos).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>
    /// Returns the closest PathPoint to worldPos, ignoring one specific point.
    /// Useful for picking the "next" point after arrival.
    /// </summary>
    public static PathPoint FindClosestExcluding(Vector3 worldPos, PathPoint exclude)
    {
        PathPoint best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < All.Count; i++)
        {
            var p = All[i];
            if (p == null || p == exclude) continue;

            float d = (p.transform.position - worldPos).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                best = p;
            }
        }
        return best;
    }

    private void OnDrawGizmos()
    {
        // Draw the reach radius so you can see it in the Scene view
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, reachRadius);

        // Draw connections to neighbors
        if (neighbors != null)
        {
            Gizmos.color = Color.cyan;
            foreach (var n in neighbors)
            {
                if (n != null)
                    Gizmos.DrawLine(transform.position, n.transform.position);
            }
        }
    }
}