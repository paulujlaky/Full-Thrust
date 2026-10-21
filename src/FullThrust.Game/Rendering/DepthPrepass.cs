namespace FullThrust.Game.Rendering;

// Lays down scene depth before shading, so ambient occlusion can read it and the opaque pass shades each pixel once.
public sealed class DepthPrepass : IRenderPass {

    private readonly List<IDepthCaster> _casters = [];

    public string Name => "Depth prepass";

    public RenderStage Stage => RenderStage.Depth;

    public bool Enabled { get; set; } = true;

    public void AddCaster(IDepthCaster caster) => _casters.Add(caster);

    public void Record(in FrameContext frame) {

        var context = new DepthContext(frame.CommandBuffer, frame.FrameSet, DepthView.Camera, -1, Frustum.FromMatrix(frame.View * frame.Projection));

        foreach (var caster in _casters) {

            if (caster.CastsDepth) {

                caster.RecordDepth(context);

            }

        }

    }

    public void Dispose() {

    }

}
