using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

// Put this on an empty GameObject in the NewMap scene.
// Each day lasts dayDuration seconds.
//   - As soon as the day's quota is earned, the exit OPENS: reaching the ExitZone ends the day
//     early and opens the shop page (Continue button starts the next day).
//   - If the day timer runs out first, an ESCAPE timer starts and the player must reach the exit
//     before it hits zero. Reaching it with the quota met -> shop. Quota missed, or escape timer
//     runs out -> lives set to 0 (MenuManager shows your Game Over screen).
// Money earned = GameData.playerScore gained since the day started, so AtmSystem needs no changes.
public class DayManager : MonoBehaviour
{
    [Header("Day Settings")]
    public float dayDuration = 180f;          // seconds per day
    public int baseQuota = 500;               // quota on day 1
    public int quotaIncreasePerDay = 250;     // added for each later day

    [Header("Escape Phase")]
    [Tooltip("Seconds the player gets to reach the exit after the day timer ends.")]
    public float escapeDuration = 30f;
    [Tooltip("Optional: a CHILD object (beam, arrow, glow) shown only while the exit is open (quota met, or escape phase). Do not use the trigger object itself.")]
    public GameObject exitMarker;

    [Header("HUD (optional - auto-created if left empty)")]
    public TextMeshProUGUI dayText;
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI quotaText;
    public TextMeshProUGUI objectiveText;

    [Header("Shop Page (optional - auto-created if left empty)")]
    public GameObject shopPanel;
    public TextMeshProUGUI shopTitleText;
    public TextMeshProUGUI shopSummaryText;
    [Tooltip("Do not also wire this in the Inspector's OnClick; DayManager hooks it itself.")]
    public Button continueButton;

    private float timeLeft;
    private float escapeTimeLeft;
    private bool escaping;
    private bool exitOpen;
    private int dayStartScore;
    private bool dayActive;
    private bool shopOpen;

    public int Quota => baseQuota + (GameData.currentDay - 1) * quotaIncreasePerDay;
    public int DayEarned => GameData.playerScore - dayStartScore;

    private void Start()
    {
        if (shopPanel == null || continueButton == null || timeText == null || objectiveText == null) BuildMissingUI();

        continueButton.onClick.AddListener(ContinueToNextDay);
        shopPanel.SetActive(false);
        StartDay();
    }

    private void Update()
    {
        UpdateHUD();

        if (!dayActive || GameData.GamePause) return;   // waits while main menu / pause / shop is open
        if (GameData.playerLives <= 0) { dayActive = false; escaping = false; return; }

        if (escaping)
        {
            escapeTimeLeft -= Time.deltaTime;
            if (escapeTimeLeft <= 0f)
            {
                escapeTimeLeft = 0f;
                FailDay("Didn't reach the exit in time");
            }
            return;
        }

        timeLeft -= Time.deltaTime;

        // quota reached -> exit opens so the player can leave early
        if (!exitOpen && DayEarned >= Quota) SetExitOpen(true);

        if (timeLeft <= 0f)
        {
            timeLeft = 0f;
            BeginEscape();
        }
    }

    // ---------- Day flow ----------

    private void StartDay()
    {
        timeLeft = dayDuration;
        dayStartScore = GameData.playerScore;
        dayActive = true;
        escaping = false;
        SetExitOpen(false);

        // clean slate for the cops each morning
        GameData.wantedTimeLeft = 0f;
        GameData.playerSpotted = false;
        GameData.hasLastKnown = false;
        GameData.isRobbing = false;
    }

    private void BeginEscape()
    {
        escaping = true;
        escapeTimeLeft = escapeDuration;
        SetExitOpen(true);
        Debug.Log($"[Day] Day {GameData.currentDay} time is up. Reach the exit within {escapeDuration}s!");
    }

    // Called by ExitZone when the player enters the trigger.
    public void PlayerReachedExit()
    {
        if (!dayActive) return;
        bool quotaMet = DayEarned >= Quota;
        if (!escaping && !quotaMet) return;   // exit stays closed until quota is met or time is up

        escaping = false;
        dayActive = false;
        SetExitOpen(false);

        if (quotaMet) OpenShop();
        else FailDay($"Quota not met: {DayEarned}/{Quota}");
    }

    private void SetExitOpen(bool open)
    {
        exitOpen = open;
        if (exitMarker) exitMarker.SetActive(open);
    }

    private void FailDay(string reason)
    {
        dayActive = false;
        escaping = false;
        SetExitOpen(false);
        Debug.Log($"[Day] Day {GameData.currentDay} failed: {reason}");
        GameData.playerLives = 0;       // MenuManager.Update shows the Game Over page
    }

    private void OpenShop()
    {
        shopOpen = true;
        if (shopTitleText) shopTitleText.text = $"Day {GameData.currentDay} Complete!";
        if (shopSummaryText)
            shopSummaryText.text = $"Collected ${DayEarned} / ${Quota}\nBank: ${GameData.playerScore}";

        shopPanel.SetActive(true);
        SetPaused(true);
    }

    public void ContinueToNextDay()
    {
        if (!shopOpen) return;
        shopOpen = false;

        shopPanel.SetActive(false);
        GameData.currentDay++;
        StartDay();
        SetPaused(false);
    }

    private void SetPaused(bool paused)
    {
        GameData.GamePause = paused;
        Time.timeScale = paused ? 0f : 1f;
        Cursor.lockState = paused ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = paused;
    }

    private void UpdateHUD()
    {
        if (dayText) dayText.text = "Day " + GameData.currentDay;
        if (quotaText) quotaText.text = $"${Mathf.Max(0, DayEarned)} / ${Quota}";
        if (timeText)
        {
            int t = Mathf.CeilToInt(escaping ? escapeTimeLeft : timeLeft);
            timeText.text = $"{t / 60:00}:{t % 60:00}";
            timeText.color = escaping ? Color.red : Color.white;
        }
        if (objectiveText)
        {
            if (escaping) { objectiveText.text = "GET TO THE EXIT!"; objectiveText.color = Color.red; }
            else if (exitOpen) { objectiveText.text = "QUOTA MET - HEAD TO THE EXIT!"; objectiveText.color = Color.green; }
            else objectiveText.text = "";
        }
    }

    // ---------- Auto-built UI (only creates what you didn't assign) ----------

    private void BuildMissingUI()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        var canvasGO = new GameObject("DayCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // HUD
        if (dayText == null)
            dayText = MakeText(canvasGO.transform, "DayText", 40, new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(500, 60));
        if (timeText == null)
            timeText = MakeText(canvasGO.transform, "TimeText", 56, new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(500, 80));
        if (quotaText == null)
            quotaText = MakeText(canvasGO.transform, "QuotaText", 40, new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(500, 60));

        if (objectiveText == null)
        {
            objectiveText = MakeText(canvasGO.transform, "ObjectiveText", 64, new Vector2(0.5f, 1f), new Vector2(0, -230), new Vector2(1000, 90));
            objectiveText.color = Color.red;
        }

        // Shop page
        if (shopPanel == null)
        {
            shopPanel = new GameObject("ShopPanel", typeof(RectTransform), typeof(Image));
            shopPanel.transform.SetParent(canvasGO.transform, false);
            var rt = shopPanel.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            shopPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

            shopTitleText = MakeText(shopPanel.transform, "ShopTitle", 72, new Vector2(0.5f, 0.5f), new Vector2(0, 280), new Vector2(1200, 100));
            shopSummaryText = MakeText(shopPanel.transform, "ShopSummary", 48, new Vector2(0.5f, 0.5f), new Vector2(0, 130), new Vector2(1200, 160));
            var soon = MakeText(shopPanel.transform, "ShopPlaceholder", 36, new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1200, 80));
            soon.text = "(Item shop coming soon)";
            soon.color = new Color(1f, 1f, 1f, 0.6f);
        }

        if (continueButton == null)
        {
            var btnGO = new GameObject("ContinueButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGO.transform.SetParent(shopPanel.transform, false);
            var brt = btnGO.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0, -200);
            brt.sizeDelta = new Vector2(420, 100);
            btnGO.GetComponent<Image>().color = new Color(0.2f, 0.7f, 0.3f, 1f);
            continueButton = btnGO.GetComponent<Button>();

            var label = MakeText(btnGO.transform, "Label", 44, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 100));
            label.text = "Next Day";
        }
    }

    private TextMeshProUGUI MakeText(Transform parent, string name, float size, Vector2 anchor, Vector2 pos, Vector2 dims)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, anchor.y);
        rt.anchoredPosition = pos;
        rt.sizeDelta = dims;
        var t = go.GetComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }
}