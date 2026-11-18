using UnityEngine;

// Put this on an object with a Box Collider (Is Trigger is forced on at start).
// Does nothing until the day timer ends; then entering it with the Player finishes the day.
[RequireComponent(typeof(Collider))]
public class ExitZone : MonoBehaviour
{
    private DayManager dayManager;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        dayManager = FindFirstObjectByType<DayManager>();
        if (dayManager == null)
            Debug.LogError("[ExitZone] No DayManager found in the scene.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (dayManager != null && other.CompareTag("Player"))
            dayManager.PlayerReachedExit();
    }
}