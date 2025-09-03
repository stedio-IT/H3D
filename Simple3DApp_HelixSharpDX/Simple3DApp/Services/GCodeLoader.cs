using Hx = HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using Color = SharpDX.Color;
using HelixToolkit.Wpf.SharpDX;

namespace Simple3DApp.Services
{
    public class GCodeSegment
    {
        public Vector3 Start;
        public Vector3 End;
        public bool IsExtrusion;
        public float Feed; // mm/min
        public float EDelta;
        public float Z;
        public int LayerIndex;
        public string Tag; // libero: es. "WALL-OUTER", "TRAVEL", ecc.
    }

    public enum GCodeColorMode
    {
        ByType,   // estrusione vs travel
        BySpeed,  // gradienti per feed rate
        ByTag     // per Tag (se presente), fallback a ByType
    }

    public static class GCodeLoader
    {
        private static readonly CultureInfo inv = CultureInfo.InvariantCulture;

        public static IEnumerable<GCodeSegment> ParseStream(string path)
        {
            using (var sr = new StreamReader(File.OpenRead(path), Encoding.ASCII, true, 1 << 16))
            {
                bool absoluteXYZ = true; // G90/G91
                bool absoluteE = true;   // M82/M83
                float x = 0, y = 0, z = 0, e = 0, f = 0;
                int layer = 0;
                string currentTag = string.Empty;

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string raw = line;
                    int sc = raw.IndexOf(';');
                    string comment = sc >= 0 ? raw.Substring(sc + 1).Trim() : string.Empty;
                    string code = sc >= 0 ? raw.Substring(0, sc) : raw;

                    if (!string.IsNullOrWhiteSpace(comment))
                    {
                        // Heuristic: layer markers from various slicers
                        if (comment.StartsWith("LAYER:", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(comment.Substring(6).Trim(), out layer);
                        }
                        else if (comment.StartsWith("Z:", StringComparison.OrdinalIgnoreCase))
                        {
                            float zc;
                            if (float.TryParse(comment.Substring(2).Trim(), NumberStyles.Float, inv, out zc)) z = zc;
                        }
                        else if (comment.StartsWith("TYPE:", StringComparison.OrdinalIgnoreCase))
                        {
                            currentTag = comment.Substring(5).Trim();
                        }
                        else if (comment.IndexOf("retract", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            currentTag = "RETRACT";
                        }
                    }

                    var tokens = code.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0) continue;

                    string cmd = tokens[0].ToUpperInvariant();
                    if (cmd == "G20") { /* inches (unsupported, assume mm)*/ }
                    else if (cmd == "G21") { /* mm */ }
                    else if (cmd == "G90") { absoluteXYZ = true; }
                    else if (cmd == "G91") { absoluteXYZ = false; }
                    else if (cmd == "M82") { absoluteE = true; }
                    else if (cmd == "M83") { absoluteE = false; }
                    else if (cmd == "G0" || cmd == "G1")
                    {
                        float nx = x, ny = y, nz = z, ne = e, nf = f;
                        for (int i = 1; i < tokens.Length; i++)
                        {
                            string t = tokens[i];
                            if (t.Length < 2) continue;
                            char p = char.ToUpperInvariant(t[0]);
                            string vs = t.Substring(1);
                            float v;
                            if (!float.TryParse(vs, NumberStyles.Float, inv, out v)) continue;
                            switch (p)
                            {
                                case 'X': nx = absoluteXYZ ? v : (x + v); break;
                                case 'Y': ny = absoluteXYZ ? v : (y + v); break;
                                case 'Z': nz = absoluteXYZ ? v : (z + v); break;
                                case 'E': ne = absoluteE ? v : (e + v); break;
                                case 'F': nf = v; break;
                            }
                        }

                        bool hasMove = (nx != x) || (ny != y) || (nz != z);
                        float de = ne - e;
                        bool isExtrude = de > 1e-6f; // piccolo epsilon

                        if (hasMove)
                        {
                            // costruiamo segmento
                            var seg = new GCodeSegment
                            {
                                Start = new Vector3(x, y, z),
                                End = new Vector3(nx, ny, nz),
                                IsExtrusion = isExtrude,
                                Feed = nf != 0 ? nf : f,
                                EDelta = de,
                                Z = nz,
                                LayerIndex = layer,
                                Tag = !string.IsNullOrEmpty(currentTag) ? currentTag : (isExtrude ? "EXTRUDE" : "TRAVEL")
                            };
                            yield return seg;
                        }

                        x = nx; y = ny; z = nz; e = ne; f = nf != 0 ? nf : f;
                    }
                }
            }
        }

        public static List<Models.LineItem> BuildLineGeometry(string path, GCodeColorMode mode, int speedBins = 8)
        {
            // Bucket approach → un LineItem per colore/bucket, per ridurre numero di modelli
            var buckets = new List<Bucket>();
            if (mode == GCodeColorMode.ByType)
            {
                buckets.Add(new Bucket("TRAVEL", new Color4(0.4f, 0.6f, 0.9f, 1f), 0.6));
                buckets.Add(new Bucket("EXTRUDE", new Color4(1.0f, 0.4f, 0.2f, 1f), 1.2));
            }

            float fMin = float.MaxValue, fMax = float.MinValue;
            if (mode == GCodeColorMode.BySpeed)
            {
                // Prima passata per range feed
                foreach (var seg in ParseStream(path))
                {
                    if (seg.Feed > 0)
                    {
                        if (seg.Feed < fMin) fMin = seg.Feed;
                        if (seg.Feed > fMax) fMax = seg.Feed;
                    }
                }
                if (fMin == float.MaxValue) { fMin = 0; fMax = 1; }
                // crea bin
                for (int i = 0; i < speedBins; i++)
                {
                    float t = (speedBins == 1) ? 0f : (float)i / (speedBins - 1);
                    var col = HsvToRgb(240f - 240f * t, 0.9f, 0.95f); // blu→rosso
                    buckets.Add(new Bucket($"SPEED_{i}", col, 1.0 + 0.2 * t)
                    {
                        SpeedMin = fMin + (fMax - fMin) * i / speedBins,
                        SpeedMax = fMin + (fMax - fMin) * (i + 1) / speedBins
                    });
                }
            }

            if (mode == GCodeColorMode.ByTag)
            {
                // Costruire bucket dinamicamente mano a mano (tag visti) + default
                buckets.Add(new Bucket("__DEFAULT_TRAVEL__", new Color4(0.5f, 0.5f, 0.6f, 1f), 0.7));
                buckets.Add(new Bucket("__DEFAULT_EXTRUDE__", new Color4(0.9f, 0.6f, 0.2f,1f), 1.2));
            }

            // Line builders per bucket
            foreach (var b in buckets) b.Builder = new Hx.LineBuilder();

            var dynamicBuckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);

            foreach (var seg in ParseStream(path))
            {
                Bucket bucket = null;
                if (mode == GCodeColorMode.ByType)
                {
                    bucket = seg.IsExtrusion ? buckets[1] : buckets[0];
                }
                else if (mode == GCodeColorMode.BySpeed)
                {
                    var f = seg.Feed;
                    if (f <= 0) f = (fMin + fMax) * 0.5f;
                    for (int i = 0; i < buckets.Count; i++)
                    {
                        var bk = buckets[i];
                        if (bk.Name.StartsWith("SPEED_", StringComparison.OrdinalIgnoreCase))
                        {
                            if (f >= bk.SpeedMin && f < bk.SpeedMax) { bucket = bk; break; }
                        }
                    }
                    if (bucket == null) bucket = buckets[buckets.Count - 1];
                }
                else // ByTag
                {
                    string tag = string.IsNullOrEmpty(seg.Tag) ? (seg.IsExtrusion ? "__DEFAULT_EXTRUDE__" : "__DEFAULT_TRAVEL__") : seg.Tag;
                    if (!dynamicBuckets.TryGetValue(tag, out bucket))
                    {
                        // Colore generato deterministicamente dal tag
                        var col = HashColor(tag);
                        bucket = new Bucket(tag, col, seg.IsExtrusion ? 1.2 : 0.7);
                        bucket.Builder = new Hx.LineBuilder();
                        dynamicBuckets[tag] = bucket;
                    }
                }

                if (bucket.Builder == null) bucket.Builder = new Hx.LineBuilder();
                bucket.Builder.AddLine(seg.Start, seg.End);
            }

            var items = new List<Models.LineItem>();
            // Buckets statici
            foreach (var b in buckets)
            {
                if (b.Builder == null) continue;
                var geo = b.Builder.ToLineGeometry3D();
                if (geo == null || geo.Indices == null || geo.Indices.Count == 0) continue;
                items.Add(new Models.LineItem
                {
                    Name = b.Name,
                    Geometry = geo,
                    Color = b.Color.ToColor(),
                    Thickness = b.Thickness
                });
            }
            // Buckets dinamici (ByTag)
            foreach (var kv in dynamicBuckets)
            {
                var b = kv.Value;
                if (b.Builder == null) continue;
                var geo = b.Builder.ToLineGeometry3D();
                if (geo == null || geo.Indices == null || geo.Indices.Count == 0) continue;
                items.Add(new Models.LineItem
                {
                    Name = b.Name,
                    Geometry = geo,
                    Color = b.Color.ToColor(),
                    Thickness = b.Thickness
                });
            }

            return items;
        }

        private class Bucket
        {
            public string Name;
            public Color4 Color;
            public double Thickness;
            public float SpeedMin;
            public float SpeedMax;
            public Hx.LineBuilder Builder;

            public Bucket(string name, Color4 color, double thickness)
            {
                Name = name; Color = color; Thickness = thickness;
            }
        }

        private static Color4 HashColor(string s)
        {
            unchecked
            {
                int h = 23;
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                float hue = Math.Abs(h % 360);
                return HsvToRgb(hue, 0.8f, 0.95f);
            }
        }

        // HSV→RGB (0..360, 0..1, 0..1) → Color4
        private static Color4 HsvToRgb(float h, float s, float v)
        {
            float c = v * s;
            float x = c * (1 - Math.Abs(((h / 60f) % 2) - 1));
            float m = v - c;
            float r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return new Color4(r + m, g + m, b + m, 1f);
        }
    }
}