using HelixToolkit.Wpf.SharpDX;
using Microsoft.Win32;
using SharpDX;
using Simple3DApp.Helpers;
using Simple3DApp.Models;
using Simple3DApp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using Hx = HelixToolkit.Wpf.SharpDX;

namespace Simple3DApp.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {

        public double TubeRadius { get; set; } = 0.25;

        // Collezione bindabile di elementi 3D (usata in XAML con ItemsModel3D/GroupModel3D)
        public ObservableCollection<Element3D> Tubes { get; } = new ObservableCollection<Element3D>();

        // Mappa layer -> modello (utile per show/hide per layer)
        private Dictionary<int, List<Hx.MeshGeometryModel3D>> _layerToTubes = new Dictionary<int, List<Hx.MeshGeometryModel3D>>();


        public Hx.IEffectsManager EffectsManager { get; } = new Hx.DefaultEffectsManager();

        public Hx.PerspectiveCamera Camera { get; } = new Hx.PerspectiveCamera
        {
            Position = new Point3D(120, 120, 120),
            LookDirection = new Vector3D(-120, -120, -120),
            UpDirection = new Vector3D(0, 0, 1), // Z+ up
            FarPlaneDistance = 5000,
            NearPlaneDistance = 0.1
        };

        private double _frameRate;
        public double FrameRate
        {
            get { return _frameRate; }
            set { _frameRate = value; OnPropertyChanged(); }
        }

        public ObservableCollection<SceneItem> Items { get; } = new ObservableCollection<SceneItem>();
        public ObservableCollection<LineItem> Lines { get; } = new ObservableCollection<LineItem>();
        private string LastGcodePath;

        private System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<LineItem>> _layerToLines = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<LineItem>>();
        private int _maxLayer;
        public int MaxLayer { get => _maxLayer; private set { _maxLayer = value; OnPropertyChanged(); } }
        private int _currentMaxLayer;
        public int CurrentMaxLayer
        {
            get => _currentMaxLayer; set
            {
                _currentMaxLayer = value; OnPropertyChanged(); RefreshVisibleLines();
                BuildTubesAfterLines();
            }
        }
        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set { _isLoading = value; OnPropertyChanged(); } }
        private double _progress;
        public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }
        private string _status;
        public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
        private System.Threading.CancellationTokenSource _loadCts;

        private Simple3DApp.Services.GCodeColorMode _colorMode = Simple3DApp.Services.GCodeColorMode.ByType;
        public Simple3DApp.Services.GCodeColorMode ColorMode
        {
            get { return _colorMode; }
            set
            {
                if (_colorMode == value) return;
                _colorMode = value;
                OnPropertyChanged();
                RebuildGcodeLines();
            }
        }

        public ICommand AddCubeCommand { get; }
        public ICommand AddSphereCommand { get; }
        public ICommand AddPlaneCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ImportStlCommand { get; }
        public ICommand ImportGcodeCommand { get; }
        public ICommand ClearGcodeCommand { get; }
        public ICommand CancelLoadCommand { get; }

        public MainViewModel()
        {
            AddCubeCommand = new RelayCommand(_ => AddCube());
            AddSphereCommand = new RelayCommand(_ => AddSphere());
            AddPlaneCommand = new RelayCommand(_ => AddPlane());
            ClearCommand = new RelayCommand(_ => Items.Clear());
            ImportStlCommand = new RelayCommand(_ => ImportStl());
            ImportGcodeCommand = new RelayCommand(_ => ImportGcode());
            ClearGcodeCommand = new RelayCommand(_ => Lines.Clear());
            CancelLoadCommand = new RelayCommand(_ => _loadCts?.Cancel());
        }

        private static readonly Random Rng = new Random();

        private static Hx.Material RandomMaterial()
        {
            var color = new Color4((float)Rng.NextDouble(), (float)Rng.NextDouble(), (float)Rng.NextDouble(), 1f);
            return new Hx.PhongMaterial
            {
                DiffuseColor = color,
                AmbientColor = new Color4(color.Red * 0.2f, color.Green * 0.2f, color.Blue * 0.2f, 1f),
                SpecularColor = new Color4(0.3f, 0.3f, 0.3f, 1f),
                SpecularShininess = 40f
            };
        }

        private void AddCube()
        {
            var mb = new Hx.MeshBuilder(true, true, true);
            mb.AddBox(new Vector3(0, 5, 0), 10, 10, 10);
            var mesh = mb.ToMeshGeometry3D();
            Items.Add(new SceneItem
            {
                Name = "Cubo",
                Geometry = mesh,
                Material = RandomMaterial(),
                Transform = Transform3D.Identity
            });
        }

        private void AddSphere()
        {
            var mb = new Hx.MeshBuilder(true, true, true);
            mb.AddSphere(new Vector3(20, 8, 10), 8, 32, 16);
            var mesh = mb.ToMeshGeometry3D();
            Items.Add(new SceneItem
            {
                Name = "Sfera",
                Geometry = mesh,
                Material = RandomMaterial()
            });
        }

        private void AddPlane()
        {
            var mb = new Hx.MeshBuilder(true, true, true);
            // Piano sul piano XY a Z=0 (100x100)
            var p0 = new Vector3(-50, -50, 0);
            var p1 = new Vector3(50, -50, 0);
            var p2 = new Vector3(50, 50, 0);
            var p3 = new Vector3(-50, 50, 0);
            mb.AddTriangle(p0, p1, p2);
            mb.AddTriangle(p0, p2, p3);
            var mesh = mb.ToMeshGeometry3D();
            Items.Add(new SceneItem
            {
                Name = "Piano XY",
                Geometry = mesh,
                Material = new Hx.PhongMaterial { DiffuseColor = new Color4(0.6f, 0.6f, 0.7f, 1f) }
            });
        }

        private void RebuildGcodeLines()
        {
            try
            {
                if (string.IsNullOrEmpty(LastGcodePath)) return;
                var items = Simple3DApp.Services.GCodeLoader.BuildLineGeometry(LastGcodePath, ColorMode, 8);
                Lines.Clear();
                foreach (var it in items) Lines.Add(it);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Errore nel ricalcolo G-code:\n" + ex.Message, "G-code", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private async void ImportGcode()
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "G-code files (*.gcode;*.gco;*.gc)|*.gcode;*.gco;*.gc|All files (*.*)|*.*",
                    Title = "Seleziona un file G-code"
                };
                if (ofd.ShowDialog() == true && File.Exists(ofd.FileName))
                {
                    LastGcodePath = ofd.FileName;
                    _loadCts?.Cancel();
                    _loadCts = new System.Threading.CancellationTokenSource();
                    IsLoading = true;
                    Progress = 0; Status = "Parsing G-code...";
                    Lines.Clear();
                    _layerToLines.Clear();

                    var prog = new Progress<Simple3DApp.Services.GCodeProgress>(p =>
                    {
                        if (p.TotalBytes > 0) Progress = (double)p.BytesRead / p.TotalBytes;
                        Status = $"Passo {p.Pass}: {p.BytesRead / (1024 * 1024)}MB / {p.TotalBytes / (1024 * 1024)}MB - Layers: {p.LayersParsed}  Segments: {p.SegmentsParsed}";
                    });

                    var res = await Simple3DApp.Services.GCodeAsyncLoader.BuildLayeredAsync(
                        LastGcodePath, ColorMode, 8, prog, _loadCts.Token);

                    _layerToLines = res.LayerToItems;
                    MaxLayer = res.MaxLayer;
                    CurrentMaxLayer = MaxLayer; // mostra tutto

                    RefreshVisibleLines();
                    BuildTubesAfterLines();
                    IsLoading = false;
                    Status = $"Caricato: {MaxLayer + 1} layer";
                }
            }
            catch (OperationCanceledException)
            {
                IsLoading = false;
                Status = "Caricamento annullato";
            }
            catch (Exception ex)
            {
                IsLoading = false;
                System.Windows.MessageBox.Show("Errore durante l'import G-code:" + ex.Message, "Import G-code", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void RefreshVisibleLines()
        {
            Lines.Clear();
            foreach (var kv in _layerToLines)
            {
                if (kv.Key <= CurrentMaxLayer)
                {
                    foreach (var it in kv.Value) Lines.Add(it);
                }
            }
        }

        private void ImportStl()
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "3D files|*.stl;*.obj;*.ply;*.3mf;*.fbx;*.dae;*.off;*.x;*.gltf;*.glb|All files (*.*)|*.*",
                    Title = "Seleziona un file 3D (Assimp)"
                };
                if (ofd.ShowDialog() == true && System.IO.File.Exists(ofd.FileName))
                {
                    foreach (var si in Simple3DApp.Services.AssimpSceneLoader.LoadAsSceneItems(ofd.FileName))
                        Items.Add(si);
                }
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show("Errore durante l'import 3D (Assimp):\n" + ex.Message, "Import 3D", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }


        private void BuildTubesAfterLines()
        {
            if (_layerToLines == null || _layerToLines.Count == 0) return;

            // Ottengo SceneItem per layer dal builder
            var sceneDict = Simple3DApp.Services.TubesFromLinesBuilder.BuildFromLayeredLines(_layerToLines, (float)TubeRadius, 8);

            // Converto SceneItem -> MeshGeometryModel3D (Element3D) per poterli inserire nella collection Tubes
            var dict = new Dictionary<int, List<Hx.MeshGeometryModel3D>>();
            foreach (var kv in sceneDict)
            {
                var layer = kv.Key;
                var list = new List<Hx.MeshGeometryModel3D>();
                foreach (var si in kv.Value)
                {
                    var model = new Hx.MeshGeometryModel3D
                    {
                        Geometry = si.Geometry,
                        Material = si.Material,
                        Transform = si.Transform
                    };
                    list.Add(model);
                }
                dict[layer] = list;
            }

            _layerToTubes = dict;
            RefreshVisibleTubes();
        }

        private void RefreshVisibleTubes()
        {
            Tubes.Clear();
            foreach (var kv in _layerToTubes)
                if (kv.Key <= CurrentMaxLayer)
                    foreach (var it in kv.Value) Tubes.Add(it);
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}