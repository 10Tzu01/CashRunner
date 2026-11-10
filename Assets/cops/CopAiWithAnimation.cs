using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[RequireComponent(typeof(NavMeshAgent))]
public class CopAIWithAnimation : MonoBehaviour
{
    public enum State { Patrol, Chase, Investigate }

    [Header("Routes")]
    public Transform routesRoot;

    [Header("Patrol")]
    public float patrolSpeed = 2f;
    public float waitAtPoint = 1.5f;
    public float pointReachDistance = 0.6f;
    [Tooltip("Tick = walk the route as a loop (last point -> first point). Untick = walk to the end and come back.")]
    public bool loopRoute = true;
    [Tooltip("Used when Wait At Point is 0: the cop turns to the next point this far BEFORE reaching the current one, so it flows through corners without stopping.")]
    public float rollingReachDistance = 2f;
    [Tooltip("Waypoints closer than this to the previous one are ignored (removes duplicate points).")]
    public float duplicatePointDistance = 0.5f;

    [Header("Vision")]
    public float viewDistance = 15f;
    [Range(1f, 360f)] public float viewAngle = 90f;
    public float eyeHeight = 1.6f;
    public float targetHeight = 1.0f;
    public LayerMask sightBlockers = ~0;

    [Header("Chase")]
    public float chaseSpeed = 5f;
    public bool trackPlayerWhileWanted = true;
    public bool shareSightings = true;
    public float catchDistance = 0.5f;
    public float catchCooldown = 3f;
    public UnityEvent onCaughtPlayer;

    [Header("Rotation")]
    public float turnSpeed = 360f;
    public float agentAcceleration = 20f;

    [Header("ATM Response")]
    public float atmResponseRadius = 25f;
    public float investigateSpeed = 4f;
    public float investigateArriveDistance = 2.5f;
    public float investigateWaitTime = 6f;
    public float lookAroundSpeed = 60f;
    public float maxInvestigateTravelTime = 30f;

    [Header("Animation")]
    public Animator anim;
    public float movementThreshold = 0.1f;
    public float animationDampTime = 0.1f;

    [Header("Testing")]
    public bool forceWanted = false;

    [Header("Debug Logs")]
    public bool debugLogs = true;
    public float statusLogInterval = 1f;

    [Header("Debug (read only)")]
    [SerializeField] private State state = State.Patrol;
    [SerializeField] private float lostTimer;

    private NavMeshAgent agent;
    private Collider copCollider;
    private Collider playerCollider;
    private Transform player;

    private List<Transform[]> routes;
    private int routeIndex;
    private int pointIndex;
    private int patrolDirection = 1;

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

    private bool Wanted => forceWanted || GameData.IsWanted;

    // ---------- Debug helpers ----------

    private void Log(string msg)
    {
        if (debugLogs)
            Debug.Log($"[Cop {name} t={Time.time:F1}] {msg}", this);
    }

    private string WantedReason()
    {
        if (forceWanted) return "forceWanted ticked";
        if (GameData.isRobbing) return "player is robbing";

        if (GameData.wantedTimeLeft > 0f)
            return $"wanted timer ({GameData.wantedTimeLeft:F1}s / {GameData.wantedDuration:F1}s)";

        return "NOT wanted";
    }

    private void LogStatus(bool sees)
    {
        if (!debugLogs || statusLogInterval <= 0f ||
            Time.time < nextStatusLog)
            return;

        nextStatusLog = Time.time + statusLogInterval;

        string extra = "";

        switch (state)
        {
            case State.Patrol:
                extra = $"route {routeIndex} point {pointIndex}" +
                        (waiting ? " (waiting)" : "");
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
            Vector3 flat = player.position - transform.position;
            flat.y = 0f;

            if (playerCollider == null)
                playerCollider = player.GetComponent<Collider>();

            float reach = catchDistance +
                          GetRadius(copCollider) +
                          GetRadius(playerCollider);

            dist = $"{flat.magnitude:F1} (hit when <= {reach:F1}, catchReady={Time.time >= nextCatchTime})";
        }

        Log($"STATUS state={state} | sees={sees} ({sightInfo}) | " +
            $"wanted: {WantedReason()} | {extra} | playerDist={dist}");
    }

    // ---------- Initialization ----------

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        copCollider = GetComponent<Collider>();

        // Automatically find the Animator on this object or its children.
        if (anim == null)
            anim = GetComponentInChildren<Animator>();

        // Let the NavMeshAgent control the cop's rotation.
        agent.updateRotation = true;
        agent.angularSpeed = turnSpeed;
        agent.acceleration = agentAcceleration;

        // Let the NavMeshAgent control movement, not animation root motion.
        if (anim != null)
            anim.applyRootMotion = false;
        else
            Debug.LogWarning("Animator not found on " + name, this);
    }

    private void OnEnable()
    {
        GameData.OnAtmBreached += HandleAtmBreached;
    }

    private void OnDisable()
    {
        GameData.OnAtmBreached -= HandleAtmBreached;
    }

    // Called by CopSpawner after Instantiate.
    public void Init(Transform routesParent, Transform playerTransform)
    {
        routesRoot = routesParent;
        player = playerTransform;

        BuildRoutes();
        GoToNearestRoutePoint("spawned");
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");

            if (p != null)
                player = p.transform;
        }

        if (routes == null)
        {
            BuildRoutes();
            GoToNearestRoutePoint("started in scene");
        }

        if (player == null)
        {
            Debug.LogWarning(
                $"[Cop {name}] No Player found (tag 'Player'). It can never see anyone.",
                this
            );
        }
    }


    private void UpdateAnimation()
    {
        if (anim == null || agent == null)
            return;

        Vector3 velocity = agent.velocity;
        velocity.y = 0f;

        bool isMoving =
            !GameData.GamePause &&
            !agent.isStopped &&
            velocity.magnitude > movementThreshold;

        // patrolling = walk, chasing / investigating = run
        bool walking = isMoving && state == State.Patrol;
        bool running = isMoving && state != State.Patrol;

        anim.SetBool("isWalking", walking);
        anim.SetBool("isRunning", running);
        anim.SetBool("isIdle", !isMoving);
    }

    // ---------- Routes ----------

    private void BuildRoutes()
    {
        routes = new List<Transform[]>();

        if (routesRoot == null)
            return;

        foreach (Transform route in routesRoot)
        {
            if (route.childCount == 0)
                continue;

            routes.Add(GetChildren(route));
        }

        if (routes.Count == 0 && routesRoot.childCount > 0)
            routes.Add(GetChildren(routesRoot));
    }

    private Transform[] GetChildren(Transform parent)
    {
        List<Transform> pts = new List<Transform>();

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform p = parent.GetChild(i);

            // skip a point that sits on top of the previous one (would cause a pointless extra stop)
            if (pts.Count > 0 &&
                Vector3.Distance(pts[pts.Count - 1].position, p.position) < duplicatePointDistance)
                continue;

            pts.Add(p);
        }

        return pts.ToArray();
    }

    private void GoToNearestRoutePoint(string reason = "")
    {
        State previous = state;

        state = State.Patrol;
        waiting = false;
        waitTimer = 0f;
        lostTimer = 0f;
        agent.speed = patrolSpeed;

        if (routes == null || routes.Count == 0)
        {
            Log($"-> PATROL ({reason}) but there are NO routes. Check Routes Root and waypoint children.");
            return;
        }

        float best = float.MaxValue;

        for (int r = 0; r < routes.Count; r++)
        {
            for (int p = 0; p < routes[r].Length; p++)
            {
                float d = (routes[r][p].position -
                           transform.position).sqrMagnitude;

                if (d < best)
                {
                    best = d;
                    routeIndex = r;
                    pointIndex = p;
                }
            }
        }

        patrolDirection = 1;

        agent.isStopped = false;
        agent.SetDestination(routes[routeIndex][pointIndex].position);

        Log($"-> PATROL (was {previous}): {reason}. " +
            $"Heading to route {routeIndex} point {pointIndex}.");
    }

    // ---------- Main loop ----------

    private void Update()
    {
        if (agent == null || !agent.isOnNavMesh)
            return;

        agent.isStopped = GameData.GamePause;

        UpdateAnimation();

        // no braking while rolling through waypoints, braking everywhere else (chase / ATM)
        agent.autoBraking = !(state == State.Patrol && waitAtPoint <= 0f);

        if (GameData.GamePause)
            return;

        GameData.Tick();

        bool sees = CanSeePlayer();

        if (sees && Wanted)
            GameData.ReportSighting(player.position);

        if (debugLogs && sees != lastSees)
        {
            lastSees = sees;

            Log(sees
                ? "SEES the player"
                : $"LOST SIGHT of the player ({sightInfo})");
        }

        LogStatus(sees);

        switch (state)
        {
            case State.Patrol:
                UpdatePatrol(sees);
                break;

            case State.Chase:
                UpdateChase(sees);
                break;

            case State.Investigate:
                UpdateInvestigate(sees);
                break;
        }
    }

    // ---------- Patrol ----------

    private void UpdatePatrol(bool sees)
    {
        if (Wanted &&
            (sees || (shareSightings && GameData.playerSpotted)))
        {
            StartChase(sees, "patrol");
            return;
        }

        if (routes == null || routes.Count == 0)
            return;

        if (routeIndex < 0 || routeIndex >= routes.Count)
            return;

        Transform[] pts = routes[routeIndex];

        if (pts == null || pts.Length == 0)
            return;

        if (agent.pathPending)
            return;

        if (waiting)
        {
            waitTimer -= Time.deltaTime;

            if (waitTimer <= 0f)
            {
                waiting = false;
                AdvanceToNextPoint(pts);
            }

            return;
        }

        // with no wait, start turning toward the next point a little early so the cop never slows down
        float reach = waitAtPoint <= 0f
            ? Mathf.Max(rollingReachDistance, pointReachDistance)
            : Mathf.Max(agent.stoppingDistance, pointReachDistance);

        if (agent.remainingDistance <= reach)
        {
            if (waitAtPoint <= 0f)
            {
                AdvanceToNextPoint(pts);
            }
            else
            {
                waiting = true;
                waitTimer = waitAtPoint;
            }
        }
    }

    private void AdvanceToNextPoint(Transform[] pts)
    {
        if (pts.Length > 1)
        {
            if (loopRoute)
            {
                // 0 -> 1 -> 2 -> ... -> last -> 0 -> ...
                pointIndex = (pointIndex + 1) % pts.Length;
            }
            else
            {
                if (pointIndex >= pts.Length - 1)
                    patrolDirection = -1;
                else if (pointIndex <= 0)
                    patrolDirection = 1;

                pointIndex += patrolDirection;
            }
        }

        agent.isStopped = false;
        agent.SetDestination(pts[pointIndex].position);
    }

    // ---------- Investigate ATM ----------

    private void HandleAtmBreached(Vector3 atmPosition)
    {
        if (agent == null || !agent.isOnNavMesh)
        {
            Log("ATM breached, but I'm not on the NavMesh. Ignoring.");
            return;
        }

        if (state == State.Chase)
        {
            Log("ATM breached, but I'm already chasing. Ignoring.");
            return;
        }

        float d = Vector3.Distance(transform.position, atmPosition);

        if (d > atmResponseRadius)
        {
            Log($"ATM breached, too far to respond ({d:F1} > {atmResponseRadius:F1}). Ignoring.");
            return;
        }

        Log($"ATM breached {d:F1} away (radius {atmResponseRadius:F1}). Responding.");
        StartInvestigate(atmPosition);
    }

    private void StartInvestigate(Vector3 atmPosition)
    {
        if (!NavMesh.SamplePosition(
                atmPosition,
                out NavMeshHit hit,
                6f,
                NavMesh.AllAreas))
        {
            Log("ATM is not near the NavMesh (6m). Bake the NavMesh closer to it.");
            return;
        }

        Log($"-> INVESTIGATE (was {state}): going to the ATM.");

        state = State.Investigate;
        waiting = false;
        arrivedAtAtm = false;
        investigateTimer = 0f;
        agent.speed = investigateSpeed;
        investigatePos = hit.position;

        agent.isStopped = false;
        agent.SetDestination(investigatePos);
    }

    private void UpdateInvestigate(bool sees)
    {
        if (Wanted &&
            (sees || (shareSightings && GameData.playerSpotted)))
        {
            StartChase(sees, "investigate");
            return;
        }

        investigateTimer += Time.deltaTime;

        if (!arrivedAtAtm)
        {
            bool close = !agent.pathPending &&
                         agent.remainingDistance <= investigateArriveDistance;

            if (close || investigateTimer >= maxInvestigateTravelTime)
            {
                arrivedAtAtm = true;

                Log(close
                    ? "Arrived at the ATM. Looking around."
                    : "Gave up reaching the ATM. Looking around here.");

                investigateTimer = 0f;
                agent.ResetPath();
            }

            return;
        }

        // Manual rotation is only used for looking around at the ATM.
        agent.updateRotation = false;
        transform.Rotate(
            0f,
            lookAroundSpeed * Time.deltaTime,
            0f
        );

        if (investigateTimer >= investigateWaitTime)
        {
            agent.updateRotation = true;

            if (Wanted)
                StartChase(false, "finished investigating but still wanted");
            else
                GoToNearestRoutePoint("finished investigating the ATM");
        }
    }

    // ---------- Chase ----------

    private void StartChase(bool sees, string from = "")
    {
        Log($"-> CHASE (was {state}, {from}). Wanted because: {WantedReason()}");

        state = State.Chase;
        waiting = false;
        lostTimer = 0f;

        // Restore automatic rotation when starting a chase.
        agent.updateRotation = true;
        agent.isStopped = false;
        agent.speed = chaseSpeed;

        if (sees)
            lastKnownPos = player.position;
        else if (GameData.hasLastKnown)
            lastKnownPos = GameData.lastKnownPlayerPos;
        else
            lastKnownPos = investigatePos != Vector3.zero
                ? investigatePos
                : transform.position;
    }

    private void UpdateChase(bool sees)
    {
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

            if (shareSightings && GameData.hasLastKnown)
                lastKnownPos = GameData.lastKnownPlayerPos;
        }

        Vector3 target =
            (!sees && trackPlayerWhileWanted && player != null)
            ? player.position
            : lastKnownPos;

        // NavMeshAgent handles rotation toward the chase destination.
        agent.updateRotation = true;
        agent.SetDestination(target);

        if (!sees &&
            !trackPlayerWhileWanted &&
            !agent.pathPending &&
            agent.remainingDistance <= investigateArriveDistance)
        {
            // Stop automatic rotation while the cop searches around.
            agent.updateRotation = false;

            transform.Rotate(
                0f,
                lookAroundSpeed * Time.deltaTime,
                0f
            );
        }

        if (player != null &&
            Time.time >= nextCatchTime &&
            IsTouchingPlayer())
        {
            nextCatchTime = Time.time + catchCooldown;

            GameData.LoseLife(1);

            Log($"HIT the player! Lives left: {GameData.playerLives}");

            onCaughtPlayer?.Invoke();
        }
    }

    // ---------- Player catch detection ----------

    private bool IsTouchingPlayer()
    {
        if (playerCollider == null)
            playerCollider = player.GetComponent<Collider>();

        Vector3 flat = player.position - transform.position;
        flat.y = 0f;

        float reach = catchDistance +
                      GetRadius(copCollider) +
                      GetRadius(playerCollider);

        return flat.magnitude <= reach;
    }

    private static float GetRadius(Collider c)
    {
        if (c == null)
            return 0f;

        return Mathf.Max(
            c.bounds.extents.x,
            c.bounds.extents.z
        );
    }

    // ---------- Vision ----------

    public bool CanSeePlayer()
    {
        if (player == null)
        {
            sightInfo = "no player reference";
            return false;
        }

        Vector3 eye = transform.position +
                      Vector3.up * eyeHeight;

        Vector3 target = player.position +
                         Vector3.up * targetHeight;

        Vector3 toPlayer = target - eye;

        float dist = toPlayer.magnitude;

        if (dist > viewDistance)
        {
            if (debugLogs)
                sightInfo = $"out of range {dist:F1} > {viewDistance:F1}";

            return false;
        }

        Vector3 fwd = transform.forward;
        fwd.y = 0f;

        Vector3 flat = toPlayer;
        flat.y = 0f;

        if (flat.sqrMagnitude > 0.0001f)
        {
            float angle = Vector3.Angle(fwd, flat);

            if (angle > viewAngle * 0.5f)
            {
                if (debugLogs)
                    sightInfo = $"outside cone {angle:F0} > {viewAngle * 0.5f:F0}";

                return false;
            }
        }

        if (Physics.Linecast(
                eye,
                target,
                out RaycastHit hit,
                sightBlockers,
                QueryTriggerInteraction.Ignore))
        {
            bool hitPlayer =
                hit.transform == player ||
                hit.transform.IsChildOf(player);

            if (!hitPlayer)
            {
                if (debugLogs)
                    sightInfo = $"blocked by '{hit.transform.name}'";

                return false;
            }
        }

        if (debugLogs)
            sightInfo = "clear";

        return true;
    }

    // ---------- Scene view debug ----------

    private void OnDrawGizmosSelected()
    {
        Vector3 eye = transform.position +
                      Vector3.up * eyeHeight;

        float half = viewAngle * 0.5f;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(
            transform.position,
            atmResponseRadius
        );

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(
            transform.position,
            catchDistance + GetRadius(GetComponent<Collider>())
        );

        Gizmos.color = Color.yellow;

        Vector3 prev = eye +
            Quaternion.Euler(0f, -half, 0f) *
            transform.forward * viewDistance;

        Gizmos.DrawLine(eye, prev);

        const int steps = 24;

        for (int i = 1; i <= steps; i++)
        {
            float a = -half + viewAngle * i / steps;

            Vector3 p = eye +
                Quaternion.Euler(0f, a, 0f) *
                transform.forward * viewDistance;

            Gizmos.DrawLine(prev, p);
            prev = p;
        }

        Gizmos.DrawLine(eye, prev);
    }
}