using System;
using System.Collections.Generic;
using System.Linq;

namespace CadBatch.Core
{
    public readonly struct Point2
    {
        public Point2(double x, double y) { X = x; Y = y; }
        public double X { get; }
        public double Y { get; }
        public static Point2 operator +(Point2 a, Point2 b) => new Point2(a.X + b.X, a.Y + b.Y);
        public static Point2 operator -(Point2 a, Point2 b) => new Point2(a.X - b.X, a.Y - b.Y);
        public static Point2 operator *(Point2 a, double s) => new Point2(a.X * s, a.Y * s);
        public double Length => Math.Sqrt(X * X + Y * Y);
        public static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
        public static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
        public double Distance(Point2 b) => (this - b).Length;
        public bool IsFinite => !double.IsNaN(X) && !double.IsInfinity(X) && !double.IsNaN(Y) && !double.IsInfinity(Y);
    }

    public sealed class Rectangle2
    {
        private Rectangle2(Point2[] corners)
        {
            Corners = corners;
            Width = (corners[1] - corners[0]).Length;
            Height = (corners[2] - corners[1]).Length;
            Center = (corners[0] + corners[2]) * 0.5;
            Angle = Math.Atan2(corners[1].Y - corners[0].Y, corners[1].X - corners[0].X);
        }
        public Point2[] Corners { get; }
        public double Width { get; }
        public double Height { get; }
        public Point2 Center { get; }
        public double Angle { get; }

        public static bool TryCreate(IEnumerable<Point2> vertices, double tolerance, out Rectangle2? rectangle)
        {
            rectangle = null;
            if (tolerance <= 0 || double.IsNaN(tolerance) || double.IsInfinity(tolerance)) return false;
            var points = new List<Point2>();
            foreach (var p in vertices)
            {
                if (!p.IsFinite) return false;
                if (points.Count == 0 || p.Distance(points[points.Count - 1]) > tolerance) points.Add(p);
            }
            if (points.Count > 1 && points[0].Distance(points[points.Count - 1]) <= tolerance) points.RemoveAt(points.Count - 1);
            bool changed = true;
            while (changed && points.Count > 4)
            {
                changed = false;
                for (int i = 0; i < points.Count; i++)
                {
                    var a = points[(i + points.Count - 1) % points.Count];
                    var b = points[i];
                    var c = points[(i + 1) % points.Count];
                    var ac = c - a;
                    if (ac.Length > tolerance && Math.Abs(Point2.Cross(b - a, ac)) / ac.Length <= tolerance &&
                        Point2.Dot(b - a, c - b) >= 0)
                    { points.RemoveAt(i); changed = true; break; }
                }
            }
            if (points.Count != 4) return false;
            var edges = Enumerable.Range(0, 4).Select(i => points[(i + 1) % 4] - points[i]).ToArray();
            if (edges.Any(e => e.Length <= tolerance)) return false;
            // Every consecutive edge must be perpendicular, opposite edges must close.
            for (int i = 0; i < 4; i++)
            {
                var a = edges[i]; var b = edges[(i + 1) % 4];
                if (Math.Abs(Point2.Dot(a, b)) > Math.Max(1e-8 * a.Length * b.Length, tolerance * Math.Min(a.Length, b.Length))) return false;
            }
            if ((edges[0] + edges[2]).Length > tolerance * 2 || (edges[1] + edges[3]).Length > tolerance * 2) return false;
            if (Math.Abs(Point2.Cross(edges[0], edges[1])) < tolerance * tolerance) return false;
            // Normalize winding and starting corner without discarding the actual rotation.
            if (Point2.Cross(edges[0], edges[1]) < 0) points.Reverse();
            int start = 0;
            for (int i = 1; i < 4; i++)
                if (points[i].Y < points[start].Y - tolerance ||
                    (Math.Abs(points[i].Y - points[start].Y) <= tolerance && points[i].X < points[start].X)) start = i;
            rectangle = new Rectangle2(Enumerable.Range(0, 4).Select(i => points[(start + i) % 4]).ToArray());
            return true;
        }

        public bool SameBoundary(Rectangle2 other, double tolerance) =>
            Center.Distance(other.Center) <= tolerance && Corners.All(a => other.Corners.Any(b => a.Distance(b) <= tolerance));
    }
}
