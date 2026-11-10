using UnityEngine;
using TMPro;

public class showData : MonoBehaviour
{
    public TextMeshProUGUI scoreText;
    public TextMeshProUGUI timeText;

    private float elapsed;

    private void Update()
    {
        if (!GameData.GamePause) elapsed += Time.deltaTime;

        if (scoreText != null)
            scoreText.text = "Score: " + GameData.playerScore;

        if (timeText != null)
        {
            int m = Mathf.FloorToInt(elapsed / 60f);
            int s = Mathf.FloorToInt(elapsed % 60f);
            timeText.text = $"Time: {m:00}:{s:00}";
        }
    }
}