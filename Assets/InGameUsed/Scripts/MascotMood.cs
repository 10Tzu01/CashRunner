using UnityEngine;
using UnityEngine.UI;


public class MascotMood : MonoBehaviour
{
    [Header("Faces (drag the sprites from 'sprites sheet#2')")]
    public Sprite neutralFace;   
    public Sprite happyFace;     
    public Sprite scaredFace;    

    [Header("Settings")]
    public float happyTime = 3f;     

    private SpriteRenderer spriteRenderer;
    private Image uiImage;
    private float happyTimer;
    private int lastAtmsBroken;
    private Sprite current;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        uiImage = GetComponent<Image>();

        if (spriteRenderer == null && uiImage == null)
            Debug.LogError("[MascotMood] Needs a SpriteRenderer or an Image on the same object.", this);
    }

    private void Start()
    {
        lastAtmsBroken = GameData.atmsBroken;
        Apply(neutralFace);
    }

    private void Update()
    {
       
        if (GameData.atmsBroken > lastAtmsBroken)
            happyTimer = happyTime;
        lastAtmsBroken = GameData.atmsBroken;  

        if (happyTimer > 0f)
        {
            happyTimer -= Time.deltaTime;       
            Apply(happyFace);
        }
        else if (GameData.IsWanted)
        {
            Apply(scaredFace);
        }
        else
        {
            Apply(neutralFace);
        }
    }

    private void Apply(Sprite s)
    {
        if (s == null || s == current) return;
        current = s;
        if (spriteRenderer != null) spriteRenderer.sprite = s;
        if (uiImage != null) uiImage.sprite = s;
    }
}