using Prima.VinaCAD.EditorInput;
using System;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper.Helper
{
    /// Xem trước chi tiết giảm/tăng khi quay hướng quanh điểm bắt đầu (tâm quay).
    public class ReducerJig : DrawJig
    {
        private readonly ReducerModel _model;
        private readonly Matrix3d _ucsToWcs;
        private readonly Func<bool> _isOrtho;
        private Point3d _lastCursor;

        public Vector3d Direction { get; private set; } = Vector3d.XAxis;

        public ReducerJig(ReducerModel model, Matrix3d ucsToWcs, Func<bool> isOrtho)
        {
            _model = model;
            _ucsToWcs = ucsToWcs;
            _isOrtho = isOrtho;
            _model.Direction = Direction;   // hiện sẵn tư thế gốc (hướng +X)
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions("\nChọn hướng cho chi tiết: ")
            {
                BasePoint = _model.StartPoint,
                UseBasePoint = true,
                UserInputControls = UserInputControls.GovernedByOrthoMode
            };

            PromptPointResult res = prompts.AcquirePoint(opts);
            if (res.Status == PromptStatus.Cancel) return SamplerStatus.Cancel;
            if (res.Status != PromptStatus.OK) return SamplerStatus.NoChange;

            Point3d cursor = res.Value;
            if (cursor.DistanceTo(_lastCursor) < 1e-9) return SamplerStatus.NoChange;
            _lastCursor = cursor;

            if (!ReducerHelper.TryGetDirection(
                    _model.StartPoint, cursor, _isOrtho(), out Vector3d dir))
                return SamplerStatus.NoChange;   // trùng tâm quay: giữ hướng cũ

            Direction = dir;
            _model.Direction = dir;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            Point3d[] c = ReducerHelper.GetCorners(_model);

            var pts = new Point3dCollection();
            foreach (Point3d p in c)
                pts.Add(p.TransformBy(_ucsToWcs));

            draw.Geometry.Polygon(pts);
            return true;
        }
    }
}