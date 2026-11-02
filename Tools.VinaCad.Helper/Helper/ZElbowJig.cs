using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Helper.Helper
{
    public class ZElbowJig : DrawJig
    {
        private readonly ZElbowDuctModel _model;
        private readonly Matrix3d _ucsToWcs;
        private Point3d _point;

        public Vector3d Direction { get; private set; } = Vector3d.XAxis;

        public ZElbowJig(ZElbowDuctModel model, Matrix3d ucsToWcs)
        {
            _model = model;
            _ucsToWcs = ucsToWcs;
            _point = model.StartPoint;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opts = new JigPromptPointOptions(Lang.BZ_UI_ChonHuong)
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

            ZElbowShape? shape = ZElbowHelper.Build(
                _model.StartPoint, Direction,
                _model.Width, _model.Offset, _model.Length);
            if (shape == null) return true;

            draw.SubEntityTraits.Color = 3;
            WorldGeometry g = draw.Geometry;

            int n = shape.Vertices.Length;
            for (int i = 0; i < n; i++)
            {
                List<Point3d> pts = ZElbowHelper.TessellateBulge(
                    shape.Vertices[i], shape.Vertices[(i + 1) % n], shape.Bulges[i], 24);

                for (int j = 0; j < pts.Count - 1; j++)
                    g.WorldLine(pts[j].TransformBy(_ucsToWcs), pts[j + 1].TransformBy(_ucsToWcs));
            }

            foreach (Point3d[] cut in shape.CutLines)
                g.WorldLine(cut[0].TransformBy(_ucsToWcs), cut[1].TransformBy(_ucsToWcs));

            return true;
        }
    }
}
