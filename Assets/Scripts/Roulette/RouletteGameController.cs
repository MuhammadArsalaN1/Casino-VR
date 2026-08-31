using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RoyalCasino.Roulette
{
    /// <summary>
    /// Round flow for the roulette table: the player presses the physical button,
    /// the wheel spins down over 20-25 seconds while the ball orbits, and the pocket
    /// it rests in awards points.
    /// </summary>
    [DisallowMultipleComponent]
    public class RouletteGameController : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private RouletteWheel wheel;
        [SerializeField] private RouletteBall ball;

        [Tooltip("Physical BNG button. Subscribed automatically - no inspector event wiring needed.")]
        [SerializeField] private BNG.Button spinButton;

        [Header("Round")]
        [Tooltip("Seconds the wheel takes to coast to a stop. Picked at random per round.")]
        [SerializeField] private float minSpinDuration = 20f;

        [SerializeField] private float maxSpinDuration = 25f;

        [Tooltip("Ignore button presses for this long after load. The spring-jointed BNG button " +
                 "dips through its click band while physics settles, which otherwise fires a " +
                 "phantom press and auto-spins the wheel on scene start.")]
        [SerializeField] private float startupIgnoreSeconds = 1f;

        [Header("Scoring")]
        [Tooltip("Awarded when the ball lands in the green zero pocket.")]
        [SerializeField] private int zeroPocketPoints = 100;

        [Tooltip("Points are the pocket number multiplied by this.")]
        [SerializeField] private int pointsPerNumber = 10;

        [Header("UI")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI lastNumberText;
        [SerializeField] private TextMeshProUGUI lastPointsText;
        [SerializeField] private TextMeshProUGUI totalScoreText;

        [Header("Audio - wire these up in the inspector")]
        [Tooltip("Looping spin bed. Left empty means silent, no errors.")]
        [SerializeField] private AudioSource wheelAudioSource;

        [SerializeField] private AudioClip wheelSpinClip;

        [Tooltip("Drops the spin clip's pitch as the wheel slows down.")]
        [SerializeField] private bool matchPitchToWheelSpeed = true;

        [Range(0.1f, 1f)]
        [SerializeField] private float minSpinPitch = 0.4f;

        [Header("Diagnostics")]
        [Tooltip("Logs duplicate or unreadable pocket names on start.")]
        [SerializeField] private bool validatePocketsOnStart = true;

        /// <summary>Running total across the session.</summary>
        public int TotalScore { get; private set; }

        /// <summary>True while a spin is in progress.</summary>
        public bool RoundInProgress { get; private set; }

        private readonly List<RoulettePocket> playablePockets = new List<RoulettePocket>();
        private float peakWheelSpeed = 1f;
        private float armedAtTime;

        private void Start()
        {
            armedAtTime = Time.time + Mathf.Max(0f, startupIgnoreSeconds);

            if (wheel != null)
            {
                wheel.CachePockets();
                RebuildPlayablePockets();

                if (validatePocketsOnStart)
                {
                    ValidatePockets();
                }
            }

            if (ball != null)
            {
                ball.ResetToStart();
            }

            RefreshUI("Press the button to spin", "-", "-");
        }

        private void OnEnable()
        {
            if (spinButton != null)
            {
                spinButton.onButtonDown.AddListener(Spin);
            }
        }

        private void OnDisable()
        {
            if (spinButton != null)
            {
                spinButton.onButtonDown.RemoveListener(Spin);
            }
        }

        /// <summary>Starts a round. Safe to call from a UnityEvent; ignored mid-spin.</summary>
        public void Spin()
        {
            if (RoundInProgress || wheel == null || ball == null)
            {
                return;
            }

            if (Time.time < armedAtTime)
            {
                return;
            }

            if (playablePockets.Count == 0)
            {
                Debug.LogError("[Roulette] No pockets with a readable 0-36 name - cannot spin.", this);
                return;
            }

            StartCoroutine(RunRound());
        }

        /// <summary>Clears the running total.</summary>
        public void ResetScore()
        {
            TotalScore = 0;
            RefreshUI("Press the button to spin", "-", "-");
        }

        private IEnumerator RunRound()
        {
            RoundInProgress = true;

            float duration = Random.Range(minSpinDuration, maxSpinDuration);
            RoulettePocket target = playablePockets[Random.Range(0, playablePockets.Count)];

            wheel.StartSpin(duration);
            ball.BeginRound(wheel, target, duration);

            StartWheelAudio();
            RefreshUI("Spinning...", "-", "-");

            while (wheel.IsSpinning || ball.IsBusy)
            {
                UpdateWheelAudio();
                yield return null;
            }

            StopWheelAudio();

            RoulettePocket resting = ball.ResolveRestingPocket();
            if (resting == null)
            {
                Debug.LogWarning("[Roulette] Could not resolve a resting pocket for this spin.", this);
                RefreshUI("No result - try again", "-", "-");
                RoundInProgress = false;
                yield break;
            }

            int points = ScoreFor(resting.Number);
            TotalScore += points;

            RefreshUI("Press the button to spin", resting.Number.ToString(), "+" + points);

            RoundInProgress = false;
        }

        private int ScoreFor(int pocketNumber)
        {
            return pocketNumber == 0 ? zeroPocketPoints : pocketNumber * pointsPerNumber;
        }

        private void RebuildPlayablePockets()
        {
            playablePockets.Clear();

            foreach (RoulettePocket pocket in wheel.Pockets)
            {
                if (pocket != null && pocket.HasValidNumber)
                {
                    playablePockets.Add(pocket);
                }
            }
        }

        private void ValidatePockets()
        {
            var seen = new Dictionary<int, int>();
            var unreadable = new List<string>();

            foreach (RoulettePocket pocket in wheel.Pockets)
            {
                if (pocket == null)
                {
                    continue;
                }

                if (!pocket.HasValidNumber)
                {
                    unreadable.Add(pocket.name);
                    continue;
                }

                seen.TryGetValue(pocket.Number, out int count);
                seen[pocket.Number] = count + 1;
            }

            var duplicates = new List<string>();
            foreach (var entry in seen)
            {
                if (entry.Value > 1)
                {
                    duplicates.Add(entry.Key + " x" + entry.Value);
                }
            }

            var missing = new List<string>();
            for (int number = 0; number <= 36; number++)
            {
                if (!seen.ContainsKey(number))
                {
                    missing.Add(number.ToString());
                }
            }

            if (unreadable.Count > 0 || duplicates.Count > 0 || missing.Count > 0)
            {
                Debug.LogWarning(
                    "[Roulette] Pocket naming needs a look -" +
                    " unreadable: [" + string.Join(", ", unreadable) + "]" +
                    " duplicated: [" + string.Join(", ", duplicates) + "]" +
                    " missing: [" + string.Join(", ", missing) + "]." +
                    " Rename the collider objects under the wheel, or tick Override Number on the pocket.",
                    this);
            }
        }

        private void StartWheelAudio()
        {
            peakWheelSpeed = 1f;

            if (wheelAudioSource == null || wheelSpinClip == null)
            {
                return;
            }

            wheelAudioSource.clip = wheelSpinClip;
            wheelAudioSource.loop = true;
            wheelAudioSource.pitch = 1f;
            wheelAudioSource.Play();
        }

        private void UpdateWheelAudio()
        {
            if (wheelAudioSource == null || !wheelAudioSource.isPlaying || !matchPitchToWheelSpeed)
            {
                return;
            }

            float speed = Mathf.Abs(wheel.CurrentSpeedDegreesPerSecond);
            peakWheelSpeed = Mathf.Max(peakWheelSpeed, speed);

            float normalised = Mathf.Clamp01(speed / peakWheelSpeed);
            wheelAudioSource.pitch = Mathf.Lerp(minSpinPitch, 1f, normalised);
        }

        private void StopWheelAudio()
        {
            if (wheelAudioSource != null && wheelAudioSource.isPlaying)
            {
                wheelAudioSource.Stop();
                wheelAudioSource.pitch = 1f;
            }
        }

        private void RefreshUI(string status, string number, string points)
        {
            if (statusText != null)
            {
                statusText.text = status;
            }

            if (lastNumberText != null)
            {
                lastNumberText.text = number;
            }

            if (lastPointsText != null)
            {
                lastPointsText.text = points;
            }

            if (totalScoreText != null)
            {
                totalScoreText.text = TotalScore.ToString();
            }
        }
    }
}
