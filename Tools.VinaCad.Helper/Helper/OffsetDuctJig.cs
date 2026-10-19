using System;
using Prima.VinaCAD.EditorInput;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Helper.Helper
{
    public class OffsetDuctJig : DrawJig
    {
        private readonly OffsetDuctModel _model;
        private readonly Matrix3d _ucsToWcs;
        private Point3d _point;

        public Vector3d Direction { get; private set; } = Vector3d.XAxis;

        public OffsetDuctJig(OffsetDuctModel model, Matrix3d ucsToWcs)
        {
            _model = model;
            _ucsToWcs = ucsToWcs;
            _point = model.StartPoint;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opts = new JigPromptPointOptions(
                "\nChọn hướng: ")
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

            Point3d[] p = OffsetDuctHelper.GetPoints(
                _model.StartPoint, Direction,
                _model.Width1, _model.Width2, _model.Offset, _model.Length);

            draw.SubEntityTraits.Color = 3;
            WorldGeometry g = draw.Geometry;
            for (int i = 0; i < p.Length; i++)
                g.WorldLine(p[i].TransformBy(_ucsToWcs), p[(i + 1) % p.Length].TransformBy(_ucsToWcs));

            return true;
        }
    }
}
