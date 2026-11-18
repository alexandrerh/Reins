using UnityEngine;

namespace Reins
{
    /// <summary>
    /// One strip of leather. Pulling it builds strain (faster the harder the horse is running),
    /// slack lets it recover. Over-strain it and it snaps, leaving that side dead while the
    /// driver ties it back together.
    /// </summary>
    public class Rein
    {
        public const float PullRate = 5.5f;       // how fast a held rein comes taut
        public const float ReleaseRate = 8f;      // how fast it goes slack again
        public const float StrainAtWalk = 0.10f;  // strain per second, full pull, slow horse
        public const float StrainAtGallop = 0.44f;
        public const float Recovery = 0.22f;      // strain shed per second when slack
        public const float KnotTime = 4f;         // seconds a snapped rein is out of action

        /// <summary>0 = slack, 1 = hauled all the way back.</summary>
        public float Pull { get; private set; }

        /// <summary>0 = fresh, 1 = snaps.</summary>
        public float Strain { get; private set; }

        /// <summary>Seconds until a snapped rein is usable again.</summary>
        public float KnotTimer { get; private set; }

        public bool Snapped => KnotTimer > 0f;

        /// <summary>Advance the rein. Returns true on the frame it snaps.</summary>
        public bool Tick(bool held, float speed01, float dt)
        {
            if (Snapped)
            {
                KnotTimer -= dt;
                Pull = 0f;
                if (!Snapped) Strain = 0.35f; // a knotted rein never quite comes back fresh
                return false;
            }

            Pull = Mathf.MoveTowards(Pull, held ? 1f : 0f, (held ? PullRate : ReleaseRate) * dt);

            if (Pull > 0.15f)
                Strain += Pull * Mathf.Lerp(StrainAtWalk, StrainAtGallop, speed01) * dt;
            else
                Strain -= Recovery * dt;
            Strain = Mathf.Clamp01(Strain);

            if (Strain >= 1f)
            {
                KnotTimer = KnotTime;
                Pull = 0f;
                return true;
            }
            return false;
        }
    }
}
