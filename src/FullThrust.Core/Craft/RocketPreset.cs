using System.Text.Json;
using Brutal.Numerics;
using FullThrust.Core.Parts;

namespace FullThrust.Core.Craft;

public sealed class RocketCatalogData {

    public List<RocketPresetData> Rockets { get; set; } = [];

}

public sealed class RocketPresetData {

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public List<RocketPartData> Parts { get; set; } = [];

}

public sealed class RocketPartData {

    public string Part { get; set; } = "";

    // Index of an earlier entry; stack parts default to the previous stack entry.
    public int? Parent { get; set; }

    // Activation order: 0 fires on the first stage command, -1 never.
    public int Stage { get; set; } = -1;

    // Zero stacks below the parent; N places N copies around it.
    public int Radial { get; set; }

    // Radial mount height as a fraction of the parent's length, from its bottom.
    public double Height { get; set; } = 0.5;

    public double AngleDeg { get; set; }

}

public readonly struct PresetPart {

    public readonly PartDefinition Definition;

    public readonly int Parent;

    public readonly int Stage;

    public readonly bool Radial;

    public readonly double3 Position;
    public readonly doubleQuat Rotation;

    public PresetPart(PartDefinition definition, int parent, int stage, bool radial, double3 position, doubleQuat rotation) {

        Definition = definition;
        Parent = parent;
        Stage = stage;
        Radial = radial;
        Position = position;
        Rotation = rotation;

    }

}

public sealed class RocketPreset {

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required PresetPart[] Parts { get; init; }

    public int StageCount {

        get {

            var max = -1;

            foreach (var part in Parts) {

                max = Math.Max(max, part.Stage);

            }

            return max + 1;

        }

    }

    // Craft-space Y of the lowest point, so a pad spawn can rest the stack on the ground.
    public double Bottom {

        get {

            var bottom = 0.0;

            foreach (var part in Parts) {

                bottom = Math.Min(bottom, part.Position.Y - part.Definition.Length / 2);

            }

            return bottom;

        }

    }

    public double Top => Parts.Length > 0 ? Parts[0].Position.Y + Parts[0].Definition.Length / 2 : 0;

    public double Height => Top - Bottom;

    public double TotalMass {

        get {

            var mass = 0.0;

            foreach (var part in Parts) {

                mass += part.Definition.DryMass;

                if (part.Definition.Module<TankDefinition>() is { } tank) {

                    mass += tank.Capacity.Sum() + tank.PressurantMass;

                }

            }

            return mass;

        }

    }

    public override string ToString() => Name;

}

public sealed class RocketCatalog {

    public RocketCatalog(RocketPreset[] rockets) {

        Rockets = rockets;

    }

    public IReadOnlyList<RocketPreset> Rockets { get; }

    public RocketPreset? Find(string id) {

        foreach (var rocket in Rockets) {

            if (string.Equals(rocket.Id, id, StringComparison.OrdinalIgnoreCase)) {

                return rocket;

            }

        }

        return null;

    }

    public int IndexOf(RocketPreset rocket) {

        for (var i = 0; i < Rockets.Count; i++) {

            if (ReferenceEquals(Rockets[i], rocket)) {

                return i;

            }

        }

        return -1;

    }

}

public static class RocketCatalogLoader {

    private static readonly JsonSerializerOptions Options = new() {

        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

    };

    public static RocketCatalog LoadFile(string path, PartCatalog parts) => Load(File.ReadAllText(path), parts);

    public static RocketCatalog Load(string json, PartCatalog parts) {

        var data = JsonSerializer.Deserialize<RocketCatalogData>(json, Options)
            ?? throw new InvalidDataException("Rocket catalog file is empty.");

        var rockets = new RocketPreset[data.Rockets.Count];

        for (var i = 0; i < rockets.Length; i++) {

            rockets[i] = Build(data.Rockets[i], parts);

        }

        if (rockets.Length == 0) {

            throw new InvalidDataException("The rocket catalog defines no rockets.");

        }

        return new RocketCatalog(rockets);

    }

    public static RocketPreset Build(RocketPresetData data, PartCatalog catalog) {

        if (data.Parts.Count == 0) {

            throw new InvalidDataException($"{data.Id}: a rocket needs at least one part.");

        }

        var parts = new List<PresetPart>();
        var entryToPart = new int[data.Parts.Count];
        var lastStack = -1;

        for (var i = 0; i < data.Parts.Count; i++) {

            var entry = data.Parts[i];
            var definition = catalog[entry.Part];

            if (i == 0) {

                if (entry.Radial > 0 || entry.Parent is not null) {

                    throw new InvalidDataException($"{data.Id}: the first part must be the stack root.");

                }

                entryToPart[i] = parts.Count;
                parts.Add(new PresetPart(definition, -1, entry.Stage, false, double3.Zero, doubleQuat.Identity));
                lastStack = i;

                continue;

            }

            var parentEntry = entry.Parent ?? lastStack;

            if (parentEntry < 0 || parentEntry >= i || data.Parts[parentEntry].Radial > 0) {

                throw new InvalidDataException($"{data.Id}: part {i} ({entry.Part}) must attach to an earlier stack part.");

            }

            var parentIndex = entryToPart[parentEntry];
            var parent = parts[parentIndex];
            var parentLength = parent.Definition.Length;

            entryToPart[i] = parts.Count;

            if (entry.Radial <= 0) {

                var y = parent.Position.Y - parentLength / 2 - definition.Length / 2;

                parts.Add(new PresetPart(definition, parentIndex, entry.Stage, false, new double3(parent.Position.X, y, parent.Position.Z), doubleQuat.Identity));
                lastStack = i;

                continue;

            }

            var height = parent.Position.Y - parentLength / 2 + Math.Clamp(entry.Height, 0, 1) * parentLength;
            var distance = parent.Definition.Radius + definition.Radius;

            for (var k = 0; k < entry.Radial; k++) {

                var angle = (entry.AngleDeg + 360.0 * k / entry.Radial) * Math.PI / 180;
                var outward = new double3(Math.Cos(angle), 0, -Math.Sin(angle));
                var rotation = doubleQuat.CreateFromAxisAngle(double3.UnitY, angle);

                parts.Add(new PresetPart(definition, parentIndex, entry.Stage, true, new double3(parent.Position.X, height, parent.Position.Z) + outward * distance, rotation));

            }

        }

        return new RocketPreset {

            Id = data.Id,
            Name = string.IsNullOrWhiteSpace(data.Name) ? data.Id : data.Name,
            Description = data.Description,
            Parts = parts.ToArray(),

        };

    }

}
