using Brutal.Numerics;

namespace FullThrust.Core.Rendering;

public enum ModelFit {

    // Use the model's own units and origin.
    None,

    // Largest uniform scale that fits the box, centred.
    Uniform,

    // Independent per-axis scale so the bounds match the box exactly, centred.
    Stretch,

    // Uniform scale matching the box height (Y), centred.
    Length,

}

// A model file plus how to place it in its owner's space (Y up, metres). File paths are relative to data/models.
public sealed class ModelDefinition {

    public required int Index { get; init; }

    public required string File { get; init; }

    public double3 RotationDeg { get; init; }

    public ModelFit Fit { get; init; }

    public double3 FitSize { get; init; }

    public double3 Offset { get; init; }

    public override string ToString() => $"{File} ({Fit})";

}

// Renderer-agnostic registry; the game resolves Index to GPU resources.
public sealed class ModelCatalog {

    private readonly List<ModelDefinition> _models = [];

    public int Count => _models.Count;

    public ModelDefinition this[int index] => _models[index];

    public IReadOnlyList<ModelDefinition> Models => _models;

    public ModelDefinition Register(string file, double3 rotationDeg = default, ModelFit fit = ModelFit.None, double3 fitSize = default, double3 offset = default) {

        var normalized = file.Replace('\\', '/');

        foreach (var existing in _models) {

            if (string.Equals(existing.File, normalized, StringComparison.OrdinalIgnoreCase) && existing.RotationDeg == rotationDeg && existing.Fit == fit && existing.FitSize == fitSize && existing.Offset == offset) {

                return existing;

            }

        }

        var model = new ModelDefinition {

            Index = _models.Count,
            File = normalized,
            RotationDeg = rotationDeg,
            Fit = fit,
            FitSize = fitSize,
            Offset = offset,

        };

        _models.Add(model);

        return model;

    }

}

// Draws the referenced model at the entity's Transform.
public struct ModelInstance {

    public int Model;

}
