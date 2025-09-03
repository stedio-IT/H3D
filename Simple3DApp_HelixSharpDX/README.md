# Simple3DApp • HelixToolkit.Wpf.SharpDX (.NET Framework 4.8)

Esempio WPF con **Viewport3DX** e **ItemsModel3D** che mostra geometrie semplici (cubo, sfera, piano) e consente di **importare file STL**. 
L'aggiunta degli oggetti al viewport avviene **modificando l'`ItemsSource` di `ItemsModel3D`** (binding ad `ObservableCollection<SceneItem>`).

## Requisiti
- Visual Studio 2022 (o 2019) con sviluppo .NET desktop
- .NET Framework 4.8
- NuGet attivo

## Come aprire e compilare
1. Apri `Simple3DApp_HelixSharpDX.sln` in Visual Studio.
2. Compila (la prima volta NuGet ripristinerà il pacchetto `HelixToolkit.Wpf.SharpDX` tramite `PackageReference` con **versione flottante 2.\***).
3. Avvia l'app.

## Uso
- **Aggiungi Cubo / Sfera / Piano**: crea geometrie con materiale Phong casuale.
- **Importa STL**: carica file STL **ASCII o binari** con un loader integrato (nessuna dipendenza da Assimp).
- **Pulisci**: rimuove tutti gli elementi.
- **Zoom Extents**: adatta la vista agli oggetti.
- Navigazione: mouse per ruotare/zoomare/traslare (controlli standard HelixToolkit).

## Struttura MVVM
- `MainViewModel` espone: `EffectsManager`, `Camera`, `Items` (ObservableCollection), e comandi.
- `ItemsModel3D` nel `MainWindow.xaml` fa binding a `Items` e usa un `DataTemplate` → `MeshGeometryModel3D`.
- Le geometrie vengono create con `MeshBuilder` (SharpDX) e convertite in `MeshGeometry3D`.

## Note
- Il loader STL è volutamente semplice e non unisce vertici uguali (ogni triangolo ha 3 vertici); è però robusto (ASCII/ Binario).
- Puoi aggiungere facilmente altre primitive (`AddCylinder`, `AddPipe`, ecc.) tramite `MeshBuilder`.