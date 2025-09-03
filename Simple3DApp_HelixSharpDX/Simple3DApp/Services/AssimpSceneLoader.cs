using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Media3D;
using Hx = HelixToolkit.Wpf.SharpDX;
using SharpDX;
using Assimp;

namespace Simple3DApp.Services
{
    public static class AssimpSceneLoader
    {
        public static IEnumerable<Simple3DApp.Models.SceneItem> LoadAsSceneItems(string path)
        {
            var items = new List<Simple3DApp.Models.SceneItem>();
            var ctx = new AssimpContext();
            var scene = ctx.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices | PostProcessSteps.FlipWindingOrder);

            void Visit(Node node, Matrix4x4 parent)
            {
                var world = node.Transform * parent;
                if (node.HasMeshes)
                {
                    foreach (var mi in node.MeshIndices)
                    {
                        var m = scene.Meshes[mi];
                        if (!m.HasVertices || !m.HasFaces) continue;

                        var positions = new List<Vector3>(m.VertexCount);
                        for (int i = 0; i < m.VertexCount; i++)
                        {
                            var v = m.Vertices[i];
                            var p = Vector3.TransformCoordinate(new Vector3(v.X, v.Y, v.Z), Convert(world));
                            positions.Add(p);
                        }

                        var indices = new List<int>(m.FaceCount * 3);
                        foreach (var f in m.Faces)
                        {
                            if (f.IndexCount == 3)
                            {
                                indices.Add(f.Indices[0]);
                                indices.Add(f.Indices[1]);
                                indices.Add(f.Indices[2]);
                            }
                        }

                        var mesh = new Hx.MeshGeometry3D
                        {
                            Positions = new Hx.Vector3Collection(positions),
                            Indices = new Hx.IntCollection(indices)
                        };

                        items.Add(new Simple3DApp.Models.SceneItem
                        {
                            Name = Path.GetFileName(path),
                            Geometry = mesh,
                            Material = Hx.PhongMaterials.Gray,
                            Transform = Transform3D.Identity
                        });
                    }
                }
                foreach (var c in node.Children) Visit(c, world);
            }

            Visit(scene.RootNode, Matrix4x4.Identity);
            return items;
        }

        private static Matrix Convert(Matrix4x4 m)
        {
            return new Matrix(m.A1, m.B1, m.C1, m.D1,
                              m.A2, m.B2, m.C2, m.D2,
                              m.A3, m.B3, m.C3, m.D3,
                              m.A4, m.B4, m.C4, m.D4);
        }
    }
}