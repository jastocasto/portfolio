using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Stratum.Core;
using Stratum.Documents;

namespace Stratum.Ui
{
  /// <summary>
  /// The BIM Wall panel: the place where a wall stops being geometry and starts
  /// being a specification.
  ///
  /// Select a wall and its layer stack appears - every layer, its product, its
  /// thickness, its R-value and its cost. Change 1/2" plywood to 3/4" plywood in
  /// the Product column and the wall thickens by a quarter of an inch on that
  /// side only, the R-value and the cost update, and the structural core stays
  /// exactly where it was drawn.
  ///
  /// Registered by the plug-in at load; opened with BimWallProperties or from the
  /// panel menu.
  /// </summary>
  [Guid("414c958a-ffbd-460a-ac5a-bc6c8e7d4c3a")]
  public class WallPanel : Panel
  {
    public static Guid PanelId => typeof(WallPanel).GUID;

    public static void Show() => Rhino.UI.Panels.OpenPanel(PanelId);

    // ---- state -------------------------------------------------------------

    RhinoDoc _doc;
    BimModel _model;
    WallAssembly _assembly;
    readonly List<WallDefinition> _walls = new List<WallDefinition>();
    bool _loading;

    readonly ObservableCollection<LayerRow> _layerRows = new ObservableCollection<LayerRow>();
    readonly ObservableCollection<OpeningRow> _openingRows = new ObservableCollection<OpeningRow>();

    // ---- widgets -----------------------------------------------------------

    readonly Label _heading = new Label { Font = SystemFonts.Bold(), Text = "No wall selected" };
    readonly Label _subheading = new Label { TextColor = Colors.Gray };
    readonly DropDown _assemblyPicker = new DropDown();
    readonly DropDown _justificationPicker = new DropDown();
    readonly TextBox _heightBox = new TextBox();
    readonly TextBox _baseBox = new TextBox();
    readonly Button _flipButton = new Button { Text = "Flip", ToolTip = "Swap which side of the reference line is the exterior" };
    readonly GridView _layerGrid = new GridView();
    readonly GridView _openingGrid = new GridView();

    readonly Label _totalThickness = new Label();
    readonly Label _totalR = new Label();
    readonly Label _totalCost = new Label();
    readonly Label _totalWeight = new Label();

    readonly EventHandler<RhinoDoc> _onSelectionChanged;
    readonly EventHandler<RhinoDoc> _onModelChanged;

    public WallPanel()
    {
      Padding = new Padding(8);
      Content = BuildLayout();

      _onSelectionChanged = (s, doc) => Reload(doc);
      _onModelChanged = (s, doc) => Reload(doc);

      StratumDoc.SelectionChanged += _onSelectionChanged;
      StratumDoc.ModelChanged += _onModelChanged;

      Reload(RhinoDoc.ActiveDoc);
    }

    /// <summary>Static events outlive the panel, so the handlers have to come off
    /// with it - otherwise a closed panel keeps answering document changes.</summary>
    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        StratumDoc.SelectionChanged -= _onSelectionChanged;
        StratumDoc.ModelChanged -= _onModelChanged;
      }
      base.Dispose(disposing);
    }

    // ------------------------------------------------------------------------
    //  Layout
    // ------------------------------------------------------------------------

    Control BuildLayout()
    {
      BuildLayerGrid();
      BuildOpeningGrid();

      _assemblyPicker.SelectedIndexChanged += (s, e) => OnAssemblyPicked();
      _justificationPicker.DataStore = Enum.GetNames(typeof(WallJustification))
        .Select(PrettyJustification).ToList();
      _justificationPicker.SelectedIndexChanged += (s, e) => OnJustificationPicked();

      _heightBox.LostFocus += (s, e) => OnHeightEdited();
      _heightBox.KeyDown += (s, e) => { if (e.Key == Keys.Enter) OnHeightEdited(); };
      _baseBox.LostFocus += (s, e) => OnBaseEdited();
      _baseBox.KeyDown += (s, e) => { if (e.Key == Keys.Enter) OnBaseEdited(); };
      _flipButton.Click += (s, e) => OnFlip();

      var addLayer = new Button { Text = "+", ToolTip = "Add a layer below the selected one", Width = 30 };
      var removeLayer = new Button { Text = "−", ToolTip = "Remove the selected layer", Width = 30 };
      var moveUp = new Button { Text = "↑", ToolTip = "Move the layer toward the exterior", Width = 30 };
      var moveDown = new Button { Text = "↓", ToolTip = "Move the layer toward the interior", Width = 30 };
      var setCore = new Button { Text = "Core", ToolTip = "Make the selected layer the structural core" };
      var duplicateType = new Button { Text = "Duplicate type…", ToolTip = "Copy this wall type and assign the copy to the selected walls" };
      var editCatalog = new Button { Text = "Catalog…", ToolTip = "Edit wall types and products" };

      addLayer.Click += (s, e) => OnAddLayer();
      removeLayer.Click += (s, e) => OnRemoveLayer();
      moveUp.Click += (s, e) => OnMoveLayer(-1);
      moveDown.Click += (s, e) => OnMoveLayer(1);
      setCore.Click += (s, e) => OnSetCore();
      duplicateType.Click += (s, e) => OnDuplicateType();
      editCatalog.Click += (s, e) => OnEditCatalog();

      var addOpening = new Button { Text = "+", ToolTip = "Add an opening at the centre of the wall", Width = 30 };
      var removeOpening = new Button { Text = "−", ToolTip = "Remove the selected opening", Width = 30 };
      addOpening.Click += (s, e) => OnAddOpening();
      removeOpening.Click += (s, e) => OnRemoveOpening();

      var header = new DynamicLayout { Spacing = new Size(6, 4) };
      header.AddRow(_heading);
      header.AddRow(_subheading);
      header.AddRow(new Label { Text = "Wall type" }, _assemblyPicker);
      header.AddRow(new Label { Text = "Justification" }, _justificationPicker);
      header.AddRow(new Label { Text = "Height" }, Row(_heightBox, _flipButton));
      header.AddRow(new Label { Text = "Base" }, _baseBox);

      var layerButtons = new StackLayout
      {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        Items =
        {
          addLayer, removeLayer, moveUp, moveDown, setCore,
          new StackLayoutItem(null, true),
          duplicateType, editCatalog
        }
      };

      var totals = new DynamicLayout { Spacing = new Size(6, 2) };
      totals.AddRow(new Label { Text = "Thickness" }, _totalThickness);
      totals.AddRow(new Label { Text = "R-value" }, _totalR);
      totals.AddRow(new Label { Text = "Cost" }, _totalCost);
      totals.AddRow(new Label { Text = "Weight" }, _totalWeight);

      var assemblyTab = new DynamicLayout { Spacing = new Size(6, 6), Padding = new Padding(4) };
      assemblyTab.AddRow(_layerGrid);
      assemblyTab.Add(layerButtons);
      assemblyTab.Add(totals);

      var openingButtons = new StackLayout
      {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        Items = { addOpening, removeOpening }
      };

      var openingTab = new DynamicLayout { Spacing = new Size(6, 6), Padding = new Padding(4) };
      openingTab.AddRow(_openingGrid);
      openingTab.Add(openingButtons);
      openingTab.Add(new Label
      {
        Text = "Sizes are unit sizes in inches. The rough opening is the unit size " +
               "plus the clearance. Each layer terminates by its own rule - set those " +
               "in the Catalog editor.",
        TextColor = Colors.Gray,
        Wrap = WrapMode.Word
      });

      var tabs = new TabControl();
      tabs.Pages.Add(new TabPage(assemblyTab) { Text = "Assembly" });
      tabs.Pages.Add(new TabPage(openingTab) { Text = "Openings" });

      var root = new DynamicLayout { Spacing = new Size(6, 8) };
      root.Add(header);
      root.Add(tabs, yscale: true);
      return root;
    }

    static Control Row(params Control[] controls)
    {
      var stack = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 4 };
      foreach (var control in controls) stack.Items.Add(control);
      return stack;
    }

    void BuildLayerGrid()
    {
      _layerGrid.DataStore = _layerRows;
      _layerGrid.ShowHeader = true;
      _layerGrid.AllowMultipleSelection = false;
      _layerGrid.Height = 240;

      _layerGrid.Columns.Add(new GridColumn
      {
        HeaderText = "",
        Width = 26,
        Editable = true,
        DataCell = new CheckBoxCell
        {
          Binding = Binding.Delegate<LayerRow, bool?>(r => r.Enabled, (r, v) => r.Enabled = v ?? true)
        }
      });

      _layerGrid.Columns.Add(new GridColumn
      {
        HeaderText = "Function",
        Width = 90,
        Editable = true,
        DataCell = new ComboBoxCell
        {
          DataStore = Enum.GetNames(typeof(LayerFunction)).Cast<object>().ToList(),
          Binding = Binding.Delegate<LayerRow, object>(r => r.FunctionName, (r, v) => r.FunctionName = v as string)
        }
      });

      _layerGrid.Columns.Add(new GridColumn
      {
        HeaderText = "Product",
        Width = 230,
        Editable = true,
        DataCell = new ComboBoxCell
        {
          DataStore = new List<object>(),
          Binding = Binding.Delegate<LayerRow, object>(r => r.ProductName, (r, v) => r.ProductName = v as string)
        }
      });

      _layerGrid.Columns.Add(new GridColumn
      {
        HeaderText = "Thickness",
        Width = 80,
        Editable = true,
        DataCell = new TextBoxCell
        {
          Binding = Binding.Delegate<LayerRow, string>(r => r.ThicknessText, (r, v) => r.ThicknessText = v)
        }
      });

      _layerGrid.Columns.Add(new GridColumn
      {
        HeaderText = "R",
        Width = 52,
        Editable = false,
        DataCell = new TextBoxCell { Binding = Binding.Delegate<LayerRow, string>(r => r.RText) }
      });

      _layerGrid.Columns.Add(new GridColumn
      {
        HeaderText = "$/sf",
        Width = 60,
        Editable = false,
        DataCell = new TextBoxCell { Binding = Binding.Delegate<LayerRow, string>(r => r.CostText) }
      });

      _layerGrid.CellEdited += (s, e) => OnLayerEdited(e.Row);
      _layerGrid.SelectedRowsChanged += (s, e) => OnLayerRowSelected();
    }

    void BuildOpeningGrid()
    {
      _openingGrid.DataStore = _openingRows;
      _openingGrid.ShowHeader = true;
      _openingGrid.AllowMultipleSelection = false;
      _openingGrid.Height = 240;

      _openingGrid.Columns.Add(TextColumn("Mark", 70, r => r.Name, (r, v) => r.Name = v));
      _openingGrid.Columns.Add(new GridColumn
      {
        HeaderText = "Type",
        Width = 80,
        Editable = true,
        DataCell = new ComboBoxCell
        {
          DataStore = Enum.GetNames(typeof(OpeningKind)).Cast<object>().ToList(),
          Binding = Binding.Delegate<OpeningRow, object>(r => r.KindName, (r, v) => r.KindName = v as string)
        }
      });
      _openingGrid.Columns.Add(TextColumn("Width", 70, r => r.WidthText, (r, v) => r.WidthText = v));
      _openingGrid.Columns.Add(TextColumn("Height", 70, r => r.HeightText, (r, v) => r.HeightText = v));
      _openingGrid.Columns.Add(TextColumn("Sill", 70, r => r.SillText, (r, v) => r.SillText = v));
      _openingGrid.Columns.Add(TextColumn("From start", 90, r => r.StationText, (r, v) => r.StationText = v));

      _openingGrid.CellEdited += (s, e) => OnOpeningEdited(e.Row);
    }

    static GridColumn TextColumn(string header, int width, Func<OpeningRow, string> get, Action<OpeningRow, string> set)
    {
      return new GridColumn
      {
        HeaderText = header,
        Width = width,
        Editable = set != null,
        DataCell = new TextBoxCell { Binding = Binding.Delegate(get, set) }
      };
    }

    // ------------------------------------------------------------------------
    //  Loading
    // ------------------------------------------------------------------------

    void Reload(RhinoDoc doc)
    {
      if (doc == null) doc = RhinoDoc.ActiveDoc;
      if (doc == null) return;

      _loading = true;
      try
      {
        _doc = doc;
        _model = StratumDoc.Get(doc);

        HashSet<int> selectedLayers;
        var walls = StratumDoc.SelectedWalls(doc, out selectedLayers);

        _walls.Clear();
        _walls.AddRange(walls);

        _assembly = _walls.Count > 0
          ? _model.AssemblyOf(_walls[0])
          : _model.ActiveAssembly;

        RefreshAssemblyPicker();
        RefreshHeader(selectedLayers);
        RefreshProductChoices();
        RefreshLayerRows();
        RefreshOpeningRows();
        RefreshTotals();
        HighlightLayerRows(selectedLayers);
      }
      catch (Exception ex)
      {
        RhinoApp.WriteLine("Stratum panel: " + ex.Message);
      }
      finally
      {
        _loading = false;
      }
    }

    void RefreshAssemblyPicker()
    {
      _assemblyPicker.DataStore = _model.Catalog.Assemblies
        .Select(a => a.Code + " — " + a.Name)
        .Cast<object>()
        .ToList();

      int index = _assembly == null ? -1 : _model.Catalog.Assemblies.FindIndex(a => a.Id == _assembly.Id);
      _assemblyPicker.SelectedIndex = index;
    }

    void RefreshHeader(HashSet<int> selectedLayers)
    {
      if (_walls.Count == 0)
      {
        _heading.Text = "Wall type defaults";
        _subheading.Text = "Nothing selected — editing the type used by the next wall you draw.";
      }
      else if (_walls.Count == 1)
      {
        var wall = _walls[0];
        _heading.Text = string.IsNullOrEmpty(wall.Name) ? wall.GroupName : wall.Name;
        _subheading.Text = string.Format(CultureInfo.InvariantCulture,
          "{0} long · {1} openings{2}",
          Units.FormatInches(_doc, wall.Length * Units.ModelToInch(_doc)),
          wall.Openings.Count,
          selectedLayers.Count > 0 ? " · layer " + string.Join(", ", selectedLayers.Select(i => i + 1)) : "");
      }
      else
      {
        _heading.Text = _walls.Count + " walls selected";
        _subheading.Text = "Edits apply to all of them.";
      }

      var reference = _walls.Count > 0 ? _walls[0] : null;
      _justificationPicker.SelectedIndex = (int)(reference?.Justification ?? _model.ActiveJustification);

      double heightModel = reference?.Height ?? _model.ActiveHeight;
      double baseModel = reference?.BaseElevation ?? 0.0;
      double toInch = Units.ModelToInch(_doc);

      _heightBox.Text = Units.FormatInches(_doc, heightModel * toInch);
      _baseBox.Text = Units.FormatInches(_doc, baseModel * toInch);

      _flipButton.Enabled = _walls.Count > 0;
    }

    void RefreshProductChoices()
    {
      var names = _model.Catalog.Products
        .OrderBy(p => p.Category)
        .ThenBy(p => p.Name)
        .Select(p => p.Name)
        .Cast<object>()
        .ToList();

      var productColumn = _layerGrid.Columns.Count > 2 ? _layerGrid.Columns[2].DataCell as ComboBoxCell : null;
      if (productColumn != null) productColumn.DataStore = names;
    }

    void RefreshLayerRows()
    {
      _layerRows.Clear();
      if (_assembly == null) return;

      foreach (var layer in _assembly.Layers)
        _layerRows.Add(new LayerRow(_doc, _model, layer));
    }

    void RefreshOpeningRows()
    {
      _openingRows.Clear();
      if (_walls.Count != 1) return;

      foreach (var opening in _walls[0].Openings)
        _openingRows.Add(new OpeningRow(_doc, opening));
    }

    void RefreshTotals()
    {
      if (_assembly == null)
      {
        _totalThickness.Text = _totalR.Text = _totalCost.Text = _totalWeight.Text = "—";
        return;
      }

      double rNominal = _assembly.RValue(_model.Catalog);
      double rEffective = _assembly.EffectiveRValue(_model.Catalog);
      double costPerSf = _assembly.CostPerSqFt(_model.Catalog);

      _totalThickness.Text = Units.FormatInches(_doc, _assembly.TotalThicknessIn);
      _totalR.Text = string.Format(CultureInfo.CurrentCulture,
        "R-{0:0.0} nominal · R-{1:0.0} effective · U-{2:0.000}",
        rNominal, rEffective, rEffective > 0 ? 1.0 / rEffective : 0.0);

      double area = _walls.Sum(w =>
      {
        double toInch = Units.ModelToInch(_doc);
        double gross = (w.Length * toInch / 12.0) * (w.Height * toInch / 12.0);
        double openings = w.Openings.Sum(o =>
          ((o.WidthIn + o.RoughClearanceIn) * (o.HeightIn + o.RoughClearanceIn)) / 144.0);
        return Math.Max(0.0, gross - openings);
      });

      _totalCost.Text = area > 0
        ? string.Format(CultureInfo.CurrentCulture, "${0:0.00}/sf · {1:0} sf selected · ${2:0}",
                        costPerSf, area, costPerSf * area)
        : string.Format(CultureInfo.CurrentCulture, "${0:0.00}/sf", costPerSf);

      _totalWeight.Text = string.Format(CultureInfo.CurrentCulture, "{0:0.0} psf",
                                        _assembly.WeightPsf(_model.Catalog));
    }

    void HighlightLayerRows(HashSet<int> selectedLayers)
    {
      if (selectedLayers == null || selectedLayers.Count != 1) return;
      int index = selectedLayers.First();
      if (index >= 0 && index < _layerRows.Count) _layerGrid.SelectRow(index);
    }

    // ------------------------------------------------------------------------
    //  Edits
    // ------------------------------------------------------------------------

    void Commit(string undoLabel, Action action, bool rebuildAllOfType = true)
    {
      if (_loading || _doc == null || _model == null) return;

      uint undoRecord = _doc.BeginUndoRecord(undoLabel);
      try
      {
        action();

        var walls = rebuildAllOfType && _assembly != null
          ? _model.WallsUsing(_assembly.Id).ToList()
          : _walls.ToList();

        List<string> warnings;
        WallBaker.RebuildMany(_doc, _model, walls, out warnings);
        foreach (var warning in warnings.Distinct().Take(3))
          RhinoApp.WriteLine("Stratum: " + warning);
      }
      catch (Exception ex)
      {
        RhinoApp.WriteLine("Stratum: " + ex.Message);
      }
      finally
      {
        _doc.EndUndoRecord(undoRecord);
      }

      _doc.Views.Redraw();
      Reload(_doc);
    }

    void OnAssemblyPicked()
    {
      if (_loading) return;
      int index = _assemblyPicker.SelectedIndex;
      if (index < 0 || index >= _model.Catalog.Assemblies.Count) return;

      var picked = _model.Catalog.Assemblies[index];

      if (_walls.Count == 0)
      {
        _model.ActiveAssemblyId = picked.Id;
        _assembly = picked;
        Reload(_doc);
        return;
      }

      Commit("Change wall type", () =>
      {
        foreach (var wall in _walls) wall.AssemblyId = picked.Id;
        _assembly = picked;
      }, rebuildAllOfType: false);
    }

    void OnJustificationPicked()
    {
      if (_loading) return;
      var justification = (WallJustification)Math.Max(0, _justificationPicker.SelectedIndex);

      if (_walls.Count == 0)
      {
        _model.ActiveJustification = justification;
        return;
      }

      Commit("Change justification",
             () => { foreach (var wall in _walls) wall.Justification = justification; },
             rebuildAllOfType: false);
    }

    void OnHeightEdited()
    {
      if (_loading) return;
      double inches;
      if (!Units.TryParseInches(_heightBox.Text, out inches) || inches <= 0) { Reload(_doc); return; }

      double model = inches * Units.InchToModel(_doc);

      if (_walls.Count == 0) { _model.ActiveHeight = model; return; }

      Commit("Change wall height",
             () => { foreach (var wall in _walls) wall.Height = model; },
             rebuildAllOfType: false);
    }

    void OnBaseEdited()
    {
      if (_loading || _walls.Count == 0) return;
      double inches;
      if (!Units.TryParseInches(_baseBox.Text, out inches)) { Reload(_doc); return; }

      double model = inches * Units.InchToModel(_doc);
      Commit("Change base elevation",
             () => { foreach (var wall in _walls) wall.BaseElevation = model; },
             rebuildAllOfType: false);
    }

    void OnFlip()
    {
      if (_walls.Count == 0) return;
      Commit("Flip wall",
             () => { foreach (var wall in _walls) wall.Flipped = !wall.Flipped; },
             rebuildAllOfType: false);
    }

    void OnLayerEdited(int rowIndex)
    {
      if (_loading || _assembly == null) return;
      if (rowIndex < 0 || rowIndex >= _layerRows.Count) return;

      var row = _layerRows[rowIndex];
      Commit("Edit wall layer", () => row.Apply(_model));
    }

    /// <summary>Selecting a row in the panel selects that single layer solid in the
    /// model, so "which one is the plywood" is answered by looking at the screen.</summary>
    void OnLayerRowSelected()
    {
      if (_loading || _doc == null || _walls.Count != 1) return;

      int rowIndex = _layerGrid.SelectedRow;
      if (rowIndex < 0 || rowIndex >= _layerRows.Count) return;

      var wall = _walls[0];
      var target = _layerRows[rowIndex].Layer;
      int layerIndex = _assembly.Layers.IndexOf(target);
      if (layerIndex < 0) return;

      foreach (var id in wall.LayerObjectIds)
      {
        var obj = _doc.Objects.FindId(id);
        if (obj == null) continue;

        int objLayerIndex = StratumDoc.LayerIndexOf(obj);
        bool highlight = objLayerIndex == layerIndex;
        obj.Highlight(highlight);
      }
      _doc.Views.Redraw();
    }

    void OnAddLayer()
    {
      if (_assembly == null) return;
      int at = Math.Max(0, _layerGrid.SelectedRow + 1);

      Commit("Add wall layer", () =>
      {
        var product = _model.Catalog.Products.FirstOrDefault();
        var layer = new AssemblyLayer
        {
          ProductId = product?.Id ?? Guid.Empty,
          ProductName = product?.Name ?? "",
          ThicknessIn = product?.ThicknessIn ?? 0.5,
          Function = LayerFunction.Finish
        };
        _assembly.Layers.Insert(Math.Min(at, _assembly.Layers.Count), layer);
        _assembly.NormalizeSides();
      });
    }

    void OnRemoveLayer()
    {
      if (_assembly == null) return;
      int row = _layerGrid.SelectedRow;
      if (row < 0 || row >= _assembly.Layers.Count) return;

      Commit("Remove wall layer", () =>
      {
        _assembly.Layers.RemoveAt(row);
        _assembly.NormalizeSides();
      });
    }

    void OnMoveLayer(int delta)
    {
      if (_assembly == null) return;
      int row = _layerGrid.SelectedRow;
      int target = row + delta;
      if (row < 0 || target < 0 || row >= _assembly.Layers.Count || target >= _assembly.Layers.Count) return;

      Commit("Reorder wall layer", () =>
      {
        var layer = _assembly.Layers[row];
        _assembly.Layers.RemoveAt(row);
        _assembly.Layers.Insert(target, layer);
        _assembly.NormalizeSides();
      });
    }

    void OnSetCore()
    {
      if (_assembly == null) return;
      int row = _layerGrid.SelectedRow;
      if (row < 0 || row >= _assembly.Layers.Count) return;

      Commit("Set structural core", () =>
      {
        foreach (var layer in _assembly.Layers) layer.IsCore = false;
        _assembly.Layers[row].IsCore = true;
        _assembly.NormalizeSides();
      });
    }

    void OnDuplicateType()
    {
      if (_assembly == null) return;

      var copy = _assembly.Duplicate();
      copy.Code = NextCode(_assembly.Code);
      copy.Name = _assembly.Name + " (variant)";

      Commit("Duplicate wall type", () =>
      {
        _model.Catalog.Assemblies.Add(copy);
        foreach (var wall in _walls) wall.AssemblyId = copy.Id;
        if (_walls.Count == 0) _model.ActiveAssemblyId = copy.Id;
        _assembly = copy;
      }, rebuildAllOfType: false);
    }

    string NextCode(string code)
    {
      string root = new string((code ?? "W").TakeWhile(char.IsLetter).ToArray());
      if (string.IsNullOrEmpty(root)) root = "W";

      for (int i = 1; i < 400; i++)
      {
        string candidate = root + i.ToString(CultureInfo.InvariantCulture);
        if (_model.Catalog.FindAssemblyByCode(candidate) == null) return candidate;
      }
      return root + Guid.NewGuid().ToString("N").Substring(0, 4);
    }

    void OnEditCatalog()
    {
      if (_doc == null || _model == null) return;

      var dialog = new AssemblyManagerDialog(_doc, _model);
      if (!dialog.ShowModal(Rhino.UI.RhinoEtoApp.MainWindow)) return;

      uint undoRecord = _doc.BeginUndoRecord("Edit catalog");
      try
      {
        List<string> warnings;
        WallBaker.RebuildAll(_doc, _model, out warnings);
        foreach (var warning in warnings.Distinct().Take(3))
          RhinoApp.WriteLine("Stratum: " + warning);
      }
      finally
      {
        _doc.EndUndoRecord(undoRecord);
      }

      Reload(_doc);
    }

    void OnAddOpening()
    {
      if (_walls.Count != 1) { RhinoApp.WriteLine("Stratum: select exactly one wall to add an opening."); return; }
      var wall = _walls[0];

      Commit("Add opening", () =>
      {
        wall.Openings.Add(new Opening
        {
          WallId = wall.Id,
          Kind = OpeningKind.Window,
          Name = "W-" + (wall.Openings.Count + 1).ToString("00", CultureInfo.InvariantCulture),
          StationAlongWall = wall.Length * 0.5,
          WidthIn = 36,
          HeightIn = 48,
          SillHeightIn = 36
        });
      }, rebuildAllOfType: false);
    }

    void OnRemoveOpening()
    {
      if (_walls.Count != 1) return;
      int row = _openingGrid.SelectedRow;
      var wall = _walls[0];
      if (row < 0 || row >= wall.Openings.Count) return;

      Commit("Remove opening", () => wall.Openings.RemoveAt(row), rebuildAllOfType: false);
    }

    void OnOpeningEdited(int rowIndex)
    {
      if (_loading || _walls.Count != 1) return;
      if (rowIndex < 0 || rowIndex >= _openingRows.Count) return;

      var row = _openingRows[rowIndex];
      Commit("Edit opening", () => row.Apply(), rebuildAllOfType: false);
    }

    static string PrettyJustification(string name)
    {
      switch (name)
      {
        case "ExteriorFace": return "Exterior face";
        case "ExteriorCore": return "Exterior face of core";
        case "CoreCenter": return "Centre of core";
        case "InteriorCore": return "Interior face of core";
        case "InteriorFace": return "Interior face";
        case "WallCenter": return "Centre of wall";
        default: return name;
      }
    }
  }
}
