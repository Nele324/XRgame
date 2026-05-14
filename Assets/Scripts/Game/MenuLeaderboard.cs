using TMPro;
using UnityEngine;

namespace SpaceClimb
{
    /// <summary>
    /// Renders the top-3 leaderboard into a TextMeshPro label on the main menu.
    /// Refreshes on enable and on every SettingsManager change so a record set
    /// in-game (then back to menu) shows up without a scene reload.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class MenuLeaderboard : MonoBehaviour
    {
        [SerializeField] TMP_Text label;
        [SerializeField] string title = "LEADERBOARD";
        [Tooltip("Color for the title and rank numbers — gives the panel an arcade scoreboard read.")]
        [SerializeField] Color accentHex = new(1f, 0.78f, 0.34f, 1f);
        [Tooltip("Color for the time digits.")]
        [SerializeField] Color timeHex = new(0.92f, 0.95f, 1f, 1f);

        void Awake()
        {
            if (label == null) label = GetComponent<TMP_Text>();
        }

        void OnEnable()
        {
            SettingsManager.OnSettingsChanged += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            SettingsManager.OnSettingsChanged -= Refresh;
        }

        void Refresh()
        {
            if (label == null) return;
            var s = SettingsManager.Instance;
            if (s == null)
            {
                label.text = title + "\n--:--.--\n--:--.--\n--:--.--";
                return;
            }
            string accent = ColorUtility.ToHtmlStringRGB(accentHex);
            string timeC = ColorUtility.ToHtmlStringRGB(timeHex);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<color=#{accent}>{title}</color>");
            var top = s.TopTimes;
            for (int i = 0; i < top.Count; i++)
            {
                string t = top[i] > 0f ? Format(top[i]) : "--:--.--";
                sb.AppendLine($"<color=#{accent}>{i + 1}.</color> <color=#{timeC}>{t}</color>");
            }
            label.text = sb.ToString().TrimEnd();
        }

        static string Format(float t)
        {
            int min = Mathf.FloorToInt(t / 60f);
            float sec = t - min * 60f;
            return $"{min:00}:{sec:00.00}";
        }
    }
}
