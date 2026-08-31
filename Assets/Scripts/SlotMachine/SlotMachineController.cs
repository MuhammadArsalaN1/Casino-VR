using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RoyalCasino.SlotMachine
{
    /// <summary>Points awarded when a symbol lands on the payline or completes a match.</summary>
    [System.Serializable]
    public class SlotSymbolPayout
    {
        public string SymbolName;
        public int Points = 10;
    }

    /// <summary>
    /// Round flow for one slot machine cabinet: player presses the physical button, the three
    /// reels spin down in a staggered cascade, and the symbols they land on decide the payout.
    /// Fully self-contained per cabinet - two machines in the scene each get their own instance.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotMachineController : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private SlotReel reel1;
        [SerializeField] private SlotReel reel2;
        [SerializeField] private SlotReel reel3;

        [Tooltip("Physical BNG button. Subscribed automatically - no inspector event wiring needed.")]
        [SerializeField] private BNG.Button spinButton;

        [Header("Round")]
        [Tooltip("Delay before starting each following reel, giving the classic cascading stop. " +
                 "Each reel's own spin length is set on its SlotReel component.")]
        [SerializeField] private float reelStartStagger = 0.3f;

        [Tooltip("Ignore button presses for this long after load - the spring-jointed BNG button " +
                 "dips through its click band while physics settles, which otherwise fires a phantom press.")]
        [SerializeField] private float startupIgnoreSeconds = 1f;

        [Header("Scoring")]
        [Tooltip("Payout when a symbol lands on the payline as part of a 2-of-3 or 3-of-3 match.")]
        [SerializeField]
        private List<SlotSymbolPayout> payouts = new List<SlotSymbolPayout>
        {
            new SlotSymbolPayout { SymbolName = "StrawBerry", Points = 10 },
            new SlotSymbolPayout { SymbolName = "Apple", Points = 20 },
            new SlotSymbolPayout { SymbolName = "Spade", Points = 30 },
            new SlotSymbolPayout { SymbolName = "Heart", Points = 50 },
            new SlotSymbolPayout { SymbolName = "Seven", Points = 100 },
        };

        [Tooltip("Multiplies the matched symbol's payout for a 2-of-3 match.")]
        [SerializeField] private int twoMatchMultiplier = 2;

        [Tooltip("Multiplies the matched symbol's payout for a 3-of-3 jackpot.")]
        [SerializeField] private int threeMatchMultiplier = 10;

        [Header("UI")]
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI resultText;
        [SerializeField] private TextMeshProUGUI lastPointsText;
        [SerializeField] private TextMeshProUGUI totalScoreText;

        [Header("Audio - wire these up in the inspector")]
        [Tooltip("Plays the spin and stinger clips. Left empty means silent, no errors.")]
        [SerializeField] private AudioSource reelAudioSource;

        [Tooltip("Plays once, starting the moment the button is pressed. Sized to run for roughly " +
                 "the whole spin rather than looped.")]
        [SerializeField] private AudioClip reelSpinClip;

        [Tooltip("Plays once when a reel stops.")]
        [SerializeField] private AudioClip reelStopClip;

        [SerializeField] private AudioClip winClip;
        [SerializeField] private AudioClip jackpotClip;

        /// <summary>Running total across the session.</summary>
        public int TotalScore { get; private set; }

        /// <summary>True while a spin is in progress.</summary>
        public bool RoundInProgress { get; private set; }

        private SlotReel[] reels;
        private float armedAtTime;

        private void Awake()
        {
            reels = new[] { reel1, reel2, reel3 };
        }

        private void Start()
        {
            armedAtTime = Time.time + Mathf.Max(0f, startupIgnoreSeconds);
            RefreshUI("Press the button to spin", "- - -", "-");
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
            if (RoundInProgress || reel1 == null || reel2 == null || reel3 == null)
            {
                return;
            }

            if (Time.time < armedAtTime)
            {
                return;
            }

            if (reel1.Symbols.Count == 0)
            {
                Debug.LogError("[SlotMachine] Reels have no symbols cached - cannot spin.", this);
                return;
            }

            StartCoroutine(RunRound());
        }

        /// <summary>Clears the running total.</summary>
        public void ResetScore()
        {
            TotalScore = 0;
            RefreshUI("Press the button to spin", "- - -", "-");
        }

        private IEnumerator RunRound()
        {
            RoundInProgress = true;
            RefreshUI("Spinning...", "- - -", "-");
            StartReelAudio();

            string[] targets = new string[reels.Length];
            for (int i = 0; i < reels.Length; i++)
            {
                targets[i] = RandomSymbolName();
            }

            for (int i = 0; i < reels.Length; i++)
            {
                reels[i].SpinTo(targets[i]);
                if (i < reels.Length - 1 && reelStartStagger > 0f)
                {
                    yield return new WaitForSeconds(reelStartStagger);
                }
            }

            bool[] stopped = new bool[reels.Length];
            while (System.Array.Exists(stopped, s => !s))
            {
                for (int i = 0; i < reels.Length; i++)
                {
                    if (!stopped[i] && !reels[i].IsSpinning)
                    {
                        stopped[i] = true;
                        PlayClip(reelStopClip, 1f);
                    }
                }
                yield return null;
            }

            StopReelAudio();

            string[] results = new string[reels.Length];
            for (int i = 0; i < reels.Length; i++)
            {
                results[i] = reels[i].ResolveCurrentSymbol();
            }

            int points = ScoreFor(results);
            TotalScore += points;

            string resultLine = string.Join(" | ", results);
            string status = points > 0 ? (IsJackpot(results) ? "JACKPOT!" : "You win!") : "Press the button to spin";
            RefreshUI(status, resultLine, points > 0 ? "+" + points : "-");

            if (points > 0)
            {
                PlayClip(IsJackpot(results) ? jackpotClip : winClip, 1f);
            }

            RoundInProgress = false;
        }

        private string RandomSymbolName()
        {
            var symbols = reel1.Symbols;
            return symbols[Random.Range(0, symbols.Count)].CanonicalName;
        }

        private bool IsJackpot(string[] results)
        {
            return results[0] == results[1] && results[1] == results[2];
        }

        private int ScoreFor(string[] results)
        {
            if (IsJackpot(results))
            {
                return PayoutFor(results[0]) * threeMatchMultiplier;
            }

            if (results[0] == results[1]) return PayoutFor(results[0]) * twoMatchMultiplier;
            if (results[1] == results[2]) return PayoutFor(results[1]) * twoMatchMultiplier;
            if (results[0] == results[2]) return PayoutFor(results[0]) * twoMatchMultiplier;

            return 0;
        }

        private int PayoutFor(string symbolName)
        {
            foreach (SlotSymbolPayout p in payouts)
            {
                if (p.SymbolName == symbolName) return p.Points;
            }

            Debug.LogWarning("[SlotMachine] No payout configured for symbol '" + symbolName + "' - defaulting to 10.", this);
            return 10;
        }

        private void StartReelAudio()
        {
            if (reelAudioSource == null || reelSpinClip == null)
            {
                return;
            }

            reelAudioSource.clip = reelSpinClip;
            reelAudioSource.loop = false;
            reelAudioSource.Play();
        }

        private void StopReelAudio()
        {
            if (reelAudioSource != null && reelAudioSource.isPlaying)
            {
                reelAudioSource.Stop();
            }
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (reelAudioSource != null && clip != null)
            {
                reelAudioSource.PlayOneShot(clip, volume);
            }
        }

        private void RefreshUI(string status, string result, string points)
        {
            if (statusText != null) statusText.text = status;
            if (resultText != null) resultText.text = result;
            if (lastPointsText != null) lastPointsText.text = points;
            if (totalScoreText != null) totalScoreText.text = TotalScore.ToString();
        }
    }
}
