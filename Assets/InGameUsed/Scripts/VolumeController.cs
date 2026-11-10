using UnityEngine;
using UnityEngine.UI;

public class VolumeController : MonoBehaviour
{
    [Header("UI Reference")]
    public Slider volumeSlider;

    private void Awake()
    {
        // Safety: if you forgot to drag the slider in the Inspector,
        // try to find one on the same GameObject.
        if (volumeSlider == null)
            volumeSlider = GetComponent<Slider>();
    }

    private void OnEnable()
    {
        // Runs every time this GameObject is enabled.
        // Since your panels start disabled and get enabled when opened,
        // this ensures the slider always reflects the current GameData value.
        if (volumeSlider != null)
        {
            // Set the slider position WITHOUT triggering OnVolumeChanged
            volumeSlider.SetValueWithoutNotify(GameData.masterVolume);

            // Remove first, then add, so we never double-subscribe if
            // OnEnable fires multiple times.
            volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        }

        // Apply the stored volume to the actual AudioListener
        AudioListener.volume = GameData.masterVolume;
    }

    private void OnDisable()
    {
        // Clean unsubscribe so we don't leak listeners between toggles
        if (volumeSlider != null)
            volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
    }

    public void OnVolumeChanged(float value)
    {
        // Update global static variable
        GameData.masterVolume = value;

        // Apply global volume to all game sounds
        AudioListener.volume = value;

        Debug.Log("Master Volume set to: " + GameData.masterVolume);
    }
}