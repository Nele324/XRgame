using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceClimb
{
    /// <summary>
    /// Small world-space readout attached to a hand (typically the LEFT
    /// controller). Shows altitude above start, distance to the goal, and —
    /// when speedrun mode is on — an elapsed-run timer and best-time tag.
    ///
    /// The HUD self-builds its UI in Awake so we don't need a hand-authored
    /// canvas hierarchy in the scene; just drop the component on a Transform
    /// (or a GameObject parented to the controller) and wire the rig + goal.
    /// </summary>
    public class WristHUD : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] Transform rigTransform;
        [SerializeField] Transform goal;
        [Tooltip("Y of the spawn point. Altitude is reported relative to this.")]
        [SerializeField] float spawnY = 5f;

        [Header("Layout")]
        [SerializeField] Vector3 localOffset = new(0, 0.06f, 0.05f);
        [SerializeField] Vector3 localEuler = new(40f, 0f, 0f);
        [SerializeField] float canvasScale = 0.0015f;

        [Header("Styling")]
        [SerializeField] Color barColor = new(0.05f, 0.07f, 0.10f, 0.85f);
        [SerializeField] Color labelColor = new(0.50f, 0.95f, 1.0f, 1f);
        [SerializeField] Color valueColor = new(0.85f, 0.95f, 1.0f, 1f);
        [SerializeField] Color speedrunColor = new(1.0f, 0.85f, 0.35f, 1f);

        Canvas canvas;
        TMP_Text altitudeLabel;
        TMP_Text distanceLabel;
        TMP_Text timerLabel;
        TMP_Text bestLabel;
        // HP bar — built in BuildUI, fed each frame from HealthSystem if present.
        Image hpBarBg;
        Image hpBarFill;
        TMP_Text hpLabel;
        HealthSystem health;

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            // Auto-resolve refs if they weren't wired (cheap convenience —
            // most setups will use this since the rig is unique in scene).
            if (rigTransform == null)
            {
                var rig = FindAnyObjectByType<ZeroGRig>();
                if (rig != null) rigTransform = rig.transform;
            }
            if (goal == null)
            {
                var gz = FindAnyObjectByType<GoalZone>();
                if (gz != null) goal = gz.transform;
            }
            // Pull spawn Y from LevelManager if available — keeps everyone
            // aligned to the same reference altitude.
            var level = FindAnyObjectByType<LevelManager>();
            if (level != null && level.SpawnPoint != null) spawnY = level.SpawnPoint.position.y;
            // Resolve HealthSystem on the same rig — null is fine, we just hide the bar.
            health = FindAnyObjectByType<HealthSystem>();
        }

        void Update()
        {
            if (rigTransform == null) return;

            float altitude = rigTransform.position.y - spawnY;
            altitudeLabel.text = $"ALT  <b>{altitude:F0}m</b>";

            if (goal != null)
            {
                float dist = (goal.position - rigTransform.position).magnitude;
                distanceLabel.text = $"DOCK <b>{dist:F0}m</b>";
            }
            else
            {
                distanceLabel.text = "DOCK <b>—</b>";
            }

            // Speedrun timer & best-time visibility track the persisted setting,
            // re-checked every frame so toggling settings updates instantly.
            bool speedrun = SettingsManager.Instance != null && SettingsManager.Instance.SpeedrunMode;
            timerLabel.gameObject.SetActive(speedrun);
            bestLabel.gameObject.SetActive(speedrun);
            if (speedrun && GameStateController.Instance != null)
            {
                float t = GameStateController.Instance.RunElapsedSeconds;
                int min = Mathf.FloorToInt(t / 60f);
                float sec = t - min * 60f;
                timerLabel.text = $"<color=#{ColorUtility.ToHtmlStringRGB(speedrunColor)}>{min:00}:{sec:00.00}</color>";

                float best = SettingsManager.Instance.BestTime;
                bestLabel.text = best > 0f
                    ? $"<size=70%>BEST {Mathf.FloorToInt(best/60f):00}:{(best - Mathf.FloorToInt(best/60f)*60f):00.00}</size>"
                    : "<size=70%>BEST —</size>";
            }

            // Health bar. Hidden when there's no HealthSystem so non-damage
            // builds don't show an empty bar.
            bool showHp = health != null && health.enabled;
            if (hpBarBg != null) hpBarBg.gameObject.SetActive(showHp);
            if (hpBarFill != null) hpBarFill.gameObject.SetActive(showHp);
            if (hpLabel != null) hpLabel.gameObject.SetActive(showHp);
            if (showHp && hpBarFill != null)
            {
                float pct = Mathf.Clamp01(health.Health / Mathf.Max(1f, health.MaxHealth));
                // Fill from left: scale x of the bar (anchored left, width = pct * full).
                hpBarFill.rectTransform.sizeDelta = new Vector2(116f * pct, hpBarFill.rectTransform.sizeDelta.y);
                // Color shifts from cyan → amber → red as HP drops.
                if (pct > 0.6f) hpBarFill.color = new Color(0.4f, 0.95f, 1f, 1f);
                else if (pct > 0.3f) hpBarFill.color = new Color(1f, 0.75f, 0.25f, 1f);
                else hpBarFill.color = new Color(1f, 0.30f, 0.20f, 1f);
                if (hpLabel != null) hpLabel.text = $"HP {Mathf.CeilToInt(health.Health)}/{Mathf.CeilToInt(health.MaxHealth)}";
            }
        }

        void BuildUI()
        {
            transform.localPosition = localOffset;
            transform.localEulerAngles = localEuler;

            var canvasGO = new GameObject("HUDCanvas", typeof(RectTransform));
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            var rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(120, 92);     // taller — extra space for HP row at the bottom
            canvasGO.transform.localScale = Vector3.one * canvasScale;

            var bg = new GameObject("BG", typeof(RectTransform));
            bg.transform.SetParent(canvasGO.transform, false);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = barColor;
            bgImg.raycastTarget = false;

            altitudeLabel = MakeLabel("Alt", canvasGO.transform, new Vector2(0, 33), new Vector2(120, 18), 9f, valueColor);
            distanceLabel = MakeLabel("Dist", canvasGO.transform, new Vector2(0, 16), new Vector2(120, 18), 9f, valueColor);
            timerLabel    = MakeLabel("Time", canvasGO.transform, new Vector2(0, -1), new Vector2(120, 14), 11f, speedrunColor);
            bestLabel     = MakeLabel("Best", canvasGO.transform, new Vector2(0, -14), new Vector2(120, 10), 7f, labelColor);
            timerLabel.gameObject.SetActive(false);
            bestLabel.gameObject.SetActive(false);

            // HP bar — bottom strip. Background frame + foreground fill anchored
            // to left so we can grow/shrink the fill width as HP changes.
            BuildHpBar(canvasGO.transform);
        }

        void BuildHpBar(Transform parent)
        {
            // Background (full width, dark)
            var bg = new GameObject("HpBg", typeof(RectTransform));
            bg.transform.SetParent(parent, false);
            var bgRT = bg.GetComponent<RectTransform>();
            bgRT.anchorMin = bgRT.anchorMax = new Vector2(0.5f, 0.5f);
            bgRT.pivot = new Vector2(0.5f, 0.5f);
            bgRT.sizeDelta = new Vector2(120, 8);
            bgRT.anchoredPosition = new Vector2(0, -32);
            hpBarBg = bg.AddComponent<Image>();
            hpBarBg.color = new Color(0.05f, 0.07f, 0.10f, 0.9f);
            hpBarBg.raycastTarget = false;

            // Foreground fill, anchored to LEFT so width animates from the left edge
            var fill = new GameObject("HpFill", typeof(RectTransform));
            fill.transform.SetParent(parent, false);
            var fillRT = fill.GetComponent<RectTransform>();
            fillRT.anchorMin = fillRT.anchorMax = new Vector2(0.5f, 0.5f);
            fillRT.pivot = new Vector2(0f, 0.5f);
            fillRT.sizeDelta = new Vector2(116, 6);
            fillRT.anchoredPosition = new Vector2(-58, -32);
            hpBarFill = fill.AddComponent<Image>();
            hpBarFill.color = new Color(0.4f, 0.95f, 1f, 1f);
            hpBarFill.raycastTarget = false;

            // Numeric label below the bar
            hpLabel = MakeLabel("HpLabel", parent, new Vector2(0, -42), new Vector2(120, 8), 6f, valueColor);
        }

        TMP_Text MakeLabel(string name, Transform parent, Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = "";
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }
    }
}
