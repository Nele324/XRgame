using TMPro;
using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Floating world-space hint shown on the first asteroid. Auto-dismisses
    /// after the player's first successful grab (any hand) by listening to
    /// <see cref="ZeroGGrabber.AnyGrabbed"/>. Faces the player so the text is
    /// always readable.
    /// </summary>
    public class TutorialHint : MonoBehaviour
    {
        [SerializeField] string line1 = "GRIP TO GRAB";
        [SerializeField] string line2 = "RELEASE TO DRIFT";
        [Tooltip("Optional third line — typically used for the menu-button reminder.")]
        [SerializeField] string line3 = "B / Y  →  MENU";
        [Tooltip("Optional fourth line — typically used to point at the goal.")]
        [SerializeField] string line4 = "DOCK WITH THE STATION TO WIN";
        [SerializeField] float fontSize = 0.6f;
        [SerializeField] Color textColor = new(0.85f, 0.95f, 1f, 1f);
        [SerializeField] Color accentColor = new(1.0f, 0.85f, 0.35f, 1f);
        [SerializeField] float fadeOutDuration = 1.5f;

        TMP_Text label;
        Transform cam;
        float fadeStartTime = -1f;

        void Awake()
        {
            // Self-build a TMP label at runtime so designers don't have to
            // assemble a canvas — the hint is just a single floating text.
            var go = new GameObject("HintText");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            label = go.AddComponent<TextMeshPro>();
            string accentHex = ColorUtility.ToHtmlStringRGB(accentColor);
            var sb = new System.Text.StringBuilder(192);
            sb.Append($"<size=120%><b>{line1}</b></size>");
            if (!string.IsNullOrEmpty(line2)) sb.Append($"\n<size=85%>{line2}</size>");
            if (!string.IsNullOrEmpty(line3)) sb.Append($"\n<size=85%><color=#{accentHex}>{line3}</color></size>");
            if (!string.IsNullOrEmpty(line4)) sb.Append($"\n<size=70%>{line4}</size>");
            label.text = sb.ToString();
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = textColor;
            label.fontStyle = FontStyles.Bold;
            label.textWrappingMode = TextWrappingModes.NoWrap;
        }

        void OnEnable()
        {
            ZeroGGrabber.AnyGrabbed += OnFirstGrab;
        }

        void OnDisable()
        {
            ZeroGGrabber.AnyGrabbed -= OnFirstGrab;
        }

        void Start()
        {
            // Find the main camera once. We re-resolve in Update if it goes
            // missing (scene load races).
            if (Camera.main != null) cam = Camera.main.transform;
        }

        void Update()
        {
            // Billboard toward the player camera. Flip the forward so the text
            // reads correctly (TMP renders facing -forward by default).
            if (cam == null && Camera.main != null) cam = Camera.main.transform;
            if (cam != null)
            {
                Vector3 toCam = transform.position - cam.position;
                if (toCam.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
            }

            // Fade out after the first grab.
            if (fadeStartTime >= 0f)
            {
                float t = (Time.time - fadeStartTime) / fadeOutDuration;
                if (label != null)
                {
                    Color c = textColor;
                    c.a = Mathf.Clamp01(1f - t);
                    label.color = c;
                }
                if (t >= 1f) Destroy(gameObject);
            }
        }

        void OnFirstGrab()
        {
            // Only react once. After triggering we unsubscribe to free the event slot.
            ZeroGGrabber.AnyGrabbed -= OnFirstGrab;
            if (fadeStartTime < 0f) fadeStartTime = Time.time;
        }
    }
}
