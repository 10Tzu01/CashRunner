
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class policemananimator : MonoBehaviour
{
    private NavMeshAgent agent;
    private Animator animator;

    [Header("Animation Settings")]
    public float animationDampTime = 0.15f;

    private static readonly int Speed =
        Animator.StringToHash("Speed");

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogError("Animator not found!", this);
            enabled = false;
            return;
        }

        // Let the NavMeshAgent control movement.
        animator.applyRootMotion = false;
    }

    void Update()
    {
        if (agent == null || animator == null)
            return;

        // Use horizontal movement speed.
        Vector3 velocity = agent.velocity;
        velocity.y = 0f;

        float speed = velocity.magnitude;

        animator.SetFloat(
            Speed,
            speed,
            animationDampTime,
            Time.deltaTime
        );
    }
}
