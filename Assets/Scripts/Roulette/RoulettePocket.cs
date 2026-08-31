using UnityEngine;

namespace RoyalCasino.Roulette
{
    /// <summary>
    /// Marks a single numbered pocket on the wheel. The number is read from the
    /// GameObject name, so fixing a mislabelled pocket is just a rename in the
    /// hierarchy. Tick <see cref="overrideNumber"/> when the name has to stay as-is.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoulettePocket : MonoBehaviour
    {
        [Tooltip("Ignore the GameObject name and use Number Override instead.")]
        [SerializeField] private bool overrideNumber = false;

        [Tooltip("Pocket value used when Override Number is ticked.")]
        [Range(0, 36)]
        [SerializeField] private int numberOverride = 0;

        /// <summary>Pocket value 0-36, or -1 when the name could not be read.</summary>
        public int Number { get; private set; } = -1;

        /// <summary>False when the GameObject name is not a number in the 0-36 range.</summary>
        public bool HasValidNumber { get; private set; }

        private void Awake()
        {
            ResolveNumber();
        }

        /// <summary>Re-reads the pocket value. Safe to call from editor tooling.</summary>
        public void ResolveNumber()
        {
            if (overrideNumber)
            {
                Number = numberOverride;
                HasValidNumber = true;
                return;
            }

            if (int.TryParse(name.Trim(), out int parsed) && parsed >= 0 && parsed <= 36)
            {
                Number = parsed;
                HasValidNumber = true;
            }
            else
            {
                Number = -1;
                HasValidNumber = false;
            }
        }
    }
}
