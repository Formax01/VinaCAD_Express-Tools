using Prima.VinaCAD.EditorInput;
using System;
using System.Globalization;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.VinaCad.Helper.Helper;

namespace Tools.VinaCad.Helper
{
    public class DuctSideJig : DrawJig
    {
        private readonly Point3d _p1;
        private readonly Point3d _p2;
        private readonly Vector3d _normal;
        private double _offset;          // có dấu: >0 bên trái P1->P2, <0 bên phải
        private double _lastSign = 1.0;

        public double Offset => _offset;

        public DuctSideJig(Point3d p1, Point3d p2)
        {
            _p1 = p1;
            _p2 = p2;
            _normal = (p2 - p1).GetNormal().RotateBy(Math.PI / 2, Vector3d.ZAxis);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opts = new JigPromptPointOptions(
    "\nKéo vuông góc và click, hoặc nhập chiều rộng: ")
            {
                BasePoint = _p2,
                UseBasePoint = true,
                Cursor = CursorType.RubberBand,
                UserInputControls = UserInputControls.AcceptOtherInputString
            };

            PromptPointResult res = prompts.AcquirePoint(opts);

            if (res.Status == PromptStatus.OK)
            {
                double off = (res.Value - _p1).DotProduct(_normal);
                if (Math.Abs(off) < 1e-9) return SamplerStatus.NoChange;
                if (Math.Abs(off - _offset) < 1e-9) return SamplerStatus.NoChange;

                _offset = off;
                _lastSign = off >= 0 ? 1.0 : -1.0;
                return SamplerStatus.OK;
            }

            // Người dùng gõ số: dùng làm chiều rộng, giữ phía đang ướm
            if (res.Status == PromptStatus.Keyword || res.Status == PromptStatus.Other)
            {
                string s = (res.StringResult ?? "").Trim().Replace(',', '.');
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double w)
                    && w > 1e-9)
                {
                    _offset = _lastSign * w;
                    return SamplerStatus.OK;
                }
                return SamplerStatus.NoChange;
            }

            return SamplerStatus.Cancel;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            if (Math.Abs(_offset) < 1e-9)
            {
                draw.Geometry.WorldLine(_p1, _p2);
                return true;
            }

            Point3d[] c = DuctHelper.GetCornersFromSide(_p1, _p2, _offset);
            draw.SubEntityTraits.Color = 3;
            WorldGeometry g = draw.Geometry;
            g.WorldLine(c[0], c[1]);
            g.WorldLine(c[1], c[2]);
            g.WorldLine(c[2], c[3]);
            g.WorldLine(c[3], c[0]);
            return true;
        }
    }
}