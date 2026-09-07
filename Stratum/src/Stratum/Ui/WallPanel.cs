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
    LayeredAssembly _assembly;
    readonly List<WallDefinition> _walls = new List<WallDefinition>();
    bool _loading;

    readonly ObservableCollection<LayerRow> _layerRows = new ObservableCollection<LayerRow>();
    readonly ObservableCollection<OpeningRow> _openingRows = new ObservableCollection<OpeningRow>();

    // ---- widgets -----------------------------------------------------------

    readonly Label _heading = new Label { Font = SystemFonts.Bold(), Text = "No wall selected" };
    readonly Label _subheading = new Label { TextColor = Colors.Gray };
    readonly DropDown _assemblyPicker = new DropDown();
    readonly DropDown _justificationPicker = new DropDown();
    readonly DropDown _levelPicker = new DropDown();
    readonly Label _topCondition = new Label { TextColor = Colors.Gray };
    readonly TextBox _heightBox = new TextBox();
    readonly TextBox _baseBox = new TextBox();
    readonly Button _flipButton = new Button { Text = "Flip", ToolTip = "Swap which side of the reference line is the exterior" };
    readonly GridView _layerGrid = new GridView();
    readonly GridView _openingGrid = new GridView();
    readonly AssemblyPreview _preview = new AssemblyPreview();
    readonly TextBox _nameBox = new TextBox();
    readonly Label _warning = new Label { TextColor = Colors.Red, Wrap = WrapMode.Word, Visible = false };

    /// <summary>Product column cell, kept by reference so the choice list can be
    /// refreshed without depending on the column's position.</summary>
    ComboBoxCell _productCell;
    ComboBoxCell _unitCell;

    /// <summary>Objects currently highlighted by a row click, so the highlight can
    /// be taken off again when the selection moves on.</summary>
    readonly List<Guid> _highlighted = new List<Guid>();

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
        ClearHighlights();
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
      // Filled per selection in RefreshHeader, because the wording depends on
      // what kind of element the assembly describes.
      _justificationPicker.DataStore = AssemblyNaming.DisplayNames(AssemblyKind.Wall)
        .Cast<object>().ToList();
      _justificationPicker.SelectedIndexChanged += (s, e) => OnJustificationPicked();
      _levelPicker.SelectedIndexChanged += (s, e) => OnLevelPicked();

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

      _nameBox.LostFocus += (s, e) => OnNameEdited();
      _nameBox.KeyDown += (s, e) => { if (e.Key == Keys.Enter) OnNameEdited(); };

      _preview.LayerClicked += (s, index) => OnPreviewLayerClicked(index);

      var header = new DynamicLayout { Spacing = new Size(6, 4) };
      header.AddRow(_heading);
      header.AddRow(_subheading);
      header.AddRow(_warning);
      header.AddRow(new Label { Text = "Name" }, _nameBox);
      header.AddRow(new Label { Text = "Level" }, _levelPicker);
      header.AddRow(new Label { Text = "Wall type" }, _assemblyPicker);
      header.AddRow(new Label { Text = "Justification" }, _justificationPicker);
      header.AddRow(new Label { Text = "Height" }, Row(_heightBox, _flipButton));
      header.AddRow(new Label { Text = "Base" }, _baseBox);
      header.AddRow(new Label { Text = "Top" }, _topCondition);

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
      assemblyTab.Add(_preview);
      assemblyTab.Add(new Label
      {
        Text = "Click a layer in the section above, or a row below, to highlight it " +
               "in the model.",
        TextColor = Colors.Gray,
        Wrap = WrapMode.Word
      });
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
        HeaderText = "Core",
        Width = 40,
        Editable = true,
        DataCell = new CheckBoxCell
        {
          Binding = Binding.Delegate<LayerRow, bool?>(r => r.IsCore, (r, v) => r.IsCore = v ?? false)
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

      _productCell = new ComboBoxCell
      {
        DataStore = new List<object>(),
        Binding = Binding.Delegate<LayerRow, object>(r => r.ProductName, (r, v) => r.ProductName = v as string)
      };
      _layerGrid.Columns.Add(new GridColumn
      {
        HeaderText = "Product",
        Width = 230,
        Editable = true,
        DataCell = _productCell
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
      _unitCell = new ComboBoxCell
      {
        DataStore = new List<object>(),
        Binding = Binding.Delegate<OpeningRow, object>(r => r.UnitName, (r, v) => r.UnitName = v as string)
      };
      _openingGrid.Columns.Add(new GridColumn
      {
        HeaderText = "Type", Width = 150, Editable = true, DataCell = _unitCell
      });
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

        ClearHighlights();

        RefreshAssemblyPicker();
        RefreshHeader(selectedLayers);
        RefreshProductChoices();
        RefreshLayerRows();
        RefreshOpeningRows();
        RefreshTotals();
        RefreshWarning();
        HighlightLayerRows(selectedLayers);

        _preview.Update(_doc, _model, _assembly,
                        _walls.Count > 0 ? _walls[0].Justification : _model.ActiveJustification,
                        _walls.Count > 0 && _walls[0].Flipped);
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
      var kind = _assembly?.Kind ?? AssemblyKind.Wall;
      _justificationPicker.DataStore = AssemblyNaming.DisplayNames(kind).Cast<object>().ToList();
      _justificationPicker.SelectedIndex = (int)(reference?.Justification ?? _model.ActiveJustification);

      double heightModel = reference?.Height ?? _model.ActiveHeight;
      double baseModel = reference?.BaseElevation ?? 0.0;
      double toInch = Units.ModelToInch(_doc);

      _heightBox.Text = Units.FormatInches(_doc, heightModel * toInch);
      _baseBox.Text = Units.FormatInches(_doc, baseModel * toInch);

      _flipButton.Enabled = _walls.Count > 0;

      // Levels
      var levels = _model.SortedLevels.ToList();
      _levelPicker.DataStore = levels.Select(l => (object)l.Name).ToList();
      _levelPicker.Enabled = _walls.Count > 0 && levels.Count > 0;
      _levelPicker.SelectedIndex = reference == null
        ? -1
        : levels.FindIndex(l => l.Id == reference.LevelId);

      // Top condition. The height box only drives the wall when it is the thing
      // deciding the top - otherwise it would look editable and do nothing.
      _topCondition.Text = DescribeTop(reference);
      _heightBox.Enabled = reference == null || reference.TopMode == WallTopMode.Height;

      _nameBox.Enabled = _walls.Count == 1;
      _nameBox.Text = _walls.Count == 1
        ? (string.IsNullOrEmpty(_walls[0].Name) ? "" : _walls[0].Name)
        : "";
      _nameBox.PlaceholderText = _walls.Count == 1 ? _walls[0].GroupName : "";
    }

    string DescribeTop(WallDefinition wall)
    {
      if (wall == null) return "—";

      switch (wall.TopMode)
      {
        case WallTopMode.ToLevel:
          var top = _model.FindLevel(wall.TopLevelId);
          return top == null
            ? "to a level that no longer exists — run BimWallTop"
            : "to " + top.Name +
              (Math.Abs(wall.TopOffset) < 1e-9
                ? ""
                : " " + (wall.TopOffset > 0 ? "+" : "-") +
                  Units.FormatInches(_doc, Math.Abs(wall.TopOffset) * Units.ModelToInch(_doc)));

        case WallTopMode.ToSurface:
          return wall.TopSurfaceObjectId != Guid.Empty && _doc.Objects.FindId(wall.TopSurfaceObjectId) != null
            ? "raked to a surface — edit it and run BimRebuild to re-cut"
            : "capping surface is missing — run BimWallTop";

        default:
          return "height (set above) — use BimWallTop to rake it to a roof";
      }
    }

    void OnLevelPicked()
    {
      if (_loading || _walls.Count == 0) return;

      var levels = _model.SortedLevels.ToList();
      int index = _levelPicker.SelectedIndex;
      if (index < 0 || index >= levels.Count) return;

      var level = levels[index];

      // Moving a wall to another level keeps its offset, so a wall sitting 4"
      // above Level 1 sits 4" above Level 2 rather than jumping to the slab.
      Commit("Change wall level", () =>
      {
        foreach (var wall in _walls) wall.LevelId = level.Id;
      }, rebuildAllOfType: false);
    }

    void RefreshProductChoices()
    {
      var names = _model.Catalog.Products
        .OrderBy(p => p.Category)
        .ThenBy(p => p.Name)
        .Select(p => p.Name)
        .Cast<object>()
        .ToList();

      if (_productCell != null) _productCell.DataStore = names;

      if (_unitCell != null)
        _unitCell.DataStore = new[] { "" }
          .Concat(_model.Catalog.OpeningUnits.Select(u => u.Name))
          .Cast<object>()
          .ToList();
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
        _openingRows.Add(new OpeningRow(_doc, opening, _model.Catalog));
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

    /// <summary>Warns when the selection spans more than one wall type - without
    /// this, editing the layer stack would silently change one type while walls of
    /// another sit selected alongside it.</summary>
    void RefreshWarning()
    {
      if (!string.IsNullOrEmpty(_pendingError))
      {
        _warning.Text = _pendingError;
        _warning.Visible = true;
        _pendingError = null;
        return;
      }

      var distinct = _walls.Select(w => w.AssemblyId).Distinct().Count();
      if (distinct > 1)
      {
        _warning.Text = "The selection contains " + distinct + " different wall types. " +
                        "Layer edits below apply to \"" + (_assembly?.Code ?? "?") +
                        "\" only. Select one type at a time to edit its layers.";
        _warning.Visible = true;
      }
      else if (_assembly != null && _model.WallsUsing(_assembly.Id).Count() > 1 && _walls.Count > 0)
      {
        int count = _model.WallsUsing(_assembly.Id).Count();
        _warning.Text = "";
        _warning.Visible = false;
        _subheading.Text += "  ·  editing " + _assembly.Code + " updates " + count + " walls";
      }
      else
      {
        _warning.Text = "";
        _warning.Visible = false;
      }
    }

    /// <summary>Tells the user why an entry was rejected. A field that silently
    /// snaps back to its old value teaches nothing.</summary>
    void ShowInputError(string text, string what)
    {
      _pendingError = "Couldn't read \"" + (text ?? "") + "\" as " + what +
                      ". Try 8, 8'-0\", 96\" or 2400mm.";
    }

    string _pendingError;

    void ClearHighlights()
    {
      if (_doc == null || _highlighted.Count == 0) return;
      foreach (var id in _highlighted)
      {
        var obj = _doc.Objects.FindId(id);
        if (obj != null) obj.Highlight(false);
      }
      _highlighted.Clear();
    }

    void OnNameEdited()
    {
      if (_loading || _walls.Count != 1) return;
      var text = (_nameBox.Text ?? "").Trim();
      if (text == (_walls[0].Name ?? "")) return;

      Commit("Rename wall", () => _walls[0].Name = text, rebuildAllOfType: false);
    }

    void OnPreviewLayerClicked(int layerIndex)
    {
      if (_assembly == null) return;
      for (int row = 0; row < _layerRows.Count; row++)
      {
        if (_assembly.Layers.IndexOf(_layerRows[row].Layer) != layerIndex) continue;
        _layerGrid.SelectRow(row);
        OnLayerRowSelected();
        return;
      }
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
      var justification = (AssemblyJustification)Math.Max(0, _justificationPicker.SelectedIndex);

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
      if (!Units.TryParseInches(_heightBox.Text, out inches) || inches <= 0)
      {
        ShowInputError(_heightBox.Text, "a wall height");
        Reload(_doc);
        return;
      }

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
      if (!Units.TryParseInches(_baseBox.Text, out inches))
      {
        ShowInputError(_baseBox.Text, "a base elevation");
        Reload(_doc);
        return;
      }

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
      Commit("Edit wall layer", () =>
      {
        bool wantsCore = row.IsCore && !row.Layer.IsCore;
        row.Apply(_model);

        // An assembly has exactly one structural core. Ticking a new one unticks
        // the old, rather than leaving the wall with two or none.
        if (wantsCore)
          foreach (var other in _assembly.Layers)
            if (!ReferenceEquals(other, row.Layer)) other.IsCore = false;

        _assembly.NormalizeSides();
      });
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

      ClearHighlights();

      foreach (var id in wall.LayerObjectIds)
      {
        var obj = _doc.Objects.FindId(id);
        if (obj == null) continue;
        if (StratumDoc.LayerIndexOf(obj) != layerIndex) continue;

        obj.Highlight(true);
        _highlighted.Add(id);
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
        var unit = _model.Catalog.UnitsOfKind(OpeningKind.Window).FirstOrDefault();
        wall.Openings.Add(new Opening
        {
          WallId = wall.Id,
          UnitId = unit?.Id ?? Guid.Empty,
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
      Commit("Edit opening", () => row.Apply(_model.Catalog), rebuildAllOfType: false);
    }

  }
}
