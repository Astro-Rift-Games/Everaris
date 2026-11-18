using UnityEngine;

/// <summary>Stores only a non-secret username, never a password, token or session.</summary>
public sealed class PlayerPrefsRememberedUsernameStore : IRememberedUsernameStore
{
    private const string Key = "Grimhold.Auth.RememberedUsername";
    public string Load() => PlayerPrefs.GetString(Key, "");
    public void Save(string username)
    {
        PlayerPrefs.SetString(Key, (username ?? "").Trim());
        PlayerPrefs.Save();
    }
    public void Clear()
    {
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }
}
