using Content.Shared.Abilities.Psionics;
using Content.Server.Abilities.Psionics;
using Content.Shared.Eye;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Server.GameObjects;
using Content.Shared.NPC.Systems;
using Content.Shared.Psionics;

namespace Content.Server.Psionics
{
    public sealed class PsionicInvisibilitySystem : EntitySystem
    {
        [Dependency] private readonly PsionicInvisibilityPowerSystem _invisSystem = default!;
        [Dependency] private readonly NpcFactionSystem _npcFactonSystem = default!;
        [Dependency] private readonly SharedEyeSystem _eye = default!;
        public override void Initialize()
        {
            base.Initialize();
            /// Masking
            SubscribeLocalEvent<ActorComponent, ComponentInit>(OnInit);
            SubscribeLocalEvent<PsionicInsulationComponent, ComponentInit>(OnInsulInit);
            SubscribeLocalEvent<PsionicInsulationComponent, ComponentShutdown>(OnInsulShutdown);

            /// Layer
            SubscribeLocalEvent<PsionicallyInvisibleComponent, ComponentStartup>(OnInvisInit);
            SubscribeLocalEvent<PsionicallyInvisibleComponent, ComponentShutdown>(OnInvisShutdown);

            // PVS Stuff
            SubscribeLocalEvent<PsionicallyInvisibleComponent, EntInsertedIntoContainerMessage>(OnEntInserted);
            SubscribeLocalEvent<PsionicallyInvisibleComponent, EntRemovedFromContainerMessage>(OnEntRemoved);
        }

        private void OnInit(EntityUid uid, ActorComponent component, ComponentInit args)
        {
            if (!HasComp<PsionicInsulationComponent>(uid))
                SetCanSeePsionicInvisiblity(uid, false);
        }

        private void OnInsulInit(EntityUid uid, PsionicInsulationComponent component, ComponentInit args)
        {
            if (HasComp<PsionicInvisibilityUsedComponent>(uid))
                _invisSystem.ToggleInvisibility(uid);

            if (_npcFactonSystem.ContainsFaction(uid, "PsionicInterloper"))
            {
                component.SuppressedFactions.Add("PsionicInterloper");
                _npcFactonSystem.RemoveFaction(uid, "PsionicInterloper");
            }

            if (_npcFactonSystem.ContainsFaction(uid, "GlimmerMonster"))
            {
                component.SuppressedFactions.Add("GlimmerMonster");
                _npcFactonSystem.RemoveFaction(uid, "GlimmerMonster");
            }

            SetCanSeePsionicInvisiblity(uid, true);
        }

        private void OnInsulShutdown(EntityUid uid, PsionicInsulationComponent component, ComponentShutdown args)
        {
            SetCanSeePsionicInvisiblity(uid, false);

            if (!HasComp<PsionicComponent>(uid))
            {
                component.SuppressedFactions.Clear();
                return;
            }

            foreach (var faction in component.SuppressedFactions)
            {
                _npcFactonSystem.AddFaction(uid, faction);
            }
            component.SuppressedFactions.Clear();
        }

        // Eclipsion - concealment is a stealth field, not a PVS layer swap, so no visibility layer goes on at all.
        // PVS only sends an entity when the viewer's mask contains every bit the entity carries
        // ((mask & visMask) == visMask) and the engine forces bit 1 on for everything regardless, so adding the
        // PsionicInvisibility layer was on its own enough to delete the psion from every ordinary client -
        // dropping the Normal layer alongside it never made any difference. With the layer off everyone keeps
        // receiving the entity and renders it through the stealth shader, which is the point: the power hides a
        // silhouette instead of the whole person. Only the eye bit is still set, for legacy content on that layer.
        private void OnInvisInit(EntityUid uid, PsionicallyInvisibleComponent component, ComponentStartup args)
        {
            SetCanSeePsionicInvisiblity(uid, true);
        }


        private void OnInvisShutdown(EntityUid uid, PsionicallyInvisibleComponent component, ComponentShutdown args)
        {
            SetCanSeePsionicInvisiblity(uid, false);
        }

        private void OnEntInserted(EntityUid uid, PsionicallyInvisibleComponent component, EntInsertedIntoContainerMessage args)
        {
            DirtyEntity(args.Entity);
        }

        private void OnEntRemoved(EntityUid uid, PsionicallyInvisibleComponent component, EntRemovedFromContainerMessage args)
        {
            DirtyEntity(args.Entity);
        }

        public void SetCanSeePsionicInvisiblity(EntityUid uid, bool set)
        {
            if (set == true)
            {
                if (EntityManager.TryGetComponent(uid, out EyeComponent? eye))
                {
                    _eye.SetVisibilityMask(uid, eye.VisibilityMask | (int) VisibilityFlags.PsionicInvisibility, eye);
                }
            }
            else
            {
                if (EntityManager.TryGetComponent(uid, out EyeComponent? eye))
                {
                    _eye.SetVisibilityMask(uid, eye.VisibilityMask & ~(int) VisibilityFlags.PsionicInvisibility, eye);
                }
            }
        }
    }
}
