using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using SurgingWaves;

/// <summary>Desktop debug overlay. Keep this object outside both player origins.</summary>
[DefaultExecutionOrder(-1000)]
public sealed class DebugPlayerMenu : MonoBehaviour
{
    [System.Serializable]
    private sealed class EnvironmentPreset
    {
        public string name = "Environment";
        public VolumeProfile volumeProfile = null;
        public Light directionalLight = null;

        public bool IsValid => volumeProfile != null && directionalLight != null
            && directionalLight.type == LightType.Directional;
    }

    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private DebugFpsController fpsOrigin;
    [Tooltip("Boat motion component controlled by the separate height and tilt buttons.")]
    [SerializeField] private BoatWaveMotion boatMotion;
    [SerializeField] private bool startInFpsMode;
    [SerializeField] private Transform fpsSpawnPoint;

    [Header("Environment Presets")]
    [SerializeField] private Volume environmentVolume;
    [Tooltip("Cycle through HDRP sky and fog profiles. Each needs a Volume Profile and a directional light. "
        + "The scene's initial environment is kept until you select a preset in the debug menu.")]
    [SerializeField] private EnvironmentPreset[] environmentPresets = new EnvironmentPreset[0];

    private int activeEnvironmentIndex = -1;

    private readonly List<LocomotionProvider> pausedLocomotion = new List<LocomotionProvider>();
    private Camera sceneCamera;
    private bool originalSceneCameraEnabled;
    private AudioListener sceneListener;
    private bool originalSceneListenerEnabled;
    private bool menuOpen;
    private bool fpsActive;
    private bool fpsSpawned;
    private bool initialized;
    private bool originalXrActive;
    private bool originalCursorVisible;
    private CursorLockMode originalCursorLock;
    private GUIStyle titleStyle;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;
    private Vector2 menuScroll;

    private void Start()
    {
        if (xrOrigin == null)
        {
            foreach (XROrigin candidate in FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene == gameObject.scene)
                {
                    xrOrigin = candidate;
                    break;
                }
            }
        }

        if (fpsOrigin == null || fpsOrigin.PlayerCamera == null || (xrOrigin != null && xrOrigin.Camera == null))
        {
            Debug.LogError("Debug Player Controls needs an FPS Origin with a camera. An optional XR Origin also needs a camera.", this);
            enabled = false;
            return;
        }

        originalXrActive = xrOrigin != null && xrOrigin.gameObject.activeSelf;
        if (xrOrigin == null)
        {
            sceneCamera = Camera.main;
            if (sceneCamera == fpsOrigin.PlayerCamera || (sceneCamera != null && sceneCamera.gameObject.scene != gameObject.scene))
                sceneCamera = null;
            if (sceneCamera != null)
            {
                originalSceneCameraEnabled = sceneCamera.enabled;
                sceneListener = sceneCamera.GetComponent<AudioListener>();
                originalSceneListenerEnabled = sceneListener != null && sceneListener.enabled;
            }
        }
        originalCursorVisible = Cursor.visible;
        originalCursorLock = Cursor.lockState;
        if (environmentVolume == null)
        {
            foreach (Volume candidate in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != gameObject.scene || !candidate.isGlobal) continue;
                environmentVolume = candidate;
                break;
            }
        }
        fpsOrigin.InputBlocked = true;
        fpsOrigin.gameObject.SetActive(false);
        if (xrOrigin != null) xrOrigin.gameObject.SetActive(true);
        initialized = true;
        ApplyCursor();
        if (startInFpsMode || xrOrigin == null || !UnityEngine.XR.XRSettings.isDeviceActive)
            SwitchPlayer();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.iKey.wasPressedThisFrame)
                SetMenuOpen(!menuOpen);
            else if (keyboard.escapeKey.wasPressedThisFrame)
                SetMenuOpen(true);
        }

        // Unity releases the cursor on Escape/focus loss. A game-view click recaptures it.
        if (fpsActive && !menuOpen && Application.isFocused && Mouse.current != null
            && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            ApplyCursor();
    }

    private void SetMenuOpen(bool open)
    {
        menuOpen = open;
        fpsOrigin.InputBlocked = menuOpen || !fpsActive;
        RestoreLocomotion();
        if (menuOpen && !fpsActive)
            PauseLocomotion();
        ApplyCursor();
    }

    private void SwitchPlayer()
    {
        RestoreLocomotion();
        if (!fpsActive)
        {
            Camera sourceCamera = xrOrigin != null ? xrOrigin.Camera : sceneCamera;
            Vector3 feet = fpsOrigin.transform.position;
            float yaw = fpsOrigin.transform.eulerAngles.y;
            if (sourceCamera != null)
            {
                feet = sourceCamera.transform.position;
                feet.y = xrOrigin != null ? xrOrigin.transform.position.y
                    : feet.y - fpsOrigin.PlayerCamera.transform.localPosition.y;
                yaw = sourceCamera.transform.eulerAngles.y;
            }

            // Disabling the entire rig also releases grabs, tracking, its camera and listener.
            if (xrOrigin != null) xrOrigin.gameObject.SetActive(false);
            else if (sceneCamera != null)
            {
                sceneCamera.enabled = false;
                if (sceneListener != null) sceneListener.enabled = false;
            }
            if (!fpsSpawned)
            {
                // Prefer the actual floor beneath the headset over a possibly seated origin.
                Vector3 rayStart = (sourceCamera != null ? sourceCamera.transform.position : feet + Vector3.up * 1.65f)
                    + Vector3.up * 0.2f;
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && Vector3.Dot(hit.normal, Vector3.up) > 0.5f)
                    feet = hit.point + Vector3.up * 0.05f;

                if (fpsSpawnPoint != null)
                {
                    feet = fpsSpawnPoint.position;
                    yaw = fpsSpawnPoint.eulerAngles.y;
                }
                fpsOrigin.SetSpawnPose(feet, yaw);
                fpsSpawned = true;
            }

            Camera fpsCamera = fpsOrigin.PlayerCamera;
            if (sourceCamera != null)
            {
                fpsCamera.clearFlags = sourceCamera.clearFlags;
                fpsCamera.backgroundColor = sourceCamera.backgroundColor;
                fpsCamera.cullingMask = sourceCamera.cullingMask;
                fpsCamera.farClipPlane = sourceCamera.farClipPlane;
                if (!fpsCamera.TryGetComponent<HDAdditionalCameraData>(out var fpsData))
                    fpsData = fpsCamera.gameObject.AddComponent<HDAdditionalCameraData>();
                if (sourceCamera.TryGetComponent<HDAdditionalCameraData>(out var sourceData))
                    sourceData.CopyTo(fpsData);
                fpsData.xrRendering = false;
            }
            fpsOrigin.gameObject.SetActive(true);
            fpsActive = true;
        }
        else if (xrOrigin != null)
        {
            fpsOrigin.InputBlocked = true;
            fpsOrigin.gameObject.SetActive(false);
            xrOrigin.gameObject.SetActive(true);
            fpsActive = false;
        }

        // Keep the menu open so its current mode and the reverse switch stay accessible.
        SetMenuOpen(menuOpen);
    }

    private int GetNextEnvironmentIndex()
    {
        if (environmentPresets == null || environmentPresets.Length == 0)
            return -1;

        for (int offset = 1; offset <= environmentPresets.Length; offset++)
        {
            int index = (activeEnvironmentIndex + offset) % environmentPresets.Length;
            EnvironmentPreset preset = environmentPresets[index];
            if (preset != null && preset.IsValid)
                return index;
        }
        return -1;
    }

    private string GetEnvironmentName(int index)
    {
        if (environmentPresets == null || index < 0 || index >= environmentPresets.Length
            || environmentPresets[index] == null)
            return "Szeneneinstellung";

        string presetName = environmentPresets[index].name;
        return string.IsNullOrWhiteSpace(presetName) ? "Environment " + (index + 1) : presetName;
    }

    private void SwitchEnvironment()
    {
        int nextIndex = GetNextEnvironmentIndex();
        if (nextIndex < 0 || environmentVolume == null)
            return;

        EnvironmentPreset preset = environmentPresets[nextIndex];
        // Only the chosen directional light should illuminate this scene.
        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.gameObject.scene == gameObject.scene && light.type == LightType.Directional)
                light.enabled = false;
        }

        preset.directionalLight.gameObject.SetActive(true);
        preset.directionalLight.enabled = true;
        RenderSettings.sun = preset.directionalLight;
        environmentVolume.sharedProfile = preset.volumeProfile;
        activeEnvironmentIndex = nextIndex;
    }

    private void PauseLocomotion()
    {
        if (xrOrigin == null) return;
        foreach (LocomotionProvider provider in xrOrigin.GetComponentsInChildren<LocomotionProvider>(true))
        {
            if (provider == null || !provider.enabled)
                continue;
            pausedLocomotion.Add(provider);
            provider.enabled = false;
        }
    }

    private void RestoreLocomotion()
    {
        foreach (LocomotionProvider provider in pausedLocomotion)
        {
            if (provider != null)
                provider.enabled = true;
        }
        pausedLocomotion.Clear();
    }

    private void ApplyCursor()
    {
        bool capture = fpsActive && !menuOpen && Application.isFocused;
        Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !capture;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (initialized)
            ApplyCursor();
    }

    private void OnDisable()
    {
        if (!initialized)
            return;

        RestoreLocomotion();
        if (fpsOrigin != null)
        {
            fpsOrigin.InputBlocked = true;
            fpsOrigin.gameObject.SetActive(false);
        }
        if (xrOrigin != null)
            xrOrigin.gameObject.SetActive(originalXrActive);
        if (sceneCamera != null) sceneCamera.enabled = originalSceneCameraEnabled;
        if (sceneListener != null) sceneListener.enabled = originalSceneListenerEnabled;
        Cursor.lockState = originalCursorLock;
        Cursor.visible = originalCursorVisible;
        initialized = false;
    }

    private void OnGUI()
    {
        if (!initialized)
            return;

        // IMGUI keeps this temporary overlay independent of the scene's XR EventSystem.
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true };
        }

        if (!menuOpen)
        {
            GUI.Box(new Rect(12f, 12f, Mathf.Min(370f, Screen.width - 24f), 32f),
                "I: Debug-Menue  |  " + (fpsActive ? "FPS Origin" : "XR Origin (VR)"));
            return;
        }

        float width = Mathf.Min(440f, Screen.width - 24f);
        float height = Mathf.Min(560f, Screen.height - 24f);
        Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUI.Box(panel, GUIContent.none);
        GUILayout.BeginArea(new Rect(panel.x + 20f, panel.y + 16f, width - 40f, height - 32f));
        menuScroll = GUILayout.BeginScrollView(menuScroll);
        GUILayout.Label("Debug-Menue", titleStyle);
        GUILayout.Space(8f);
        GUILayout.Label("Aktiver Spieler und Kamera:\n" + (fpsActive ? "FPS Origin (WASD + Maus)" : "XR Origin (VR Player)"), labelStyle);
        GUILayout.Space(14f);
        GUI.enabled = xrOrigin != null;
        if (GUILayout.Button(xrOrigin == null ? "FPS-Modus (kein XR Origin in dieser Szene)"
            : fpsActive ? "Zu XR Origin (VR) wechseln" : "Zu FPS Origin wechseln", buttonStyle, GUILayout.Height(48f)))
            SwitchPlayer();
        GUI.enabled = true;
        GUILayout.Space(12f);
        GUI.enabled = boatMotion != null && boatMotion.isActiveAndEnabled;
        if (GUILayout.Button(boatMotion == null ? "No boat motion assigned"
            : "Boat height: " + (boatMotion.HeightMotionEnabled ? "ON - turn off" : "OFF - turn on"),
            buttonStyle, GUILayout.Height(44f)))
            boatMotion.HeightMotionEnabled = !boatMotion.HeightMotionEnabled;
        GUILayout.Space(8f);
        if (GUILayout.Button(boatMotion == null ? "No boat motion assigned"
            : "Boat tilt: " + (boatMotion.TiltMotionEnabled ? "ON - turn off" : "OFF - turn on"),
            buttonStyle, GUILayout.Height(44f)))
            boatMotion.TiltMotionEnabled = !boatMotion.TiltMotionEnabled;
        GUI.enabled = true;
        GUILayout.Space(12f);
        GUILayout.Label("Environment: " + GetEnvironmentName(activeEnvironmentIndex), labelStyle);
        int nextEnvironmentIndex = GetNextEnvironmentIndex();
        GUI.enabled = nextEnvironmentIndex >= 0 && environmentVolume != null;
        if (GUILayout.Button(nextEnvironmentIndex >= 0 ? "Environment wechseln: " + GetEnvironmentName(nextEnvironmentIndex)
            : "Environment-Presets im Inspector vervollstaendigen", buttonStyle, GUILayout.Height(44f)))
            SwitchEnvironment();
        GUI.enabled = true;
        GUILayout.Space(12f);
        GUILayout.Label("FPS: WASD bewegen | Maus umsehen\nShift rennen | Leertaste springen\nI oeffnen / schliessen | Esc Maus freigeben", labelStyle);
        GUILayout.EndScrollView();
        GUILayout.Space(8f);
        if (GUILayout.Button("Schliessen (I)", buttonStyle, GUILayout.Height(36f)))
            SetMenuOpen(false);
        GUILayout.EndArea();
    }
}
