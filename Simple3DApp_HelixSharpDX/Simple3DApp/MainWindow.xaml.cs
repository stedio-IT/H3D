using System.Windows;
using Simple3DApp.ViewModels;

using System;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using SharpDX;


namespace Simple3DApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void ZoomExtents_Click(object sender, RoutedEventArgs e)
        {
            viewport.ZoomExtents();
        }

private bool _isPanning = false;
private System.Windows.Point _lastPanPos;

protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    viewport.MouseDown += Viewport_MouseDown;
    viewport.MouseUp += Viewport_MouseUp;
    viewport.MouseMove += Viewport_MouseMove;
}

private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
{
    if (e.ChangedButton == MouseButton.Middle)
    {
        _isPanning = true;
        _lastPanPos = e.GetPosition(viewport);
        viewport.CaptureMouse();
    }
}

private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
{
    if (e.ChangedButton == MouseButton.Middle)
    {
        _isPanning = false;
        viewport.ReleaseMouseCapture();
    }
}

private void Viewport_MouseMove(object sender, MouseEventArgs e)
{
    if (!_isPanning) return;
    var pos = e.GetPosition(viewport);
    var dx = pos.X - _lastPanPos.X;
    var dy = pos.Y - _lastPanPos.Y;
    _lastPanPos = pos;

    // Convert screen delta to world pan along camera right/up
    var vm = (MainViewModel)DataContext;
    var cam = vm.Camera;

    // Build right/up vectors in world space
    var look = new Vector3((float)cam.LookDirection.X, (float)cam.LookDirection.Y, (float)cam.LookDirection.Z);
    if (look.LengthSquared() < 1e-6) return;
    look.Normalize();

    var up = new Vector3((float)cam.UpDirection.X, (float)cam.UpDirection.Y, (float)cam.UpDirection.Z);
    up.Normalize();

    var right = Vector3.Cross(look, up);
    right.Normalize();

    double dist = new Vector3D(cam.LookDirection.X, cam.LookDirection.Y, cam.LookDirection.Z).Length;
    double fov = cam.FieldOfView > 0 ? cam.FieldOfView : 45.0;
    double scale = Math.Tan(fov * Math.PI / 360.0) * dist;
    double viewportH = Math.Max(1.0, viewport.ActualHeight);
    double viewportW = Math.Max(1.0, viewport.ActualWidth);

    double worldDx = (dx / viewportW) * 2.0 * scale;
    double worldDy = (dy / viewportH) * 2.0 * scale;

    // Move camera position and target (LookDirection preserved)
    var delta = right * (float)(-worldDx) + up * (float)(worldDy);
    var pos3 = new Point3D(
        cam.Position.X + delta.X,
        cam.Position.Y + delta.Y,
        cam.Position.Z + delta.Z);

    cam.Position = pos3;
}
    }
}