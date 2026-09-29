using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace YoridoriModifiers.OutlineExtender
{
    /// <summary>
    /// Euclidean distance to a marching-squares cutoff contour, with interpolated edge
    /// crossings (not alpha intensity or a chamfer/Manhattan distance approximation).
    /// A bounded contour raster avoids per-pixel global nearest-neighbor searches.
    /// </summary>
    internal static class OutlineSdfGenerator
    {
        private struct Segment
        {
            public Vector2 a, b;
            public Vector2 Min => Vector2.Min(a, b);
            public Vector2 Max => Vector2.Max(a, b);
        }

        // A bounded distance field: this is storage range, NOT the runtime line width.
        // Rasterize exact contour distances only where representable; never query a
        // recursive BVH nine times for every texel of every mip.
        public const float DistanceRange = 8f;

        public static float[] Generate(Color[] pixels, int width, int height, float cutoff, bool repeatU, bool repeatV,
            Action<float> progress = null, float range = DistanceRange, byte[] uvCoverage = null)
        {
            if (pixels == null || pixels.Length != width * height || width < 1 || height < 1)
                throw new ArgumentException("Invalid SDF source dimensions.");
            if (uvCoverage != null && uvCoverage.Length != pixels.Length)
                throw new ArgumentException("Invalid UV coverage dimensions.");
            var distances = new float[pixels.Length];
            for (var i = 0; i < distances.Length; i++) distances[i] = range * range;
            var segments = BuildContour(pixels, width, height, cutoff, repeatU, repeatV, progress, uvCoverage);
            var rasterSegments = new List<Segment>(segments.Count);
            for (var i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                rasterSegments.Add(s);
                // Only seams need periodic copies, not the entire texture.
                var left = repeatU && s.Min.x < range;
                var right = repeatU && s.Max.x > width - 1 - range;
                var bottom = repeatV && s.Min.y < range;
                var top = repeatV && s.Max.y > height - 1 - range;
                for (var ty = bottom ? -1 : 0; ty <= (top ? 1 : 0); ty++)
                for (var tx = left ? -1 : 0; tx <= (right ? 1 : 0); tx++)
                    if (tx != 0 || ty != 0) rasterSegments.Add(Offset(s, -tx * width, -ty * height));
            }
            progress?.Invoke(0.3f);
            var workers = Math.Max(1, Math.Min(Environment.ProcessorCount, height));
            Parallel.For(0, workers, worker =>
            {
                var firstRow = height * worker / workers;
                var endRow = height * (worker + 1) / workers;
                foreach (var segment in rasterSegments)
                    RasterizeRows(segment, distances, width, height, firstRow, endRow, range, uvCoverage);
            });
            progress?.Invoke(0.9f);
            Parallel.For(0, height, y =>
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    var i = row + x;
                    distances[i] = 0.5f + (pixels[i].a >= cutoff ? 1 : -1) * (float)Math.Sqrt(distances[i]);
                }
            });
            progress?.Invoke(1);
            return distances;
        }

        private static Segment Offset(Segment s, int x, int y)
        {
            var offset = new Vector2(x, y);
            return new Segment { a = s.a + offset, b = s.b + offset };
        }

        private static void RasterizeRows(Segment s, float[] distances, int width, int height,
            int firstRow, int endRow, float range, byte[] uvCoverage)
        {
            var ax = s.a.x; var ay = s.a.y;
            var bx = s.b.x; var by = s.b.y;
            var dx = bx - ax; var dy = by - ay;
            var invLength = 1f / Math.Max(dx * dx + dy * dy, 1e-20f);
            var segmentMinX = Math.Min(ax, bx); var segmentMaxX = Math.Max(ax, bx);
            var segmentMinY = Math.Min(ay, by); var segmentMaxY = Math.Max(ay, by);
            var minX = Math.Max(0, (int)Math.Ceiling(segmentMinX - range));
            var maxX = Math.Min(width - 1, (int)Math.Floor(segmentMaxX + range));
            var minY = Math.Max(firstRow, (int)Math.Ceiling(segmentMinY - range));
            var maxY = Math.Min(endRow - 1, (int)Math.Floor(segmentMaxY + range));
            if (minX > maxX || minY > maxY) return;
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var index = y * width + x;
                if (uvCoverage != null && uvCoverage[index] == 0) continue;
                var px = x - ax; var py = y - ay;
                var t = Math.Max(0f, Math.Min(1f, (px * dx + py * dy) * invLength));
                var ex = px - t * dx; var ey = py - t * dy;
                var d = ex * ex + ey * ey;
                if (d < distances[index]) distances[index] = d;
            }
        }

        private static List<Segment> BuildContour(Color[] pixels, int width, int height, float cutoff, bool repeatU,
            bool repeatV, Action<float> progress, byte[] uvCoverage)
        {
            var rows = new List<Segment>[height + 1];
            Parallel.For(0, rows.Length, rowIndex =>
            {
                var y = rowIndex - 1;
                var segments = new List<Segment>();
                BuildContourRow(pixels, width, height, cutoff, repeatU, repeatV, y, segments, uvCoverage);
                rows[rowIndex] = segments;
            });
            progress?.Invoke(0.25f);
            var result = new List<Segment>();
            foreach (var row in rows) result.AddRange(row);
            return result;
        }

        private static void BuildContourRow(Color[] pixels, int width, int height, float cutoff,
            bool repeatU, bool repeatV, int y, List<Segment> segments, byte[] uvCoverage)
        {
            var corners = new Vector2[4];
            var values = new float[4];
            var crossings = new Vector2[4];
            var edges = new int[4];
            // One exterior cell supports Clamp at edges, including 1 x N textures.
            for (var x = -1; x < width; x++)
            {
                corners[0] = new Vector2(x, y);
                corners[1] = new Vector2(x + 1, y);
                corners[2] = new Vector2(x + 1, y + 1);
                corners[3] = new Vector2(x, y + 1);
                for (var c = 0; c < 4; c++)
                {
                    var px = Address((int)corners[c].x, width, repeatU);
                    var py = Address((int)corners[c].y, height, repeatV);
                    values[c] = pixels[py * width + px].a - cutoff;
                }
                if ((values[0] >= 0) == (values[1] >= 0)
                    && (values[0] >= 0) == (values[2] >= 0)
                    && (values[0] >= 0) == (values[3] >= 0)) continue;
                var count = 0;
                for (var c = 0; c < 4; c++)
                {
                    var next = (c + 1) % 4;
                    if ((values[c] >= 0) == (values[next] >= 0)) continue;
                    crossings[c] = Vector2.Lerp(corners[c], corners[next], values[c] / (values[c] - values[next]));
                    edges[count++] = c;
                }
                if (count == 2)
                    AddCovered(segments, new Segment { a = crossings[edges[0]], b = crossings[edges[1]] },
                        uvCoverage, width, height, repeatU, repeatV);
                else if (count == 4)
                {
                    // Asymptotic decider for the ambiguous bilinear saddle cell.
                    var connect02 = values[0] * values[2] - values[1] * values[3] >= 0;
                    AddCovered(segments, new Segment { a = crossings[0], b = crossings[connect02 ? 1 : 3] },
                        uvCoverage, width, height, repeatU, repeatV);
                    AddCovered(segments, new Segment { a = crossings[2], b = crossings[connect02 ? 3 : 1] },
                        uvCoverage, width, height, repeatU, repeatV);
                }
            }
        }

        private static void AddCovered(List<Segment> segments, Segment segment, byte[] uvCoverage,
            int width, int height, bool repeatU, bool repeatV)
        {
            if (uvCoverage == null || Covered(segment.a) || Covered(segment.b) || Covered((segment.a + segment.b) * 0.5f))
                segments.Add(segment);

            bool Covered(Vector2 point)
            {
                var x = Address(Mathf.RoundToInt(point.x), width, repeatU);
                var y = Address(Mathf.RoundToInt(point.y), height, repeatV);
                return uvCoverage[y * width + x] != 0;
            }
        }

        private static int Address(int value, int size, bool repeat) => repeat
            ? (value % size + size) % size : Math.Max(0, Math.Min(value, size - 1));

    }
}
