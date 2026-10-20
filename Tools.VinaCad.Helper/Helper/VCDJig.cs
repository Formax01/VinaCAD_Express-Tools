using System;
using System.Collections.Generic;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.VinaCad.Helper.Helper;

namespace Tools.VinaCad.Action.Actions
{
    /// <summary>
    /// Ướm hình VCD: tâm xoay là góc dưới trái, hướng xoay theo con trỏ.
    /// </summary>
    public class VCDJig : DrawJig
    {
        private readonly Database _db;
        private readonly Point3d _origin;   // toạ độ UCS
        private readonly double _length;
        private readonly double _width;
        private readonly Matrix3d _ucs;
        private Point3d _lastPoint;

        public double Rotation { get; private set; }   // radian, so với trục X của UCS

        public VCDJig(Database db, Point3d origin, double length, double width, Matrix3d ucs)
        {
            _db = db;
            _origin = origin;
            _length = length;
            _width = width;
            _ucs = ucs;
            _lastPoint = origin;
            Rotation = 0;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opt =
                new JigPromptPointOptions("\nChọn hướng xoay : ")
                {
                    BasePoint = _origin,
                    UseBasePoint = true,
                    Cursor = CursorType.RubberBand,
                    UserInputControls = UserInputControls.Accept3dCoordinates
                };

            PromptPointResult res = prompts.AcquirePoint(opt);

            if (res.Status != PromptStatus.OK)
                return SamplerStatus.Cancel;

            Point3d p = res.Value;

            if (p.DistanceTo(_lastPoint) < 1e-9)
                return SamplerStatus.NoChange;

            _lastPoint = p;

            double dx = p.X - _origin.X;
            double dy = p.Y - _origin.Y;

            if (Math.Abs(dx) > 1e-9 || Math.Abs(dy) > 1e-9)
                Rotation = Math.Atan2(dy, dx) - Math.PI / 2.0; // con trỏ chỉ theo hướng chiều dài (trục Y của hình)

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            Matrix3d xform = _ucs * Matrix3d.Rotation(Rotation, Vector3d.ZAxis, _origin);

            List<Entity> ents = VCDHelper.BuildPreviewEntities(_db, _origin, _length, _width);

            foreach (Entity e in ents)
            {
                e.TransformBy(xform);
                draw.Geometry.Draw(e);
                e.Dispose();
            }

            return true;
        }
    }
}