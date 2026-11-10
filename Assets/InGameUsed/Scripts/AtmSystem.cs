using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class NewMonoBehaviourScript : MonoBehaviour
{
    [Header("References")]
    public GameObject cashPrefab;
    public Transform spawnPoint;
    public Image progressBar;          // optional UI Image (Image Type = Filled)

    [Header("Hold Settings")]
    public Key key = Key.E;            // new Input System key
    public float holdTime = 3f;

    [Header("Spawn Settings")]
    public int cashCount = 10;
    public float delayBetweenSpawns = 0.08f;
    public float ejectForce = 4f;
    public float randomSpread = 0.3f;
    public float cashLifetime = 30f;   // 0 = never destroy

    [Header("Reward & Cooldown")]
    public int scorePerRob = 300;
    [Tooltip("Seconds after a rob before this ATM can be robbed again.")]
    public float cooldownTime = 30f;
    [Tooltip("Optional: a TMP text that flashes '+300' when you rob. Leave empty to skip.")]
    public TextMeshProUGUI rewardText;
    public float rewardShowTime = 1.5f;
    [Tooltip("Optional: a TMP text on/near the ATM that shows the cooldown (e.g. 'Ready in 12s').")]
    public TextMeshProUGUI cooldownText;

    [Header("Debug")]
    public bool debugLogs = true;

    private bool playerInside;
    private bool isSpawning;
    private bool holding;
    private int lastLoggedSecond;
    private float holdTimer;
    private bool hasBeenBreached;   // true while this ATM is on cooldown
    private float cooldownTimer;
    private float rewardTimer;

    private void Log(string msg)
    {
        if (debugLogs) Debug.Log($"[ATM] {msg}", this);
    }

    // ---------- Setup check ----------
    private void Start()
    {
        Collider col = GetComponent<Collider>();
        if (col == null)
            Debug.LogError("[ATM] No Collider on this object. Add a BoxCollider with Is Trigger ticked.", this);
        else if (!col.isTrigger)
            Debug.LogWarning("[ATM] Collider found but 'Is Trigger' is NOT ticked, so OnTriggerEnter will never fire.", this);

        if (cashPrefab == null)
            Debug.LogError("[ATM] Cash Prefab is not assigned.", this);
        else if (!cashPrefab.TryGetComponent<Rigidbody>(out _))
            Debug.LogWarning("[ATM] Cash Prefab has no Rigidbody, bills will appear but not fly out.", this);

        if (spawnPoint == null)
            Debug.LogError("[ATM] Spawn Point is not assigned.", this);

        if (rewardText != null) rewardText.gameObject.SetActive(false);
        if (cooldownText != null) cooldownText.gameObject.SetActive(false);

        Log("Ready. Walk into the trigger zone and hold " + key + " for " + holdTime + "s.");
    }

    // ---------- Trigger ----------
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInside = true;
            Log($"Player ENTERED zone ({other.name}).");
        }
        else
        {
            Log($"Something entered zone but tag is '{other.tag}', not 'Player' ({other.name}).");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInside = false;
            Log("Player LEFT zone.");
            CancelHold();
        }
    }

    // ---------- Hold logic ----------
    private void Update()
    {
        UpdateCooldown();
        UpdateRewardText();

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        var keyControl = kb[key];

        // explain why a key press does nothing
        if (keyControl.wasPressedThisFrame)
        {
            if (!playerInside) Log(key + " pressed but player is NOT inside the zone.");
            else if (hasBeenBreached) Log(key + $" pressed but this ATM is on cooldown ({cooldownTimer:F0}s left).");
            else if (isSpawning) Log(key + " pressed but cash is still spawning.");
        }

        if (!playerInside || isSpawning || hasBeenBreached)
        {
            CancelHold();
            return;
        }

        if (keyControl.isPressed)
        {
            if (!holding)
            {
                holding = true;
                lastLoggedSecond = 0;
                GameData.isRobbing = true;
                Log("Started holding " + key + ". Player is now ROBBING.");
            }

            holdTimer += Time.deltaTime;
            if (progressBar) progressBar.fillAmount = holdTimer / holdTime;

            int sec = Mathf.FloorToInt(holdTimer);
            if (sec > lastLoggedSecond)
            {
                lastLoggedSecond = sec;
                Log($"Holding... {sec}/{holdTime}s");
            }

            if (holdTimer >= holdTime)
            {
                holding = false;   // so it doesn't log as cancelled
                ResetHold();

                // breach: ATM goes on cooldown, player gets paid
                hasBeenBreached = true;
                cooldownTimer = cooldownTime;
                GameData.playerScore += scorePerRob;
                ShowReward();
                GameData.atmsBroken++;
                GameData.StartWanted();                          // wanted timer turns ON (100%)
                GameData.RaiseAtmBreached(transform.position);   // tells the cops
                Log($"ATM BREACHED! +{scorePerRob} (score {GameData.playerScore}). Total ATMs broken: {GameData.atmsBroken}");

                StartCoroutine(SpewCash());
            }
        }
        else
        {
            CancelHold();
        }
    }

    // ---------- Cooldown & reward display ----------
    private void UpdateCooldown()
    {
        if (!hasBeenBreached) return;

        cooldownTimer -= Time.deltaTime;   // 0 while paused (timeScale = 0)

        if (cooldownText != null)
        {
            cooldownText.gameObject.SetActive(true);
            cooldownText.text = "Ready in " + Mathf.CeilToInt(Mathf.Max(0f, cooldownTimer)) + "s";
        }

        // wait for the cash to finish spewing before re-arming
        if (cooldownTimer <= 0f && !isSpawning)
        {
            hasBeenBreached = false;
            if (cooldownText != null) cooldownText.gameObject.SetActive(false);
            Log("Cooldown over. This ATM can be robbed again.");
        }
    }

    private void ShowReward()
    {
        if (rewardText == null) return;
        rewardText.text = "+" + scorePerRob;
        rewardText.gameObject.SetActive(true);
        rewardTimer = rewardShowTime;
    }

    private void UpdateRewardText()
    {
        if (rewardText == null || rewardTimer <= 0f) return;
        rewardTimer -= Time.deltaTime;
        if (rewardTimer <= 0f) rewardText.gameObject.SetActive(false);
    }

    private void CancelHold()
    {
        if (holding)
        {
            Log($"Hold cancelled at {holdTimer:F1}s (released key, left zone, or blocked).");
            GameData.isRobbing = false;
        }
        holding = false;
        ResetHold();
    }

    private void ResetHold()
    {
        holdTimer = 0f;
        if (progressBar) progressBar.fillAmount = 0f;
    }

    // ---------- Spawning ----------
    private IEnumerator SpewCash()
    {
        if (cashPrefab == null || spawnPoint == null)
        {
            Debug.LogError("[ATM] Can't spawn: Cash Prefab or Spawn Point is missing.", this);
            GameData.isRobbing = false;
            yield break;
        }

        isSpawning = true;
        Log($"Spawning {cashCount} bills...");

        for (int i = 0; i < cashCount; i++)
        {
            GameObject cash = Instantiate(cashPrefab, spawnPoint.position, Random.rotation);

            if (cash.TryGetComponent(out Rigidbody rb))
            {
                Vector3 dir = spawnPoint.forward
                            + spawnPoint.right * Random.Range(-randomSpread, randomSpread)
                            + spawnPoint.up * Random.Range(0.1f, randomSpread);

                rb.AddForce(dir.normalized * ejectForce, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);
            }
            else
            {
                Debug.LogWarning($"[ATM] Bill {i + 1} has no Rigidbody, no force applied.", this);
            }

            if (cashLifetime > 0f) Destroy(cash, cashLifetime);

            Log($"Spawned bill {i + 1}/{cashCount} at {spawnPoint.position}");

            yield return new WaitForSeconds(delayBetweenSpawns);
        }

        isSpawning = false;
        GameData.isRobbing = false;
        Log("Done spewing cash. Player is no longer robbing. ATM on cooldown.");
    }
}