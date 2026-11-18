using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Reins
{
    /// <summary>
    /// Owns the run: builds the world, feeds input to the rig, keeps score and drives the camera.
    /// The scene contains nothing but this component.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public enum State { Title, Playing, Delivered, Wrecked, Fallen }

        const int CourseSeed = 1887;

        /// <summary>Set before reloading the scene to go straight back into a run.</summary>
        static bool skipTitle;

        public State Current { get; private set; }
        public Rig Rig { get; private set; }
        public Track Track { get; private set; }
        public int Letters { get; private set; }
        public int LettersTotal => Track.Pickups.Count;
        public float RunTime { get; private set; }
        public float Distance { get; private set; }   // how far the run got, frozen when it ends
        public float StateTime { get; private set; }
        public string Toast { get; private set; }
        public float ToastTime { get; private set; }
        public bool HoldLeft { get; private set; }
        public bool HoldRight { get; private set; }

        Camera cam;
        Transform sky;
        Sfx sfx;
        Fx fx;
        float camYaw;
        float shake;
        int streak;
        float streakTimer;
        float dustTimer;

        // Capture mode (-autoshot <dir>): the autopilot plays and frames are saved for the README.
        string shotDir;
        int shotIndex;
        bool resultShot;
        static readonly float[] ShotTimes = { 9f, 21f, 40f, 62f, 84f, 96f };

        void Awake()
        {
            Build.Reset();
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            QualitySettings.shadowDistance = 95f;
            QualitySettings.antiAliasing = 4;

            SetupAtmosphere();

            Track = new Track();
            Track.Generate(Build.Empty("World", transform, Vector3.zero), CourseSeed);

            Rig = Rig.Create(Track, transform);
            sfx = Sfx.Create(transform);
            fx = Fx.Create(transform);
            gameObject.AddComponent<Hud>().Game = this;

            Rig.Snapped += OnSnapped;
            Rig.Hit += OnHit;
            Rig.Spooked += OnSpooked;
            Rig.Collected += OnCollected;
            Rig.Fell += () => End(State.Fallen);
            Rig.Hoofbeat += () => sfx.Hoof(Rig.Speed01);

            string[] args = Environment.GetCommandLineArgs();
            int flag = Array.IndexOf(args, "-autoshot");
            if (flag >= 0 && flag + 1 < args.Length)
            {
                shotDir = args[flag + 1];
                Directory.CreateDirectory(shotDir);
            }

            if (skipTitle) Begin();
            else
            {
                Current = State.Title;
                Rig.Invulnerable = true;
            }
            skipTitle = false;
            camYaw = Rig.Heading;
            UpdateCamera(1f);
        }

        void SetupAtmosphere()
        {
            Color dusk = new Color(0.22f, 0.20f, 0.38f);

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(transform, false);
            camGo.tag = "MainCamera";
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = dusk;
            cam.farClipPlane = 900f;
            cam.nearClipPlane = 0.2f;
            camGo.AddComponent<AudioListener>();

            var lightGo = new GameObject("Moonlight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.rotation = Quaternion.Euler(34f, -32f, 0f);
            var moonlight = lightGo.AddComponent<Light>();
            moonlight.type = LightType.Directional;
            moonlight.color = new Color(0.66f, 0.74f, 1f);
            moonlight.intensity = 1.05f;
            moonlight.shadows = LightShadows.Soft;
            moonlight.shadowStrength = 0.75f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.32f, 0.50f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = dusk;
            RenderSettings.fogStartDistance = 28f;
            RenderSettings.fogEndDistance = 330f;

            // Sky dome that rides along with the camera: a moon and a scatter of stars.
            sky = Build.Empty("Sky", transform, Vector3.zero);
            var moon = Build.Prim(PrimitiveType.Sphere, sky, new Vector3(-150f, 190f, 600f), Vector3.one * 62f,
                Build.Glow(new Color(1f, 0.95f, 0.80f)));
            moon.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Build.Prim(PrimitiveType.Sphere, sky, new Vector3(-132f, 200f, 585f), Vector3.one * 56f, Build.Glow(dusk))
                .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var rnd = new System.Random(3);
            Material star = Build.Glow(new Color(0.85f, 0.86f, 1f));
            for (int i = 0; i < 110; i++)
            {
                float az = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float el = Mathf.Lerp(0.12f, 1.2f, (float)rnd.NextDouble());
                Vector3 dir = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
                float size = Mathf.Lerp(1.2f, 3.2f, (float)rnd.NextDouble());
                Build.Prim(PrimitiveType.Cube, sky, dir * 700f, Vector3.one * size, star, new Vector3(45f, 45f, 0f))
                    .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ---------------------------------------------------------------- flow

        void Begin()
        {
            Current = State.Playing;
            StateTime = 0f;
            RunTime = 0f;
            Rig.Invulnerable = false;
            Say("Slack reins: she runs.  Both reins: she slows.", 4.5f);
            sfx.Horn();
        }

        void End(State how)
        {
            if (Current != State.Playing) return;
            Current = how;
            StateTime = 0f;
            Distance = Rig.Pos.z;
            if (how == State.Delivered) sfx.Horn();
            else sfx.Crash();
        }

        void Restart(bool straightIn)
        {
            skipTitle = straightIn;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void Say(string text, float seconds = 2.2f)
        {
            Toast = text;
            ToastTime = seconds;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            StateTime += dt;
            ToastTime -= dt;
            streakTimer -= dt;
            if (streakTimer <= 0f) streak = 0;

            bool confirm = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Current == State.Title) Application.Quit();
                else Restart(false);
                return;
            }

            bool capture = shotDir != null;
            switch (Current)
            {
                case State.Title:
                    Autopilot.Decide(Rig, Track, out bool al, out bool ar);
                    Drive(al, ar, dt);
                    if (Rig.Pos.z > Track.Length - 150f) Restart(false);
                    if (confirm || (capture && StateTime > 2.2f)) Restart(true);
                    break;

                case State.Playing:
                    RunTime += dt;
                    if (capture)
                    {
                        // Capture runs film the whole course, so the coach never takes its last knock.
                        Autopilot.Decide(Rig, Track, out bool cl, out bool cr);
                        Drive(cl, cr, dt);
                        Rig.Wheels = Mathf.Max(Rig.Wheels, 1);
                    }
                    else
                    {
                        Drive(ReadLeft(), ReadRight(), dt);
                    }
                    if (Input.GetKeyDown(KeyCode.R)) Restart(true);
                    if (Rig.Wheels <= 0) End(State.Wrecked);
                    else if (Rig.Pos.z >= Track.Length) End(State.Delivered);
                    break;

                case State.Delivered:
                    // She trots on into town by herself.
                    Rig.Invulnerable = true;
                    Autopilot.Decide(Rig, Track, out bool dl, out bool dr);
                    Drive(true, true, dt);
                    if (confirm && StateTime > 1f) Restart(true);
                    break;

                case State.Wrecked:
                    Rig.Speed = Mathf.MoveTowards(Rig.Speed, 0f, 14f * dt);
                    Rig.Invulnerable = true;
                    Drive(false, false, dt * (Rig.Speed > 0.1f ? 1f : 0f));
                    if (confirm && StateTime > 0.8f) Restart(true);
                    break;

                case State.Fallen:
                    Rig.Tick(false, false, dt);
                    if (confirm && StateTime > 0.8f) Restart(true);
                    break;
            }

            AnimateCourse(dt);
            UpdateCamera(dt);
            sfx.SetWind(Rig.Falling ? 1f : Rig.Speed01);
            Capture();
        }

        static bool ReadLeft()
        {
            return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) || Input.GetMouseButton(0);
        }

        static bool ReadRight()
        {
            return Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) || Input.GetMouseButton(1);
        }

        void Drive(bool left, bool right, float dt)
        {
            HoldLeft = left;
            HoldRight = right;
            if (dt <= 0f) return;
            Rig.Tick(left, right, dt);

            // Dust from the hooves and wheels, heavier on the verge.
            dustTimer -= dt;
            if (dustTimer <= 0f && Rig.Speed > 3f && !Rig.Falling)
            {
                dustTimer = Rig.OnVerge ? 0.03f : Mathf.Lerp(0.14f, 0.05f, Rig.Speed01);
                Color dust = Rig.OnVerge ? new Color(0.36f, 0.40f, 0.30f) : new Color(0.56f, 0.47f, 0.40f);
                fx.Burst(Rig.CoachPos + Vector3.up * 0.2f, dust, 1, 1.2f, 0.7f, -1.5f, 0.3f, -Rig.Forward * 2f);
                if (Rig.OnVerge) shake = Mathf.Max(shake, 0.12f);
            }
        }

        // ---------------------------------------------------------------- events

        void OnSnapped(int side)
        {
            sfx.Snap();
            shake = 0.35f;
            Say(side < 0 ? "The LEFT rein snapped!" : "The RIGHT rein snapped!", 2.6f);
        }

        void OnHit(Obstacle o)
        {
            sfx.Crash();
            shake = 0.8f;
            o.Visual.SetActive(false);
            Vector3 at = new Vector3(o.Mid.x, Track.Height(o.Z) + 0.6f, o.Z);
            Color debris = o.IsLog ? new Color(0.40f, 0.27f, 0.18f) : new Color(0.36f, 0.35f, 0.44f);
            fx.Burst(at, debris, 16, 6f, 1.1f, 14f, 0.5f, Rig.Forward * 5f + Vector3.up * 4f);

            int spilled = Mathf.Min(Letters, 2);
            Letters -= spilled;
            if (spilled > 0)
                fx.Burst(Rig.CoachPos + Vector3.up * 2.4f, new Color(1f, 0.93f, 0.72f), spilled * 4, 4f, 1.4f, 5f, 0.35f,
                    Vector3.up * 4f, true);

            if (Rig.Wheels > 0)
                Say(spilled > 0 ? "Crack!  Letters all over the road." : "Crack!  The axle won't take many of those.");
        }

        void OnSpooked(Spook s)
        {
            sfx.Caw();
            s.Crows.SetActive(false);
            fx.Burst(s.Perch, new Color(0.04f, 0.04f, 0.06f), 9, 4f, 1.8f, -6f, 0.4f,
                Vector3.up * 3f + Vector3.left * s.Side * 4f);
            if (Current == State.Playing) Say("Crows!  She bolts.");
        }

        void OnCollected(Pickup p)
        {
            p.Visual.gameObject.SetActive(false);
            if (Current != State.Playing) return;
            Letters++;
            sfx.Chime(streak);
            streak++;
            streakTimer = 1.6f;
            fx.Burst(p.Pos, new Color(1f, 0.9f, 0.6f), 5, 2.5f, 0.5f, -2f, 0.18f, Vector3.up * 2f, true);
        }

        // ---------------------------------------------------------------- presentation

        void AnimateCourse(float dt)
        {
            float z = Rig.Pos.z;
            foreach (var p in Track.Pickups)
            {
                if (p.Taken || p.Pos.z < z - 10f || p.Pos.z > z + 130f) continue;
                p.Visual.localRotation = Quaternion.Euler(12f, Time.time * 140f + p.Pos.z * 20f, 0f);
                p.Visual.position = p.Pos + Vector3.up * Mathf.Sin(Time.time * 3f + p.Pos.z) * 0.12f;
            }

            Vector3 l = Rig.LanternPos;
            float flicker = 0.92f + 0.08f * Mathf.PerlinNoise(Time.time * 9f, 0.5f);
            Shader.SetGlobalVector("_LanternPos", new Vector4(l.x, l.y, l.z, 17f));
            Shader.SetGlobalColor("_LanternColor", new Color(1.25f, 0.82f, 0.42f) * flicker);
        }

        void UpdateCamera(float dt)
        {
            float lag = Current == State.Title ? 1.2f : 3.2f;
            camYaw = Mathf.LerpAngle(camYaw, Rig.Heading, 1f - Mathf.Exp(-lag * dt));
            float rad = camYaw * Mathf.Deg2Rad;
            Vector3 back = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

            Vector3 anchor = Vector3.Lerp(Rig.Pos, Rig.CoachPos, Current == State.Title ? 0.6f : 0.45f);
            Vector3 pos, look;
            if (Current == State.Title)
            {
                // Low three-quarter view for the attract loop.
                Vector3 side = new Vector3(back.z, 0f, -back.x);
                pos = anchor - back * 3.5f + side * 7.5f + Vector3.up * 2.6f;
                look = anchor + back * 2.5f + Vector3.up * 1.6f;
            }
            else if (Rig.Falling)
            {
                pos = cam.transform.position;
                look = Rig.CoachPos;
            }
            else
            {
                float s = Rig.Speed01;
                // High enough that the horse and both reins stay in view over the coach roof.
                pos = anchor - back * Mathf.Lerp(10.5f, 11.5f, s) + Vector3.up * Mathf.Lerp(9f, 8.6f, s);
                look = anchor + back * 5f + Vector3.up * 0.6f;
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, Mathf.Lerp(58f, 76f, s * s), 1f - Mathf.Exp(-3f * dt));
            }

            shake = Mathf.MoveTowards(shake, 0f, 1.6f * dt);
            pos += UnityEngine.Random.insideUnitSphere * shake * 0.35f;

            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            sky.position = pos;
        }

        void Capture()
        {
            if (shotDir == null) return;

            if (Current == State.Title)
            {
                if (shotIndex == 0 && StateTime > 1.5f)
                {
                    Shot("title");
                    shotIndex = 1;
                }
            }
            else if (Current == State.Playing)
            {
                if (shotIndex < ShotTimes.Length && RunTime > ShotTimes[shotIndex])
                {
                    shotIndex++;
                    Shot("run" + shotIndex);
                }
            }
            else
            {
                // Run over, however it ended: grab the result card and leave.
                if (!resultShot && StateTime > 2.5f)
                {
                    Shot("result");
                    resultShot = true;
                    Debug.Log("Capture run ended: " + Current + ", letters " + Letters + "/" + LettersTotal +
                              ", time " + RunTime.ToString("0.0") + "s, knocks left " + Rig.Wheels);
                }
                if (StateTime > 4f) Application.Quit();
            }
        }

        void Shot(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, name + ".png"));
        }
    }
}
