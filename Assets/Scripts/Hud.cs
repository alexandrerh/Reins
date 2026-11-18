using UnityEngine;

namespace Reins
{
    /// <summary>
    /// Immediate-mode HUD. Laid out on a virtual 1080-tall canvas and scaled to the window.
    /// The two rein gauges sit under the player's two hands, bottom-left and bottom-right.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public GameManager Game;

        GUIStyle style;
        float width;
        const float H = 1080f;

        static readonly Color Ink = new Color(0.07f, 0.06f, 0.12f, 0.78f);
        static readonly Color Cream = new Color(1f, 0.94f, 0.78f);
        static readonly Color Leather = new Color(0.78f, 0.56f, 0.30f);
        static readonly Color Hot = new Color(1f, 0.25f, 0.15f);
        static readonly Color Brass = new Color(0.95f, 0.74f, 0.30f);

        void OnGUI()
        {
            if (Game == null || Game.Rig == null) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = false };

            float scale = Screen.height / H;
            width = Screen.width / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            switch (Game.Current)
            {
                case GameManager.State.Title:
                    DrawTitle();
                    break;
                case GameManager.State.Playing:
                    DrawRun();
                    break;
                case GameManager.State.Delivered:
                    DrawResult(true);
                    break;
                default:
                    DrawResult(false);
                    break;
            }
        }

        void DrawTitle()
        {
            Fill(0, 0, width, 330, new Color(0.07f, 0.06f, 0.12f, 0.45f));
            Fill(0, H - 250, width, 250, new Color(0.07f, 0.06f, 0.12f, 0.55f));

            Label("R E I N S", 0, 60, width, 170, 150, Cream);
            Label("The night mail, one horse, two strips of leather.", 0, 230, width, 50, 34, Brass, FontStyle.Italic);
            if (Game.HasBest)
                Label("Best run:  " + Game.BestLetters + " letters in " + Clock(Game.BestTime), 0, 282, width, 40, 24,
                    Cream, FontStyle.Normal);

            float cx = width * 0.5f;
            Key("A", cx - 420, H - 205);
            Label("left rein", cx - 350, H - 205, 200, 64, 30, Cream, FontStyle.Normal, TextAnchor.MiddleLeft);
            Key("D", cx - 110, H - 205);
            Label("right rein", cx - 40, H - 205, 200, 64, 30, Cream, FontStyle.Normal, TextAnchor.MiddleLeft);
            Key("A", cx + 190, H - 205);
            Key("D", cx + 260, H - 205);
            Label("both: slow her", cx + 330, H - 205, 260, 64, 30, Cream, FontStyle.Normal, TextAnchor.MiddleLeft);

            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 3.5f);
            Label("PRESS  SPACE  TO  TAKE  THE  REINS", 0, H - 110, width, 60, 36,
                new Color(Cream.r, Cream.g, Cream.b, pulse));
        }

        void DrawRun()
        {
            var rig = Game.Rig;

            // Progress along the ridge.
            float barW = Mathf.Min(900f, width * 0.5f);
            float bx = (width - barW) * 0.5f;
            Fill(bx - 6, 44, barW + 12, 22, Ink);
            float progress = Mathf.Clamp01(rig.Pos.z / Track.Length);
            Fill(bx, 50, barW * progress, 10, Brass);
            Fill(bx + barW * progress - 5, 40, 10, 30, Cream);
            Label("RIDGE ROAD", bx, 8, 300, 30, 20, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);
            Label("HOLLOWAY", bx + barW - 300, 8, 300, 30, 20, Cream, FontStyle.Bold, TextAnchor.MiddleRight);

            // Letters and coach condition.
            Fill(40, 36, 250, 74, Ink);
            Fill(60, 56, 46, 32, Cream);
            Fill(77, 66, 12, 12, new Color(0.85f, 0.16f, 0.14f));
            Label(Game.Letters + " / " + Game.LettersTotal, 120, 40, 170, 66, 38, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);

            Fill(width - 290, 36, 250, 74, Ink);
            Label("COACH", width - 280, 40, 110, 66, 22, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);
            for (int i = 0; i < Rig.StartingWheels; i++)
                Fill(width - 170 + i * 42, 56, 32, 32, i < rig.Wheels ? Brass : new Color(1, 1, 1, 0.12f));

            // Gait.
            string gait = rig.Bolting ? "BOLTING" : rig.Speed > 17f ? "GALLOP" : rig.Speed > 11f ? "CANTER" : rig.Speed > 6.5f ? "TROT" : "WALK";
            Label(gait, 0, H - 96, width, 44, 30, rig.Bolting ? Hot : Cream);
            Label(Mathf.RoundToInt(rig.Speed * 3.6f) + " km/h", 0, H - 56, width, 30, 20, new Color(1, 1, 1, 0.6f), FontStyle.Normal);

            DrawRein(rig.Left, "A", 70, Game.HoldLeft);
            DrawRein(rig.Right, "D", width - 70 - 150, Game.HoldRight);

            if (Game.ToastTime > 0f && !string.IsNullOrEmpty(Game.Toast))
            {
                float a = Mathf.Clamp01(Game.ToastTime * 2f);
                Fill(0, 190, width, 70, new Color(Ink.r, Ink.g, Ink.b, 0.6f * a));
                Label(Game.Toast, 0, 190, width, 70, 38, new Color(Cream.r, Cream.g, Cream.b, a));
            }

            if (rig.OnVerge)
                Label("OFF THE ROAD", 0, 290, width, 50, 34, Hot);
        }

        /// <summary>A rein gauge: a strap that fills with strain, and a grip that drops as you pull.</summary>
        void DrawRein(Rein rein, string key, float x, bool held)
        {
            const float w = 150f, top = H - 440f, strapH = 300f;
            Fill(x, top - 20, w, strapH + 130, Ink);

            float sx = x + w * 0.5f - 22f;
            Fill(sx, top, 44, strapH, new Color(1, 1, 1, 0.10f));

            if (rein.Snapped)
            {
                bool blink = Mathf.Sin(Time.time * 14f) > 0f;
                Fill(sx, top, 44, strapH * 0.3f, blink ? Hot : new Color(0.5f, 0.12f, 0.08f));
                Label("SNAPPED", x, top + strapH * 0.36f, w, 36, 24, Hot);
                Label(rein.KnotTimer.ToString("0.0") + "s", x, top + strapH * 0.5f, w, 50, 40, Cream);
                Label("re-knotting", x, top + strapH * 0.68f, w, 30, 18, new Color(1, 1, 1, 0.6f), FontStyle.Normal);
            }
            else
            {
                Color c = Color.Lerp(Leather, Hot, Mathf.InverseLerp(0.4f, 1f, rein.Strain));
                if (rein.Strain > 0.8f && Mathf.Sin(Time.time * 30f) > 0f) c = Cream;
                float fill = strapH * rein.Strain;
                Fill(sx, top + strapH - fill, 44, fill, c);
                // Snap line.
                Fill(sx - 12, top - 2, 68, 4, Hot);
                // Grip.
                float gy = top + rein.Pull * (strapH - 26f);
                Fill(sx - 16, gy, 76, 26, held ? Cream : new Color(1, 1, 1, 0.55f));
            }

            Key(key, x + w * 0.5f - 32f, top + strapH + 28f, held);
        }

        void DrawResult(bool delivered)
        {
            Fill(0, 0, width, H, new Color(0.05f, 0.04f, 0.10f, delivered ? 0.45f : 0.6f));
            float a = Mathf.Clamp01(Game.StateTime * 1.5f);
            Color cream = new Color(Cream.r, Cream.g, Cream.b, a);

            string head, sub;
            if (delivered)
            {
                head = "MAIL DELIVERED";
                sub = Rank();
            }
            else if (Game.Current == GameManager.State.Fallen)
            {
                head = "OVER THE EDGE";
                sub = "The ridge does not give letters back.";
            }
            else
            {
                head = "COACH WRECKED";
                sub = "Three knocks and the axle is kindling.";
            }

            Label(head, 0, 250, width, 140, 110, cream);
            Label(sub, 0, 400, width, 50, 34, new Color(Brass.r, Brass.g, Brass.b, a), FontStyle.Italic);

            float cx = width * 0.5f;
            Stat("LETTERS", Game.Letters + " / " + Game.LettersTotal, cx - 400, a);
            Stat("TIME", Clock(Game.RunTime), cx - 110, a);

            if (Game.NewBest)
                Label("NEW BEST RUN", 0, 680, width, 50, 34, new Color(Brass.r, Brass.g, Brass.b, a));
            else if (Game.HasBest)
                Label("Best run:  " + Game.BestLetters + " letters in " + Clock(Game.BestTime), 0, 680, width, 40, 24,
                    new Color(1, 1, 1, 0.6f * a), FontStyle.Normal);
            Stat("DISTANCE", Mathf.RoundToInt(Mathf.Clamp(Game.Distance, 0, Track.Length)) + " m", cx + 180, a);

            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 3.5f);
            Label("SPACE  to ride again        ESC  for the title", 0, H - 200, width, 50, 30,
                new Color(Cream.r, Cream.g, Cream.b, pulse * a));
        }

        static string Clock(float seconds)
        {
            int mins = (int)(seconds / 60f);
            return mins + ":" + (seconds - mins * 60).ToString("00.0");
        }

        string Rank()
        {
            float share = Game.LettersTotal > 0 ? Game.Letters / (float)Game.LettersTotal : 0f;
            if (share >= 0.9f && Game.Rig.Wheels == Rig.StartingWheels) return "Rank:  Royal Mail.  Not a scratch on her.";
            if (share >= 0.7f) return "Rank:  Postmaster.  Holloway reads tonight.";
            if (share >= 0.4f) return "Rank:  Post Rider.  Most of it made it.";
            return "Rank:  Late Post.  You arrived, at least.";
        }

        void Stat(string name, string value, float x, float alpha)
        {
            Fill(x, 520, 220, 130, new Color(Ink.r, Ink.g, Ink.b, Ink.a * alpha));
            Label(name, x, 530, 220, 34, 22, new Color(Brass.r, Brass.g, Brass.b, alpha));
            Label(value, x, 566, 220, 74, 46, new Color(Cream.r, Cream.g, Cream.b, alpha));
        }

        // ---------------------------------------------------------------- primitives

        void Key(string key, float x, float y, bool lit = false)
        {
            Fill(x, y, 64, 64, lit ? Cream : new Color(1, 1, 1, 0.16f));
            Fill(x + 3, y + 3, 58, 54, lit ? Brass : Ink);
            Label(key, x, y - 2, 64, 60, 34, lit ? Ink : Cream);
        }

        void Fill(float x, float y, float w, float h, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Label(string text, float x, float y, float w, float h, int size, Color color,
            FontStyle fontStyle = FontStyle.Bold, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            style.fontSize = size;
            style.fontStyle = fontStyle;
            style.alignment = anchor;
            float d = Mathf.Max(2f, size * 0.05f);
            style.normal.textColor = new Color(0f, 0f, 0f, 0.55f * color.a);
            GUI.Label(new Rect(x + d, y + d, w, h), text, style);
            style.normal.textColor = color;
            GUI.Label(new Rect(x, y, w, h), text, style);
        }
    }
}
