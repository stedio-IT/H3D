using Hx = HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Simple3DApp.Services
{
    public static class StlLoader
    {
        public static Hx.MeshGeometry3D Load(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                bool isAscii = IsAsciiStl(fs);
                fs.Position = 0;
                return isAscii ? LoadAscii(fs) : LoadBinary(fs);
            }
        }

        private static bool IsAsciiStl(Stream s)
        {
            using (var reader = new StreamReader(s, Encoding.ASCII, true, 1024, true))
            {
                char[] buf = new char[512];
                int n = reader.Read(buf, 0, buf.Length);
                var head = new string(buf, 0, n).TrimStart();
                if (!head.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
                    return false;
                foreach (char c in head.Substring(0, Math.Min(256, head.Length)))
                {
                    if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
                        return false;
                }
                return head.IndexOf("facet", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static Hx.MeshGeometry3D LoadAscii(Stream s)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<int>();

            using (var reader = new StreamReader(s))
            {
                string line;
                Vector3 currentNormal = new Vector3(0, 0, 1);
                var inv = CultureInfo.InvariantCulture;

                while ((line = reader.ReadLine()) != null)
                {
                    var t = line.Trim();
                    if (t.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            float nx, ny, nz;
                            if (float.TryParse(parts[2], NumberStyles.Float, inv, out nx) &&
                                float.TryParse(parts[3], NumberStyles.Float, inv, out ny) &&
                                float.TryParse(parts[4], NumberStyles.Float, inv, out nz))
                            {
                                currentNormal = new Vector3(nx, ny, nz);
                            }
                        }
                    }
                    else if (t.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            float x, y, z;
                            if (float.TryParse(parts[1], NumberStyles.Float, inv, out x) &&
                                float.TryParse(parts[2], NumberStyles.Float, inv, out y) &&
                                float.TryParse(parts[3], NumberStyles.Float, inv, out z))
                            {
                                positions.Add(new Vector3(x, y, z));
                                normals.Add(currentNormal);
                                indices.Add(positions.Count - 1);
                            }
                        }
                    }
                }
            }

            var mesh = new Hx.MeshGeometry3D
            {
                Positions = new Hx.Vector3Collection(positions),
                Normals = new Hx.Vector3Collection(normals),
                Indices = new Hx.IntCollection(indices)
            };
            return mesh;
        }

        private static Hx.MeshGeometry3D LoadBinary(Stream s)
        {
            using (var br = new BinaryReader(s))
            {
                br.ReadBytes(80);
                uint triCount = br.ReadUInt32();

                var positions = new List<Vector3>((int)triCount * 3);
                var normals = new List<Vector3>((int)triCount * 3);
                var indices = new List<int>((int)triCount * 3);

                for (uint i = 0; i < triCount; i++)
                {
                    float nx = br.ReadSingle();
                    float ny = br.ReadSingle();
                    float nz = br.ReadSingle();
                    var n = new Vector3(nx, ny, nz);

                    var v1 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    var v2 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    var v3 = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    br.ReadUInt16();

                    int baseIndex = positions.Count;
                    positions.Add(v1); positions.Add(v2); positions.Add(v3);
                    normals.Add(n); normals.Add(n); normals.Add(n);
                    indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
                }

                var mesh = new Hx.MeshGeometry3D
                {
                    Positions = new Hx.Vector3Collection(positions),
                    Normals = new Hx.Vector3Collection(normals),
                    Indices = new Hx.IntCollection(indices)
                };
                return mesh;
            }
        }
    }
}