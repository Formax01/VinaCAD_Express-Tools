using Prima.VinaCAD.EditorInput;
using System;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Helper
{
    public class ShoePlacementJig : DrawJig
    {
        private readonly ShoeModel _model;
        private readonly Matrix3d _ucsToWcs;
        private Point3d _point;

        public Vector3d Direction { get; private set; } = Vector3d.XAxis;
        public bool Mirror { get; private set; }
        public bool FlipRequested { get; private set; }

        public ShoePlacementJig(ShoeModel model, Matrix3d ucsToWcs)
        {
            _model = model;
            _ucsToWcs = ucsToWcs;
            _point = model.StartPoint;
            Mirror = model.Mirror;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            FlipRequested = false;

            JigPromptPointOptions opts = new JigPromptPointOptions
            {
                BasePoint = _model.StartPoint,
                UseBasePoint = true,
                Cursor = CursorType.RubberBand
            };
            opts.SetMessageAndKeywords(
                "\nChọn hướng cho chi tiết [Flip]: ", "Flip");

            PromptPointResult res = prompts.AcquirePoint(opts);

            if (res.Status == PromptStatus.Keyword)
            {
                Mirror = !Mirror;
                FlipRequested = true;
                return SamplerStatus.Cancel;      // Action sẽ Drag lại
            }

            if (res.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (res.Value.DistanceTo(_point) < 1e-6) return SamplerStatus.NoChange;

            _point = res.Value;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            object o = Application.GetSystemVariable("ORTHOMODE");
            bool ortho = o != null && Convert.ToInt32(o) == 1;

            Direction = DuctHelper.GetDirection(_model.StartPoint, _point, ortho);

            Point3d[] c = ShoeHelper.GetCorners(
    _model.StartPoint, Direction,
    _model.Height, _model.SmallSide, _model.BaseSide, Mirror);

            draw.SubEntityTraits.Color = 3;
            WorldGeometry g = draw.Geometry;
            for (int i = 0; i < c.Length; i++)
                g.WorldLine(c[i].TransformBy(_ucsToWcs), c[(i + 1) % c.Length].TransformBy(_ucsToWcs));

            return true;
        }
    }
}
