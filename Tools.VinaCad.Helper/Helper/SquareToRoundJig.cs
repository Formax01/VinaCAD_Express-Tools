using Prima.VinaCAD.EditorInput;
using System;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Helper
{
    public class SquareToRoundJig : DrawJig
    {
        private readonly SquareToRoundModel _model;
        private readonly Matrix3d _ucsToWcs;
        private Point3d _point;

        public Vector3d Direction { get; private set; } = Vector3d.XAxis;

        public SquareToRoundJig(SquareToRoundModel model, Matrix3d ucsToWcs)
        {
            _model = model;
            _ucsToWcs = ucsToWcs;
            _point = model.StartPoint;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opts = new JigPromptPointOptions(
                "\nChọn hướng cho chi tiết giảm: ")
            {
                BasePoint = _model.StartPoint,
                UseBasePoint = true,
                Cursor = CursorType.RubberBand
            };

            PromptPointResult res = prompts.AcquirePoint(opts);
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

            Point3d[] c = SquareToRoundHelper.GetCorners(
                _model.StartPoint, Direction,
                _model.BigSize, _model.SmallSize, _model.Length);

            draw.SubEntityTraits.Color = 3;
            WorldGeometry g = draw.Geometry;
            Point3d[] path = SquareToRoundHelper.GetPath(c);
            for (int i = 0; i < path.Length - 1; i++)
                g.WorldLine(path[i].TransformBy(_ucsToWcs), path[i + 1].TransformBy(_ucsToWcs));

            return true;
        }
    }
}
