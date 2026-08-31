using System.Collections.Generic;
using UnityEngine;

namespace RoyalCasino.Roulette
{
    /// <summary>
    /// Spins the wheel plate around its local up axis with an ease-out curve so it
    /// coasts to a natural stop, and owns the list of pockets riding with it.
    /// </summary>
    [DisallowMultipleComponent]
    public class RouletteWheel : MonoBehaviour
    {
        [Header("Rotation")]
        [Tooltip("Transform that actually rotates. Falls back to this GameObject.")]
        [SerializeField] private Transform spinPlate;

        [Tooltip("Axis the plate turns about, in the plate's local space.")]
        [SerializeField] private Vector3 localSpinAxis = Vector3.up;

        [Tooltip("Full revolutions completed over one spin. Randomised per round.")]
        [SerializeField] private Vector2 revolutionRange = new Vector2(8f, 14f);

        [Tooltip("Direction the plate turns.")]
        [SerializeField] private bool clockwise = true;

        [Header("Pockets")]
        [Tooltip("Parent holding one child per numbered pocket. Defaults to the spin plate.")]
        [SerializeField] private Transform pocketRoot;

        /// <summary>True while the plate is still turning.</summary>
        public bool IsSpinning { get; private set; }

        /// <summary>Signed angular speed in degrees/second, useful for driving audio pitch.</summary>
        public float CurrentSpeedDegreesPerSecond { get; private set; }

        /// <summary>+1 clockwise, -1 counter-clockwise.</summary>
        public float SpinSign => clockwise ? 1f : -1f;

        public IReadOnlyList<RoulettePocket> Pockets => pockets;

        public Transform SpinPlate => spinPlate != null ? spinPlate : transform;

        /// <summary>World-space centre the pockets orbit.</summary>
        public Vector3 WheelCenter => SpinPlate.position;

        /// <summary>World-space up axis of the wheel plane.</summary>
        public Vector3 WheelUp => SpinPlate.TransformDirection(localSpinAxis).normalized;

        private readonly List<RoulettePocket> pockets = new List<RoulettePocket>();
        private float elapsed;
        private float spinDuration;
        private float totalDegrees;
        private float appliedDegrees;

        private void Awake()
        {
            if (spinPlate == null)
            {
                spinPlate = transform;
            }

            CachePockets();
        }

        /// <summary>Rebuilds the pocket list from the hierarchy.</summary>
        public void CachePockets()
        {
            pockets.Clear();

            Transform root = pocketRoot != null ? pocketRoot : SpinPlate;
            if (root == null)
            {
                return;
            }

            root.GetComponentsInChildren(true, pockets);
        }

        /// <summary>Starts a spin that decelerates to a stop after <paramref name="duration"/> seconds.</summary>
        public void StartSpin(float duration)
        {
            if (spinPlate == null)
            {
                spinPlate = transform;
            }

            spinDuration = Mathf.Max(0.01f, duration);
            elapsed = 0f;
            appliedDegrees = 0f;
            totalDegrees = Random.Range(revolutionRange.x, revolutionRange.y) * 360f * SpinSign;
            IsSpinning = true;
        }

        private void Update()
        {
            if (!IsSpinning)
            {
                return;
            }

            elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsed / spinDuration);
            float target = totalDegrees * EaseOutCubic(progress);
            float delta = target - appliedDegrees;
            appliedDegrees = target;

            SpinPlate.Rotate(localSpinAxis, delta, Space.Self);
            CurrentSpeedDegreesPerSecond = Time.deltaTime > 0f ? delta / Time.deltaTime : 0f;

            if (progress >= 1f)
            {
                IsSpinning = false;
                CurrentSpeedDegreesPerSecond = 0f;
            }
        }

        /// <summary>Decelerating curve: fast off the mark, gently easing to zero.</summary>
        public static float EaseOutCubic(float t)
        {
            float inverse = 1f - Mathf.Clamp01(t);
            return 1f - (inverse * inverse * inverse);
        }
    }
}
