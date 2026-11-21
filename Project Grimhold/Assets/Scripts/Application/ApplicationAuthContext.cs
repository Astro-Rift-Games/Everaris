using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Grimhold.Backend;

public class ApplicationAuthContext : MonoBehaviour
{
    public static ApplicationAuthContext Instance { get; private set; }

    public bool IsAuthenticated { get; private set; }
    public string Token { get; private set; }
    /// <summary>Single-use refresh credential; memory only, cleared wherever <see cref="Token"/> is.</summary>
    public string RefreshToken { get; private set; }
    /// <summary>True while one refresh exchange is running; requests wait for it instead of using the expired token.</summary>
    public bool IsRefreshInFlight => _refreshInFlight != null && !_refreshInFlight.IsCompleted;
    public string CharacterId { get; private set; }
    public string CharacterName { get; private set; }
    public CharacterProfileValues Profile { get; private set; }
    public TownSessionTicket ActiveTownTicket { get; private set; }
    public BackendConfiguration BackendConfig { get; private set; }
    public ITownSessionTicketVerifier TownTicketVerifier { get; private set; }
    public IRaidAdmissionVerifier RaidAdmissionVerifier { get; private set; }

    public event Action SessionInvalidated;

    // Access tokens this session has already replaced, so a holder of a stale copy is recognised as ours.
    private const int MaxRetiredTokens = 8;
    private readonly List<string> _retiredTokens = new List<string>();
    private Task<TokenRefreshResult> _refreshInFlight;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }
        else
        {
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Initialize(
        string token,
        CharacterData characterData,
        CharacterProfileData profileData,
        BackendConfiguration backendConfig = null,
        string refreshToken = null)
    {
        Token = token;
        RefreshToken = refreshToken;
        _retiredTokens.Clear();
        CharacterId = characterData.characterId;
        CharacterName = characterData.name;
        Profile = profileData.profile;
        ActiveTownTicket = default;
        BackendConfig = backendConfig;
        if (backendConfig != null && !string.IsNullOrEmpty(token))
        {
            // Resolved per call so a renewed access token reaches the verifiers.
            TownTicketVerifier = new BackendTownSessionTicketVerifier(backendConfig, () => Token);
            RaidAdmissionVerifier = new BackendRaidAdmissionVerifier(backendConfig, () => Token);
        }
        else
        {
            TownTicketVerifier = null;
            RaidAdmissionVerifier = null;
        }
        IsAuthenticated = true;
    }

    public void SetVerifiers(ITownSessionTicketVerifier townTicketVerifier, IRaidAdmissionVerifier raidAdmissionVerifier)
    {
        TownTicketVerifier = townTicketVerifier;
        RaidAdmissionVerifier = raidAdmissionVerifier;
    }

    public void SetTownSessionTicket(in TownSessionTicket ticket)
    {
        ActiveTownTicket = ticket;
        if (ticket.IsValid && TownTicketVerifier != null)
        {
            _ = TownTicketVerifier.VerifyTownTicketAsync(ticket.RawTicket);
        }
    }

    public void UpdateProfile(CharacterProfileValues updatedProfile)
    {
        Profile = updatedProfile;
    }

    /// <summary>
    /// Maps an access token sent by a client to the one currently valid for this session. False when the
    /// token never belonged to the session (for example a login still in progress or a previous session).
    /// </summary>
    public bool TryResolveCurrentToken(string sentToken, out string currentToken)
    {
        currentToken = Token;
        if (string.IsNullOrEmpty(sentToken) || string.IsNullOrEmpty(Token)) return false;
        return sentToken == Token || _retiredTokens.Contains(sentToken);
    }

    /// <summary>Completes when the refresh in flight (if any) has finished; never starts one.</summary>
    public Task WaitForRefreshAsync() => IsRefreshInFlight ? _refreshInFlight : Task.CompletedTask;

    /// <summary>
    /// Exchanges the refresh token for new credentials. Single-flight: concurrent callers share one backend
    /// call, because the backend treats a second use of the same refresh token as reuse and revokes the whole
    /// session. The exchange is never retried here.
    /// </summary>
    public Task<TokenRefreshResult> RefreshAccessTokenAsync(BackendConfiguration config)
    {
        if (IsRefreshInFlight) return _refreshInFlight;
        _refreshInFlight = RefreshCoreAsync(config);
        return _refreshInFlight;
    }

    private async Task<TokenRefreshResult> RefreshCoreAsync(BackendConfiguration config)
    {
        string expiredToken = Token;
        string spentRefreshToken = RefreshToken;
        if (config == null || !IsAuthenticated || string.IsNullOrEmpty(spentRefreshToken))
        {
            return TokenRefreshResult.SessionEnded(new BackendError { error = BackendErrorUtility.Unauthorized, message = "No refresh token." });
        }

        var (success, result, error) = await AuthenticationClient.PostRefreshAsync(config, spentRefreshToken);

        // The session may have been closed or replaced while the exchange ran: never resurrect it.
        if (this == null || !IsAuthenticated || Token != expiredToken || RefreshToken != spentRefreshToken)
        {
            return TokenRefreshResult.Failed(new BackendError { error = "SESSION_CHANGED", message = "The session changed while renewing credentials." });
        }

        if (!success)
        {
            // Only a proven-invalid credential ends the session; transient failures keep every token.
            bool endsSession = error.error == BackendErrorUtility.RefreshInvalid || error.error == BackendErrorUtility.Unauthorized;
            return endsSession ? TokenRefreshResult.SessionEnded(error) : TokenRefreshResult.Failed(error);
        }

        if (string.IsNullOrEmpty(result.token) || string.IsNullOrEmpty(result.refreshToken))
        {
            return TokenRefreshResult.Failed(new BackendError { error = "INVALID_REFRESH_RESPONSE", message = "Refresh response without credentials." });
        }

        _retiredTokens.Add(expiredToken);
        if (_retiredTokens.Count > MaxRetiredTokens) _retiredTokens.RemoveAt(0);
        Token = result.token;
        RefreshToken = result.refreshToken;
        return TokenRefreshResult.Succeeded();
    }

    public void HandleUnauthorizedSession()
    {
        if (!IsAuthenticated && string.IsNullOrEmpty(Token))
        {
            return;
        }

        Debug.LogWarning($"[{nameof(ApplicationAuthContext)}] Session invalidated due to 401 UNAUTHORIZED. Clearing local auth and stash state.");
        Clear();
        LocalProfileProvider.ClearRemoteCharacterId();
        ApplicationStashServiceBootstrapper.ResetForLogout();
        SessionInvalidated?.Invoke();
    }

    public void Clear()
    {
        Token = null;
        RefreshToken = null;
        _retiredTokens.Clear();
        CharacterId = null;
        CharacterName = null;
        Profile = default;
        ActiveTownTicket = default;
        BackendConfig = null;
        TownTicketVerifier = null;
        RaidAdmissionVerifier = null;
        IsAuthenticated = false;
    }
}
