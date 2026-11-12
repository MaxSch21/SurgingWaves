using System;
using UnityEngine;
using UnityEngine.UI;

namespace SurgingWaves.Radio
{
    public sealed class RadioController : MonoBehaviour
    {
        [Serializable]
        public sealed class Station
        {
            public string frequency = "93,4 MHz";
            [TextArea(1, 3)] public string info = "No Info";
            public AudioClip audioTrack;
        }

        private const int MaximumStations = 5;

        [Header("Sender")]
        [SerializeField, Tooltip("Bis zu fünf Sender, von links nach rechts. Nur 5 Einträge")]
        private Station[] stations =
        {
            new Station { frequency = "89,1 MHz" },
            new Station { frequency = "93,4 MHz" },
            new Station { frequency = "96,9 MHz" },
            new Station { frequency = "99,7 MHz" },
            new Station { frequency = "102,1 MHz" }
        };

        [Header("Audio")]
        [SerializeField] private AudioSource stationAudio;
        [SerializeField] private AudioSource switchAudio;
        [SerializeField] private AudioClip stationChangeClip;

        [Header("Anzeige")]
        [SerializeField] private Text frequencyText;
        [SerializeField] private Text infoText;
        [SerializeField] private Image[] stationLights;
        [SerializeField] private Color lightOnColor = new Color(1f, 0.13f, 0.5f);
        [SerializeField] private Color lightOffColor = new Color(0.27f, 0.18f, 0.22f);

        private bool isOn;
        private int stationIndex;

        public bool IsOn => isOn;
        private int StationCount => stations == null ? 0 : Mathf.Min(stations.Length, MaximumStations);

        private void OnEnable()
        {
            RefreshDisplay();
            PlayStation();
        }

        private void OnDisable()
        {
            if (stationAudio != null) stationAudio.Stop();
            if (switchAudio != null) switchAudio.Stop();
            RefreshDisplay();
        }

        public void SetPower(bool value)
        {
            if (isOn == value) return;
            isOn = value;
            if (switchAudio != null) switchAudio.Stop();
            RefreshDisplay();
            PlayStation();
        }

        public void NextStation()
        {
            if (!isActiveAndEnabled || !isOn || StationCount == 0) return;
            stationIndex = (stationIndex + 1) % StationCount;
            RefreshDisplay();
            PlayStation();
            if (switchAudio != null && stationChangeClip != null)
            {
                switchAudio.Stop();
                switchAudio.PlayOneShot(stationChangeClip);
            }
        }

        private void PlayStation()
        {
            if (stationAudio == null) return;
            stationAudio.Stop();
            stationAudio.clip = null;
            if (!isActiveAndEnabled || !isOn || StationCount == 0) return;

            stationIndex = Mathf.Clamp(stationIndex, 0, StationCount - 1);
            Station station = stations[stationIndex];
            stationAudio.clip = station != null ? station.audioTrack : null;
            stationAudio.loop = true;
            if (stationAudio.clip != null) stationAudio.Play();
        }

        private void RefreshDisplay()
        {
            bool showStation = isActiveAndEnabled && isOn && StationCount > 0;
            if (StationCount > 0) stationIndex = Mathf.Clamp(stationIndex, 0, StationCount - 1);
            Station station = showStation ? stations[stationIndex] : null;
            if (frequencyText != null) frequencyText.text = station != null ? station.frequency : string.Empty;
            if (infoText != null)
                infoText.text = showStation
                    ? (station == null || string.IsNullOrWhiteSpace(station.info) ? "No Info" : station.info)
                    : string.Empty;

            if (stationLights == null) return;
            for (int i = 0; i < stationLights.Length; i++)
                if (stationLights[i] != null)
                    stationLights[i].color = showStation && i == stationIndex ? lightOnColor : lightOffColor;
        }
    }
}
