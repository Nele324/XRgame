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
        [Tooltip("Change this to re-roll the entire field deterministically.")]
        [SerializeField] int seed = 12345;

        [Header("Path shape")]
        [SerializeField] float yStart = 6f;
        [SerializeField] float yEnd = 200f;
        [Tooltip("Number of control points the path interpolates between. More = wigglier path.")]
        [SerializeField] int pathControlPoints = 8;
        [Tooltip("Maximum horizontal deviation of any control point from x=0,z=0.")]
        [SerializeField] float pathHorizontalRadius = 12f;

        [Header("Counts")]
        [Tooltip("Number of asteroids placed along the main path.")]
        [SerializeField] int mainPathCount = 36;
        [SerializeField] int hazardOnPathCount = 4;
        [SerializeField] int clusterCount = 3;
        [SerializeField] int stonesPerCluster = 5;
        [SerializeField] int decoySpurCount = 3;
        [SerializeField] int asteroidsPerSpur = 2;

        [Header("Per-asteroid scatter")]
        [SerializeField] float minPerpScatter = 0.5f;
        [SerializeField] float maxPerpScatter = 4.5f;
        [Tooltip("Reject placements within this distance of any already-placed asteroid (after scaling).")]
        [SerializeField] float minSpacing = 1.6f;

        [Header("Per-asteroid scale")]
        [SerializeField] float scaleMin = 0.55f;
        [SerializeField] float scaleMax = 2.4f;

        [Header("Behavior chances (main-path asteroids)")]
        [Range(0f, 1f)][SerializeField] float spinningChance = 0.18f;
        [Range(0f, 1f)][SerializeField] float driftingChance = 0.10f;

        [Header("Cluster appearance")]
        [Tooltip("Cluster stones are smaller, lighter handhold rocks.")]
        [SerializeField] float clusterStoneScaleMin = 0.4f;
        [SerializeField] float clusterStoneScaleMax = 0.8f;
        [SerializeField] float clusterRadius = 3f;

        // ---- Internal state during one Generate() ----
        Vector3[] controlPoints;
        System.Random rng;
        readonly List<Vector3> placed = new();
        readonly List<Asteroid> generated = new();

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
            generated.Clear();

            BuildControlPoints();
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
            // Iterate backwards because DestroyImmediate reshuffles siblings.
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i).gameObject;
#if UNITY_EDITOR
                DestroyImmediate(child);
#else
                Destroy(child);
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

        /// <summary>Smoothstep-interpolated point on the path at world Y.</summary>
        Vector3 PathAt(float y)
        {
            if (controlPoints == null || controlPoints.Length < 2) return new Vector3(0, y, 0);
            // Find segment by walking the y-monotone control point list.
            for (int i = 0; i < controlPoints.Length - 1; i++)
            {
                Vector3 a = controlPoints[i];
                Vector3 b = controlPoints[i + 1];
                if (y >= a.y && y <= b.y)
                {
                    float t = Mathf.InverseLerp(a.y, b.y, y);
                    float s = Mathf.SmoothStep(0f, 1f, t);
                    return new Vector3(Mathf.Lerp(a.x, b.x, s), y, Mathf.Lerp(a.z, b.z, s));
                }
            }
            // Out of range — return endpoint clamped at correct y.
            var last = controlPoints[controlPoints.Length - 1];
            return new Vector3(last.x, y, last.z);
        }

        /// <summary>Local "up" direction along the path (mostly y, with x/z drift contribution).</summary>
        Vector3 TangentAt(float y)
        {
            Vector3 a = PathAt(y - 0.5f);
            Vector3 b = PathAt(y + 0.5f);
            Vector3 t = b - a;
            return t.sqrMagnitude < 1e-4f ? Vector3.up : t.normalized;
        }

        // ===== Placement =====

        void PlaceMainPath()
        {
            // Even spacing in y plus jitter per slot. This avoids both perfect
            // bands (looks artificial) and pure-random clumping (looks broken).
            float ySpan = yEnd - yStart;
            float slot = ySpan / mainPathCount;
            float yJitter = slot * 0.55f;
            for (int i = 0; i < mainPathCount; i++)
            {
                float y = yStart + (i + 0.5f) * slot + ((float)rng.NextDouble() * 2f - 1f) * yJitter;
                Vector3 center = PathAt(y);
                Vector3 tangent = TangentAt(y);
                Vector3 pos = SamplePerpAround(center, tangent);
                if (!CanPlace(pos)) continue;       // skip on overlap; field thins where it would otherwise bunch
                AsteroidBehavior beh = PickBehavior();
                AsteroidWeight wt = PickWeightForBehavior(beh);
                SpawnAsteroid(pos, RandomScale(scaleMin, scaleMax), wt, beh, false);
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
                    Vector3 inCluster = clusterCenter + RandomInsideSphere() * clusterRadius;
                    if (!CanPlace(inCluster)) continue;
                    SpawnAsteroid(inCluster,
                        RandomScale(clusterStoneScaleMin, clusterStoneScaleMax),
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
                    Vector3 pos = center + dir * distAlong + new Vector3(0, ((float)rng.NextDouble() * 2f - 1f) * 1.2f, 0);
                    if (!CanPlace(pos)) continue;
                    bool isLast = (k == asteroidsPerSpur - 1);
                    SpawnAsteroid(pos,
                        RandomScale(scaleMin, scaleMax * 0.85f),
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

        bool CanPlace(Vector3 pos)
        {
            // Linear scan is fine — placed list rarely exceeds 60 entries.
            // For larger fields a spatial hash would pay off.
            float minSqr = minSpacing * minSpacing;
            for (int i = 0; i < placed.Count; i++)
                if ((placed[i] - pos).sqrMagnitude < minSqr) return false;
            return true;
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
                    asteroid.SetBehavior(AsteroidBehavior.Drifting, driftDir,
                        Mathf.Lerp(2f, 4f, (float)rng.NextDouble()));
                }
                generated.Add(asteroid);
            }
            placed.Add(pos);

#if UNITY_EDITOR
            EditorUtility.SetDirty(go);
#endif
        }
    }
}
