using System;

namespace CadBatch.Core
{
    public static class OutlineGeometry
    {
        // An open polyline drawn back to its start point is a closed outline. The gap must be tiny compared with the
        // first side, so a nearly closed sketch is not taken for a frame.
        public static bool EndsAtStart(Point2 first,Point2 last,Point2 second)
        {
            double side=(second-first).Length;
            return side>0&&(last-first).Length<=Math.Max(1e-9,side*1e-5);
        }
    }
}
