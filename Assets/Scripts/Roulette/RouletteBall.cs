using System.Collections.Generic;
using UnityEngine;

namespace RoyalCasino.Roulette
{
    /// <summary>
    /// Drives the single reused ball. It is released from its authored start point,
    /// orbits the rim against the wheel, then spirals down into the winning pocket
    /// and rides with it. Motion runs in LateUpdate so the wheel has already turned
    /// for the frame and pocket transforms are final.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class RouletteBall : MonoBehaviour
    {
        [Header("Path shape")]
        [Tooltip("Portion of the round spent spiralling into the winning pocket.")]
        [Range(0.05f, 0.6f)]
        [SerializeField] private float settleFraction = 0.3f;

        [Tooltip("Revolutions the ball travels around the rim. Randomised per round.")]
        [SerializeField] private Vector2 revolutionRange = new Vector2(14f, 22f);

        [Tooltip("Ball runs against the wheel, like a real table.")]
        [SerializeField] private bool orbitOppositeToWheel = true;

        [Tooltip("Scales the orbit radius taken from the ball's authored start position.")]
        [SerializeField] private float rimRadiusMultiplier = 1f;

        [Header("Audio - wire these up in the inspector")]
        [Tooltip("Plays the tap and landing clips. Left empty means silent, no errors.")]
        [SerializeField] private AudioSource ballAudioSource;

        [Tooltip("Clicks as the ball crosses each pocket fret.")]
        [SerializeField] private AudioClip ballTapClip;

        [Tooltip("One-shot when the ball finally settles.")]
        [SerializeField] private AudioClip ballLandedClip;

        [Range(0f, 1f)]
        [SerializeField] private float tapVolume = 0.6f;

        [Tooltip("Stops the tap clip machine-gunning at high orbit speed.")]
        [SerializeField] private float minSecondsBetweenTaps = 0.04f;

        /// <summary>True from release until the ball has settled.</summary>
        public bool IsBusy { get; private set; }

        /// <summary>Pocket the ball was aimed at this round.</summary>
        public RoulettePocket TargetPocket { get; private set; }

        private readonly HashSet<RoulettePocket> occupiedPockets = new HashSet<RoulettePocket>();

        private Rigidbody body;
        private Vector3 startPosition;
        private Quaternion startRotation;

        private RouletteWheel wheel;
        private Vector3 center;
        private Vector3 up;
        private Vector3 refRight;
        private Vector3 refForward;

        private float rimRadius;
        private float rimHeight;
        private float startAngle;
        private float totalDegrees;

        private float elapsed;
        private float duration;
        private bool attached;
        private float lastTapTime;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        /// <summary>Returns the ball to its authored drop point and clears round state.</summary>
        public void ResetToStart()
        {
            IsBusy = false;
            attached = false;
            elapsed = 0f;
            occupiedPockets.Clear();

            transform.SetPositionAndRotation(startPosition, startRotation);
        }

        /// <summary>Releases the ball for a round that lasts <paramref name="roundDuration"/> seconds.</summary>
        public void BeginRound(RouletteWheel spinningWheel, RoulettePocket target, float roundDuration)
        {
            ResetToStart();

            wheel = spinningWheel;
            TargetPocket = target;
            duration = Mathf.Max(0.01f, roundDuration);

            center = wheel.WheelCenter;
            up = wheel.WheelUp;

            refRight = Vector3.ProjectOnPlane(Vector3.right, up);
            if (refRight.sqrMagnitude < 0.0001f)
            {
                refRight = Vector3.ProjectOnPlane(Vector3.forward, up);
            }

            refRight.Normalize();
            refForward = Vector3.Cross(up, refRight);

            ToPolar(startPosition, out rimRadius, out startAngle, out rimHeight);
            rimRadius *= rimRadiusMultiplier;

            float direction = orbitOppositeToWheel ? -wheel.SpinSign : wheel.SpinSign;
            totalDegrees = Random.Range(revolutionRange.x, revolutionRange.y) * 360f * direction;

            IsBusy = true;
        }

        private void LateUpdate()
        {
            if (attached)
            {
                if (TargetPocket != null)
                {
                    transform.position = TargetPocket.transform.position;
                }

                return;
            }

            if (!IsBusy)
            {
                return;
            }

            elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsed / duration);
            float angle = startAngle + (totalDegrees * RouletteWheel.EaseOutCubic(progress));
            float radius = rimRadius;
            float height = rimHeight;

            float settleStart = 1f - settleFraction;
            if (progress > settleStart && TargetPocket != null)
            {
                float blend = Mathf.SmoothStep(0f, 1f, (progress - settleStart) / settleFraction);

                ToPolar(TargetPocket.transform.position, out float pocketRadius, out float pocketAngle, out float pocketHeight);

                angle = Mathf.LerpAngle(angle, pocketAngle, blend);
                radius = Mathf.Lerp(rimRadius, pocketRadius, blend);
                height = Mathf.Lerp(rimHeight, pocketHeight, blend);
            }

            transform.position = FromPolar(radius, angle, height);

            if (progress >= 1f)
            {
                Land();
            }
        }

        private void Land()
        {
            IsBusy = false;
            attached = true;

            if (TargetPocket != null)
            {
                transform.position = TargetPocket.transform.position;
            }

            PlayClip(ballLandedClip, 1f);
        }

        /// <summary>
        /// Where the ball actually came to rest. Prefers the trigger the ball is sitting
        /// inside, falling back to the nearest pocket so a missed trigger never loses a score.
        /// </summary>
        public RoulettePocket ResolveRestingPocket()
        {
            RoulettePocket best = null;
            float bestDistance = float.MaxValue;

            foreach (RoulettePocket pocket in occupiedPockets)
            {
                if (pocket == null || !pocket.HasValidNumber)
                {
                    continue;
                }

                float distance = (pocket.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pocket;
                }
            }

            if (best != null)
            {
                return best;
            }

            if (wheel == null)
            {
                return TargetPocket;
            }

            foreach (RoulettePocket pocket in wheel.Pockets)
            {
                if (pocket == null || !pocket.HasValidNumber)
                {
                    continue;
                }

                float distance = (pocket.transform.position - transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pocket;
                }
            }

            return best != null ? best : TargetPocket;
        }

        private void OnTriggerEnter(Collider other)
        {
            RoulettePocket pocket = other.GetComponent<RoulettePocket>();
            if (pocket == null)
            {
                return;
            }

            occupiedPockets.Add(pocket);

            if (Time.time - lastTapTime >= minSecondsBetweenTaps)
            {
                lastTapTime = Time.time;
                PlayClip(ballTapClip, tapVolume);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            RoulettePocket pocket = other.GetComponent<RoulettePocket>();
            if (pocket != null)
            {
                occupiedPockets.Remove(pocket);
            }
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (ballAudioSource != null && clip != null)
            {
                ballAudioSource.PlayOneShot(clip, volume);
            }
        }

        private void ToPolar(Vector3 worldPoint, out float radius, out float angleDegrees, out float height)
        {
            Vector3 offset = worldPoint - center;
            height = Vector3.Dot(offset, up);

            Vector3 planar = offset - (up * height);
            radius = planar.magnitude;
            angleDegrees = Mathf.Atan2(Vector3.Dot(planar, refForward), Vector3.Dot(planar, refRight)) * Mathf.Rad2Deg;
        }

        private Vector3 FromPolar(float radius, float angleDegrees, float height)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return center
                + (refRight * (Mathf.Cos(radians) * radius))
                + (refForward * (Mathf.Sin(radians) * radius))
                + (up * height);
        }
    }
}
