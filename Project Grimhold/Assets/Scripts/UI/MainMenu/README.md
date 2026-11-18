# MainMenu code / scene composition boundary

T2 does not modify scenes or prefabs. T3 keeps the existing serialized field names.

## MainMenuController wiring

Required existing references: `createRoomButton` (Button, Jugar), `menuPanel` (GameObject), `_loginPanel` (LoginPanelView), `_characterCreationPanel` (CharacterCreationPanelView), `_settingsButton`, `_creditsButton`, `_exitButton` (Button), `_settingsPanel` (SettingsPanelView), `_creditsPanel` (CreditsPanelView). Wire `_loginFlowController` and `_connectionCoordinator` to their existing application services. The development room-code references remain hidden/null-safe.

New references are optional for legacy serialization, but wire all of these for the redesigned menu:

| Field | Type | Purpose |
| --- | --- | --- |
| `_backendConfig` | BackendConfiguration | Existing backend asset; fallback is ApplicationAuthContext.BackendConfig, then LoginFlowController.Config. At least one is required, including before sign-in. |
| `_accountText`, `_accountActionText` | TextMeshProUGUI | Session name/state and Iniciar sesión/Cerrar sesión action label. |
| `_accountActionButton` | Button | Single account action. Replaces the legacy pair `_loginButton`/`_logoutButton` in the new composition. |
| `_serverText`, `_playSubtitle` | TextMeshProUGUI | Checking/online/unreachable and Play reason. |
| `_serverRetryButton` | Button | Reintentar health check; no polling. |
| `_townRetryButton` | Button | Reintentar failed Town entry. |
| `_confirmationDialog`, `_infoDialog` | MainMenuDialogView | Separate confirmation/info scrims; mandatory for those actions, never silently bypassed. |
| `_authDialogRoot` | GameObject | One shared auth scrim containing the login and character form roots as siblings. Do not put the controller or menu underneath it. |

Keep the menu visible behind the dialogs. The menu controller owns its button listeners; do not also add persistent Inspector actions for those buttons. The existing `_statusText` is the status/notice line.

## MainMenuDialogView (two instances)

Required: `_titleText`, `_bodyText` (TextMeshProUGUI), `_cancelButton` (Button). Confirmation additionally requires `_acceptText` (TextMeshProUGUI), `_acceptButton` (Button). Put the view on the entire scrim root so closing it hides its contents. All labels/buttons must be its descendants. Use Cancelar for confirmation and Volver for info; accept text is supplied by code. Opening selects cancel; Escape cancels. Configure navigation/raycast shielding in the scene so focus stays in the topmost modal.

## CharacterCreationPanelView

Preserve `_characterNameInput` (TMP_InputField), `_createButton` (Button), `_statusText` (TextMeshProUGUI). New optional legacy-compatible references required for the redesigned form: `_stepText`, `_finishLaterText` (TextMeshProUGUI), `_finishLaterButton` (Button). Open supplies Paso 2 de 2 · Personaje and Cerrar sesión y terminar después. The controller wires finish-later to the existing logout flow, not to local auth clearing.

LoginPanelView uses T1's Open/ApplyResult/SetInteractable/listener APIs. Its internal fields and remembered-username policy are unchanged. Successful login/character creation still enters Town automatically. `MainMenuSessionActions` retains its legacy visibility semantics; the controller additively keeps Jugar visible for guests and gates it by health.

## Composition

The menu UI lives in `Assets/Prefabs/UI/MainMenu Canvas.prefab`; `Assets/Scenes/MainMenu.unity` only instances it and keeps scene-service references (`_loginFlowController`, `_connectionCoordinator`, `_backendConfig`, EventSystem first selection) as instance overrides. Regenerate it with **Grimhold > Author main menu** (`Assets/Editor/MainMenuComposition.cs`) from a saved, non-playing Editor. `SettingsPanel.prefab` (shared with Town and Raid menus) and `CreditsPanel.prefab` stay nested prefab instances. `MainMenuCompositionTests` guards the structure.
