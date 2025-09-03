using Hx = HelixToolkit.Wpf.SharpDX;
using System.Windows.Media.Media3D;

namespace Simple3DApp.Models
{
    public class SceneItem
    {
        public Hx.MeshGeometry3D Geometry { get; set; } = new Hx.MeshGeometry3D();
        public Hx.Material Material { get; set; } = Hx.PhongMaterials.Gray;
        public Transform3D Transform { get; set; } = Transform3D.Identity;
        public string Name { get; set; } = "Item";
    }
}