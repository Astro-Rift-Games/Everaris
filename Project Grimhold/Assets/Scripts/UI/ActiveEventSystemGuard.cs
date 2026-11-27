using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Runtime guard ensuring strictly one active <see cref="EventSystem"/> exists across all scene
/// transitions (MainMenu, Town, Raid, Host Migration, and additive/DDOL overlays) (FIX-60).
/// Immediately disables duplicate EventSystems before <c>EventSystem.Update()</c> can warn or split
/// UI focus, preferring the active scene's <see cref="InputSystemUIInputModule"/> EventSystem and
/// clearing any stale UI selection from an unloaded scene.
/// </summary>
public static class ActiveEventSystemGuard
{
    private static bool _initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnforceSingleActiveEventSystem(SceneManager.GetActiveScene());
    }

    private static void OnActiveSceneChanged(Scene previous, Scene next)
    {
        EnforceSingleActiveEventSystem(next);
    }

    /// <summary>
    /// Scans all loaded EventSystems and disables any duplicates, returning the single surviving
    /// canonical EventSystem (or null if none exist).
    /// </summary>
    public static EventSystem EnforceSingleActiveEventSystem(Scene preferredScene)
    {
        EventSystem[] all = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return DeduplicateEventSystems(all, preferredScene);
    }

    /// <summary>
    /// Pure selection and deduplication logic (directly testable in EditMode).
    /// Picks one canonical <see cref="EventSystem"/> — prioritizing an enabled instance in
    /// <paramref name="preferredScene"/> with an <see cref="InputSystemUIInputModule"/> — disables
    /// all others immediately so Unity's active list stays at 1, and clears stale selection.
    /// </summary>
    public static EventSystem DeduplicateEventSystems(EventSystem[] activeSystems, Scene preferredScene)
    {
        if (activeSystems == null || activeSystems.Length == 0)
        {
            return null;
        }

        EventSystem keeper = SelectCanonical(activeSystems, preferredScene);
        if (keeper == null)
        {
            return null;
        }

        for (int i = 0; i < activeSystems.Length; i++)
        {
            EventSystem candidate = activeSystems[i];
            if (candidate == null || candidate == keeper)
            {
                continue;
            }

            if (candidate.enabled)
            {
                candidate.enabled = false;
            }
        }

        if (!keeper.enabled)
        {
            keeper.enabled = true;
        }

        // Disabling every duplicate removes them from Unity's internal m_EventSystems list so
        // EventSystem.current resolves to the single surviving keeper once active in PlayMode.
        // Never call EventSystem.current = keeper when keeper is not yet registered in m_EventSystems.
        if (Application.isPlaying && EventSystem.current == keeper)
        {
            // Clear any stale selection referencing an inactive or unloaded-scene object so
            // keyboard focus is clean when transitioning back to Town.
            GameObject selected = keeper.currentSelectedGameObject;
            if (selected != null && (!selected.activeInHierarchy ||
                (preferredScene.IsValid() && selected.scene.IsValid() && selected.scene != preferredScene)))
            {
                keeper.SetSelectedGameObject(null);
            }
        }

        return keeper;
    }

    private static EventSystem SelectCanonical(EventSystem[] systems, Scene preferredScene)
    {
        EventSystem best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < systems.Length; i++)
        {
            EventSystem candidate = systems[i];
            if (candidate == null || !candidate.gameObject.activeInHierarchy)
            {
                continue;
            }

            int score = 0;
            if (candidate.enabled) score += 1;
            if (candidate.GetComponent<InputSystemUIInputModule>() != null) score += 4;
            if (preferredScene.IsValid() && candidate.gameObject.scene == preferredScene) score += 8;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }
}
