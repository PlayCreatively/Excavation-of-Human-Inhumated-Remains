using UnityEngine;

namespace Excavation.Tools
{
    /// <summary>
    /// Defines a digging brush/tool preset with its characteristics.
    /// </summary>
    [CreateAssetMenu(fileName = "New Dig Brush", menuName = "Excavation/Dig Brush Preset")]
    public class DigBrushPreset : ScriptableObject
    {
        [Header("Brush Properties")]
        [Tooltip("Radius of the brush in meters")]
        [Range(0.01f, 0.5f)]
        public float radius = 0.05f; // 5cm default

        [Tooltip("How fast this tool digs (units per second)")]
        [Range(0.1f, 10f)]
        public float digSpeed = 1f;

        [Tooltip("Falloff curve for brush (controls softness at edges)")]
        public AnimationCurve falloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Header("Audio Feedback")]
        [Tooltip("Scraping/digging sounds for this tool")]
        public AudioClip[] scrapeSounds;

        /// <summary>
        /// Get a random scraping sound for variety.
        /// </summary>
        public AudioClip GetRandomScrapeSound()
        {
            if (scrapeSounds == null || scrapeSounds.Length == 0)
                return null;

            return scrapeSounds[Random.Range(0, scrapeSounds.Length)];
        }

        /// <summary>
        /// Evaluate the brush intensity at a given distance from center.
        /// </summary>
        /// <param name="distanceFromCenter">Distance from brush center in meters</param>
        /// <returns>Intensity value from 0 to 1</returns>
        public float EvaluateFalloff(float distanceFromCenter)
        {
            if (distanceFromCenter >= radius)
                return 0f;

            float normalizedDistance = distanceFromCenter / radius;
            return falloffCurve.Evaluate(normalizedDistance);
        }
    }
}
