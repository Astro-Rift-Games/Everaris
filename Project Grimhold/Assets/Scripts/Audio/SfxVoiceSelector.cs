using System;

/// <summary>
/// Política pura (sin Unity) para elegir la voz del pool de SFX: la primera libre; si todas suenan,
/// la que empezó antes (menor orden de inicio). Los empates se resuelven por el menor índice.
/// </summary>
public static class SfxVoiceSelector
{
    /// <summary>Devuelve el índice a usar, o -1 si no hay voces.</summary>
    public static int Select(ReadOnlySpan<bool> isPlaying, ReadOnlySpan<long> startOrder)
    {
        int count = Math.Min(isPlaying.Length, startOrder.Length);
        if (count == 0) return -1;

        int oldest = 0;
        for (int i = 0; i < count; i++)
        {
            if (!isPlaying[i]) return i;
            if (startOrder[i] < startOrder[oldest]) oldest = i;
        }
        return oldest;
    }
}
