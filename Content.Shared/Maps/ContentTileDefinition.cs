using Content.Shared._Crescent;
using Content.Shared.Atmos;
using Content.Shared.Light.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Tools;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

namespace Content.Shared.Maps
{
    [Prototype("tile")]
    public sealed partial class ContentTileDefinition : IPrototype, IInheritingPrototype, ITileDefinition
    {
        [ValidatePrototypeId<ToolQualityPrototype>]
        public const string PryingToolQuality = "Prying";

        public const string SpaceID = "Space";

        [ParentDataFieldAttribute(typeof(AbstractPrototypeIdArraySerializer<ContentTileDefinition>))]
        public string[]? Parents { get; private set; }

        [NeverPushInheritance]
        [AbstractDataFieldAttribute]
        public bool Abstract { get; private set; }

        [IdDataField] public string ID { get; private set; } = string.Empty;

        public ushort TileId { get; private set; }

        [DataField("name")]
        public string Name { get; private set; } = "";
        [DataField("sprite")] public ResPath? Sprite { get; private set; }
        /* SPCR/MLGTASTICa 2025
         The base name of the DecalPrototypes for directionals.
         Decals are used to do the directional feel of the tile.
         If you set your directionals name to be "ground" , you will need to have
         3 decal prototypes, named groundEdge, groundCorner and groundOuterCorner
         with rotation 0 being at the direction North for edges , and North-East for corners.
         Corners draw over edges. Ensure your edge overlap is fully covered by the corner sprite.
         */
        [DataField("directionals")] public string? Directionals { get; private set; }

        [DataField("directionalRequirement")] public DirectionalType DirectionalType { get; private set; }

        // Wheter the sprite has a special directional defined for each direction. Makes it so instead of rotation,
        // It will look for decals with direction appended, like EdgeN , EdgeS, edgeE, edgeW
        // for corners it will be CornerNE, CornerNW , etc etc
        [DataField("uniqueDirectionals")] public bool uniqueDirectionals { get; private set; }

        [DataField("edgeSprites")] public Dictionary<Direction, ResPath> EdgeSprites { get; private set; } = new();

        [DataField("edgeSpritePriority")] public int EdgeSpritePriority { get; private set; } = 0;

        [DataField("isSubfloor")] public bool IsSubFloor { get; private set; }

        /// <summary>
        /// Multiplier applied to explosion intensity when rolling to break this tile.
        /// Values below one make the tile more resistant to explosions.
        /// </summary>
        [DataField]
        public float ExplosionBreakMultiplier { get; private set; } = 1f;

        /// <summary>
        /// Whether an explosion that uncovers this tile should stop breaking further tile layers on this square.
        /// The tile can still be damaged normally by a later explosion while it is exposed.
        /// </summary>
        [DataField]
        public bool StopsExplosionBreakChain { get; private set; }

        [DataField("baseTurf")]
        public string BaseTurf { get; private set; } = string.Empty;

        [DataField]
        public PrototypeFlags<ToolQualityPrototype> DeconstructTools { get; set; } = new();

        // Delta V
        [DataField("canShovel")] public bool CanShovel { get; private set; }
        //Delta V
        
        /// <summary>
        /// Effective mass of this tile for grid impacts.
        /// </summary>
        [DataField]
        public float Mass = 800f;

        /// <remarks>
        /// Legacy AF but nice to have.
        /// </remarks>
        public bool CanCrowbar => DeconstructTools.Contains(PryingToolQuality);

        /// <summary>
        /// These play when the mob has shoes on.
        /// </summary>
        [DataField("footstepSounds")] public SoundSpecifier? FootstepSounds { get; private set; }

        /// <summary>
        /// These play when the mob has no shoes on.
        /// </summary>
        [DataField("barestepSounds")] public SoundSpecifier? BarestepSounds { get; private set; } = new SoundCollectionSpecifier("BarestepHard");

        [DataField("friction")] public float Friction { get; set; } = 0.2f;

        [DataField("variants")] public byte Variants { get; set; } = 1;

        /// <summary>
        /// This controls what variants the `variantize` command is allowed to use.
        /// </summary>
        [DataField("placementVariants")] public float[] PlacementVariants { get; set; } = { 1f };

        [DataField("thermalConductivity")] public float ThermalConductivity = 0.04f;

        // Heat capacity is opt-in, not opt-out.
        [DataField("heatCapacity")] public float HeatCapacity = Atmospherics.MinimumHeatCapacity;

        [DataField("itemDrop", customTypeSerializer:typeof(PrototypeIdSerializer<EntityPrototype>))]
        public string ItemDropPrototypeName { get; private set; } = "FloorTileItemSteel";

        // TODO rename data-field in yaml
        /// <summary>
        /// Whether or not the tile is exposed to the map's atmosphere.
        /// </summary>
        [DataField("isSpace")] public bool MapAtmosphere { get; private set; }

        /// <summary>
        ///     Friction override for mob mover in <see cref="SharedMoverController"/>
        /// </summary>
        [DataField("mobFriction")]
        public float? MobFriction { get; private set; }

        /// <summary>
        ///     "Average" static coefficient of friction for assuming a steel tile. This is only used as a fallback for a fallback for a fallback,
        ///     except in the case of Space Wind. This default value is assuming an interaction interface of "Rubber on steel tile".
        /// </summary>
        [DataField]
        public float? MobFrictionNoInput;

        /// <summary>
        ///     Accel override for mob mover in <see cref="SharedMoverController"/>
        /// </summary>
        [DataField("mobAcceleration")]
        public float? MobAcceleration { get; private set; }

        [DataField("sturdy")] public bool Sturdy { get; private set; } = true;

        /// <summary>
        /// Can weather affect this tile.
        /// </summary>
        [DataField("weather")] public bool Weather = false;

        /// <summary>
        /// Is this tile immune to RCD deconstruct.
        /// </summary>
        [DataField("indestructible")] public bool Indestructible = false;

        // _RMC14 start
        // These come with the RMC14 tile prototypes. The systems that act on them (xeno weeds,
        // digging, tunnels) were NOT ported, so nothing reads these yet - they exist so the
        // ported yaml keeps its data and validates instead of erroring out. If you port one of
        // those systems, wire it up here rather than re-adding the field.

        /// <summary>
        /// Whether a xeno can dig this tile out. Not yet consumed by any system.
        /// </summary>
        [DataField] public bool CanDig;

        /// <summary>
        /// Whether xeno weeds spread onto this tile. Not yet consumed by any system.
        /// </summary>
        [DataField] public bool WeedsSpreadable = true;

        /// <summary>
        /// Whether this tile only holds weeds sourced from a neighbouring tile. Not yet consumed by any system.
        /// </summary>
        [DataField] public bool SemiWeedable;

        /// <summary>
        /// Whether a xeno tunnel may be placed on this tile. Not yet consumed by any system.
        /// </summary>
        [DataField] public bool CanPlaceTunnel = true;

        /// <summary>
        /// Whether this tile refuses anchoring entities to it. Not yet consumed by any system.
        /// </summary>
        [DataField] public bool BlockAnchoring;

        /// <summary>
        /// Whether this tile refuses construction on top of it. Not yet consumed by any system.
        /// </summary>
        [DataField] public bool BlockConstruction;

        /// <summary>
        /// Colour this tile draws as on the RMC14 minimap. Not yet consumed by any system.
        /// </summary>
        [DataField] public Color? MinimapColor;
        // _RMC14 end

        public void AssignTileId(ushort id)
        {
            TileId = id;
        }

        /// <summary>
        ///     For optionally handling per-tile behavior of airflow simulation. Which is useful for ZAS-like air sim, and for MAS.
        ///     Intentionally public because I want entities to be able to mess with this, such as ship shielding that prevents air from flowing across a shielded tile.
        ///     For planet maps, you can instead mark the GridAtmosphere as !Simulated. Which will make the entire atmos system not run on a given grid.
        /// </summary>
        [DataField]
        public bool Reinforced;

        [DataField]
        public bool SimulatedTurf = true;
    }

    [Flags]
    public enum TileFlag : byte
    {
        None = 0,
        Roof = 1 << 0,
    }
}
