using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Preferences;
using Robust.Client;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby
{
    /// <summary>
    ///     Receives <see cref="PlayerPreferences" /> and <see cref="GameSettings" /> from the server during the initial
    ///     connection.
    ///     Stores preferences on the server through <see cref="SelectCharacter" /> and <see cref="UpdateCharacter" />.
    /// </summary>
    public sealed class ClientPreferencesManager : IClientPreferencesManager
    {
        [Dependency] private readonly IClientNetManager _netManager = default!;
        [Dependency] private readonly IBaseClient _baseClient = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;

        public event Action? OnServerDataLoaded;
        public event Action<int>? SlotSelected;

        public GameSettings Settings { get; private set; } = default!;
        public PlayerPreferences Preferences { get; private set; } = default!;

        public void Initialize()
        {
            _netManager.RegisterNetMessage<MsgPreferencesAndSettings>(HandlePreferencesAndSettings);
            _netManager.RegisterNetMessage<MsgUpdatePreferences>(UpdatePreferences);
            _netManager.RegisterNetMessage<MsgUpdateCharacter>();
            _netManager.RegisterNetMessage<MsgSelectCharacter>();
            _netManager.RegisterNetMessage<MsgDeleteCharacter>();

            _baseClient.RunLevelChanged += BaseClientOnRunLevelChanged;
        }

        private void BaseClientOnRunLevelChanged(object? sender, RunLevelChangedEventArgs e)
        {
            if (e.NewLevel == ClientRunLevel.Initialize)
            {
                Settings = default!;
                Preferences = default!;
            }
        }

        public void SelectCharacter(ICharacterProfile profile)
        {
            SelectCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void SelectCharacter(int slot)
        {
            Preferences = new PlayerPreferences(Preferences.Characters, slot, Preferences.AdminOOCColor);
            var msg = new MsgSelectCharacter
            {
                SelectedCharacterIndex = slot
            };
            _netManager.ClientSendMessage(msg);
            SlotSelected?.Invoke(slot);
        }

        public void UpdateCharacter(ICharacterProfile profile, int slot)
        {
            var collection = IoCManager.Instance!;
            profile.EnsureValid(_playerManager.LocalSession!, collection);
            var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters) {[slot] = profile};
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor);
            var msg = new MsgUpdateCharacter
            {
                Profile = profile,
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        public void CreateCharacter(ICharacterProfile profile)
        {
            var characters = new Dictionary<int, ICharacterProfile>(Preferences.Characters);
            var lowest = Enumerable.Range(0, Settings.MaxCharacterSlots)
                .Except(characters.Keys)
                .FirstOrNull();

            if (lowest == null)
            {
                throw new InvalidOperationException("Out of character slots!");
            }

            var l = lowest.Value;
            characters.Add(l, profile);
            Preferences = new PlayerPreferences(characters, Preferences.SelectedCharacterIndex, Preferences.AdminOOCColor);

            UpdateCharacter(profile, l);
        }

        public void DeleteCharacter(ICharacterProfile profile)
        {
            DeleteCharacter(Preferences.IndexOfCharacter(profile));
        }

        public void DeleteCharacter(int slot)
        {
            var characters = Preferences.Characters.Where(p => p.Key != slot).ToList();

            // If we just deleted the selected slot, move the selection with it. The server does this on its
            // side (see HandleDeleteCharacterMessage) but never echoes the result back, so leaving the index
            // dangling here means every later SelectedCharacter lookup throws KeyNotFoundException and the
            // lobby silently degrades into "no preview, dead buttons".
            var selected = Preferences.SelectedCharacterIndex;
            if (selected == slot && characters.Count > 0)
                selected = characters[0].Key;

            Preferences = new PlayerPreferences(characters, selected, Preferences.AdminOOCColor);
            var msg = new MsgDeleteCharacter
            {
                Slot = slot
            };
            _netManager.ClientSendMessage(msg);
        }

        private void HandlePreferencesAndSettings(MsgPreferencesAndSettings message)
        {
            Preferences = SanitizeReceivedPreferences(message.Preferences);
            Settings = message.Settings;

            OnServerDataLoaded?.Invoke();
        }

        public void UpdatePreferences(MsgUpdatePreferences message)
        {
            Preferences = SanitizeReceivedPreferences(message.Preferences);

            OnServerDataLoaded?.Invoke();
        }

        /// <summary>
        ///     Re-validate profiles the server sent us against the LOCAL prototype set before anything in the
        ///     lobby UI touches them. The server validates against its own prototypes, but if this client is
        ///     missing/behind on a prototype the profile references (job, marking, etc.) the character-setup UI
        ///     would index a missing prototype and hard-crash the whole client (black screen). Validated() drops
        ///     anything this client can't resolve, turning a crash into graceful degradation. Purely in-memory —
        ///     nothing is written back unless the player explicitly saves.
        /// </summary>
        private PlayerPreferences SanitizeReceivedPreferences(PlayerPreferences prefs)
        {
            var session = _playerManager.LocalSession;
            if (session == null)
                return prefs;

            var collection = IoCManager.Instance!;
            var characters = new Dictionary<int, ICharacterProfile>(prefs.Characters.Count);
            foreach (var (slot, profile) in prefs.Characters)
            {
                try
                {
                    characters[slot] = profile.Validated(session, collection);
                }
                catch (Exception e)
                {
                    // A profile we somehow can't even validate must not take the whole client down.
                    Logger.ErrorS("prefs", $"Failed to validate received character in slot {slot}: {e}");
                    characters[slot] = profile;
                }
            }

            // Same clamp the server does in SanitizePreferences. Kept here too so a stale server build (or a
            // slot the server dropped while validating) can't hand us an index with no profile behind it —
            // SelectedCharacter throws on that and the whole lobby degrades to "no character, dead buttons".
            var selected = prefs.SelectedCharacterIndex;
            if (characters.Count > 0 && !characters.ContainsKey(selected))
            {
                var replacement = characters.Keys.Min();
                Logger.ErrorS("prefs",
                    $"Server sent selected slot {selected} but only slots [{string.Join(", ", characters.Keys)}] " +
                    $"exist. Falling back to slot {replacement}.");
                selected = replacement;
            }

            return new PlayerPreferences(characters, selected, prefs.AdminOOCColor);
        }
    }
}
