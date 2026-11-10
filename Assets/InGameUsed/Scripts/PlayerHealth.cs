using UnityEngine;
using UnityEngine.UI;

public class HeartHUD : MonoBehaviour
{
    [Header("Hearts already in the scene (left to right)")]
    public Image[] hearts;

    [Header("Sprites")]
    public Sprite fullHeart;     // unbroken heart
    public Sprite brokenHeart;   // broken heart

    [Header("Testing")]
    [SerializeField] private bool useTestLives = false;   // tick to override lives from the Inspector
    [SerializeField] private int testLives = 3;           // change this while the game runs
    [SerializeField] private int currentLives;            // shows GameData.playerLives (for viewing)

    private int lastLives = -1;

    private void Update()
    {
        // testing: push the Inspector value into GameData
        if (useTestLives)
            GameData.playerLives = Mathf.Clamp(testLives, 0, GameData.maxLives);

        currentLives = GameData.playerLives;

        // only redraw when the number of lives changes
        if (GameData.playerLives != lastLives)
            Refresh();
    }

    private void Refresh()
    {
        lastLives = GameData.playerLives;

        for (int i = 0; i < hearts.Length; i++)
        {
            // heart number i is full if it's below the current lives, otherwise broken
            hearts[i].sprite = (i < lastLives) ? fullHeart : brokenHeart;
        }
    }
}