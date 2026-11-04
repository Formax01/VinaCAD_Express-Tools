using System;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.GraphicsInterface;
using Teigha.Geometry;
using Tools.VinaCad.Helper;
using Tools.Resources.Language;
namespace Tools.VinaCad.Helper.Helper
{
    public class HGJig : DrawJig
    {
        private readonly Matrix3d _ucs;
        private readonly Point3d _startUcs;     // dùng làm BasePoint cho prompt
        private readonly Point3d _startWcs;

        private Point3d _endWcs;

        public Point3d EndWcs => _endWcs;

        public static HGJig ForEndPoint(Point3d startUcs, Matrix3d ucs)
            => new HGJig(startUcs, ucs);

        private HGJig(Point3d startUcs, Matrix3d ucs)
        {
            _ucs = ucs;
            _startUcs = startUcs;
            _startWcs = startUcs.TransformBy(ucs);
            _endWcs = _startWcs;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions po = new JigPromptPointOptions(Lang.HG_UI_CHONDIEMCUOI)
            {
                UseBasePoint = true,
                BasePoint = _startUcs,
                Cursor = CursorType.RubberBand,
                UserInputControls = UserInputControls.Accept3dCoordinates
            };

            PromptPointResult r = prompts.AcquirePoint(po);
            if (r.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;

            Point3d p = r.Value.TransformBy(_ucs);
            if (p.DistanceTo(_endWcs) < 1e-9)
                return SamplerStatus.NoChange;

            _endWcs = p;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            double length, rotation;

            Vector3d d = _endWcs - _startWcs;
            length = d.Length;
            rotation = Math.Atan2(d.Y, d.X);

            // Chưa đủ dài: chỉ vẽ đường rubber-band
            if (length < HGHelper.LineLength - 1e-9)
            {
                Line tmp = new Line(_startWcs, _endWcs);
                draw.Geometry.Draw(tmp);
                tmp.Dispose();
                return true;
            }

            // Xoay trước rồi dời về điểm đầu (a * b: áp dụng b trước)
            Matrix3d xf =
                Matrix3d.Displacement(_startWcs.GetAsVector()) *
                Matrix3d.Rotation(rotation, Vector3d.ZAxis, Point3d.Origin);

            foreach (Entity ent in HGHelper.BuildEntities(length))
            {
                ent.TransformBy(xf);
                draw.Geometry.Draw(ent);
                ent.Dispose();
            }
            return true;
        }
    }
}