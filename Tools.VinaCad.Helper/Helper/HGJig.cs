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
        private enum Mode { EndPoint, Angle }

        private readonly Mode _mode;
        private readonly Matrix3d _ucs;
        private readonly double _ucsAngle;      // góc trục X của UCS so với WCS
        private readonly Point3d _startUcs;     // dùng làm BasePoint cho prompt
        private readonly Point3d _startWcs;
        private readonly double _fixedLength;   // dùng ở mode Angle

        private Point3d _endWcs;
        private double _angleWcs;

        public Point3d EndWcs => _endWcs;
        public double AngleWcs => _angleWcs;
        public bool NullResponse { get; private set; }   // người dùng nhấn Enter ở bước góc

        // ---- Bước 1: kéo chọn điểm cuối ----
        public static HGJig ForEndPoint(Point3d startUcs, Matrix3d ucs)
            => new HGJig(Mode.EndPoint, startUcs, ucs, 0, 0);

        // ---- Bước 2: kéo chọn góc (mặc định = hướng đường nối) ----
        public static HGJig ForAngle(Point3d startUcs, Matrix3d ucs, double length, double axisAngleWcs)
            => new HGJig(Mode.Angle, startUcs, ucs, length, axisAngleWcs);

        private HGJig(Mode mode, Point3d startUcs, Matrix3d ucs, double length, double axisAngleWcs)
        {
            _mode = mode;
            _ucs = ucs;
            _startUcs = startUcs;
            _startWcs = startUcs.TransformBy(ucs);
            _fixedLength = length;
            _angleWcs = axisAngleWcs;
            _endWcs = _startWcs;

            Vector3d ux = ucs.CoordinateSystem3d.Xaxis;
            _ucsAngle = Math.Atan2(ux.Y, ux.X);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            if (_mode == Mode.EndPoint)
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
            else
            {
                JigPromptAngleOptions ao = new JigPromptAngleOptions(
                    Lang.HG_UI_NHAPGOCQUAY)
                {
                    UseBasePoint = true,
                    BasePoint = _startUcs,
                    Cursor = CursorType.RubberBand,
                    UserInputControls = UserInputControls.NullResponseAccepted
                };

                PromptDoubleResult r = prompts.AcquireAngle(ao);

                if (r.Status == PromptStatus.None)
                {
                    NullResponse = true;
                    return SamplerStatus.Cancel;        // Action sẽ xử lý như "dùng mặc định"
                }
                if (r.Status != PromptStatus.OK)
                    return SamplerStatus.Cancel;

                double a = r.Value + _ucsAngle;          // UCS -> WCS
                if (Math.Abs(a - _angleWcs) < 1e-9)
                    return SamplerStatus.NoChange;

                _angleWcs = a;
                return SamplerStatus.OK;
            }
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            double length, rotation;

            if (_mode == Mode.EndPoint)
            {
                Vector3d d = _endWcs - _startWcs;
                length = d.Length;
                rotation = Math.Atan2(d.Y, d.X);
            }
            else
            {
                length = _fixedLength;
                rotation = _angleWcs;
            }

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