using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;

/// <summary>A temporary desktop player, independent of headset tracking.</summary>
[RequireComponent(typeof(CharacterController))]
public sealed class DebugFpsController : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
    [SerializeField, Min(1f)] private float sprintMultiplier = 1.8f;
    [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.1f;
    [SerializeField, Min(0f)] private float jumpHeight = 1f;
    [SerializeField] private float gravity = -20f;

    private CharacterController controller;
    private float verticalSpeed;
    private float pitch;
    private SurgingWaves.Radar.FpsHelmInteractor helmInteractor;

    public bool InputBlocked { get; set; } = true;
    public Camera PlayerCamera => playerCamera;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);
        if (playerCamera == null)
        {
            Debug.LogError("Debug FPS Controller needs a child camera.", this);
            enabled = false;
            return;
        }
        helmInteractor = GetComponent<SurgingWaves.Radar.FpsHelmInteractor>();
        if (helmInteractor == null)
            helmInteractor = gameObject.AddComponent<SurgingWaves.Radar.FpsHelmInteractor>();
        // HDRP controls stereo rendering through its additional camera data.
        if (!playerCamera.TryGetComponent<HDAdditionalCameraData>(out var cameraData))
            cameraData = playerCamera.gameObject.AddComponent<HDAdditionalCameraData>();
        cameraData.xrRendering = false;
    }

    private void OnEnable()
    {
        verticalSpeed = 0f;
    }

    public void SetSpawnPose(Vector3 feetPosition, float yaw)
    {
        CharacterController character = GetComponent<CharacterController>();
        bool wasEnabled = character.enabled;
        character.enabled = false;
        transform.SetPositionAndRotation(feetPosition, Quaternion.Euler(0f, yaw, 0f));
        character.enabled = wasEnabled;
        playerCamera.transform.localRotation = Quaternion.identity;
        pitch = 0f;
        verticalSpeed = 0f;
    }

    private void Update()
    {
        if (!Application.isFocused || Time.deltaTime <= 0f)
        {
            helmInteractor.ProcessInput(playerCamera, false);
            return;
        }

        bool acceptInput = !InputBlocked && Cursor.lockState == CursorLockMode.Locked;
        bool usingHelm = helmInteractor.ProcessInput(playerCamera, acceptInput);
        acceptInput &= !usingHelm;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        Vector2 move = Vector2.zero;
        bool sprint = false;

        if (acceptInput)
        {
            if (mouse != null)
            {
                // Mouse delta already represents this frame's movement.
                Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
                transform.Rotate(0f, delta.x, 0f, Space.World);
                pitch = Mathf.Clamp(pitch - delta.y, -85f, 85f);
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            if (keyboard != null)
            {
                move.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                move.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                move = Vector2.ClampMagnitude(move, 1f);
                sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            }
        }

        if (controller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;

        if (acceptInput && keyboard != null && keyboard.spaceKey.wasPressedThisFrame && controller.isGrounded)
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);

        // Gravity continues while the menu is open, but movement and looking stop.
        verticalSpeed += gravity * Time.deltaTime;
        Vector3 velocity = (transform.right * move.x + transform.forward * move.y)
            * moveSpeed * (sprint ? sprintMultiplier : 1f);
        velocity.y = verticalSpeed;
        controller.Move(velocity * Time.deltaTime);
    }
}
