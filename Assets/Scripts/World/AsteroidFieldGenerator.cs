using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceClimb
{
    /// <summary>
    /// Editor-time generator for the asteroid corridor. Produces a seeded,
    /// reproducible layout: a meandering "spline" path of asteroids, a few
    /// stepping-stone clusters, and decoy spurs that lead to hazards.
    /// Intended workflow:
    ///   1. Tweak the seed/parameters in the inspector.
    ///   2. Right-click the component header → "Generate Field".
    ///   3. If the layout's bad, change the seed and regenerate.
    ///   4. Save the scene; the generated asteroids ship with the scene.
    ///
    /// Generation is deterministic for a given (seed, parameter) pair so the
    /// final scene snapshot is stable.
    /// </summary>
    public class AsteroidFieldGenerator : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Asteroid prefab to instantiate. Must have an Asteroid component.")]
        [SerializeField] GameObject asteroidPrefab;
        [Tooltip("Parent transform that receives all generated asteroids. Cleared on each Generate().")]
        [SerializeField] Transform container;

        [Header("Seed")]
        [Tooltip("Change this to re-roll the entire field deterministically. " +
            "Used for editor Generate() and as the runtime fallback when " +
            "seedRotation is empty.")]
        [SerializeField] int seed = 12345;
        [Tooltip("Pool of seeds the runtime cycles through, one per play. " +
            "Each entry is a vetted layout — leave empty to disable runtime " +
            "regeneration and ship the editor-baked field instead.")]
        [SerializeField] int[] seedRotation = { 12345, 28471, 99102, 31337, 76801, 41213 };
        [Tooltip("If true, on Awake the runtime picks the next seed from " +
            "seedRotation, wipes the editor-baked field, and regenerates. " +
            "Each play gets a different layout; the index advances and persists " +
            "in PlayerPrefs.")]
        [SerializeField] bool rotateSeedAtRuntime = true;
        const string SeedIndexPrefKey = "SpaceClimb.FieldSeedIndex";

        [Header("Path shape")]
        [Tooltip("Bottom of the procedural path. Should sit ABOVE the starter " +
            "ring's top edge — otherwise the path's first slots fight the " +
            "starters for placement and most get dropped.")]
        [SerializeField] float yStart = 7f;
        [SerializeField] float yEnd = 200f;
        [Tooltip("Tilt the climb path off vertical so the player has to traverse " +
            "sideways as they climb. 0 = straight up; 30 = path leans 30° from " +
            "vertical (climbing at 60° from horizontal).")]
        [Range(0f, 75f)][SerializeField] float pathTiltDegrees = 30f;
        [Tooltip("Horizontal direction the path leans toward as it ascends. " +
            "Default +X — change to angle the station to a different side.")]
        [SerializeField] Vector3 pathTiltDirection = Vector3.right;
        [Tooltip("Number of control points the path interpolates between. More = wigglier path.")]
        [SerializeField] int pathControlPoints = 8;
        [Tooltip("Maximum horizontal deviation of any control point from x=0,z=0. " +
            "Wider corridors tolerate denser packing because rocks have more " +
            "perpendicular space to scatter into.")]
        [SerializeField] float pathHorizontalRadius = 18f;
        [Tooltip("Density curve along Y. 1.0 = uniform spacing top-to-bottom. " +
            "1.4–1.7 front-loads asteroids near yStart, giving the player a " +
            "richly-handheld learning area before the corridor thins out for " +
            "the long climb. Above 2.0 gets too clumped at the bottom.")]
        [SerializeField] float pathDensityExponent = 1.45f;

        [Header("Counts")]
        [Tooltip("Number of slots along the main path. Each slot tries to place " +
            "one asteroid with up to placementAttempts retries. Final count is " +
            "usually slightly under this due to spacing failures in the densest " +
            "section.")]
        [SerializeField] int mainPathCount = 80;
        [SerializeField] int hazardOnPathCount = 8;
        [SerializeField] int clusterCount = 5;
        [SerializeField] int stonesPerCluster = 7;
        [SerializeField] int decoySpurCount = 4;
        [SerializeField] int asteroidsPerSpur = 3;

        [Header("Per-asteroid scatter")]
        [SerializeField] float minPerpScatter = 0.6f;
        [SerializeField] float maxPerpScatter = 4.5f;
        [Tooltip("Visual gap (meters) between asteroid surfaces. Center spacing " +
            "is enforced as (radiusA + radiusB + this), so two big rocks naturally " +
            "stay further apart than two small ones. 0.8m gives plenty of " +
            "breathing room — rocks read as separate objects, not a textured wall.")]
        [SerializeField] float surfaceGap = 0.8f;
        [Tooltip("Tries per slot before giving up — placement re-rolls the perp angle " +
            "(and a small Y jitter) if the first sample collides, instead of " +
            "dropping the slot entirely. Higher counts cost ~nothing at bake " +
            "time and pay off in the densest section near the start.")]
        [SerializeField] int placementAttempts = 10;

        [Header("Per-asteroid scale")]
        [Tooltip("Scale is a multiplier on the asteroid prefab. The mesh root " +
            "has effective extents ~1.85m at scale 1, so scale 0.65 ≈ 1.20m radius " +
            "(chunky climb anchor) and scale 1.55 ≈ 2.87m (landmark boulder).")]
        [SerializeField] float scaleMin = 0.65f;
        [SerializeField] float scaleMax = 1.55f;
        [Tooltip("Approximate asteroid bounding radius at root scale 1.0 — " +
            "measured at runtime as ~1.96 from the prefab's mesh bounds. We use " +
            "2.0 to round up: random rotation lets the convex hull's long axis " +
            "point in any direction, so placement math must assume the worst " +
            "case to avoid spawning a rotated mesh inside the player.")]
        [SerializeField] float assumedBaseRadius = 2.0f;

        [Header("Behavior chances (main-path asteroids)")]
        [Range(0f, 1f)][SerializeField] float spinningChance = 0.10f;
        [Tooltip("Drifting asteroids oscillate via MovePosition, which sweeps " +
            "their collider through whatever sits in their amplitude window. " +
            "Keep this low and use small drift amplitudes (set in SpawnAsteroid) " +
            "so drifters don't visually interpenetrate static neighbors.")]
        [Range(0f, 1f)][SerializeField] float driftingChance = 0.04f;

        [Header("Cluster appearance")]
        [Tooltip("Cluster stones are smaller, lighter handhold rocks.")]
        [SerializeField] float clusterStoneScaleMin = 0.25f;
        [SerializeField] float clusterStoneScaleMax = 0.55f;
        [SerializeField] float clusterRadius = 3.5f;

        [Header("Starter handholds (near spawn)")]
        [Tooltip("How many big handholds to place around the player spawn. " +
            "These spawn through the same Asteroid pipeline as the procedural " +
            "rocks (mass tier, damping, behavior all configured in Awake) — " +
            "so they don't have the weird drift behavior hand-authored " +
            "Rigidbody starters had before.")]
        [SerializeField] int starterCount = 4;
        [Tooltip("Small handhold scale — chunky enough to grab cleanly, small " +
            "enough to fit close to the rig without occupying half the spawn " +
            "pocket. Range tuned so adjacent starters never surface-overlap.")]
        [SerializeField] float starterScaleMin = 0.30f;
        [SerializeField] float starterScaleMax = 0.45f;
        [Tooltip("Surface gap from rig center to closest rock surface (m). 1.0 " +
            "puts the rocks at extended-arm reach — the player can grab them " +
            "without drifting first. Below 0.7 risks mesh-spike overlap with " +
            "the rig's capsule when a starter is rotated unfavorably.")]
        [SerializeField] float starterSurfaceFromSpawn = 1.0f;
        [Tooltip("Vertical offset above the spawn position (m). 0 places rock " +
            "centers level with the rig — keeping starter tops below yStart " +
            "so the main path's first slots don't have to fight for placement.")]
        [SerializeField] float starterHeightOffset = 0f;
        [Tooltip("Arc (degrees) the starters spread across. 360 = full ring " +
            "around the player (each step = 360/count). Smaller values bias " +
            "the rocks toward the forward direction with endpoints.")]
        [SerializeField] float starterArcDegrees = 360f;

        [Header("Starter trail (between ring and main path)")]
        [Tooltip("Extra small handhold rocks placed in the Y gap between the " +
            "starter ring's top edge and the main path's first slot. These " +
            "bridge the climb visually and give the player stepping stones " +
            "to begin the ascent without a long unanchored drift. Set 0 to " +
            "disable.")]
        [SerializeField] int starterTrailCount = 6;
        [SerializeField] float starterTrailScaleMin = 0.30f;
        [SerializeField] float starterTrailScaleMax = 0.55f;

        // ---- Internal state during one Generate() ----
        Vector3[] controlPoints;
        // Cached rotation applied by PathAt/TangentAt to map "path-local"
        // coordinates (where the path runs straight up the local Y axis) to
        // world. Control points are stored in the unrotated local frame so
        // the y-monotone segment lookup keeps working unchanged.
        Quaternion pathRotation = Quaternion.identity;
        Vector3 pathOrigin = Vector3.zero;
        System.Random rng;
        readonly List<Vector3> placed = new();
        readonly List<float> placedRadii = new();          // parallel to `placed`
        readonly List<Asteroid> generated = new();
        // External "no-go" spheres that the generator must avoid (e.g. the
        // player's spawn capsule and the starter handhold cluster). Cleared
        // at the top of each Generate().
        readonly List<(Vector3 c, float r)> externalAvoid = new();

        void Awake()
        {
            // Skip in editor non-play mode — Generate() is the editor entry point.
            if (!Application.isPlaying || !rotateSeedAtRuntime) return;
            if (seedRotation == null || seedRotation.Length == 0) return;
            if (asteroidPrefab == null || container == null) return;
            int idx = PlayerPrefs.GetInt(SeedIndexPrefKey, 0);
            seed = seedRotation[((idx % seedRotation.Length) + seedRotation.Length) % seedRotation.Length];
            PlayerPrefs.SetInt(SeedIndexPrefKey, idx + 1);
            Generate();
        }

        /// <summary>
        /// Wipe the container and produce a new field. Public so the editor
        /// menu and external automation (like our execute_code helper) can both
        /// call it.
        /// </summary>
        public void Generate()
        {
            if (asteroidPrefab == null || container == null)
            {
                Debug.LogWarning("AsteroidFieldGenerator: asteroidPrefab and container must be assigned.", this);
                return;
            }

            ClearContainer();
            rng = new System.Random(seed);
            placed.Clear();
            placedRadii.Clear();
            generated.Clear();
            EnsurePathFrame();
            BuildExternalAvoid();

            BuildControlPoints();
            // Starters BEFORE main path: SpawnAsteroid adds them to `placed`, so
            // procedural rocks naturally avoid the starter ring and we get a
            // clean pocket around spawn that doesn't fight with the pathing.
            PlaceStartersNearSpawn();
            // Trail fills the Y gap between the starter ring and the first
            // dense main-path slots — keeps the climb visually continuous.
            PlaceStarterTrail();
            PlaceMainPath();
            PlaceClusters();
            PlaceDecoySpurs();
            AssignOnPathHazards();

#if UNITY_EDITOR
            // Mark the scene dirty so the change actually saves.
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
            Debug.Log($"[AsteroidFieldGenerator] Generated {generated.Count} asteroids (seed={seed}).", this);
        }

#if UNITY_EDITOR
        [ContextMenu("Generate Field")]
        void GenerateFromContextMenu() => Generate();

        [ContextMenu("Clear Field")]
        void ClearFromContextMenu() => ClearContainer();
#endif

        void ClearContainer()
        {
            // Iterate backwards because the Destroy* calls reshuffle siblings.
            // We use DestroyImmediate even at runtime BECAUSE the runtime regen
            // happens in Awake — if we use deferred Destroy, the old colliders
            // remain active for one frame alongside the freshly spawned new
            // ones. With ~120 asteroids in a 200m corridor, an old/new overlap
            // around the rig's spawn pocket is common, and the depenetration
            // impulse that produces sends the player off the map. Editor
            // (non-play) also needs DestroyImmediate so the scene change is
            // visible in the inspector before the next paint.
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i).gameObject;
#if UNITY_EDITOR
                DestroyImmediate(child);
#else
                if (Application.isPlaying) DestroyImmediate(child); else Destroy(child);
#endif
            }
        }

        // ===== Path =====

        void BuildControlPoints()
        {
            controlPoints = new Vector3[pathControlPoints];
            // Anchor the first and last to (0, yStart) and (0, yEnd) so the path
            // starts and ends near the climb axis. Middle points wander.
            float ySpan = yEnd - yStart;
            for (int i = 0; i < pathControlPoints; i++)
            {
                float t = (float)i / (pathControlPoints - 1);
                float y = yStart + ySpan * t;
                bool firstOrLast = i == 0 || i == pathControlPoints - 1;
                float x = firstOrLast ? 0f : ((float)rng.NextDouble() * 2f - 1f) * pathHorizontalRadius;
                float z = firstOrLast ? 0f : ((float)rng.NextDouble() * 2f - 1f) * pathHorizontalRadius;
                controlPoints[i] = new Vector3(x, y, z);
            }
        }

        /// <summary>Compute pathRotation/pathOrigin from inspector values.</summary>
        void EnsurePathFrame()
        {
            pathOrigin = Vector3.zero;
            Vector3 dir = pathTiltDirection;
            dir.y = 0f;
            dir = dir.sqrMagnitude < 1e-4f ? Vector3.right : dir.normalized;
            float a = pathTiltDegrees * Mathf.Deg2Rad;
            // The path's local up (0,1,0) maps to this world direction. Mostly
            // vertical, with a horizontal component pointing toward `dir`. With
            // tilt=0 this is straight up — i.e., the original behavior.
            Vector3 desiredUp = (Vector3.up * Mathf.Cos(a) + dir * Mathf.Sin(a)).normalized;
            pathRotation = Quaternion.FromToRotation(Vector3.up, desiredUp);
        }

        /// <summary>Smoothstep-interpolated point on the path at path-local Y, returned in world space.</summary>
        Vector3 PathAt(float y)
        {
            Vector3 local;
            if (controlPoints == null || controlPoints.Length < 2)
            {
                local = new Vector3(0, y, 0);
            }
            else
            {
                local = new Vector3(0, y, 0);
                bool found = false;
                // Find segment by walking the y-monotone control point list.
                for (int i = 0; i < controlPoints.Length - 1; i++)
                {
                    Vector3 a = controlPoints[i];
                    Vector3 b = controlPoints[i + 1];
                    if (y >= a.y && y <= b.y)
                    {
                        float t = Mathf.InverseLerp(a.y, b.y, y);
                        float s = Mathf.SmoothStep(0f, 1f, t);
                        local = new Vector3(Mathf.Lerp(a.x, b.x, s), y, Mathf.Lerp(a.z, b.z, s));
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    // Out of range — return endpoint clamped at correct y.
                    var last = controlPoints[controlPoints.Length - 1];
                    local = new Vector3(last.x, y, last.z);
                }
            }
            return pathOrigin + pathRotation * local;
        }

        /// <summary>"Up" direction along the (possibly tilted) path at path-local Y.</summary>
        Vector3 TangentAt(float y)
        {
            Vector3 a = PathAt(y - 0.5f);
            Vector3 b = PathAt(y + 0.5f);
            Vector3 t = b - a;
            return t.sqrMagnitude < 1e-4f ? pathRotation * Vector3.up : t.normalized;
        }

        // ===== Placement =====

        void PlaceStartersNearSpawn()
        {
            // A few big handholds in a forward-biased arc around the rig. Spawning
            // through SpawnAsteroid means the prefab's full Asteroid lifecycle runs
            // (Rigidbody mass / damping / collision mode), avoiding the
            // hand-authored quirks that made the previous Starters root drift
            // unpredictably when grabbed.
            var rig = UnityEngine.Object.FindAnyObjectByType<ZeroGRig>();
            if (rig == null) return;
            Vector3 spawn = rig.transform.position;

            // Player default-faces -Z. Two layouts:
            //   • Full circle (arc ≥ 359°): step uniformly without duplicating
            //     forward and back endpoints. 4 starters → 0/90/180/270°.
            //   • Forward arc: spread with endpoints so the first/last sit at
            //     the arc edges and the middle starter sits dead-center forward.
            float arcRad = starterArcDegrees * Mathf.Deg2Rad;
            bool fullCircle = starterArcDegrees >= 359f;

            for (int i = 0; i < starterCount; i++)
            {
                float angleFromForward;
                if (fullCircle)
                {
                    // Half-step offset means starters land at diagonals
                    // (45°/135°/225°/315° for count=4) — leaves the cardinal
                    // forward direction clear for the fixed Satellite anchor,
                    // so a starter never spawns INSIDE the satellite's body.
                    angleFromForward = (((float)i + 0.5f) / starterCount) * Mathf.PI * 2f;
                }
                else
                {
                    float t = starterCount == 1 ? 0.5f : (float)i / (starterCount - 1);
                    angleFromForward = Mathf.Lerp(-arcRad * 0.5f, arcRad * 0.5f, t);
                }

                float scale = RandomScale(starterScaleMin, starterScaleMax);
                float radius = scale * assumedBaseRadius;
                float centerDist = starterSurfaceFromSpawn + radius;

                // Forward = -Z. Rotate by `angleFromForward` around Y.
                float c = Mathf.Cos(angleFromForward);
                float s = Mathf.Sin(angleFromForward);
                Vector3 offset = new Vector3(s * centerDist, starterHeightOffset, -c * centerDist);
                Vector3 pos = spawn + offset;

                // Skip starters that would land too close to the fixed Satellite
                // anchor — those two showed up "under" the satellite and read as
                // a placement bug. CanPlace already enforces this via externalAvoid
                // for procedural rocks; starters bypass that check, so we apply
                // it here explicitly.
                if (!CanPlace(pos, radius)) continue;

                // Static + Heavy = fixed handhold (kinematic landmark, see Asteroid.ConfigureBehavior).
                SpawnAsteroid(pos, scale, AsteroidWeight.Heavy, AsteroidBehavior.Static, false);
            }
        }

        void PlaceStarterTrail()
        {
            if (starterTrailCount <= 0) return;
            var rig = UnityEngine.Object.FindAnyObjectByType<ZeroGRig>();
            if (rig == null) return;
            Vector3 spawn = rig.transform.position;

            // Y window above the starter ring (its tallest possible top edge)
            // up to just below the main path's first slot. Conservative bounds
            // so the trail rocks don't accidentally overlap either neighbor.
            float starterTop = spawn.y + starterHeightOffset + (starterScaleMax * assumedBaseRadius);
            float trailMinY = starterTop + 0.5f;
            float trailMaxY = yStart - 0.4f;
            if (trailMaxY <= trailMinY) return;     // no room — yStart too low

            for (int i = 0; i < starterTrailCount; i++)
            {
                // Even Y spread; each rock samples a perp scatter around the
                // path centerline and retries on collision like the main pass.
                float t = (i + 0.5f) / starterTrailCount;
                float y = Mathf.Lerp(trailMinY, trailMaxY, t);
                Vector3 center = PathAt(y);
                Vector3 tangent = TangentAt(y);
                float thisScale = RandomScale(starterTrailScaleMin, starterTrailScaleMax);
                float thisRadius = thisScale * assumedBaseRadius;

                Vector3 pos = Vector3.zero;
                bool placedOk = false;
                for (int attempt = 0; attempt < placementAttempts; attempt++)
                {
                    float scatter = Mathf.Lerp(1.5f, 4.0f, (float)rng.NextDouble());
                    pos = center + RandomPerp(tangent) * scatter;
                    if (CanPlace(pos, thisRadius)) { placedOk = true; break; }
                }
                if (!placedOk) continue;
                // Medium = throwable handhold. Player can yank these around;
                // gives the start area a different feel from the kinematic
                // landmark starters and lets the player practice mass-aware grabs.
                SpawnAsteroid(pos, thisScale, AsteroidWeight.Medium, AsteroidBehavior.Static, false);
            }
        }

        void PlaceMainPath()
        {
            // Density-skewed Y placement. Map slot index i ∈ [0, N) to a normalized
            // t ∈ (0, 1), apply t' = t^pathDensityExponent, lerp to [yStart, yEnd].
            // With exponent > 1, early slots cluster low — so the player gets a
            // dense handhold zone at the start and the corridor naturally thins as
            // they climb. Per-slot Y jitter is computed from the LOCAL slot delta
            // (distance to the next slot's Y) so the dense start doesn't have rocks
            // jittering across each other while the sparse top still feels organic.
            float ySpan = yEnd - yStart;
            for (int i = 0; i < mainPathCount; i++)
            {
                float t = (i + 0.5f) / mainPathCount;
                float y = yStart + ySpan * Mathf.Pow(t, pathDensityExponent);
                float tNext = (i + 1.5f) / mainPathCount;
                float yNext = yStart + ySpan * Mathf.Pow(tNext, pathDensityExponent);
                float slotDelta = Mathf.Max(0.5f, yNext - y);
                float yJitter = slotDelta * 0.40f;
                float jitteredY = y + ((float)rng.NextDouble() * 2f - 1f) * yJitter;

                Vector3 center = PathAt(jitteredY);
                Vector3 tangent = TangentAt(jitteredY);
                float thisScale = RandomScale(scaleMin, scaleMax);
                float thisRadius = thisScale * assumedBaseRadius;

                // Retry loop. Re-roll perp angle, and on later attempts also nudge
                // Y within the slot's jitter window — gives placement more freedom
                // in the dense start where same-Y collisions are most likely.
                Vector3 pos = Vector3.zero;
                bool placedOk = false;
                for (int attempt = 0; attempt < placementAttempts; attempt++)
                {
                    float ay = jitteredY;
                    if (attempt >= placementAttempts / 2)
                        ay += ((float)rng.NextDouble() * 2f - 1f) * yJitter;
                    Vector3 c = PathAt(ay);
                    Vector3 tg = TangentAt(ay);
                    pos = SamplePerpAround(c, tg);
                    if (CanPlace(pos, thisRadius)) { placedOk = true; break; }
                }
                if (!placedOk) continue;
                AsteroidBehavior beh = PickBehavior();
                AsteroidWeight wt = PickWeightForBehavior(beh);
                SpawnAsteroid(pos, thisScale, wt, beh, false);
            }
        }

        void PlaceClusters()
        {
            // Clusters live mid-path (avoid the very start and end so the player
            // gets a smooth handhold flow first and last).
            float ySpan = yEnd - yStart;
            for (int c = 0; c < clusterCount; c++)
            {
                float t = 0.18f + (c + 0.5f) / clusterCount * 0.7f;
                float y = yStart + ySpan * t;
                Vector3 center = PathAt(y);
                Vector3 tangent = TangentAt(y);
                // Cluster center is offset further from the path than normal scatter,
                // so the cluster reads as a side platform rather than path bunching.
                Vector3 clusterCenter = center + RandomPerp(tangent) * (maxPerpScatter * 1.2f);

                for (int s = 0; s < stonesPerCluster; s++)
                {
                    float stoneScale = RandomScale(clusterStoneScaleMin, clusterStoneScaleMax);
                    float stoneRadius = stoneScale * assumedBaseRadius;
                    Vector3 inCluster = Vector3.zero;
                    bool placedOk = false;
                    for (int attempt = 0; attempt < placementAttempts; attempt++)
                    {
                        inCluster = clusterCenter + RandomInsideSphere() * clusterRadius;
                        if (CanPlace(inCluster, stoneRadius)) { placedOk = true; break; }
                    }
                    if (!placedOk) continue;
                    SpawnAsteroid(inCluster, stoneScale,
                        AsteroidWeight.Light,           // small handhold rocks
                        AsteroidBehavior.Static,
                        false);
                }
            }
        }

        void PlaceDecoySpurs()
        {
            // Decoy spurs branch off the main path with 1-2 tempting stones,
            // ending in a hazard. The "trap" appeal: a shorter visual path
            // through the corridor, but fatal at the end.
            float ySpan = yEnd - yStart;
            for (int s = 0; s < decoySpurCount; s++)
            {
                float t = 0.25f + ((float)rng.NextDouble() * 0.5f);
                float y = yStart + ySpan * t;
                Vector3 center = PathAt(y);
                Vector3 tangent = TangentAt(y);
                Vector3 dir = RandomPerp(tangent).normalized;
                for (int k = 0; k < asteroidsPerSpur; k++)
                {
                    float distAlong = (k + 1) * (maxPerpScatter * 1.4f);
                    bool isLast = (k == asteroidsPerSpur - 1);
                    float thisScale = RandomScale(scaleMin, scaleMax * 0.85f);
                    float thisRadius = thisScale * assumedBaseRadius;
                    Vector3 pos = center + dir * distAlong + new Vector3(0, ((float)rng.NextDouble() * 2f - 1f) * 1.2f, 0);
                    // Single attempt with a small y-jitter retry; spurs are
                    // intentionally sparse so we don't loop hard for placement.
                    if (!CanPlace(pos, thisRadius))
                    {
                        pos += new Vector3(0, ((float)rng.NextDouble() * 2f - 1f) * 2.0f, 0);
                        if (!CanPlace(pos, thisRadius)) continue;
                    }
                    SpawnAsteroid(pos, thisScale,
                        isLast ? AsteroidWeight.Heavy : AsteroidWeight.Medium,
                        AsteroidBehavior.Static,
                        isHazard: isLast);          // hazard at the end of every spur
                }
            }
        }

        void AssignOnPathHazards()
        {
            // Pick distinct main-path asteroids and flag them as hazards. We do
            // this after placement (rather than at spawn) so the distribution
            // is always exactly hazardOnPathCount even if some main-path slots
            // got skipped to spacing.
            var candidates = new List<Asteroid>();
            foreach (var a in generated)
                if (!a.IsHazard) candidates.Add(a);

            int target = Mathf.Min(hazardOnPathCount, candidates.Count);
            // Spread hazards across y by sorting and picking by stride.
            candidates.Sort((x, y) => x.transform.position.y.CompareTo(y.transform.position.y));
            // Trim head/tail so the very first and very last asteroids are never hazards
            // (avoids unfair surprise at the start and at the docking approach).
            int trim = Mathf.Max(2, candidates.Count / 8);
            int low = Mathf.Min(trim, candidates.Count - 1);
            int high = Mathf.Max(low, candidates.Count - trim);
            int span = Mathf.Max(1, high - low);

            for (int i = 0; i < target; i++)
            {
                int idx = low + (int)((i + 0.5f) / target * span);
                idx = Mathf.Clamp(idx, 0, candidates.Count - 1);
                candidates[idx].SetHazard(true);
                candidates.RemoveAt(idx);
                if (candidates.Count == 0) break;
                // Recompute span as we go since indices shift.
                span = Mathf.Max(1, candidates.Count - 2 * trim / Mathf.Max(1, target - i - 1));
            }
        }

        // ===== Sampling helpers =====

        Vector3 SamplePerpAround(Vector3 center, Vector3 tangent)
        {
            float radius = Mathf.Lerp(minPerpScatter, maxPerpScatter, (float)rng.NextDouble());
            return center + RandomPerp(tangent) * radius;
        }

        Vector3 RandomPerp(Vector3 tangent)
        {
            // Build a stable orthonormal basis perpendicular to the tangent.
            Vector3 right = Vector3.Cross(tangent, Vector3.up);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(right, tangent).normalized;
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            return right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
        }

        Vector3 RandomInsideSphere()
        {
            // Rejection-sampled uniform sphere. Up to 8 tries to avoid degenerate
            // loops; falls back to surface point on rejection failure.
            for (int i = 0; i < 8; i++)
            {
                Vector3 v = new Vector3(
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f);
                if (v.sqrMagnitude <= 1f) return v;
            }
            return Vector3.right;
        }

        bool CanPlace(Vector3 pos, float thisRadius)
        {
            // Linear scan is fine — placed list rarely exceeds 60 entries.
            // For larger fields a spatial hash would pay off.
            // Per-pair min distance = sum of radii + visual surfaceGap. This is the
            // fix for "asteroids clipping each other": the old constant minSpacing
            // ignored asteroid scale, so two big rocks could be placed surface-overlapping.
            for (int i = 0; i < placed.Count; i++)
            {
                float minD = thisRadius + placedRadii[i] + surfaceGap;
                if ((placed[i] - pos).sqrMagnitude < minD * minD) return false;
            }
            // External avoid spheres: spawn capsule + starter handholds.
            for (int i = 0; i < externalAvoid.Count; i++)
            {
                float minD = thisRadius + externalAvoid[i].r + surfaceGap;
                if ((externalAvoid[i].c - pos).sqrMagnitude < minD * minD) return false;
            }
            return true;
        }

        /// <summary>
        /// Seed the no-go list from the live scene: the player's spawn capsule
        /// (so the procedural field never spawns rocks inside the body) and any
        /// existing "Starters" siblings (so seeded rotations don't fight the
        /// hand-placed starter cluster). Fails open: missing references just
        /// mean the corresponding constraint is skipped.
        /// </summary>
        void BuildExternalAvoid()
        {
            externalAvoid.Clear();
            // Spawn capsule: approximate as a single sphere at the rig position.
            // Slight oversize so the field gives the spawn area a clear pocket.
            var rig = UnityEngine.Object.FindAnyObjectByType<ZeroGRig>();
            if (rig != null)
                externalAvoid.Add((rig.transform.position + new Vector3(0f, 0.9f, 0f), 1.2f));
            // Starter handhold cluster — find a sibling root named "Starters".
            var starters = GameObject.Find("Starters");
            if (starters != null)
            {
                foreach (Transform t in starters.transform)
                {
                    float r = t.localScale.x * assumedBaseRadius;
                    externalAvoid.Add((t.position, r));
                }
            }
            // Fixed satellite anchor — bounding sphere of all child colliders so
            // procedural rocks never spawn inside the satellite or close enough
            // for their meshes to clip its panels.
            var satellite = GameObject.Find("Satellite");
            if (satellite != null)
            {
                var cols = satellite.GetComponentsInChildren<Collider>();
                if (cols.Length > 0)
                {
                    Bounds b = cols[0].bounds;
                    for (int i = 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
                    externalAvoid.Add((b.center, b.extents.magnitude));
                }
            }
        }

        AsteroidBehavior PickBehavior()
        {
            float r = (float)rng.NextDouble();
            if (r < spinningChance) return AsteroidBehavior.Spinning;
            if (r < spinningChance + driftingChance) return AsteroidBehavior.Drifting;
            return AsteroidBehavior.Static;
        }

        AsteroidWeight PickWeightForBehavior(AsteroidBehavior beh)
        {
            // Spinning + Drifting want HIGH mass so the behavior dominates the
            // mass-aware grab split (player rides them, doesn't yank them around).
            // Static asteroids get a varied distribution.
            if (beh != AsteroidBehavior.Static) return AsteroidWeight.Heavy;
            float r = (float)rng.NextDouble();
            if (r < 0.5f) return AsteroidWeight.Heavy;
            if (r < 0.8f) return AsteroidWeight.Medium;
            return AsteroidWeight.Light;
        }

        float RandomScale(float lo, float hi) => Mathf.Lerp(lo, hi, (float)rng.NextDouble());

        // ===== Spawn =====

        void SpawnAsteroid(Vector3 pos, float scale, AsteroidWeight weight, AsteroidBehavior behavior, bool isHazard)
        {
#if UNITY_EDITOR
            // PrefabUtility keeps the prefab connection so further prefab edits propagate.
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asteroidPrefab, container);
#else
            var go = Instantiate(asteroidPrefab, container);
#endif
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 360f);
            go.transform.localScale = Vector3.one * scale;

            var asteroid = go.GetComponent<Asteroid>();
            if (asteroid != null)
            {
                asteroid.SetWeight(weight);
                asteroid.SetHazard(isHazard);
                if (behavior == AsteroidBehavior.Spinning)
                {
                    Vector3 spinAxis = new Vector3(
                        (float)rng.NextDouble() * 2f - 1f,
                        (float)rng.NextDouble() * 2f - 1f,
                        (float)rng.NextDouble() * 2f - 1f).normalized;
                    asteroid.SetBehavior(AsteroidBehavior.Spinning, spinAxis,
                        Mathf.Lerp(15f, 45f, (float)rng.NextDouble()));   // gentle: <60deg/s for VR comfort
                }
                else if (behavior == AsteroidBehavior.Drifting)
                {
                    Vector3 driftDir = RandomPerp(Vector3.up).normalized;   // mostly horizontal
                    // Small amplitude (0.5–1.5 m) so the drifter's swept volume
                    // stays inside its CanPlace footprint and doesn't carve
                    // through neighboring static rocks each oscillation.
                    asteroid.SetBehavior(AsteroidBehavior.Drifting, driftDir,
                        Mathf.Lerp(0.5f, 1.5f, (float)rng.NextDouble()));
                }
                generated.Add(asteroid);
            }
            placed.Add(pos);
            placedRadii.Add(scale * assumedBaseRadius);

#if UNITY_EDITOR
            EditorUtility.SetDirty(go);
#endif
        }
    }
}
