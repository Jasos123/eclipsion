using Content.Client.Movement.Systems;
using Content.Shared.Actions;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Robust.Client.Console;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Player;

namespace Content.Client.Ghost
{
    public sealed class GhostSystem : SharedGhostSystem
    {
        [Dependency] private readonly IClientConsoleHost _console = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly SharedActionsSystem _actions = default!;
        [Dependency] private readonly PointLightSystem _pointLightSystem = default!;
        [Dependency] private readonly ContentEyeSystem _contentEye = default!;

        public int AvailableGhostRoleCount { get; private set; }
        public bool InsuranceRespawnAvailable { get; private set; }
        public TimeSpan InsuranceRespawnAt { get; private set; } = TimeSpan.Zero;
        public bool InsuranceSpawnMachineBound { get; private set; } = true;
        public bool InsuranceSpawnMachinePowered { get; private set; } = true;

        private bool _ghostVisibility = true;

        private bool GhostVisibility
        {
            get => _ghostVisibility;
            set
            {
                if (_ghostVisibility == value)
                {
                    return;
                }

                _ghostVisibility = value;

                var query = AllEntityQuery<GhostComponent, SpriteComponent>();
                while (query.MoveNext(out var uid, out _, out var sprite))
                {
                    sprite.Visible = value || uid == _playerManager.LocalEntity;
                }
            }
        }

        public GhostComponent? Player => CompOrNull<GhostComponent>(_playerManager.LocalEntity);
        public bool IsGhost => Player != null;

        public event Action<GhostComponent>? PlayerRemoved;
        public event Action<GhostComponent>? PlayerUpdated;
        public event Action<GhostComponent>? PlayerAttached;
        public event Action? PlayerDetached;
        public event Action<GhostWarpsResponseEvent>? GhostWarpsResponse;
        public event Action<GhostUpdateGhostRoleCountEvent>? GhostRoleCountUpdated;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<GhostComponent, ComponentStartup>(OnStartup);
            SubscribeLocalEvent<GhostComponent, ComponentRemove>(OnGhostRemove);
            SubscribeLocalEvent<GhostComponent, AfterAutoHandleStateEvent>(OnGhostState);

            SubscribeLocalEvent<GhostComponent, LocalPlayerAttachedEvent>(OnGhostPlayerAttach);
            SubscribeLocalEvent<GhostComponent, LocalPlayerDetachedEvent>(OnGhostPlayerDetach);

            SubscribeNetworkEvent<GhostWarpsResponseEvent>(OnGhostWarpsResponse);
            SubscribeNetworkEvent<GhostUpdateGhostRoleCountEvent>(OnUpdateGhostRoleCount);
            SubscribeNetworkEvent<GhostInsuranceRespawnStatusEvent>(OnInsuranceRespawnStatus);
            SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);

            SubscribeLocalEvent<EyeComponent, ToggleLightingActionEvent>(OnToggleLighting);
            SubscribeLocalEvent<EyeComponent, ToggleFoVActionEvent>(OnToggleFoV);
            SubscribeLocalEvent<GhostComponent, ToggleGhostsActionEvent>(OnToggleGhosts);
        }

        private void OnStartup(EntityUid uid, GhostComponent component, ComponentStartup args)
        {
            if (TryComp(uid, out SpriteComponent? sprite))
                sprite.Visible = GhostVisibility || uid == _playerManager.LocalEntity;
        }

        private void OnToggleLighting(EntityUid uid, EyeComponent component, ToggleLightingActionEvent args)
        {
            if (args.Handled)
                return;

            TryComp<PointLightComponent>(uid, out var light);

            if (!component.DrawLight)
            {
                // normal lighting
                Popup.PopupEntity(Loc.GetString("ghost-gui-toggle-lighting-manager-popup-normal"), args.Performer);
                _contentEye.RequestEye(component.DrawFov, true);
            }
            else if (!light?.Enabled ?? false) // skip this option if we have no PointLightComponent
            {
                //WE DO NOT HAVE A POINTLIGHT COMPONENT, SKIPPED - WABASH
                //     // enable personal light
                //     Popup.PopupEntity(Loc.GetString("ghost-gui-toggle-lighting-manager-popup-personal-light"), args.Performer);
                //     _pointLightSystem.SetEnabled(uid, true, light);
                // }
                // else
                // {
                // fullbright mode
                Popup.PopupEntity(Loc.GetString("ghost-gui-toggle-lighting-manager-popup-fullbright"), args.Performer);
                _contentEye.RequestEye(component.DrawFov, false);
                _pointLightSystem.SetEnabled(uid, false, light);
            }
            args.Handled = true;
        }

        private void OnToggleFoV(EntityUid uid, EyeComponent component, ToggleFoVActionEvent args)
        {
            if (args.Handled)
                return;

            Popup.PopupEntity(Loc.GetString("ghost-gui-toggle-fov-popup"), args.Performer);
            _contentEye.RequestToggleFov(uid, component);
            args.Handled = true;
        }

        private void OnToggleGhosts(EntityUid uid, GhostComponent component, ToggleGhostsActionEvent args)
        {
            if (args.Handled)
                return;

            // Report the state we are switching *to*, matching the -on/-off strings.
            Popup.PopupEntity(
                Loc.GetString(GhostVisibility
                    ? "ghost-gui-toggle-ghost-visibility-popup-off"
                    : "ghost-gui-toggle-ghost-visibility-popup-on"),
                args.Performer);

            if (uid == _playerManager.LocalEntity)
                ToggleGhostVisibility();

            args.Handled = true;
        }

        private void OnGhostRemove(EntityUid uid, GhostComponent component, ComponentRemove args)
        {
            _actions.RemoveAction(uid, component.ToggleLightingActionEntity);
            _actions.RemoveAction(uid, component.ToggleFoVActionEntity);
            _actions.RemoveAction(uid, component.ToggleGhostsActionEntity);
            _actions.RemoveAction(uid, component.ToggleGhostHearingActionEntity);

            if (uid != _playerManager.LocalEntity)
                return;

            InsuranceRespawnAvailable = false;
            InsuranceRespawnAt = TimeSpan.Zero;
            InsuranceSpawnMachineBound = true;
            InsuranceSpawnMachinePowered = true;

            GhostVisibility = false;
            PlayerRemoved?.Invoke(component);
        }

        private void OnGhostPlayerAttach(EntityUid uid, GhostComponent component, LocalPlayerAttachedEvent localPlayerAttachedEvent)
        {
            GhostVisibility = true;
            PlayerAttached?.Invoke(component);
        }

        private void OnGhostState(EntityUid uid, GhostComponent component, ref AfterAutoHandleStateEvent args)
        {
            if (TryComp<SpriteComponent>(uid, out var sprite))
                sprite.LayerSetColor(0, component.color);

            if (uid != _playerManager.LocalEntity)
                return;

            // Replicated GhostComponent carries insurance respawn flags from the server; mirror them here so the
            // ghost HUD matches even if GhostInsuranceRespawnStatusEvent reorders relative to component state.
            if (InsuranceRespawnAvailable != component.InsuranceRespawnAvailable
                || InsuranceRespawnAt != component.InsuranceRespawnAt)
            {
                InsuranceRespawnAvailable = component.InsuranceRespawnAvailable;
                InsuranceRespawnAt = component.InsuranceRespawnAt;
                if (!component.InsuranceRespawnAvailable)
                {
                    InsuranceSpawnMachineBound = true;
                    InsuranceSpawnMachinePowered = true;
                }
            }

            PlayerUpdated?.Invoke(component);
        }

        private void OnGhostPlayerDetach(EntityUid uid, GhostComponent component, LocalPlayerDetachedEvent args)
        {
            GhostVisibility = false;
            PlayerDetached?.Invoke();
        }

        private void OnGhostWarpsResponse(GhostWarpsResponseEvent msg)
        {
            if (!IsGhost)
            {
                return;
            }

            GhostWarpsResponse?.Invoke(msg);
        }

        private void OnUpdateGhostRoleCount(GhostUpdateGhostRoleCountEvent msg)
        {
            AvailableGhostRoleCount = msg.AvailableGhostRoles;
            GhostRoleCountUpdated?.Invoke(msg);
        }

        private void OnInsuranceRespawnStatus(GhostInsuranceRespawnStatusEvent msg)
        {
            InsuranceRespawnAvailable = msg.Available;
            InsuranceRespawnAt = msg.RespawnAt;
            InsuranceSpawnMachineBound = msg.SpawnMachineBound;
            InsuranceSpawnMachinePowered = msg.SpawnMachinePowered;
            if (Player != null)
                PlayerUpdated?.Invoke(Player);
        }

        /// <summary>
        /// Clears stale insurance UI between rounds (replacing any attach-based reset that broke ghost visit / return).
        /// </summary>
        private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
        {
            InsuranceRespawnAvailable = false;
            InsuranceRespawnAt = TimeSpan.Zero;
            InsuranceSpawnMachineBound = true;
            InsuranceSpawnMachinePowered = true;
        }

        public void RequestWarps()
        {
            RaiseNetworkEvent(new GhostWarpsRequestEvent());
        }

        public void ReturnToBody()
        {
            var msg = new GhostReturnToBodyRequest();
            RaiseNetworkEvent(msg);
        }

        public void OpenGhostRoles()
        {
            _console.RemoteExecuteCommand(null, "ghostroles");
        }

        public void GhostBarSpawn() // Goobstation - Ghost Bar
        {
            RaiseNetworkEvent(new GhostBarSpawnEvent());
        }

        public void ToggleGhostVisibility(bool? visibility = null)
        {
            GhostVisibility = visibility ?? !GhostVisibility;
        }

        public void ReturnToRound()
        {
            var msg = new GhostReturnToRoundRequest();
            RaiseNetworkEvent(msg);
        }

        public void UseInsuranceRespawn()
        {
            RaiseNetworkEvent(new GhostInsuranceRespawnRequest());
        }
    }
}
