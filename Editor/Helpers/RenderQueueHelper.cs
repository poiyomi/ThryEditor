using UnityEngine;

namespace Thry.ThryEditor.Helpers
{
    public static class RenderQueueHelper
    {
        // Names a queue matching no preset by its offset from the nearest one, as Unity's built in
        // RenderQueueField does. Nearest, not nearest below: 3999 reads better as "Overlay -1" than
        // "Transparent +999". Negative presets are the From Shader sentinel, not a band.
        public static string GetDisplayName(int queue, string[] presetNames, int[] presetValues)
        {
            int band = -1;
            for (int i = 0; i < presetValues.Length; i++)
            {
                if (presetValues[i] < 0) continue;
                // Strict <, so a midpoint keeps the lower band and reads as a positive offset up.
                if (band < 0 || Mathf.Abs(queue - presetValues[i]) < Mathf.Abs(queue - presetValues[band]))
                    band = i;
            }

            if (band < 0) return queue.ToString();

            int offset = queue - presetValues[band];
            if (offset == 0) return presetNames[band];
            return presetNames[band] + (offset > 0 ? " +" : " ") + offset;
        }
    }
}
