using System.Collections.Generic;
using Hx = HelixToolkit.Wpf.SharpDX;
using SharpDX;
using Simple3DApp.Models;

namespace Simple3DApp.Services
{
    /// <summary>
    /// Costruisce mesh di cilindri/tubi a partire dai LineItem (già prodotti dal parser G-code).
    /// Per ogni LineItem genera una SceneItem con una mesh unica (cylinders merged).
    /// </summary>
    public static class TubesFromLinesBuilder
    {
        public static Dictionary<int, List<SceneItem>> BuildFromLayeredLines(
            Dictionary<int, List<LineItem>> layerToLines,
            float radius = 0.2f, int thetaDiv = 8)
        {
            var result = new Dictionary<int, List<SceneItem>>();
            foreach (var kv in layerToLines)
            {
                var layer = kv.Key;
                var list = new List<SceneItem>();
                foreach (var lineItem in kv.Value)
                {
                    var geo = lineItem.Geometry;
                    if (geo == null || geo.Indices == null || geo.Positions == null) continue;

                    var mb = new Hx.MeshBuilder(true, true, true);
                    // Indices sono coppie: ogni 2 indici una linea (segmento)
                    var pos = geo.Positions;
                    var idx = geo.Indices ?? geo.Indices; // robustness
                    if (idx == null) idx = new Hx.IntCollection();
                    for (int i = 0; i + 1 < idx.Count; i += 2)
                    {
                        var a = pos[idx[i]];
                        var b = pos[idx[i + 1]];
                        var va = new Vector3(a.X, a.Y, a.Z);
                        var vb = new Vector3(b.X, b.Y, b.Z);
                        if ((vb - va).LengthSquared() > 1e-12f)
                            mb.AddCylinder(va, vb, radius, thetaDiv, true, true);
                    }

                    var mesh = mb.ToMeshGeometry3D();
                    var mat = new Hx.PhongMaterial
                    {
                        DiffuseColor = new Color4(lineItem.Color.ScR, lineItem.Color.ScG, lineItem.Color.ScB, lineItem.Color.ScA)
                    };

                    list.Add(new SceneItem
                    {
                        Name = "GCodeTubes",
                        Geometry = mesh,
                        Material = mat,
                        Transform = lineItem.Transform
                    });
                }
                result[layer] = list;
            }
            return result;
        }
    }
}