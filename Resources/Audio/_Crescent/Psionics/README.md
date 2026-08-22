# Psionic power audio

Drop `.ogg` files here, add an entry for each to `attributions.yml`, then point the matching
prototype at it. Every psionic sound is wired from a single file:

`Resources/Prototypes/Entities/Effects/psionics.yml`

Each effect entity there has an `EmitSoundOnSpawn`, so swapping a sound is a one-line change to its
`path:`. Nothing in C# plays psionic audio.

Once a file is here the path to use is `/Audio/_Crescent/Psionics/<name>.ogg`.

## How long a sound may be

**A sound is cut off when the effect that spawned it despawns.** `PlayPvs` parents the audio entity
to the emitter, so a `TimedDespawn` on the effect is a hard ceiling on the audio - a 1.2s file on a
0.4s effect simply stops at 0.4s. Trailing silence counts against that budget, so trim the tail.

Two effects also **retrigger on a 0.35s throttle** (`ImpactCooldown`, in `PsionicDefensePowerSystem`
and `AegisDomeSystem`). Their sounds must finish inside that window or a sustained burst of fire
stacks them into mush. That throttle, not the despawn, is their real limit.

Effects with **no despawn** live as long as the power does, so nothing truncates them - but they
fire once, on spawn. They want a short "it went up" hit, not a drone. A continuous hum for the whole
duration is a different component (`AmbientSound`); ask and it can be added.

| What it plays for | Prototype | Hard limit | Aim for | Suggested filename |
| --- | --- | --- | --- | --- |
| Energy Aegis goes up | `EffectPsionicEnergyShield` | none (bubble lasts 15s) | 0.6 - 1.0s | `aegis_raise.ogg` |
| Energy Aegis soaks a hit | `EffectPsionicShieldImpact` | **0.35s** (retrigger) | 0.20 - 0.30s | `aegis_impact.ogg` |
| Aegis Dome goes up | `PsionicAegisDome` | none (dome lasts 20s) | 0.8 - 1.2s | `dome_raise.ogg` |
| Aegis Dome soaks a hit | `EffectPsionicAegisImpact` | **0.35s** (retrigger) | 0.20 - 0.28s | `dome_impact.ogg` |
| Aegis Dome shatters | `EffectPsionicAegisShatter` | 0.55s (despawn) | 0.40 - 0.50s | `dome_shatter.ogg` |
| Stasis Field is cast | `PsionicRecurrenceField` | none (field lasts 14s) | 0.5 - 0.7s of source | `stasis_cast.ogg` |
| Recurrence Pulse fires | `EffectPsionicRecurrencePulse` | 0.45s (despawn) | 0.35 - 0.45s | `recurrence_pulse.ogg` |
| Reweave repairs armour | `EffectPsionicReweave` | 2.0s (despawn) | 1.2 - 1.8s | `reweave.ogg` |
| Armour unravels | `EffectPsionicUnravel` | 2.0s (despawn) | 0.8 - 1.5s | `unravel.ogg` |
| Flame Breath | `EffectPsionicFlameBreath` | 0.8s (despawn) | 0.6 - 0.8s | `flame_breath.ogg` |

**Stasis Field is pitched down.** It plays at `pitch: 0.6`, which stretches the sample to about
1.67x its file length. A 0.6s file is heard as ~1.0s. Author for the file length in the table, not
the played length.

Any "hard limit" that is a despawn can be raised - it is the `lifetime:` on that effect. The two
retrigger limits need `ImpactCooldown` raised in C# as well, which also makes the barrier flash
less often, so those are better left alone.

## Format

- `.ogg` (Vorbis), 44.1 kHz.
- **Mono.** These are positional sounds and OpenAL only spatialises mono buffers - a stereo file
  plays at full volume everywhere with no direction.
- Normalise to roughly the level of the stock sounds; per-sound trim lives in the `volume:` param in
  `psionics.yml` and is already set for the placeholders.

`EffectPsionicReweave` and `EffectPsionicUnravel` exist only to carry audio - they have no sprite.
If a power needs a sound and has no effect of its own, add a sound-only entity like those two rather
than playing audio from a system, so this table stays complete.
