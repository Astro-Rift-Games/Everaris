/// <summary>Non-secret username storage only. Never accepts credentials or session data.</summary>
public interface IRememberedUsernameStore
{
    string Load();
    void Save(string username);
    void Clear();
}
