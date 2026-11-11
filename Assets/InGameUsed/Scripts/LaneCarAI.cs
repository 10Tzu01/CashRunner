using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives a car along LaneNodes. No Rigidbody, NavMeshAgent or collider needed.
/// Assumes the car model faces +Z (the SimplePoly cars do).
/// </summary>
public class LaneCarAI : MonoBehaviour
{
    [Header("Route")]
    [Tooltip("Optional. Leave empty and the car picks the nearest lane node that runs the same way it is facing.")]
    public LaneNode startNode;

    [Header("Speed (metres / second)")]
    public float maxSpeed = 10f;
    public float cornerSpeed = 4f;
    public float acceleration = 6f;
    public float braking = 14f;

    [Header("Steering")]
    [Tooltip("Max turn rate in degrees per second.")]
    public float turnSpeed = 120f;
    [Tooltip("Distance at which a node counts as reached.")]
    public float reachDistance = 1.5f;

    [Header("Following other cars")]
    [Tooltip("Centre-to-centre distance at which the car starts slowing for a car ahead.")]
    public float followDistance = 16f;
    [Tooltip("Centre-to-centre distance at which the car comes to a full stop behind a car ahead.")]
    public float stopDistance = 7f;
    [Tooltip("How far sideways (m) another car can be and still count as 'in my lane'.")]
    public float laneHalfWidth = 2.2f;

    public float CurrentSpeed { get; private set; }

    private LaneNode target;
    private static readonly List<LaneCarAI> AllCars = new List<LaneCarAI>();

    private void OnEnable() { AllCars.Add(this); }
    private void OnDisable() { AllCars.Remove(this); }

    private void Start()
    {
        if (startNode == null) startNode = FindBestStartNode();
        target = startNode;

        if (target == null)
            Debug.LogWarning($"{name}: no LaneNode found. Build a lane first (LaneLoopBuilder).", this);
    }

    private void Update()
    {
        if (target == null) return;

        float dt = Time.deltaTime;
        Vector3 pos = transform.position;
        Vector3 fwd = Flat(transform.forward);

        // ---- 1. Reached (or driven past) the current target? Pick the next node. ----
        Vector3 toTarget = Flat(target.transform.position - pos);
        float dist = toTarget.magnitude;
        bool passed = dist < reachDistance * 3f && Vector3.Dot(fwd, toTarget) < 0f;

        if (dist < reachDistance || passed)
        {
            LaneNode nxt = ChooseNext(target);
            if (nxt == null)
            {
                // Dead end: stop.
                CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, 0f, braking * dt);
                transform.position += fwd * CurrentSpeed * dt;
                return;
            }
            target = nxt;
            toTarget = Flat(target.transform.position - pos);
        }

        // ---- 2. Work out how fast we want to go. ----
        float desired = maxSpeed;
        if (target.speedLimit > 0f) desired = Mathf.Min(desired, target.speedLimit);

        // Slow down if we're not pointing at the node yet (i.e. mid-corner).
        float angle = Vector3.Angle(fwd, toTarget);
        desired = Mathf.Lerp(desired, Mathf.Min(desired, cornerSpeed), Mathf.Clamp01(angle / 45f));

        // Slow down / stop behind a car in front of us (same lane, same direction).
        desired = Mathf.Min(desired, SpeedLimitFromCarAhead(pos, fwd));

        float rate = desired < CurrentSpeed ? braking : acceleration;
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, desired, rate * dt);

        // ---- 3. Steer toward the target and move. ----
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            Quaternion want = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            float turnScale = Mathf.Clamp(CurrentSpeed / 3f, 0.25f, 1f); // can't pivot on the spot
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * turnScale * dt);
        }

        transform.position += Flat(transform.forward) * CurrentSpeed * dt;
    }

    // ------------------------------------------------------------------

    private float SpeedLimitFromCarAhead(Vector3 pos, Vector3 fwd)
    {
        float limit = maxSpeed;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        foreach (var other in AllCars)
        {
            if (other == this) continue;

            Vector3 off = Flat(other.transform.position - pos);
            float ahead = Vector3.Dot(off, fwd);
            if (ahead <= 0f || ahead > followDistance) continue;                       // not in front / too far
            if (Mathf.Abs(Vector3.Dot(off, right)) > laneHalfWidth) continue;          // other lane
            if (Vector3.Dot(Flat(other.transform.forward), fwd) < 0.3f) continue;      // oncoming / crossing

            float t = Mathf.InverseLerp(stopDistance, followDistance, ahead);          // 0 = touching, 1 = far
            limit = Mathf.Min(limit, Mathf.Lerp(0f, maxSpeed, t));
        }
        return limit;
    }

    private LaneNode ChooseNext(LaneNode from)
    {
        if (from.next == null || from.next.Length == 0) return null;

        var options = new List<LaneNode>();
        foreach (var n in from.next) if (n != null) options.Add(n);
        if (options.Count == 0) return null;

        return options[Random.Range(0, options.Count)];
    }

    private LaneNode FindBestStartNode()
    {
        Vector3 fwd = Flat(transform.forward);
        LaneNode best = null;
        float bestSqr = float.MaxValue;

        foreach (var n in FindObjectsByType<LaneNode>(FindObjectsSortMode.None))
        {
            // Only lanes that run the SAME way the car is facing -> never joins an oncoming lane.
            if (Vector3.Dot(n.Direction(), fwd) < 0.5f) continue;

            Vector3 off = Flat(n.transform.position - transform.position);
            if (Vector3.Dot(off, fwd) < -1f) continue; // behind us

            float d = off.sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = n; }
        }
        return best;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position + Vector3.up, target.transform.position + Vector3.up * 0.4f);
    }
}
