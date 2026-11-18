using System;
using UnityEngine;

namespace Reins
{
    /// <summary>
    /// The horse and the mail coach behind it. The player never steers this directly:
    /// the only inputs are "left rein held" and "right rein held".
    ///
    ///   one rein   -> the horse's head comes round and she turns that way
    ///   both reins -> she slows (and both reins strain)
    ///   no reins   -> she follows the road loosely and builds to a full gallop
    /// </summary>
    public class Rig : MonoBehaviour
    {
        public const float MaxSpeed = 21f;
        public const float WalkSpeed = 4.5f;
        public const float TurnRate = 48f;          // deg/s at full single-rein pull
        public const float CoachGap = 4.3f;         // horse to coach axle
        public const float FallMargin = 1.7f;       // how far past the edge before she goes over
        public const int StartingWheels = 3;

        public Track Track;
        public readonly Rein Left = new Rein();
        public readonly Rein Right = new Rein();

        public Vector3 Pos;                         // horse position on the ground
        public float Heading;                       // degrees, 0 = +Z
        public float Speed = 6f;
        public int Wheels = StartingWheels;
        public float Lateral { get; private set; }
        public bool OnVerge { get; private set; }
        public bool Falling { get; private set; }
        public bool Bolting => boltTimer > 0f;
        public bool Invulnerable;
        public float Speed01 => Mathf.Clamp01(Speed / MaxSpeed);
        public Vector3 CoachPos { get; private set; }
        public Vector3 LanternPos => lantern.position;

        public event Action<int> Snapped;           // -1 left, +1 right
        public event Action<Obstacle> Hit;
        public event Action<Spook> Spooked;
        public event Action<Pickup> Collected;
        public event Action Fell;
        public event Action Hoofbeat;

        float coachYaw;
        float boltTimer;
        float hitTimer;
        float fallTime;
        Vector3 fallVelocity;
        float stride;
        float wanderSeed;

        Transform horse, horseBody, neck, tail, coach, cabin, lantern;
        readonly Transform[] legs = new Transform[4];
        readonly Transform[] wheels = new Transform[4];
        Transform handL, handR, bitL, bitR;
        LineRenderer lineL, lineR;
        Material lineMatL, lineMatR;

        static readonly Color Leather = new Color(0.55f, 0.36f, 0.20f);
        static readonly Color Hot = new Color(1f, 0.22f, 0.12f);

        public Vector3 Forward => Dir(Heading);

        static Vector3 Dir(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        public static Rig Create(Track track, Transform parent)
        {
            var go = new GameObject("Rig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<Rig>();
            rig.Track = track;
            rig.wanderSeed = 11.3f;
            rig.BuildHorse();
            rig.BuildCoach();
            rig.BuildReins();
            rig.Pos = track.Point(0f, 0f);
            rig.Heading = rig.coachYaw = 0f;
            rig.PlaceVisuals(0f);
            return rig;
        }

        // ---------------------------------------------------------------- simulation

        public void Tick(bool holdLeft, bool holdRight, float dt)
        {
            if (Falling)
            {
                TickFall(dt);
                return;
            }

            float s01 = Speed01;
            if (Left.Tick(holdLeft, s01, dt)) Snapped?.Invoke(-1);
            if (Right.Tick(holdRight, s01, dt)) Snapped?.Invoke(1);

            float steer = Right.Pull - Left.Pull;
            float brake = Mathf.Min(Left.Pull, Right.Pull);
            boltTimer -= dt;
            hitTimer -= dt;

            // Heading: reins, plus the horse's own loose sense of where the road goes,
            // plus a slow wander so she never runs dead straight on her own.
            float roadAngle = Track.TangentAngle(Pos.z);
            float follow = Bolting ? 0.25f : Mathf.Lerp(1.7f, 0.55f, s01);
            float wander = (Mathf.PerlinNoise(Time.time * 0.37f, wanderSeed) - 0.5f) * 2f;
            Heading += steer * TurnRate * Mathf.Lerp(1f, 0.8f, s01) * dt;
            Heading += Mathf.DeltaAngle(Heading, roadAngle) * follow * dt;
            Heading += wander * 9f * (0.5f + s01) * dt;
            Heading = roadAngle + Mathf.Clamp(Mathf.DeltaAngle(roadAngle, Heading), -55f, 55f);

            // Speed: slack reins let her run, both reins rein her in.
            if (Bolting) brake *= 0.45f;
            float target = Mathf.Lerp(Bolting ? MaxSpeed + 4f : MaxSpeed, WalkSpeed, brake);
            target *= 1f - 0.12f * Mathf.Abs(steer);
            if (OnVerge) target = Mathf.Min(target, 11f);
            float rate = target > Speed ? (Bolting ? 9f : 2.6f) : Mathf.Lerp(4f, 12f, brake);
            Speed = Mathf.MoveTowards(Speed, target, rate * dt);

            Pos += Forward * Speed * dt;
            Lateral = Pos.x - Track.CenterX(Pos.z);
            float beyond = Mathf.Abs(Lateral) - Track.HalfWidth(Pos.z);
            OnVerge = beyond > 0f;
            Pos.y = Track.Height(Pos.z) + Track.Drop(beyond);

            // The coach is dragged behind and swings round a beat after the horse does.
            coachYaw = Mathf.LerpAngle(coachYaw, Heading, 1f - Mathf.Exp(-5f * dt));
            CoachPos = Pos - Dir(coachYaw) * CoachGap;

            if (beyond > FallMargin)
            {
                if (Invulnerable)
                {
                    // Attract mode: nudge her back rather than ending the demo.
                    Heading -= Mathf.Sign(Lateral) * 60f * dt;
                }
                else
                {
                    StartFall();
                    return;
                }
            }

            CheckCourse();
            PlaceVisuals(dt);
        }

        void CheckCourse()
        {
            Vector2 h = new Vector2(Pos.x, Pos.z);
            Vector2 c = new Vector2(CoachPos.x, CoachPos.z);

            foreach (var s in Track.Spooks)
            {
                if (s.Fired || Pos.z < s.Z - 11f) continue;
                s.Fired = true;
                boltTimer = 2.4f;
                Heading -= s.Side * 8f;
                Spooked?.Invoke(s);
            }

            foreach (var p in Track.Pickups)
            {
                if (p.Taken || Mathf.Abs(p.Pos.z - Pos.z) > 8f) continue;
                Vector2 pp = new Vector2(p.Pos.x, p.Pos.z);
                if (Vector2.Distance(pp, h) < 1.7f || Vector2.Distance(pp, c) < 1.7f)
                {
                    p.Taken = true;
                    Collected?.Invoke(p);
                }
            }

            if (hitTimer > 0f || Invulnerable) return;
            foreach (var o in Track.Obstacles)
            {
                if (o.Spent || Mathf.Abs(o.Z - Pos.z) > 14f) continue;
                if (Track.Distance(h, o) < o.Radius + 0.55f || Track.Distance(c, o) < o.Radius + 0.95f)
                {
                    o.Spent = true;
                    Wheels--;
                    Speed *= 0.4f;
                    hitTimer = 1.5f;
                    float away = Mathf.Sign(Pos.x - o.Mid.x);
                    Heading += (away == 0f ? 1f : away) * 9f;
                    Hit?.Invoke(o);
                    break;
                }
            }
        }

        void StartFall()
        {
            Falling = true;
            fallTime = 0f;
            fallVelocity = Forward * Speed * 0.7f + Vector3.right * Mathf.Sign(Lateral) * 5f;
        }

        void TickFall(float dt)
        {
            fallTime += dt;
            fallVelocity += Vector3.down * 16f * dt;
            Pos += fallVelocity * dt;
            CoachPos += (fallVelocity + Vector3.down * 2f) * dt * 0.9f;
            float roll = -Mathf.Sign(Lateral) * fallTime * 150f;
            horse.position = Pos;
            horse.rotation = Quaternion.Euler(fallTime * 40f, Heading, roll);
            coach.position = CoachPos;
            coach.rotation = Quaternion.Euler(-fallTime * 30f, coachYaw, roll * 0.8f);
            UpdateReinLines();
            if (fallTime > 1.5f && Fell != null)
            {
                var f = Fell;
                Fell = null;
                f();
            }
        }

        // ---------------------------------------------------------------- visuals

        void PlaceVisuals(float dt)
        {
            float s01 = Speed01;
            float prev = stride;
            stride += Speed / 4.6f * Mathf.PI * 2f * dt;
            if (dt > 0f && Mathf.Floor(prev / Mathf.PI) != Mathf.Floor(stride / Mathf.PI)) Hoofbeat?.Invoke();

            float steer = Right.Pull - Left.Pull;
            float brake = Mathf.Min(Left.Pull, Right.Pull);

            // Horse: bob, pitch, legs, and the head coming round to whichever rein is pulled.
            horse.position = Pos;
            horse.rotation = Quaternion.Euler(0f, Heading, 0f);
            horseBody.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(stride)) * 0.14f * (0.3f + s01), 0f);
            horseBody.localRotation = Quaternion.Euler(Mathf.Sin(stride * 2f) * 2.5f * s01 - brake * 7f, 0f, 0f);
            neck.localRotation = Quaternion.Euler(-brake * 22f + Mathf.Sin(stride) * 3f, steer * 26f, 0f);
            tail.localRotation = Quaternion.Euler(35f + 30f * s01 + Mathf.Sin(stride) * 8f, Mathf.Sin(stride * 0.5f) * 12f, 0f);

            float swing = Mathf.Lerp(18f, 44f, s01);
            float[] phase = { 0f, 0.7f, Mathf.PI, Mathf.PI + 0.7f };
            for (int i = 0; i < 4; i++)
                legs[i].localRotation = Quaternion.Euler(Mathf.Sin(stride + phase[i]) * swing, 0f, 0f);

            // Coach: follows its own yaw, leans out of turns, rattles on the verge.
            float rattle = OnVerge ? Mathf.Sin(Time.time * 47f) * 0.06f : 0f;
            float turnLean = Mathf.DeltaAngle(coachYaw, Heading) * 0.35f;
            float coachY = Track.Height(CoachPos.z) +
                           Track.Drop(Mathf.Abs(CoachPos.x - Track.CenterX(CoachPos.z)) - Track.HalfWidth(CoachPos.z));
            CoachPos = new Vector3(CoachPos.x, coachY, CoachPos.z);
            coach.position = CoachPos + Vector3.up * rattle;
            coach.rotation = Quaternion.Euler(0f, coachYaw, 0f);
            cabin.localRotation = Quaternion.Euler(Mathf.Sin(stride * 2f) * 0.8f, 0f,
                -turnLean + Mathf.Sin(Time.time * 9f) * 0.6f * s01 + rattle * 40f);
            cabin.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(stride * 1.5f)) * 0.04f, 0f);

            float spin = Speed * dt / 0.55f * Mathf.Rad2Deg;
            foreach (var w in wheels) w.Rotate(spin, 0f, 0f, Space.Self);

            // Flash while the coach is shrugging off a hit.
            bool visible = hitTimer <= 0f || Mathf.Sin(Time.time * 40f) > -0.2f;
            if (cabin.gameObject.activeSelf != visible) cabin.gameObject.SetActive(visible);

            UpdateReinLines();
        }

        void UpdateReinLines()
        {
            DrawRein(lineL, lineMatL, Left, handL, bitL);
            DrawRein(lineR, lineMatR, Right, handR, bitR);
        }

        void DrawRein(LineRenderer line, Material mat, Rein rein, Transform hand, Transform bit)
        {
            int n = line.positionCount;
            Vector3 a = hand.position;
            if (rein.Snapped)
            {
                // A snapped rein just hangs from the driver's fist.
                float sway = Mathf.Sin(Time.time * 6f) * 0.25f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)(n - 1);
                    line.SetPosition(i, a + Vector3.down * t * 1.1f + hand.right * sway * t * t - hand.forward * 0.3f * t);
                }
                mat.color = Color.Lerp(Leather, Color.black, 0.5f);
                return;
            }

            Vector3 b = bit.position;
            float sag = Mathf.Lerp(0.55f, 0.03f, rein.Pull);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                line.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * sag * 4f * t * (1f - t));
            }
            float hot = Mathf.InverseLerp(0.35f, 1f, rein.Strain);
            float flicker = rein.Strain > 0.8f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 30f) : 1f;
            mat.color = Color.Lerp(Leather, Hot, hot * flicker);
            line.widthMultiplier = Mathf.Lerp(0.05f, 0.075f, rein.Pull);
        }

        // ---------------------------------------------------------------- construction

        void BuildHorse()
        {
            Color coat = new Color(0.42f, 0.25f, 0.16f);
            Color dark = new Color(0.10f, 0.08f, 0.09f);
            Color sock = new Color(0.86f, 0.82f, 0.76f);
            Color tack = new Color(0.16f, 0.11f, 0.09f);

            horse = Build.Empty("Horse", transform, Vector3.zero);
            horseBody = Build.Empty("Body", horse, Vector3.zero);

            Build.Box(horseBody, new Vector3(0, 1.45f, 0), new Vector3(0.72f, 0.82f, 2.0f), coat);
            Build.Box(horseBody, new Vector3(0, 1.52f, 0.85f), new Vector3(0.76f, 0.9f, 0.5f), coat);   // chest
            Build.Box(horseBody, new Vector3(0, 1.5f, -0.9f), new Vector3(0.78f, 0.86f, 0.5f), coat);   // haunch
            Build.Box(horseBody, new Vector3(0, 1.5f, 0.35f), new Vector3(0.80f, 0.5f, 0.22f), tack);   // harness strap
            Build.Box(horseBody, new Vector3(0, 1.6f, 0.95f), new Vector3(0.86f, 0.9f, 0.14f), tack);   // collar

            neck = Build.Empty("Neck", horseBody, new Vector3(0, 1.7f, 0.95f));
            Build.Box(neck, new Vector3(0, 0.45f, 0.25f), new Vector3(0.4f, 1.1f, 0.5f), coat, new Vector3(28f, 0, 0));
            Build.Box(neck, new Vector3(0, 0.62f, 0.02f), new Vector3(0.12f, 1.0f, 0.2f), dark, new Vector3(28f, 0, 0)); // mane
            Build.Box(neck, new Vector3(0, 0.98f, 0.78f), new Vector3(0.36f, 0.42f, 0.85f), coat, new Vector3(22f, 0, 0)); // head
            Build.Box(neck, new Vector3(0, 0.82f, 1.14f), new Vector3(0.3f, 0.3f, 0.22f), dark, new Vector3(22f, 0, 0));   // muzzle
            Build.Box(neck, new Vector3(0, 1.07f, 0.95f), new Vector3(0.1f, 0.06f, 0.5f), sock, new Vector3(22f, 0, 0));   // blaze
            Build.Box(neck, new Vector3(-0.12f, 1.3f, 0.5f), new Vector3(0.08f, 0.22f, 0.08f), coat);
            Build.Box(neck, new Vector3(0.12f, 1.3f, 0.5f), new Vector3(0.08f, 0.22f, 0.08f), coat);
            bitL = Build.Empty("BitL", neck, new Vector3(-0.2f, 0.86f, 1.02f));
            bitR = Build.Empty("BitR", neck, new Vector3(0.2f, 0.86f, 1.02f));

            tail = Build.Empty("Tail", horseBody, new Vector3(0, 1.8f, -1.12f));
            Build.Box(tail, new Vector3(0, 0, -0.45f), new Vector3(0.16f, 0.16f, 0.95f), dark);

            Vector3[] hips =
            {
                new Vector3(-0.24f, 1.15f, 0.78f), new Vector3(0.24f, 1.15f, 0.78f),
                new Vector3(-0.24f, 1.15f, -0.82f), new Vector3(0.24f, 1.15f, -0.82f)
            };
            for (int i = 0; i < 4; i++)
            {
                legs[i] = Build.Empty("Leg" + i, horseBody, hips[i]);
                Build.Box(legs[i], new Vector3(0, -0.4f, 0), new Vector3(0.2f, 0.85f, 0.22f), coat);
                Build.Box(legs[i], new Vector3(0, -0.92f, 0), new Vector3(0.17f, 0.3f, 0.19f), i % 3 == 0 ? sock : coat);
                Build.Box(legs[i], new Vector3(0, -1.1f, 0.02f), new Vector3(0.21f, 0.12f, 0.24f), dark);
            }
        }

        void BuildCoach()
        {
            Color body = new Color(0.46f, 0.11f, 0.13f);
            Color trim = new Color(0.86f, 0.66f, 0.24f);
            Color black = new Color(0.09f, 0.08f, 0.10f);
            Color wood = new Color(0.36f, 0.24f, 0.16f);
            Color sack = new Color(0.72f, 0.64f, 0.48f);
            Color coatCol = new Color(0.16f, 0.20f, 0.34f);
            Color skin = new Color(0.85f, 0.65f, 0.52f);

            coach = Build.Empty("Coach", transform, Vector3.zero);
            cabin = Build.Empty("Cabin", coach, Vector3.zero);

            // Prefer the modelled coach when it is in the project; the blockout below is the fallback.
            var model = Resources.Load<Mesh>("Models/Coach");
            var albedo = Resources.Load<Texture2D>("Models/CoachAlbedo");
            if (model != null && albedo != null)
            {
                BuildModelCoach(model, albedo, coatCol, skin, black, wood);
                return;
            }

            Build.Box(cabin, new Vector3(0, 1.5f, -0.1f), new Vector3(1.55f, 1.35f, 2.1f), body);
            Build.Box(cabin, new Vector3(0, 0.95f, -0.1f), new Vector3(1.62f, 0.14f, 2.2f), trim);
            Build.Box(cabin, new Vector3(0, 2.22f, -0.1f), new Vector3(1.75f, 0.12f, 2.35f), black);
            Build.GlowBox(cabin, new Vector3(-0.785f, 1.7f, -0.1f), new Vector3(0.03f, 0.5f, 0.8f), new Color(1f, 0.8f, 0.42f));
            Build.GlowBox(cabin, new Vector3(0.785f, 1.7f, -0.1f), new Vector3(0.03f, 0.5f, 0.8f), new Color(1f, 0.8f, 0.42f));
            Build.GlowBox(cabin, new Vector3(0, 1.75f, -1.16f), new Vector3(0.7f, 0.4f, 0.03f), new Color(1f, 0.8f, 0.42f));
            Build.Box(cabin, new Vector3(-0.35f, 2.42f, -0.5f), new Vector3(0.6f, 0.34f, 0.7f), sack, new Vector3(0, 14f, 0));
            Build.Box(cabin, new Vector3(0.3f, 2.4f, 0.1f), new Vector3(0.55f, 0.3f, 0.5f), sack, new Vector3(0, -20f, 6f));
            Build.Box(cabin, new Vector3(0.1f, 2.36f, -0.9f), new Vector3(0.9f, 0.22f, 0.4f), wood);

            // Driver's box and the driver.
            Build.Box(cabin, new Vector3(0, 1.25f, 1.25f), new Vector3(1.4f, 0.5f, 0.6f), wood);
            Build.Box(cabin, new Vector3(0, 0.95f, 1.75f), new Vector3(1.3f, 0.1f, 0.5f), wood, new Vector3(-25f, 0, 0));
            Build.Box(cabin, new Vector3(0, 1.95f, 1.2f), new Vector3(0.55f, 0.9f, 0.4f), coatCol);
            Build.Sphere(cabin, new Vector3(0, 2.6f, 1.22f), Vector3.one * 0.36f, skin);
            Build.Cylinder(cabin, new Vector3(0, 2.78f, 1.22f), new Vector3(0.6f, 0.02f, 0.6f), black);
            Build.Cylinder(cabin, new Vector3(0, 2.9f, 1.22f), new Vector3(0.36f, 0.12f, 0.36f), black);
            Build.Box(cabin, new Vector3(-0.3f, 2.0f, 1.5f), new Vector3(0.14f, 0.14f, 0.6f), coatCol, new Vector3(20f, 0, 0));
            Build.Box(cabin, new Vector3(0.3f, 2.0f, 1.5f), new Vector3(0.14f, 0.14f, 0.6f), coatCol, new Vector3(20f, 0, 0));
            handL = Build.Empty("HandL", cabin, new Vector3(-0.3f, 1.9f, 1.82f));
            handR = Build.Empty("HandR", cabin, new Vector3(0.3f, 1.9f, 1.82f));
            Build.Sphere(handL, Vector3.zero, Vector3.one * 0.14f, skin);
            Build.Sphere(handR, Vector3.zero, Vector3.one * 0.14f, skin);

            // Lantern on the near-side corner.
            Build.Box(cabin, new Vector3(-0.85f, 2.0f, 0.98f), new Vector3(0.08f, 0.5f, 0.08f), black);
            lantern = Build.GlowBox(cabin, new Vector3(-0.95f, 2.3f, 0.98f), new Vector3(0.2f, 0.26f, 0.2f), new Color(1f, 0.82f, 0.45f)).transform;

            // Shafts out to the horse.
            Build.Box(coach, new Vector3(-0.5f, 1.2f, 3.0f), new Vector3(0.08f, 0.08f, 3.6f), wood, new Vector3(-4f, 0, 0));
            Build.Box(coach, new Vector3(0.5f, 1.2f, 3.0f), new Vector3(0.08f, 0.08f, 3.6f), wood, new Vector3(-4f, 0, 0));

            Vector3[] hubs =
            {
                new Vector3(-0.9f, 0.55f, 0.75f), new Vector3(0.9f, 0.55f, 0.75f),
                new Vector3(-0.9f, 0.62f, -0.85f), new Vector3(0.9f, 0.62f, -0.85f)
            };
            for (int i = 0; i < 4; i++)
            {
                float r = i < 2 ? 0.55f : 0.62f;
                wheels[i] = Build.Empty("Wheel" + i, coach, hubs[i]);
                Build.Cylinder(wheels[i], Vector3.zero, new Vector3(r * 2f, 0.05f, r * 2f), black, new Vector3(0, 0, 90f));
                Build.Box(wheels[i], Vector3.zero, new Vector3(0.13f, r * 1.85f, 0.1f), trim);
                Build.Box(wheels[i], Vector3.zero, new Vector3(0.13f, 0.1f, r * 1.85f), trim);
            }
        }

        /// <summary>The generated stagecoach mesh, with the driver, lantern light and shafts added on.</summary>
        void BuildModelCoach(Mesh model, Texture2D albedo, Color coatCol, Color skin, Color black, Color wood)
        {
            Build.MeshObject(model, cabin, Vector3.zero, Vector3.one, Build.Textured(albedo, Color.white * 1.15f),
                new Vector3(0f, ModelYaw, 0f));

            // Driver on the box seat.
            Vector3 seat = DriverSeat;
            Build.Box(cabin, seat + new Vector3(0, 0.45f, 0), new Vector3(0.55f, 0.9f, 0.4f), coatCol);
            Build.Sphere(cabin, seat + new Vector3(0, 1.1f, 0.02f), Vector3.one * 0.36f, skin);
            Build.Cylinder(cabin, seat + new Vector3(0, 1.28f, 0.02f), new Vector3(0.6f, 0.02f, 0.6f), black);
            Build.Cylinder(cabin, seat + new Vector3(0, 1.4f, 0.02f), new Vector3(0.36f, 0.12f, 0.36f), black);
            Build.Box(cabin, seat + new Vector3(-0.3f, 0.5f, 0.3f), new Vector3(0.14f, 0.14f, 0.6f), coatCol, new Vector3(20f, 0, 0));
            Build.Box(cabin, seat + new Vector3(0.3f, 0.5f, 0.3f), new Vector3(0.14f, 0.14f, 0.6f), coatCol, new Vector3(20f, 0, 0));
            handL = Build.Empty("HandL", cabin, seat + new Vector3(-0.3f, 0.4f, 0.62f));
            handR = Build.Empty("HandR", cabin, seat + new Vector3(0.3f, 0.4f, 0.62f));
            Build.Sphere(handL, Vector3.zero, Vector3.one * 0.14f, skin);
            Build.Sphere(handR, Vector3.zero, Vector3.one * 0.14f, skin);

            lantern = Build.Empty("Lantern", cabin, new Vector3(-1.0f, 2.2f, 0.9f));

            Build.Box(coach, new Vector3(-0.5f, 1.1f, 3.0f), new Vector3(0.08f, 0.08f, 3.6f), wood, new Vector3(-4f, 0, 0));
            Build.Box(coach, new Vector3(0.5f, 1.1f, 3.0f), new Vector3(0.08f, 0.08f, 3.6f), wood, new Vector3(-4f, 0, 0));

            // The model's wheels are part of the mesh; keep empty pivots so the spin code has something to turn.
            for (int i = 0; i < 4; i++) wheels[i] = Build.Empty("Wheel" + i, coach, Vector3.zero);
        }

        // Where the driver sits on the modelled coach, and which way the mesh faces.
        const float ModelYaw = 0f;
        static readonly Vector3 DriverSeat = new Vector3(0f, 1.75f, 1.25f);

        void BuildReins()
        {
            lineMatL = new Material(Build.Glow(Leather));
            lineMatR = new Material(Build.Glow(Leather));
            lineL = MakeLine("ReinL", lineMatL);
            lineR = MakeLine("ReinR", lineMatR);
        }

        LineRenderer MakeLine(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = mat;
            line.positionCount = 12;
            line.useWorldSpace = true;
            line.widthMultiplier = 0.05f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }
    }
}
