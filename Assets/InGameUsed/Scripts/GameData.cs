using System;
using UnityEngine;

public static class GameData
{
    public static int playerScore = 0;
    public static string playerName = "Player 1";
    public static float masterVolume = 0.8f;
    public static bool isMusicOn = true;
    public static bool GamePause = false;
    public static bool isRobbing = false;

    public static int atmsBroken = 0;

   
    public static int maxLives = 3;
    public static int playerLives = 3;

    
    public static event Action<Vector3> OnAtmBreached;
    public static void RaiseAtmBreached(Vector3 atmPosition) => OnAtmBreached?.Invoke(atmPosition);

   
    public static float wantedDuration = 20f;       
    public static float wantedTimeLeft = 0f;
    public static bool playerSpotted = false;       
    public static bool hasLastKnown = false;
    public static Vector3 lastKnownPlayerPos;       

    public static bool IsWanted => isRobbing || wantedTimeLeft > 0f;
    public static float WantedTimeLeft => isRobbing ? wantedDuration : wantedTimeLeft;
    public static float Wanted01 => wantedDuration <= 0f ? 0f : Mathf.Clamp01(WantedTimeLeft / wantedDuration);  

    private static int lastTickFrame = -1;

    public static void StartWanted()
    {
        wantedTimeLeft = wantedDuration;
    }

   
    public static void ReportSighting(Vector3 playerPos)
    {
        lastKnownPlayerPos = playerPos;
        hasLastKnown = true;
        playerSpotted = true;
        if (IsWanted) wantedTimeLeft = wantedDuration;
    }


    public static void Tick()
    {
        if (lastTickFrame == Time.frameCount) return;
        lastTickFrame = Time.frameCount;
        if (GamePause || isRobbing || wantedTimeLeft <= 0f) return;

        wantedTimeLeft -= Time.deltaTime;
        if (wantedTimeLeft <= 0f)
        {
            wantedTimeLeft = 0f;
            playerSpotted = false;
            hasLastKnown = false;
        }
    }

    // Removes lives (never below 0). HeartHUD reads playerLives every frame, so the hearts update by themselves.
    public static void LoseLife(int amount = 1)
    {
        playerLives = Mathf.Max(0, playerLives - amount);
    }

    public static void ResetData()
    {
        playerScore = 0;
        playerName = "Player 1";
        atmsBroken = 0;
        playerLives = maxLives;
        wantedTimeLeft = 0f;
        playerSpotted = false;
        hasLastKnown = false;
        isRobbing = false;
    }
}