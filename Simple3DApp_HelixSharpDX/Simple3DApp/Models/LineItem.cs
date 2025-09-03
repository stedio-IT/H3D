using Hx = HelixToolkit.Wpf.SharpDX;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Simple3DApp.Models
{
    public class LineItem
    {
        public Hx.LineGeometry3D Geometry { get; set; } = new Hx.LineGeometry3D();
        public Color Color { get; set; } = Colors.DeepSkyBlue; // Media.Color works best with WPF.SharpDX DPs
        public double Thickness { get; set; } = 1.0;
        public Transform3D Transform { get; set; } = Transform3D.Identity;
        public string Name { get; set; } = "LineSet";
    }
}