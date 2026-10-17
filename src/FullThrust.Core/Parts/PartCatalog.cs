using System.Text.Json;
using Brutal.Numerics;
using FullThrust.Core.Rendering;

namespace FullThrust.Core.Parts;

public sealed class PartCatalog {

    private readonly Dictionary<string, PartDefinition> _parts;

    public PartCatalog(ResourceDefinition[] resources, Dictionary<string, PartDefinition> parts, ModelCatalog models) {

        Resources = resources;
        Models = models;
        _parts = parts;

    }

    public IReadOnlyList<ResourceDefinition> Resources { get; }

    public ModelCatalog Models { get; }

    public IEnumerable<PartDefinition> Parts => _parts.Values;

    public PartDefinition this[string id] => _parts.TryGetValue(id, out var part) ? part : throw new KeyNotFoundException($"Unknown part '{id}'.");

    public ResourceDefinition? FindResource(string name) {

        foreach (var resource in Resources) {

            if (string.Equals(resource.Name, name, StringComparison.OrdinalIgnoreCase)) {

                return resource;

            }

        }

        return null;

    }

}

public static class PartCatalogLoader {

    private static readonly JsonSerializerOptions Options = new() {

        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

    };

    public static PartCatalog LoadFile(string path, ModelCatalog? models = null) => Load(File.ReadAllText(path), models);

    public static PartCatalog Load(string json, ModelCatalog? models = null) {

        var data = JsonSerializer.Deserialize<PartCatalogData>(json, Options)
            ?? throw new InvalidDataException("Part catalog file is empty.");

        return Build(data, models);

    }

    public static PartCatalog Build(PartCatalogData data, ModelCatalog? models = null) {

        models ??= new ModelCatalog();

        if (data.Resources.Count > ResourceSet.Capacity) {

            throw new InvalidDataException($"At most {ResourceSet.Capacity} resources are supported.");

        }

        var resources = new ResourceDefinition[data.Resources.Count];

        for (var i = 0; i < resources.Length; i++) {

            var resource = data.Resources[i];

            if (string.IsNullOrWhiteSpace(resource.Name) || resource.Density <= 0) {

                throw new InvalidDataException("Resources need a name and a positive density.");

            }

            resources[i] = new ResourceDefinition { Id = i, Name = resource.Name, Density = resource.Density };

        }

        var parts = new Dictionary<string, PartDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in data.Parts) {

            if (!parts.TryAdd(part.Id, CreatePart(part, resources, models))) {

                throw new InvalidDataException($"Duplicate part id '{part.Id}'.");

            }

        }

        return new PartCatalog(resources, parts, models);

    }

    private static PartDefinition CreatePart(PartData data, ResourceDefinition[] resources, ModelCatalog models) {

        if (string.IsNullOrWhiteSpace(data.Id)) {

            throw new InvalidDataException("Every part needs an id.");

        }

        if (data.Mass <= 0 || data.Length <= 0 || data.Diameter <= 0) {

            throw new InvalidDataException($"{data.Id}: mass, length and diameter must be positive.");

        }

        var modules = new List<IPartModuleDefinition>();

        if (data.Tank is { } tank) {

            modules.Add(CreateTank(data, tank, resources));

        }

        if (data.Engine is { } engine) {

            modules.Add(CreateEngine(data, engine, resources));

        }

        if (data.Rcs is { } rcs) {

            modules.Add(CreateRcs(data, rcs, resources));

        }

        if (data.Decoupler is { } decoupler) {

            modules.Add(new DecouplerDefinition { EjectionImpulse = decoupler.EjectionImpulse });

        }

        if (data.Command is { } command) {

            modules.Add(new CommandDefinition { ReactionWheelTorque = command.ReactionWheelTorque });

        }

        var color = data.Color.Length >= 3 ? new float4((float)data.Color[0], (float)data.Color[1], (float)data.Color[2], 1) : new float4(0.8f, 0.8f, 0.8f, 1);

        return new PartDefinition {

            Id = data.Id,
            Name = string.IsNullOrWhiteSpace(data.Name) ? data.Id : data.Name,
            Category = data.Category,

            DryMass = data.Mass,
            Length = data.Length,
            Radius = data.Diameter / 2,

            Shape = Enum.Parse<ShapeKind>(data.Shape, ignoreCase: true),
            Color = color,
            Model = data.Model is { } model ? RegisterModel(data, model, models) : null,
            DragCoefficient = data.DragCoefficient,

            Modules = modules.ToArray(),

        };

    }

    private static ModelDefinition RegisterModel(PartData part, PartModelData model, ModelCatalog models) {

        if (string.IsNullOrWhiteSpace(model.File)) {

            throw new InvalidDataException($"{part.Id}: a part model needs a file.");

        }

        var fit = Enum.Parse<ModelFit>(model.Fit, ignoreCase: true);
        var size = new double3(part.Diameter, part.Length, part.Diameter);

        return models.Register(model.File, Vector(model.RotationDeg), fit, size, Vector(model.Offset));

    }

    private static double3 Vector(double[] values) => values.Length >= 3 ? new double3(values[0], values[1], values[2]) : double3.Zero;

    private static TankDefinition CreateTank(PartData part, TankData data, ResourceDefinition[] resources) {

        var capacity = new ResourceSet();
        var density = new ResourceSet();
        var propellantVolume = 0.0;

        foreach (var resource in resources) {

            density[resource.Id] = resource.Density;

        }

        foreach (var load in data.Propellants) {

            var resource = Resolve(part, load.Resource, resources);

            capacity[resource.Id] += load.Mass;
            propellantVolume += load.Mass / resource.Density;

        }

        return new TankDefinition {

            Capacity = capacity,
            Density = density,

            Volume = propellantVolume / (1 - Math.Clamp(data.UllageFraction, 0.001, 0.5)),

            Pressure = data.Pressure,
            PressurantMass = data.PressurantMass,

        };

    }

    private static EngineDefinition CreateEngine(PartData part, EngineData data, ResourceDefinition[] resources) {

        if (data.VacuumThrust <= 0 || data.VacuumIsp <= 0 || data.SeaLevelIsp < 0 || data.SeaLevelIsp > data.VacuumIsp) {

            throw new InvalidDataException($"{part.Id}: engines need positive thrust and 0 <= sea-level Isp <= vacuum Isp.");

        }

        var oxidizer = string.IsNullOrWhiteSpace(data.Oxidizer) ? -1 : Resolve(part, data.Oxidizer, resources).Id;

        return new EngineDefinition {

            Fuel = Resolve(part, data.Fuel, resources).Id,
            Oxidizer = oxidizer,
            MixtureRatio = oxidizer < 0 ? 0 : data.MixtureRatio,

            VacuumThrust = data.VacuumThrust,
            VacuumIsp = data.VacuumIsp,
            SeaLevelIsp = data.SeaLevelIsp,

            MinThrottle = Math.Clamp(data.MinThrottle, 0.01, 1),
            GimbalRange = data.GimbalRangeDeg * Math.PI / 180,
            GimbalRate = data.GimbalRateDeg * Math.PI / 180,

            Feed = Enum.Parse<EngineFeed>(data.Feed, ignoreCase: true),
            SpoolUpTime = Math.Max(0.05, data.SpoolUpTime),
            SpoolDownTime = Math.Max(0.05, data.SpoolDownTime),
            MinInletPressure = data.MinInletPressure,
            FeedPressure = data.FeedPressure,

            Ignitions = data.Ignitions,
            RequiresUllage = data.RequiresUllage,

        };

    }

    private static RcsDefinition CreateRcs(PartData part, RcsData data, ResourceDefinition[] resources) {

        var nozzles = new double3[data.Nozzles.Count];

        for (var i = 0; i < nozzles.Length; i++) {

            var n = data.Nozzles[i];

            if (n.Length < 3) {

                throw new InvalidDataException($"{part.Id}: RCS nozzle directions need three components.");

            }

            nozzles[i] = double3.Normalize(new double3(n[0], n[1], n[2]));

        }

        return new RcsDefinition {

            Propellant = Resolve(part, data.Propellant, resources).Id,
            NozzleThrust = data.NozzleThrust,
            VacuumIsp = data.VacuumIsp,
            SeaLevelIsp = data.SeaLevelIsp,
            Nozzles = nozzles,

        };

    }

    private static ResourceDefinition Resolve(PartData part, string name, ResourceDefinition[] resources) {

        foreach (var resource in resources) {

            if (string.Equals(resource.Name, name, StringComparison.OrdinalIgnoreCase)) {

                return resource;

            }

        }

        throw new InvalidDataException($"{part.Id}: unknown resource '{name}'.");

    }

}
