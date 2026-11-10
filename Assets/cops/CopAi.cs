using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[RequireComponent(typeof(NavMeshAgent))]
public class CopAI : MonoBehaviour
{
    public enum State { Patrol, Chase, Investigate }

    [Header("Routes (set by CopSpawner, or assign here for a cop placed in the scene)")]
    [Tooltip("Empty object. Each CHILD is one route, and each child of that route is a point.")]
    public Transform routesRoot;

    [Header("Patrol")]
    public float patrolSpeed = 2f;
    public float waitAtPoint = 1.5f;
    public float pointReachDistance = 0.6f;

    [Header("Vision (cone)")]
    public float viewDistance = 15f;
    [Range(1f, 360f)] public float viewAngle = 90f;   // FULL angle: 90 = 45 degrees left and 45 right
    public float eyeHeight = 1.6f;                    // ray starts here on the cop
    public float targetHeight = 1.0f;                 // ray aims here on the player (roughly the chest)
    [Tooltip("Layers that block sight. Leave Player ticked, it is handled. Untick things like Cash.")]
    public LayerMask sightBlockers = ~0;

    [Header("Chase")]
    public float chaseSpeed = 5f;
    [Tooltip("While the wanted timer is running, an unseen player is still tracked (cops keep coming straight at them). Untick: cops go to the last place the player was seen and look around.")]
    public bool trackPlayerWhileWanted = true;
    [Tooltip("Cops radio each other: once ANY cop has seen the player, patrolling cops join the chase and everyone knows the last seen position.")]
    public bool shareSightings = true;
    [Tooltip("Gap between the cop's body and the player's body at which a hit lands (0 = touching). Body sizes and scale are added automatically. Shown as a red circle.")]
    public float catchDistance = 0.5f;
    public float catchCooldown = 3f;
    public UnityEvent onCaughtPlayer;

    [Header("ATM Response")]
    [Tooltip("When an ATM is breached, a cop closer than this (straight line) goes to investigate it. Shown as a cyan sphere when the cop is selected.")]
    public float atmResponseRadius = 25f;
    public float investigateSpeed = 4f;
    public float investigateArriveDistance = 2.5f;
    [Tooltip("Seconds the cop waits at the ATM, looking around, before returning to its route.")]
    public float investigateWaitTime = 6f;
    public float lookAroundSpeed = 60f;               // degrees per second while waiting at the ATM
    [Tooltip("Give up walking to the ATM after this many seconds (in case it can't be reached).")]
    public float maxInvestigateTravelTime = 30f;

    [Header("Testing")]
    [Tooltip("Tick this to test without the ATM: the cop behaves as if the player is always robbing.")]
    public bool forceWanted = false;

    [Header("Debug Logs")]
    [Tooltip("Print what this cop is doing to the Console.")]
    public bool debugLogs = true;
    [Tooltip("Seconds between STATUS lines. 0 = only log events (state changes, sight, hits).")]
    public float statusLogInterval = 1f;

    [Header("Debug (read only)")]
    [SerializeField] private State state = State.Patrol;
    [SerializeField] private float lostTimer;

    private NavMeshAgent agent;
    private Collider copCollider;
    private Collider playerCollider;
    private Transform player;
    private List<Transform[]> routes;
    private int routeIndex, pointIndex;
    private bool waiting;
    private float waitTimer;
    private float nextCatchTime;
    private Vector3 lastKnownPos;
    private Vector3 investigatePos;
    private bool arrivedAtAtm;
    private float investigateTimer;
    private bool lastSees;
    private float nextStatusLog;
    private string sightInfo = "";

    // ONE shared wanted timer (GameData). Seeing the player refills it, otherwise it counts down.
    private bool Wanted => forceWanted || GameData.IsWanted;

    // ---------- Debug helpers ----------
    private void Log(string msg)
    {
        if (debugLogs) Debug.Log($"[Cop {name} t={Time.time:F1}] {msg}", this);
    }

    private string WantedReason()
    {
        if (forceWanted) return "forceWanted ticked";
        if (GameData.isRobbing) return "player is robbing";
        if (GameData.wantedTimeLeft > 0f) return $"wanted timer ({GameData.wantedTimeLeft:F1}s / {GameData.wantedDuration:F1}s)";
        return "NOT wanted";
    }

    private void LogStatus(bool sees)
    {
        if (!debugLogs || statusLogInterval <= 0f || Time.time < nextStatusLog) return;
        nextStatusLog = Time.time + statusLogInterval;

        string extra = "";
        switch (state)
        {
            case State.Patrol:
                extra = $"route {routeIndex} point {pointIndex}{(waiting ? " (waiting)" : "")}";
                break;
            case State.Chase:
                extra = $"unseen for {lostTimer:F1}s, wanted left {GameData.WantedTimeLeft:F1}s";
                break;
            case State.Investigate:
                extra = arrivedAtAtm
                    ? $"at ATM, waited {investigateTimer:F1}/{investigateWaitTime:F1}"
                    : $"walking to ATM, {investigateTimer:F1}s, {agent.remainingDistance:F1} left";
                break;
        }

        string dist = "no player";
        if (player != null)
        {
            Vector3 flat = player.position - transform.position; flat.y = 0f;
            if (playerCollider == null) playerCollider = player.GetComponent<Collider>();
            float reach = catchDistance + GetRadius(copCollider) + GetRadius(playerCollider);
            dist = $"{flat.magnitude:F1} (hit when <= {reach:F1}, catchReady={Time.time >= nextCatchTime})";
        }

        Log($"STATUS state={state} | sees={sees} ({sightInfo}) | wanted: {WantedReason()} | {extra} | playerDist={dist}");
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        copCollider = GetComponent<Collider>();
    }

    private void OnEnable() { GameData.OnAtmBreached += HandleAtmBreached; }
    private void OnDisable() { GameData.OnAtmBreached -= HandleAtmBreached; }

    // Fired by AtmSystem (through GameData) the moment an ATM is breached
    private void HandleAtmBreached(Vector3 atmPosition)
    {
        if (agent == null || !agent.isOnNavMesh) { Log("ATM breached, but I'm not on the NavMesh. Ignoring."); return; }
        if (state == State.Chase) { Log("ATM breached, but I'm already chasing. Ignoring."); return; }

        float d = Vector3.Distance(transform.position, atmPosition);
        if (d > atmResponseRadius) { Log($"ATM breached, too far to respond ({d:F1} > {atmResponseRadius:F1}). Ignoring."); return; }

        Log($"ATM breached {d:F1} away (radius {atmResponseRadius:F1}). Responding.");
        StartInvestigate(atmPosition);
    }

    // Called by CopSpawner right after Instantiate
    public void Init(Transform routesParent, Transform playerTransform)
    {
        routesRoot = routesParent;
        player = playerTransform;
        BuildRoutes();
        GoToNearestRoutePoint("spawned");
    }

    private void Start()
    {
        // Used when the cop was placed in the scene by hand instead of spawned
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (routes == null)
        {
            BuildRoutes();
            GoToNearestRoutePoint("started in scene");
        }
        if (player == null) Debug.LogWarning($"[Cop {name}] No Player found (tag 'Player'). It can never see anyone.", this);
    }

    // ---------- Routes ----------
    private void BuildRoutes()
    {
        routes = new List<Transform[]>();
        if (routesRoot == null) return;

        foreach (Transform route in routesRoot)
        {
            if (route.childCount == 0) continue;
            routes.Add(GetChildren(route));
        }

        // fallback: routesRoot itself is a single route (points are its direct children)
        if (routes.Count == 0 && routesRoot.childCount > 0)
            routes.Add(GetChildren(routesRoot));
    }

    private static Transform[] GetChildren(Transform parent)
    {
        Transform[] pts = new Transform[parent.childCount];
        for (int i = 0; i < pts.Length; i++) pts[i] = parent.GetChild(i);
        return pts;
    }

    // Picks the closest point across ALL routes, then patrols that route from there.
    // Used after spawning AND after giving up a chase.
    private void GoToNearestRoutePoint(string reason = "")
    {
        State previous = state;
        state = State.Patrol;
        waiting = false;
        lostTimer = 0f;
        agent.speed = patrolSpeed;

        if (routes == null || routes.Count == 0)
        {
            Log($"-> PATROL ({reason}) but there are NO routes. Is Routes Root assigned and does each route have child points?");
            return;
        }

        float best = float.MaxValue;
        for (int r = 0; r < routes.Count; r++)
        {
            for (int p = 0; p < routes[r].Length; p++)
            {
                float d = (routes[r][p].position - transform.position).sqrMagnitude;
                if (d < best) { best = d; routeIndex = r; pointIndex = p; }
            }
        }
        agent.SetDestination(routes[routeIndex][pointIndex].position);
        Log($"-> PATROL (was {previous}): {reason}. Heading to route {routeIndex} point {pointIndex}.");
    }

    // ---------- Main loop ----------
    private void Update()
    {
        if (agent == null || !agent.isOnNavMesh) return;

        agent.isStopped = GameData.GamePause;
        if (GameData.GamePause) return;

        GameData.Tick();   // shared wanted countdown (runs once per frame no matter how many cops)

        bool sees = CanSeePlayer();
        if (sees && Wanted) GameData.ReportSighting(player.position);   // seen = timer back to 100%

        if (debugLogs && sees != lastSees)
        {
            lastSees = sees;
            Log(sees ? "SEES the player" : $"LOST SIGHT of the player ({sightInfo})");
        }
        LogStatus(sees);

        switch (state)
        {
            case State.Patrol: UpdatePatrol(sees); break;
            case State.Chase: UpdateChase(sees); break;
            case State.Investigate: UpdateInvestigate(sees); break;
        }
    }

    private void UpdatePatrol(bool sees)
    {
        // Chase when wanted AND (this cop sees the player OR another cop already spotted them)
        if (Wanted && (sees || (shareSightings && GameData.playerSpotted)))
        {
            StartChase(sees, "patrol");
            return;
        }

        if (routes == null || routes.Count == 0 || agent.pathPending) return;

        Transform[] pts = routes[routeIndex];

        if (waiting)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                waiting = false;
                pointIndex = (pointIndex + 1) % pts.Length;
                agent.SetDestination(pts[pointIndex].position);
            }
            return;
        }

        if (agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, pointReachDistance))
        {
            waiting = true;
            waitTimer = waitAtPoint;
        }
    }

    // ---------- Investigate (go to the breached ATM) ----------
    private void StartInvestigate(Vector3 atmPosition)
    {
        // The ATM itself may not be on the NavMesh, so snap to the closest walkable spot
        if (!NavMesh.SamplePosition(atmPosition, out NavMeshHit hit, 6f, NavMesh.AllAreas))
        {
            Log("ATM is not near the NavMesh (6m). Can't investigate. Bake the NavMesh closer to it.");
            return;
        }

        Log($"-> INVESTIGATE (was {state}): going to the ATM.");
        state = State.Investigate;
        waiting = false;
        arrivedAtAtm = false;
        investigateTimer = 0f;
        agent.speed = investigateSpeed;
        investigatePos = hit.position;
        agent.SetDestination(investigatePos);
    }

    private void UpdateInvestigate(bool sees)
    {
        // Spotted the player on the way, or at the ATM (or a radio call): chase
        if (Wanted && (sees || (shareSightings && GameData.playerSpotted)))
        {
            StartChase(sees, "investigate");
            return;
        }

        investigateTimer += Time.deltaTime;

        if (!arrivedAtAtm)
        {
            bool close = !agent.pathPending && agent.remainingDistance <= investigateArriveDistance;
            if (close || investigateTimer >= maxInvestigateTravelTime)
            {
                arrivedAtAtm = true;
                Log(close ? "Arrived at the ATM. Looking around." : "Gave up reaching the ATM (travel time ran out). Looking around here.");
                investigateTimer = 0f;
                agent.ResetPath();   // stop walking
            }
            return;
        }

        // At the ATM: turn slowly so the vision cone sweeps the area
        transform.Rotate(0f, lookAroundSpeed * Time.deltaTime, 0f);

        if (investigateTimer >= investigateWaitTime)
        {
            // Wanted timer still running: don't go home, keep hunting from the ATM
            if (Wanted) StartChase(false, "finished investigating but still wanted");
            else GoToNearestRoutePoint("finished investigating the ATM");
        }
    }

    private void StartChase(bool sees, string from = "")
    {
        Log($"-> CHASE (was {state}, {from}). Wanted because: {WantedReason()}");
        state = State.Chase;
        waiting = false;
        lostTimer = 0f;
        agent.speed = chaseSpeed;

        // Only know the exact position if we actually see the player; otherwise use the radio / the ATM
        if (sees) lastKnownPos = player.position;
        else if (GameData.hasLastKnown) lastKnownPos = GameData.lastKnownPlayerPos;
        else lastKnownPos = investigatePos != Vector3.zero ? investigatePos : transform.position;
    }

    private void UpdateChase(bool sees)
    {
        // The ONLY way a chase ends: the wanted timer ran out (it refills whenever any cop sees you)
        if (!Wanted)
        {
            GoToNearestRoutePoint("wanted timer ran out");
            return;
        }

        if (sees)
        {
            lostTimer = 0f;
            lastKnownPos = player.position;
        }
        else
        {
            lostTimer += Time.deltaTime;
            if (shareSightings && GameData.hasLastKnown) lastKnownPos = GameData.lastKnownPlayerPos;
        }

        // Unseen but still wanted: keep tracking the player, or go to the last known spot and look around
        Vector3 target = (!sees && trackPlayerWhileWanted && player != null) ? player.position : lastKnownPos;
        agent.SetDestination(target);

        if (!sees && !trackPlayerWhileWanted && !agent.pathPending && agent.remainingDistance <= investigateArriveDistance)
            transform.Rotate(0f, lookAroundSpeed * Time.deltaTime, 0f);   // arrived at last known spot: sweep the cone

        if (player != null && Time.time >= nextCatchTime && IsTouchingPlayer())
        {
            nextCatchTime = Time.time + catchCooldown;
            GameData.LoseLife(1);   // HeartHUD updates the hearts automatically
            Log($"HIT the player! Lives left: {GameData.playerLives}");
            onCaughtPlayer?.Invoke();
        }
    }

    // Hit check. Uses flat (XZ) distance so height differences don't matter, and adds both bodies'
    // radii, so it still works when the cop/player are scaled up (e.g. scale 2.76).
    private bool IsTouchingPlayer()
    {
        if (playerCollider == null) playerCollider = player.GetComponent<Collider>();

        Vector3 flat = player.position - transform.position;
        flat.y = 0f;

        float reach = catchDistance + GetRadius(copCollider) + GetRadius(playerCollider);
        return flat.magnitude <= reach;
    }

    private static float GetRadius(Collider c)
    {
        if (c == null) return 0f;
        return Mathf.Max(c.bounds.extents.x, c.bounds.extents.z);   // world-space, includes scale
    }

    // ---------- Vision: range -> angle -> line of sight ----------
    public bool CanSeePlayer()
    {
        if (player == null) { sightInfo = "no player reference"; return false; }

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 target = player.position + Vector3.up * targetHeight;
        Vector3 toPlayer = target - eye;

        // 1) RANGE
        float dist = toPlayer.magnitude;
        if (dist > viewDistance)
        {
            if (debugLogs) sightInfo = $"out of range {dist:F1} > {viewDistance:F1}";
            return false;
        }

        // 2) ANGLE (cone). Compared on the flat plane so looking up/down doesn't matter
        Vector3 fwd = transform.forward; fwd.y = 0f;
        Vector3 flat = toPlayer; flat.y = 0f;
        if (flat.sqrMagnitude > 0.0001f)
        {
            float angle = Vector3.Angle(fwd, flat);
            if (angle > viewAngle * 0.5f)
            {
                if (debugLogs) sightInfo = $"outside cone {angle:F0} > {viewAngle * 0.5f:F0}";
                return false;
            }
        }

        // 3) LINE OF SIGHT: the first thing the ray hits must be the player
        if (Physics.Linecast(eye, target, out RaycastHit hit, sightBlockers, QueryTriggerInteraction.Ignore))
        {
            bool hitPlayer = hit.transform == player || hit.transform.IsChildOf(player);
            if (!hitPlayer)
            {
                if (debugLogs) sightInfo = $"blocked by '{hit.transform.name}'";
                return false;   // a wall/object is in the way
            }
        }

        if (debugLogs) sightInfo = "clear";
        return true;
    }

    // ---------- Scene view helper: select the cop to see the cone ----------
    private void OnDrawGizmosSelected()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        float half = viewAngle * 0.5f;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, atmResponseRadius);   // ATM response range

        Gizmos.color = Color.red;   // hit range: the player's body must touch this circle
        Gizmos.DrawWireSphere(transform.position, catchDistance + GetRadius(GetComponent<Collider>()));

        Gizmos.color = Color.yellow;
        Vector3 prev = eye + Quaternion.Euler(0f, -half, 0f) * transform.forward * viewDistance;
        Gizmos.DrawLine(eye, prev);

        const int steps = 24;
        for (int i = 1; i <= steps; i++)
        {
            float a = -half + viewAngle * i / steps;
            Vector3 p = eye + Quaternion.Euler(0f, a, 0f) * transform.forward * viewDistance;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
        Gizmos.DrawLine(eye, prev);
    }
}