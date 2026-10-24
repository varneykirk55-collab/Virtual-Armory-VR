using UnityEngine;
using UnityEngine.UI;

namespace SamuraiDuel
{
    /// <summary>World-space duel UI: samurai HP over its head, a scoreboard, and short callouts.</summary>
    public class DuelHUD : MonoBehaviour
    {
        [SerializeField] private RectTransform samuraiHealthFill;
        [SerializeField] private RectTransform playerHealthFill;
        [SerializeField] private Text statusText;
        [SerializeField] private Text scoreText;
        [SerializeField] private Text comboText;
        [SerializeField] private Text calloutText;
        [Tooltip("Canvases that yaw to face the player's head every frame.")]
        [SerializeField] private Transform[] billboards;

        private float calloutUntil;
        private Transform head;

        public void Wire(RectTransform samuraiFill, RectTransform playerFill, Text status, Text score, Text combo,
            Text callout, Transform[] faceCamera)
        {
            samuraiHealthFill = samuraiFill;
            playerHealthFill = playerFill;
            statusText = status;
            scoreText = score;
            comboText = combo;
            calloutText = callout;
            billboards = faceCamera;
        }

        public void SetSamuraiHealth(float normalized) => SetFill(samuraiHealthFill, normalized);
        public void SetPlayerHealth(float normalized) => SetFill(playerHealthFill, normalized);

        public void SetStatus(string text)
        {
            if (statusText != null)
                statusText.text = text;
        }

        public void SetScore(int score, int best)
        {
            if (scoreText != null)
                scoreText.text = $"Score {score}   Best {best}";
        }

        public void SetCombo(int combo)
        {
            if (comboText != null)
                comboText.text = combo >= 2 ? $"{combo} hit combo" : string.Empty;
        }

        public void Callout(string text, Color color, float seconds = 0.9f)
        {
            if (calloutText == null)
                return;
            calloutText.text = text;
            calloutText.color = color;
            calloutUntil = Time.time + seconds;
        }

        private void LateUpdate()
        {
            if (calloutText != null && calloutText.text.Length > 0)
            {
                float remaining = calloutUntil - Time.time;
                if (remaining <= 0f)
                {
                    calloutText.text = string.Empty;
                }
                else
                {
                    Color c = calloutText.color;
                    c.a = Mathf.Clamp01(remaining * 3f);
                    calloutText.color = c;
                }
            }

            if (head == null)
            {
                if (Camera.main == null)
                    return;
                head = Camera.main.transform;
            }

            if (billboards == null)
                return;
            foreach (var board in billboards)
            {
                if (board == null)
                    continue;
                Vector3 away = board.position - head.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f)
                    board.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }

        private static void SetFill(RectTransform fill, float normalized)
        {
            if (fill == null)
                return;
            Vector2 max = fill.anchorMax;
            max.x = Mathf.Clamp01(normalized);
            fill.anchorMax = max;
        }
    }
}
