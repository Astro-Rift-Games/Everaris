using System;
using UnityEngine;

/// <summary>
/// Isolates the player camera preferences for one test: starts from a clean (unset) state and
/// restores whatever the developer machine had stored once the test is over.
/// </summary>
internal sealed class GameplayCameraPreferencesTestScope : IDisposable
{
    private static readonly GameplayCameraPreferences.Setting[] Settings =
        (GameplayCameraPreferences.Setting[])Enum.GetValues(typeof(GameplayCameraPreferences.Setting));

    private readonly float?[] _saved = new float?[Settings.Length];

    public GameplayCameraPreferencesTestScope()
    {
        for (int i = 0; i < Settings.Length; i++)
        {
            string key = GameplayCameraPreferences.KeyOf(Settings[i]);
            _saved[i] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetFloat(key) : null;
            PlayerPrefs.DeleteKey(key);
        }

        GameplayCameraPreferences.InvalidateCacheForTests();
    }

    public void Dispose()
    {
        for (int i = 0; i < Settings.Length; i++)
        {
            string key = GameplayCameraPreferences.KeyOf(Settings[i]);
            if (_saved[i].HasValue)
            {
                PlayerPrefs.SetFloat(key, _saved[i].Value);
            }
            else
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        PlayerPrefs.Save();
        GameplayCameraPreferences.InvalidateCacheForTests();
    }
}
