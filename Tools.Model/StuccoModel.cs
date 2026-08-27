using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace Tools.Model
{
    public static class StuccoModel
    {
        public readonly struct WallEdge
        {
            public WallEdge(
                ObjectId layerId,
                Line geometry,
                bool isClosedBoundary)
            {
                LayerId = layerId;
                Geometry = geometry;
                IsClosedBoundary = isClosedBoundary;
            }

            public ObjectId LayerId { get; }
            public Line Geometry { get; }
            public bool IsClosedBoundary { get; }
        }
    }
}
