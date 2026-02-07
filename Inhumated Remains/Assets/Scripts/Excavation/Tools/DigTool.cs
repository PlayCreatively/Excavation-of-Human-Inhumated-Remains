using UnityEngine;
using UnityEngine.InputSystem;

namespace Excavation.Tools
{
    /// <summary>
    /// Player's excavation tool that handles input and digging operations.
    /// </summary>
    public class DigTool : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Core.ExcavationManager excavationManager;
        [SerializeField] private Stratigraphy.StratigraphyEvaluator stratigraphy;
        [SerializeField] private Transform toolTip;

        [Header("Tool Configuration")]
        [SerializeField] private DigBrushPreset currentBrush;

        [Header("Input")]
        [SerializeField] private InputActionReference digAction;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private float audioMinInterval = 0.1f;

        [Header("Haptics")]
        [SerializeField] private bool enableHaptics = true;
        [SerializeField] private float hapticIntensity = 0.3f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmo = true;
        [SerializeField] private Color gizmoColor = Color.yellow;

        private bool isDigging = false;
        private float lastAudioTime = 0f;
        private Core.SurfaceHit lastHit;

        void OnEnable()
        {
            if (digAction != null)
            {
                digAction.action.Enable();
            }
        }

        void OnDisable()
        {
            if (digAction != null)
            {
                digAction.action.Disable();
            }
        }

        void Update()
        {
            if (excavationManager == null || stratigraphy == null || currentBrush == null)
                return;

            HandleInput();

            if (isDigging)
                PerformDig();
        }

        /// <summary>
        /// Handle input for digging (mouse or controller).
        /// </summary>
        private void HandleInput()
        {
            // Check input action
            if (digAction != null && digAction.action != null)
            {
                isDigging = digAction.action.IsPressed();
            }
            else
            {
                // Fallback to mouse input
                isDigging = Input.GetMouseButton(0);
            }
        }

        /// <summary>
        /// Perform the actual digging operation.
        /// </summary>
        private void PerformDig()
        {
            if (toolTip == null)
            {
                Debug.LogWarning("[DigTool] Tool tip transform not assigned!");
                return;
            }

            // Detect surface at tool tip
            Vector3 tipPosition = toolTip.position;
            Core.SurfaceHit hit = stratigraphy.SphereTrace(
                tipPosition - Vector3.up * 0.5f, // Start slightly above
                Vector3.up * 2f,                 // Search down then up
                1f,
                excavationManager
            );

            lastHit = hit;

            // Check if we're close enough to the surface to dig
            if (hit.isHit)
            {
                float distanceToSurface = Vector3.Distance(tipPosition, hit.position);

                if (distanceToSurface < currentBrush.radius * 2f)
                {
                    // Apply hardness modifier from material
                    float hardnessModifier = hit.material != null ? (10f - hit.material.hardness) / 10f : 1f;
                    hardnessModifier = Mathf.Clamp(hardnessModifier, 0.1f, 2f);

                    // Create brush stroke
                    Core.BrushStroke stroke = new Core.BrushStroke(
                        tipPosition,
                        currentBrush.radius,
                        currentBrush.digSpeed * hardnessModifier,
                        Time.deltaTime
                    );

                    excavationManager.ApplyBrushStroke(stroke);

                    // Feedback
                    PlayDigAudio(hit.material);
                    TriggerHaptics();
                }
            }
        }

        /// <summary>
        /// Play audio feedback based on material being dug.
        /// </summary>
        private void PlayDigAudio(Stratigraphy.MaterialLayer material)
        {
            if (audioSource == null || currentBrush == null)
                return;

            // Throttle audio to avoid too many overlapping sounds
            if (Time.time - lastAudioTime < audioMinInterval)
                return;

            AudioClip clip = currentBrush.GetRandomScrapeSound();
            if (clip != null)
            {
                audioSource.PlayOneShot(clip, 0.5f);
                lastAudioTime = Time.time;
            }
        }

        /// <summary>
        /// Trigger controller haptic feedback.
        /// </summary>
        private void TriggerHaptics()
        {
            if (!enableHaptics)
                return;

            // Use new Input System haptics if gamepad is present
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                gamepad.SetMotorSpeeds(hapticIntensity, hapticIntensity);

                // Stop after a short duration (done in coroutine ideally)
                Invoke(nameof(StopHaptics), 0.05f);
            }
        }

        private void StopHaptics()
        {
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                gamepad.SetMotorSpeeds(0f, 0f);
            }
        }

        /// <summary>
        /// Change the current brush preset.
        /// </summary>
        public void SetBrush(DigBrushPreset newBrush)
        {
            currentBrush = newBrush;
            Debug.Log($"[DigTool] Switched to brush: {newBrush.name}");
        }

        void OnDrawGizmos()
        {
            if (!drawGizmo || toolTip == null || currentBrush == null)
                return;

            // Draw brush sphere at tool tip
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(toolTip.position, currentBrush.radius);

            // Detect surface at tool tip
            Vector3 tipPosition = toolTip.position;
            Core.SurfaceHit hit = stratigraphy.SphereTrace(
                tipPosition - Vector3.up * 0.5f, // Start slightly above
                Vector3.up * 2f,                 // Search down then up
                1f,
                excavationManager
            );

            lastHit = hit;

            // Draw detected surface hit
            if (lastHit.isHit)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(lastHit.position, 0.02f);
                Gizmos.DrawLine(lastHit.position, lastHit.position + lastHit.normal * 0.1f);
            }
        }
    }
}
