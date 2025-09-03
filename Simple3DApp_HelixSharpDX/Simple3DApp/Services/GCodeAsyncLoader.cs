using Hx = HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Media;
using HelixToolkit.Wpf.SharpDX;

namespace Simple3DApp.Services
{
    public class GCodeProgress
    {
        public long BytesRead;
        public long TotalBytes;
        public int LayersParsed;
        public int SegmentsParsed;
        public int Pass; // 1 or 2 when BySpeed needs two passes
    }

    public class GCodeBuildResult
    {
        public Dictionary<int, List<Models.LineItem>> LayerToItems = new Dictionary<int, List<Models.LineItem>>();
        public int MaxLayer;
    }

    public static class GCodeAsyncLoader
    {
        private static readonly CultureInfo inv = CultureInfo.InvariantCulture;

        public static System.Threading.Tasks.Task<GCodeBuildResult> BuildLayeredAsync(
            string path,
            GCodeColorMode mode,
            int speedBins,
            IProgress<GCodeProgress> progress,
            CancellationToken token)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                var fi = new FileInfo(path);
                long total = fi.Length;
                float fMin = float.MaxValue, fMax = float.MinValue;

                // --- Pass 1: optional scan for BySpeed ---
                if (mode == GCodeColorMode.BySpeed)
                {
                    var pg = new GCodeProgress { TotalBytes = total, Pass = 1 };
                    using (var fs = File.OpenRead(path))
                    using (var sr = new StreamReader(fs, Encoding.ASCII, true, 1 << 16))
                    {
                        bool absoluteXYZ = true, absoluteE = true;
                        float x = 0, y = 0, z = 0, e = 0, f = 0;
                        int layer = 0; // used only in pass2
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            token.ThrowIfCancellationRequested();
                            int sc = line.IndexOf(';');
                            string comment = sc >= 0 ? line.Substring(sc + 1).Trim() : string.Empty;
                            string code = sc >= 0 ? line.Substring(0, sc) : line;

                            var tokens = code.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length == 0) { pg.BytesRead = fs.Position; progress?.Report(pg); continue; }

                            string cmd = tokens[0].ToUpperInvariant();
                            if (cmd == "G90") absoluteXYZ = True();
                            else if (cmd == "G91") absoluteXYZ = False();
                            else if (cmd == "M82") absoluteE = True();
                            else if (cmd == "M83") absoluteE = False();
                            else if (cmd == "G0" || cmd == "G1")
                            {
                                float nx = x, ny = y, nz = z, ne = e, nf = f;
                                for (int i = 1; i < tokens.Length; i++)
                                {
                                    var t = tokens[i];
                                    if (t.Length < 2) continue;
                                    char p = char.ToUpperInvariant(t[0]);
                                    string vs = t.Substring(1);
                                    if (!float.TryParse(vs, NumberStyles.Float, inv, out float v)) continue;
                                    switch (p)
                                    {
                                        case 'X': nx = absoluteXYZ ? v : (x + v); break;
                                        case 'Y': ny = absoluteXYZ ? v : (y + v); break;
                                        case 'Z': nz = absoluteXYZ ? v : (z + v); break;
                                        case 'E': ne = absoluteE ? v : (e + v); break;
                                        case 'F': nf = v; break;
                                    }
                                }
                                if (nf > 0)
                                {
                                    if (nf < fMin) fMin = nf;
                                    if (nf > fMax) fMax = nf;
                                }
                                x = nx; y = ny; z = nz; e = ne; f = nf != 0 ? nf : f;
                            }

                            pg.BytesRead = fs.Position;
                            progress?.Report(pg);
                        }
                    }
                    if (fMin == float.MaxValue) { fMin = 0; fMax = 1; }
                }

                // --- Pass 2: build layered geometries ---
                var result = new GCodeBuildResult();
                var bucketsPerLayer = new Dictionary<(int layer, string name), Bucket>();
                var dynamicBucketsPerLayer = new Dictionary<(int layer, string tag), Bucket>();
                var pg2 = new GCodeProgress { TotalBytes = total, Pass = mode == GCodeColorMode.BySpeed ? 2 : 1 };
                int maxLayer = 0;

                using (var fs2 = File.OpenRead(path))
                using (var sr2 = new StreamReader(fs2, Encoding.ASCII, true, 1 << 16))
                {
                    bool absoluteXYZ = true, absoluteE = true;
                    float x = 0, y = 0, z = 0, e = 0, f = 0;
                    int layer = 0; // used only in pass2
                    string currentTag = string.Empty;
                    int segCount = 0;

                    // Prepare static buckets templates
                    Func<int, List<Bucket>> mkStaticBuckets = (ly) =>
                    {
                        var list = new List<Bucket>();
                        if (mode == GCodeColorMode.ByType)
                        {
                            list.Add(new Bucket(ly, "TRAVEL", Colors.SteelBlue.ToColor4(), 0.6));
                            list.Add(new Bucket(ly, "EXTRUDE", Colors.Orange.ToColor4(), 1.2));
                        }
                        else if (mode == GCodeColorMode.BySpeed)
                        {
                            for (int i = 0; i < Math.Max(1, speedBins); i++)
                            {
                                float t = (speedBins == 1) ? 0f : (float)i / (speedBins - 1);
                                var col = HsvToRgb(240f - 240f * t, 0.9f, 0.95f);
                                var b = new Bucket(ly, $"SPEED_{i}", col, 0.9 + 0.3 * t) { SpeedBinIndex = i };
                                list.Add(b);
                            }
                        }
                        else if (mode == GCodeColorMode.ByTag)
                        {
                            list.Add(new Bucket(ly, "__DEFAULT_TRAVEL__", ColorFromRgb(0.5f, 0.5f, 0.6f), 0.7));
                            list.Add(new Bucket(ly, "__DEFAULT_EXTRUDE__", ColorFromRgb(0.9f, 0.6f, 0.2f), 1.2));
                        }
                        return list;
                    };

                    // main loop
                    string line;
                    while ((line = sr2.ReadLine()) != null)
                    {
                        token.ThrowIfCancellationRequested();

                        int sc = line.IndexOf(';');
                        string comment = sc >= 0 ? line.Substring(sc + 1).Trim() : string.Empty;
                        string code = sc >= 0 ? line.Substring(0, sc) : line;

                        if (!string.IsNullOrWhiteSpace(comment))
                        {
                            if (comment.StartsWith("LAYER:", StringComparison.OrdinalIgnoreCase))
                            {
                                int.TryParse(comment.Substring(6).Trim(), out layer);
                                if (layer > maxLayer) maxLayer = layer;
                            }
                            else if (comment.StartsWith("Z:", StringComparison.OrdinalIgnoreCase))
                            {
                                if (float.TryParse(comment.Substring(2).Trim(), NumberStyles.Float, inv, out float zc)) z = zc;
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
                        if (tokens.Length == 0) { pg2.BytesRead = fs2.Position; pg2.LayersParsed = maxLayer; pg2.SegmentsParsed = segCount; progress?.Report(pg2); continue; }

                        string cmd = tokens[0].ToUpperInvariant();
                        if (cmd == "G90") absoluteXYZ = True();
                        else if (cmd == "G91") absoluteXYZ = False();
                        else if (cmd == "M82") absoluteE = True();
                        else if (cmd == "M83") absoluteE = False();
                        else if (cmd == "G0" || cmd == "G1")
                        {
                            float nx = x, ny = y, nz = z, ne = e, nf = f;
                            for (int i = 1; i < tokens.Length; i++)
                            {
                                var t = tokens[i];
                                if (t.Length < 2) continue;
                                char p = char.ToUpperInvariant(t[0]);
                                string vs = t.Substring(1);
                                if (!float.TryParse(vs, NumberStyles.Float, inv, out float v)) continue;
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
                            bool isExtrude = de > 1e-6f;

                            if (hasMove)
                            {
                                // pick bucket
                                Bucket bucket = null;
                                if (mode == GCodeColorMode.ByType)
                                {
                                    var key = (layer, isExtrude ? "EXTRUDE" : "TRAVEL");
                                    if (!bucketsPerLayer.TryGetValue(key, out bucket))
                                    {
                                        // create layer template if first time
                                        foreach (var b in mkStaticBuckets(layer))
                                            if (!bucketsPerLayer.ContainsKey((layer, b.Name)))
                                                bucketsPerLayer[(layer, b.Name)] = b;
                                        bucket = bucketsPerLayer[key];
                                    }
                                }
                                else if (mode == GCodeColorMode.BySpeed)
                                {
                                    // map feed to bin
                                    float effF = nf != 0 ? nf : f;
                                    if (fMax <= fMin) effF = fMin; // avoid div by zero
                                    int bin = Math.Max(0, Math.Min(speedBins - 1, (int)Math.Floor((effF - fMin) / Math.Max(1e-6f, (fMax - fMin)) * speedBins)));
                                    var name = $"SPEED_{bin}";
                                    var key = (layer, name);
                                    if (!bucketsPerLayer.TryGetValue(key, out bucket))
                                    {
                                        foreach (var b in mkStaticBuckets(layer))
                                            if (!bucketsPerLayer.ContainsKey((layer, b.Name)))
                                                bucketsPerLayer[(layer, b.Name)] = b;
                                        bucket = bucketsPerLayer[key];
                                    }
                                }
                                else // ByTag
                                {
                                    string tag = string.IsNullOrEmpty(currentTag) ? (isExtrude ? "__DEFAULT_EXTRUDE__" : "__DEFAULT_TRAVEL__") : currentTag;
                                    var key = (layer, tag);
                                    if (!dynamicBucketsPerLayer.TryGetValue(key, out bucket))
                                    {
                                        var col = HashColor(tag);
                                        bucket = new Bucket(layer, tag, col, isExtrude ? 1.2 : 0.7);
                                        dynamicBucketsPerLayer[key] = bucket;
                                    }
                                }

                                bucket.Builder.AddLine(new Vector3(x, y, z), new Vector3(nx, ny, nz));
                                segCount++;
                            }

                            x = nx; y = ny; z = nz; e = ne; f = nf != 0 ? nf : f;
                        }

                        pg2.BytesRead = fs2.Position;
                        pg2.LayersParsed = maxLayer;
                        pg2.SegmentsParsed = segCount;
                        progress?.Report(pg2);
                    }
                }

                // convert to models per layer
                foreach (var kv in bucketsPerLayer)
                {
                    var b = kv.Value;
                    var geo = b.Builder.ToLineGeometry3D();
                    if (geo.Indices != null && geo.Indices.Count > 0)
                    {
                        if (!result.LayerToItems.TryGetValue(b.Layer, out var list))
                        {
                            list = new List<Models.LineItem>();
                            result.LayerToItems[b.Layer] = list;
                        }
                        list.Add(new Models.LineItem { Name = b.Name, Geometry = geo, Color = b.Color.ToColor(), Thickness = b.Thickness });
                    }
                }
                foreach (var kv in dynamicBucketsPerLayer)
                {
                    var b = kv.Value;
                    var geo = b.Builder.ToLineGeometry3D();
                    if (geo.Indices != null && geo.Indices.Count > 0)
                    {
                        if (!result.LayerToItems.TryGetValue(b.Layer, out var list))
                        {
                            list = new List<Models.LineItem>();
                            result.LayerToItems[b.Layer] = list;
                        }
                        list.Add(new Models.LineItem { Name = b.Name, Geometry = geo, Color = b.Color.ToColor(), Thickness = b.Thickness });
                    }
                }

                result.MaxLayer = 0;
                foreach (var k in result.LayerToItems.Keys)
                    if (k > result.MaxLayer) result.MaxLayer = k;

                return result;
            }, token);
        }

        private class Bucket
        {
            public int Layer;
            public string Name;
            public Color4 Color;
            public double Thickness;
            public int SpeedBinIndex;
            public Hx.LineBuilder Builder = new Hx.LineBuilder();
            public Bucket(int layer, string name, Color4 color, double thickness)
            {
                Layer = layer; Name = name; Color = color; Thickness = thickness;
            }
        }

        private static Color4 ColorFromRgb(float r, float g, float b)
        {
            byte R = (byte)Math.Round(Math.Max(0, Math.Min(1, r)) * 255.0);
            byte G = (byte)Math.Round(Math.Max(0, Math.Min(1, g)) * 255.0);
            byte B = (byte)Math.Round(Math.Max(0, Math.Min(1, b)) * 255.0);
            return new Color4(R, G, B, 255);
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
            return new Color4 (
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255),
                255);
        }

        // helpers to avoid conditional operator boxing on bool
        private static bool True() => true;
        private static bool False() => false;
    }
}