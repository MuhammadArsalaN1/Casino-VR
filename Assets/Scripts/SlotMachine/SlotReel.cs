using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RoyalCasino.SlotMachine
{
    /// <summary>One named symbol marker riding on a reel, at a fixed local X angle.</summary>
    public class SlotSymbol
    {
        /// <summary>Raw child GameObject name, e.g. "Spade (1)" - unique only within this reel.</summary>
        public string Name;

        /// <summary>Name with any Unity duplicate suffix stripped, e.g. "Spade" - shared across reels.</summary>
        public string CanonicalName;

        public Transform Marker;
        public float LocalAngle;
    }

    /// <summary>
    /// Drives one physical reel cylinder. Symbols are pre-existing empty child transforms
    /// placed by the artist around the mesh's local X axis - this reads their angle rather
    /// than assuming a layout, so it keeps working if the art changes.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotReel : MonoBehaviour
    {
        [Tooltip("Transform that actually spins. Defaults to this GameObject.")]
        [SerializeField] private Transform reelPivot;

        [Header("Spin shape - accelerate, cruise fast, then ease to an exact stop")]
        [Tooltip("Seconds spent speeding up from a standstill to cruise speed.")]
        [SerializeField] private float accelerateSeconds = 1f;

        [Tooltip("Seconds spent spinning at full cruise speed.")]
        [SerializeField] private float cruiseSeconds = 3f;

        [Tooltip("Seconds spent slowing from cruise speed down to an exact stop on the target symbol.")]
        [SerializeField] private float decelerateSeconds = 1f;

        [Tooltip("Full revolutions completed over one spin. Randomised per spin and rounded to a " +
                 "whole number - a fractional revolution would shift the landing angle off-target.")]
        [SerializeField] private Vector2 revolutionRange = new Vector2(6f, 10f);

        [SerializeField] private bool clockwise = true;

        [Tooltip("The (reel rotation + symbol's angle-around-axis) sum that puts a symbol dead centre " +
                 "in the window. Calibrated from the one pose known good without any calculation: at " +
                 "the authored rest rotation of 310.7 the Apple sits centred, and Apple's angle around " +
                 "the axis is 332.438, so the sum is 283.138 once wrapped to 0-360. Re-derive the same " +
                 "way if the reel art or marker layout changes.")]
        [SerializeField] private float windowAlignmentAngle = 283.138f;

        public bool IsSpinning { get; private set; }
        public IReadOnlyList<SlotSymbol> Symbols => symbols;
        public Transform ReelPivot => reelPivot != null ? reelPivot : transform;

        /// <summary>Total seconds one spin takes, from a standstill back to a standstill.</summary>
        public float SpinDuration => Mathf.Max(0.01f, accelerateSeconds + cruiseSeconds + decelerateSeconds);

        private readonly List<SlotSymbol> symbols = new List<SlotSymbol>();
        private float elapsed;
        private float duration;
        private float rampFraction;
        private float decelFraction;
        private float appliedDegrees;
        private float totalDeltaDegrees;

        private void Awake()
        {
            if (reelPivot == null)
            {
                reelPivot = transform;
            }

            CacheSymbols();
        }

        /// <summary>Rebuilds the symbol list from child transforms that carry no mesh of their own.</summary>
        public void CacheSymbols()
        {
            symbols.Clear();

            foreach (Transform child in ReelPivot)
            {
                if (child.GetComponent<MeshRenderer>() != null)
                {
                    continue;
                }

                symbols.Add(new SlotSymbol
                {
                    Name = child.name,
                    CanonicalName = StripDuplicateSuffix(child.name),
                    Marker = child,
                    LocalAngle = AngleAroundAxisOf(child)
                });
            }
        }

        /// <summary>
        /// Starts spinning toward the given symbol, landing it at the shared window angle.
        /// Matched by canonical name so the same call works across reels even when Unity has
        /// suffixed duplicate child names (e.g. "Spade (1)" on a second reel).
        /// </summary>
        public void SpinTo(string canonicalSymbolName)
        {
            SlotSymbol symbol = symbols.Find(s => s.CanonicalName == canonicalSymbolName);
            if (symbol == null)
            {
                Debug.LogError("[SlotReel] Unknown symbol '" + canonicalSymbolName + "' on " + name, this);
                return;
            }

            duration = SpinDuration;
            rampFraction = Mathf.Clamp01(accelerateSeconds / duration);
            decelFraction = Mathf.Clamp01(decelerateSeconds / duration);
            elapsed = 0f;
            appliedDegrees = 0f;

            float target = TargetAngleFor(symbol);
            float current = CurrentSpinAngle;
            float sign = clockwise ? 1f : -1f;

            float forwardDiff;
            if (clockwise)
            {
                forwardDiff = target - current;
                if (forwardDiff < 0f) forwardDiff += 360f;
            }
            else
            {
                forwardDiff = current - target;
                if (forwardDiff < 0f) forwardDiff += 360f;
                forwardDiff = -forwardDiff;
            }

            // Must be a whole number of turns - a fractional revolution count would shift the
            // landing angle by that fraction of 360 degrees and miss the intended symbol.
            float revolutions = Mathf.Round(Random.Range(revolutionRange.x, revolutionRange.y));
            totalDeltaDegrees = sign * revolutions * 360f + forwardDiff;
            IsSpinning = true;
        }

        /// <summary>Reads back the canonical name of whichever symbol is actually at the window right now.</summary>
        public string ResolveCurrentSymbol()
        {
            float current = CurrentSpinAngle;
            string best = null;
            float bestDelta = float.MaxValue;

            foreach (SlotSymbol s in symbols)
            {
                float target = TargetAngleFor(s);
                float delta = Mathf.Abs(Mathf.DeltaAngle(current, target));
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = s.CanonicalName;
                }
            }

            return best;
        }

        /// <summary>
        /// The reel's rotation about its local X axis, read from the quaternion rather than from
        /// localEulerAngles.x. Unity freely returns either (a, 0, 0) or the equivalent
        /// (180-a, 180, 180) for the same physical rotation, so reading the Euler x component
        /// silently reports the wrong angle roughly half the time - which made correctly-landed
        /// reels resolve to the wrong symbol and score wrongly.
        /// </summary>
        private float CurrentSpinAngle
        {
            get
            {
                Quaternion q = ReelPivot.localRotation;
                return NormalizeAngle(2f * Mathf.Atan2(q.x, q.w) * Mathf.Rad2Deg);
            }
        }

        /// <summary>
        /// Where a symbol marker actually sits around the reel, derived from its POSITION rather
        /// than its rotation. The markers' own rotations are near-arbitrary (they only orient the
        /// artwork to face outward) and are NOT evenly spaced, so reading them put every symbol in
        /// the wrong place. Their positions are the real layout: ~72 degrees apart for 5 symbols.
        /// The reel spins about local X, so the marker's angle is measured in the local YZ plane.
        /// </summary>
        private static float AngleAroundAxisOf(Transform marker)
        {
            Vector3 p = marker.localPosition;
            return NormalizeAngle(Mathf.Atan2(p.z, p.y) * Mathf.Rad2Deg);
        }

        private static readonly Regex DuplicateSuffixPattern = new Regex(@"\s*\(\d+\)$");

        private static string StripDuplicateSuffix(string rawName)
        {
            return DuplicateSuffixPattern.Replace(rawName, string.Empty);
        }

        private float TargetAngleFor(SlotSymbol symbol)
        {
            float t = windowAlignmentAngle - symbol.LocalAngle;
            t %= 360f;
            if (t < 0f) t += 360f;
            return t;
        }

        private void Update()
        {
            if (!IsSpinning)
            {
                return;
            }

            elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = AccelerateCruiseDecelerate(progress, rampFraction, decelFraction);
            float target = totalDeltaDegrees * eased;
            float delta = target - appliedDegrees;
            appliedDegrees = target;

            ReelPivot.Rotate(Vector3.right, delta, Space.Self);

            if (progress >= 1f)
            {
                IsSpinning = false;
            }
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle < 0f) angle += 360f;
            return angle;
        }

        /// <summary>Cumulative area under a smoothstep ramp from 0 to x, x in [0,1].</summary>
        private static float SmoothstepIntegral(float x)
        {
            return (x * x * x) - (0.5f * x * x * x * x);
        }

        /// <summary>
        /// Normalised 0..1 distance fraction for a motion that eases from a standstill up to
        /// cruise speed, holds cruise speed, then eases back down to a standstill exactly at
        /// progress=1 - regardless of the ramp/decel split, this always returns exactly 1 at
        /// progress=1, so the caller's total rotation target is always reached precisely.
        /// </summary>
        private static float AccelerateCruiseDecelerate(float progress, float rampT, float decelT)
        {
            float cruiseT = Mathf.Max(0f, 1f - rampT - decelT);
            float totalArea = 1f - (0.5f * rampT) - (0.5f * decelT);
            if (totalArea <= 0.0001f)
            {
                return Mathf.Clamp01(progress);
            }

            float u = Mathf.Clamp01(progress);
            float areaSoFar;

            if (rampT > 0f && u <= rampT)
            {
                float x = Mathf.Clamp01(u / rampT);
                areaSoFar = rampT * SmoothstepIntegral(x);
            }
            else if (u <= rampT + cruiseT)
            {
                areaSoFar = (rampT * 0.5f) + (u - rampT);
            }
            else
            {
                float rampArea = rampT * 0.5f;
                float cruiseArea = cruiseT;
                float decelArea = 0f;
                if (decelT > 0f)
                {
                    float x = Mathf.Clamp01((u - rampT - cruiseT) / decelT);
                    decelArea = decelT * (x - SmoothstepIntegral(x));
                }
                areaSoFar = rampArea + cruiseArea + decelArea;
            }

            return areaSoFar / totalArea;
        }
    }
}
