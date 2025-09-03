using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Media3D;
using SharpDX;

using Hx = HelixToolkit.Wpf.SharpDX;
using HelixToolkit.SharpDX.Core;
using HelixToolkit.Wpf.SharpDX.Assimp;
using HelixToolkit.SharpDX.Core.Model;


namespace Simple3DApp.Services
{
    public static class AssimpSceneLoader
    {
        public static IEnumerable<Simple3DApp.Models.SceneItem> LoadAsSceneItems(string path)
        {
            var importer = new Importer();
            var scene = importer.Load(path);
            var items = new List<Simple3DApp.Models.SceneItem>();

            void Visit(HelixToolkit.SharpDX.Core.Model.Scene.SceneNode node, Matrix parent)
            {
                var world = node.ModelMatrix * parent;

                if (node is HelixToolkit.SharpDX.Core.Model.Scene.MeshNode meshNode)
                {
                    var g = meshNode.Geometry as HelixToolkit.SharpDX.Core.MeshGeometry3D;
                    if (g != null)
                    {
                        // bake world on positions
                        var positions = new System.Collections.Generic.List<Vector3>(g.Positions.Count);
                        for (int i = 0; i < g.Positions.Count; i++)
                        {
                            var p = Vector3.TransformCoordinate(g.Positions[i], world);
                            positions.Add(p);
                        }

                        var mesh = new Hx.MeshGeometry3D
                        {
                            Positions = new Hx.Vector3Collection(positions),
                            Indices = g.Indices != null ? new Hx.IntCollection(g.Indices) : null,
                            Normals = null,
                            TextureCoordinates = null
                        };

                        Hx.Material mat = Hx.PhongMaterials.Gray;
                        if (meshNode.Material is PhongMaterialCore pm)
                        {
                            mat = new Hx.PhongMaterial
                            {
                                DiffuseColor = pm.DiffuseColor,
                                AmbientColor = pm.AmbientColor,
                                SpecularColor = pm.SpecularColor,
                                EmissiveColor = pm.EmissiveColor,
                                SpecularShininess = pm.SpecularShininess
                            };
                        }

                        items.Add(new Simple3DApp.Models.SceneItem
                        {
                            Name = Path.GetFileName(path),
                            Geometry = mesh,
                            Material = mat,
                            Transform = Transform3D.Identity
                        });
                    }
                }

                foreach (var c in node.Items)
                    Visit(c as HelixToolkit.SharpDX.Core.Model.Scene.SceneNode, world);
            }

            Visit(scene.Root, Matrix.Identity);
            return items;
        }
    }
}
