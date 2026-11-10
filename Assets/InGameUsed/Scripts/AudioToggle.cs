using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class AudioToggle : MonoBehaviour
{
    [Header("Music Settings UI")]
    public Toggle musicToggle;

    void Start()
    {
        if (musicToggle != null)
        {
            // Set the initial checkbox state to match GameData
            musicToggle.isOn = GameData.isMusicOn;
        }
    }

    // Called when the UI Toggle is clicked
    public void OnMusicToggleChanged(bool isOn)
    {
        GameData.isMusicOn = isOn;
        Debug.Log("Music is now: " + (GameData.isMusicOn ? "ON" : "OFF"));
    }
}
