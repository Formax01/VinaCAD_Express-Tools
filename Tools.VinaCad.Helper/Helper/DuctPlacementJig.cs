
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using Lang = Tools.Resources.Language.Lang;
namespace Tools.VinaCad.Helper
{
    public class DuctPlacementJig : DrawJig
    {
        private readonly DuctModel _model;
        private readonly Matrix3d _ucsToWcs;
        private Point3d _point;

        public Point3d Point => _point;

        public Vector3d Direction { get; private set; }
            = Vector3d.XAxis;

        public DuctPlacementJig(DuctModel model, Matrix3d ucsToWcs)
        {
            _model = model;
            _ucsToWcs = ucsToWcs;
            _point = model.StartPoint;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opts =
                new JigPromptPointOptions(
                    Lang.B1_UI_ChonHuongDatOngGio)
                {
                    BasePoint = _model.StartPoint,
                    UseBasePoint = true,
                    Cursor = CursorType.RubberBand
                };

            PromptPointResult res = prompts.AcquirePoint(opts);

            if (res.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;

            if (res.Value.DistanceTo(_point) < 1e-6)
                return SamplerStatus.NoChange;

            _point = res.Value;

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            object o = Application.GetSystemVariable("ORTHOMODE");

            bool ortho =
                o != null && Convert.ToInt32(o) == 1;

            // Cập nhật hướng dựa trên vị trí chuột
            Direction = DuctHelper.GetDirection(
                _model.StartPoint,
                _point,
                ortho);

            // Tính bốn góc với StartPoint là góc neo
            Point3d[] corners = DuctHelper.GetCorners(
                _model.StartPoint,
                Direction,
                _model.Length,
                _model.Width);

            draw.SubEntityTraits.Color = 3;

            WorldGeometry g = draw.Geometry;

            g.WorldLine(corners[0].TransformBy(_ucsToWcs), corners[1].TransformBy(_ucsToWcs));
            g.WorldLine(corners[1].TransformBy(_ucsToWcs), corners[2].TransformBy(_ucsToWcs));
            g.WorldLine(corners[2].TransformBy(_ucsToWcs), corners[3].TransformBy(_ucsToWcs));
            g.WorldLine(corners[3].TransformBy(_ucsToWcs), corners[0].TransformBy(_ucsToWcs));

            return true;
        }
    }
}
