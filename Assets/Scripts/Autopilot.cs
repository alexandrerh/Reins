using UnityEngine;

namespace Reins
{
    /// <summary>
    /// A driver that plays by the same rules as the player: it can only hold or drop each rein.
    /// Drives the attract mode behind the title and doubles as a smoke test for course tuning.
    /// </summary>
    public static class Autopilot
    {
        public static void Decide(Rig rig, Track track, out bool left, out bool right)
        {
            float z = rig.Pos.z;
            float lat = rig.Lateral;
            float w = track.HalfWidth(z + 10f) - 1.5f;

            // Score a handful of lanes across the road and aim for the best one.
            float best = 0f, bestScore = float.MinValue;
            for (float lane = -w; lane <= w + 0.01f; lane += 0.6f)
            {
                float score = -Mathf.Abs(lane - lat) * 0.35f - Mathf.Abs(lane) * 0.12f;

                foreach (var o in track.Obstacles)
                {
                    float dz = o.Z - z;
                    if (o.Spent || dz < -3f || dz > 36f) continue;
                    float la = o.A.x - track.CenterX(o.A.y);
                    float lb = o.B.x - track.CenterX(o.B.y);
                    float lo = Mathf.Min(la, lb) - o.Radius - 1.7f;
                    float hi = Mathf.Max(la, lb) + o.Radius + 1.7f;
                    if (lane > lo && lane < hi) score -= 20f * (1.2f - dz / 40f);
                }

                foreach (var p in track.Pickups)
                {
                    float dz = p.Pos.z - z;
                    if (p.Taken || dz < 2f || dz > 26f) continue;
                    float pl = p.Pos.x - track.CenterX(p.Pos.z);
                    score += Mathf.Max(0f, 1.2f - Mathf.Abs(pl - lane)) * 0.9f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = lane;
                }
            }

            float desired = track.TangentAngle(z + 6f) + Mathf.Clamp((best - lat) * 6f, -30f, 30f);
            float error = Mathf.DeltaAngle(rig.Heading, desired);

            left = error < -2.5f;
            right = error > 2.5f;

            // Keep her to a canter when the reins can take it.
            bool reinsFresh = !rig.Left.Snapped && !rig.Right.Snapped &&
                              Mathf.Max(rig.Left.Strain, rig.Right.Strain) < 0.5f;
            if (rig.Speed > 14f && reinsFresh && Mathf.Abs(error) < 7f) left = right = true;

            if (rig.Left.Strain > 0.85f) left = false;
            if (rig.Right.Strain > 0.85f) right = false;
        }
    }
}
