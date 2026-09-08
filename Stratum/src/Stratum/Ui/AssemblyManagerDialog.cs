using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.Collections;
using Stratum.Core;

namespace Stratum.Ui
{
  /// <summary>
  /// The catalog editor: wall types on one tab, the products they are made of on
  /// the other.
  ///
  /// This is the single source of truth for the model. A product carries its
  /// thickness, R-value, cost, density and vapour permeance; a wall type is an
  /// ordered stack of those products plus the rule each one follows when it meets
  /// an opening. Every wall in the document is generated from these two tables,
  /// which is why the model and the drawings cannot drift apart.
  /// </summary>
  public class AssemblyManagerDialog : Dialog<bool>
  {
    readonly RhinoDoc _doc;
    readonly BimModel _model;
    readonly ArchivableDictionary _snapshot;

    readonly ObservableCollection<AssemblyRow> _assemblyRows = new ObservableCollection<AssemblyRow>();
    readonly ObservableCollection<LayerEditRow> _layerRows = new ObservableCollection<LayerEditRow>();
    readonly ObservableCollection<ProductRow> _productRows = new ObservableCollection<ProductRow>();

    readonly GridView _assemblyGrid = new GridView { Height = 260, AllowMultipleSelection = false };
    readonly GridView _layerGrid = new GridView { Height = 260, AllowMultipleSelection = false };
    readonly GridView _productGrid = new GridView { Height = 420, AllowMultipleSelection = false };

    readonly TextBox _code = new TextBox { Width = 70 };
    readonly TextBox _name = new TextBox();
    readonly TextArea _description = new TextArea { Height = 54 };
    readonly TextBox _fireRating = new TextBox { Width = 60 };
    readonly TextBox _stc = new TextBox { Width = 60 };
    readonly Label _summary = new Label { TextColor = Colors.Gray };

    LayeredAssembly _current;
    bool _loading;

    public AssemblyManagerDialog(RhinoDoc doc, BimModel model)
    {
      _doc = doc;
      _model = model;
      _snapshot = model.Catalog.ToDictionary();   // so Cancel really cancels

      Title = "Stratum — wall types and products";
      MinimumSize = new Size(940, 620);
      Padding = new Padding(10);
      Resizable = true;

      BuildAssemblyGrid();
      BuildLayerGrid();
      BuildProductGrid();

      var tabs = new TabControl();
      tabs.Pages.Add(new TabPage(BuildWallTypesTab()) { Text = "Wall types" });
      tabs.Pages.Add(new TabPage(BuildProductsTab()) { Text = "Products" });

      var ok = new Button { Text = "OK" };
      var cancel = new Button { Text = "Cancel" };
      var saveLibrary = new Button { Text = "Save to library" };
      var loadLibrary = new Button { Text = "Load library" };

      ok.Click += (s, e) => { CommitAll(); Close(true); };
      cancel.Click += (s, e) => { Restore(); Close(false); };
      saveLibrary.Click += (s, e) => OnSaveLibrary();
      loadLibrary.Click += (s, e) => OnLoadLibrary();

      DefaultButton = ok;
      AbortButton = cancel;

      var buttons = new StackLayout
      {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Items =
        {
          saveLibrary, loadLibrary,
          new StackLayoutItem(null, true),
          cancel, ok
        }
      };

      var root = new DynamicLayout { Spacing = new Size(8, 8) };
      root.Add(tabs, yscale: true);
      root.Add(buttons);
      Content = root;

      ReloadAssemblies();
    }

    // ---- wall types tab ----------------------------------------------------

    Control BuildWallTypesTab()
    {
      var newType = new Button { Text = "New" };
      var duplicate = new Button { Text = "Duplicate" };
      var delete = new Button { Text = "Delete" };
      newType.Click += (s, e) => OnNewAssembly();
      duplicate.Click += (s, e) => OnDuplicateAssembly();
      delete.Click += (s, e) => OnDeleteAssembly();

      var left = new DynamicLayout { Spacing = new Size(6, 6) };
      left.Add(new Label { Text = "Wall types", Font = SystemFonts.Bold() });
      left.Add(_assemblyGrid, yscale: true);
      left.Add(new StackLayout
      {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        Items = { newType, duplicate, delete }
      });

      var identity = new DynamicLayout { Spacing = new Size(6, 4) };
      identity.AddRow(new Label { Text = "Code" }, _code, new Label { Text = "Name" }, _name);
      identity.AddRow(new Label { Text = "Fire (hr)" }, _fireRating, new Label { Text = "STC" }, _stc);
      identity.AddRow(new Label { Text = "Notes" }, _description);

      foreach (var box in new TextBox[] { _code, _name, _fireRating, _stc })
        box.LostFocus += (s, e) => CommitIdentity();
      _description.LostFocus += (s, e) => CommitIdentity();

      var addLayer = new Button { Text = "Add layer" };
      var removeLayer = new Button { Text = "Remove" };
      var up = new Button { Text = "↑ Exterior", ToolTip = "Move the layer toward the exterior" };
      var down = new Button { Text = "↓ Interior", ToolTip = "Move the layer toward the interior" };
      addLayer.Click += (s, e) => OnAddLayer();
      removeLayer.Click += (s, e) => OnRemoveLayer();
      up.Click += (s, e) => OnMoveLayer(-1);
      down.Click += (s, e) => OnMoveLayer(1);

      var right = new DynamicLayout { Spacing = new Size(6, 6) };
      right.Add(identity);
      right.Add(new Label
      {
        Text = "Layers run first (top) to last (bottom) — for a wall that is exterior " +
               "to interior. Tick Core on the structural layer; its centre line is what " +
               "the wall is drawn on.",
        TextColor = Colors.Gray,
        Wrap = WrapMode.Word
      });
      right.Add(_layerGrid, yscale: true);
      right.Add(new StackLayout
      {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        Items = { addLayer, removeLayer, up, down }
      });
      right.Add(_summary);

      var split = new DynamicLayout { Spacing = new Size(10, 0) };
      split.BeginHorizontal();
      split.Add(left, xscale: false);
      split.Add(right, xscale: true);
      split.EndHorizontal();
      return split;
    }

    void BuildAssemblyGrid()
    {
      _assemblyGrid.DataStore = _assemblyRows;
      _assemblyGrid.Width = 320;
      _assemblyGrid.Columns.Add(Column("Kind", 56, (AssemblyRow r) => r.Kind));
      _assemblyGrid.Columns.Add(Column("Code", 56, (AssemblyRow r) => r.Code));
      _assemblyGrid.Columns.Add(Column("Name", 170, (AssemblyRow r) => r.Name));
      _assemblyGrid.Columns.Add(Column("Thk", 60, (AssemblyRow r) => r.Thickness));
      _assemblyGrid.SelectedRowsChanged += (s, e) => OnAssemblySelected();
    }

    void BuildLayerGrid()
    {
      _layerGrid.DataStore = _layerRows;

      _layerGrid.Columns.Add(new GridColumn
      {
        HeaderText = "Core",
        Width = 44,
        Editable = true,
        DataCell = new CheckBoxCell
        {
          Binding = Binding.Delegate<LayerEditRow, bool?>(r => r.IsCore, (r, v) => r.IsCore = v ?? false)
        }
      });

      _layerGrid.Columns.Add(Combo("Function", 96, Enum.GetNames(typeof(LayerFunction)),
        (LayerEditRow r) => r.FunctionName, (r, v) => r.FunctionName = v));

      _layerGrid.Columns.Add(Combo("Product", 210, ProductNames(),
        (LayerEditRow r) => r.ProductName, (r, v) => r.ProductName = v));

      _layerGrid.Columns.Add(Combo("Cavity fill", 150, CavityNames(),
        (LayerEditRow r) => r.CavityName, (r, v) => r.CavityName = v));

      _layerGrid.Columns.Add(Column("Thickness", 78, (LayerEditRow r) => r.ThicknessText,
        (r, v) => r.ThicknessText = v));

      _layerGrid.Columns.Add(Combo("Jamb", 110, Enum.GetNames(typeof(EdgeResolution)),
        (LayerEditRow r) => r.JambName, (r, v) => r.JambName = v));

      _layerGrid.Columns.Add(Column("Return", 66, (LayerEditRow r) => r.ReturnText,
        (r, v) => r.ReturnText = v));

      _layerGrid.Columns.Add(Combo("Head", 110, Enum.GetNames(typeof(EdgeResolution)),
        (LayerEditRow r) => r.HeadName, (r, v) => r.HeadName = v));

      _layerGrid.Columns.Add(Combo("Sill", 110, Enum.GetNames(typeof(EdgeResolution)),
        (LayerEditRow r) => r.SillName, (r, v) => r.SillName = v));

      _layerGrid.CellEdited += (s, e) =>
      {
        if (_loading) return;
        if (e.Row >= 0 && e.Row < _layerRows.Count) _layerRows[e.Row].Apply(_model);
        if (_current != null) _current.NormalizeSides();
        RefreshSummary();
        RefreshAssemblyRow();
      };
    }

    // ---- products tab ------------------------------------------------------

    Control BuildProductsTab()
    {
      var newProduct = new Button { Text = "New" };
      var duplicate = new Button { Text = "Duplicate" };
      var delete = new Button { Text = "Delete" };
      newProduct.Click += (s, e) => OnNewProduct();
      duplicate.Click += (s, e) => OnDuplicateProduct();
      delete.Click += (s, e) => OnDeleteProduct();

      var layout = new DynamicLayout { Spacing = new Size(6, 6) };
      layout.Add(new Label
      {
        Text = "Products are what the wall is actually made of. Thickness is the real " +
               "installed thickness; cost is installed cost per square foot of wall face, " +
               "before the waste factor. The last three columns decide how the material " +
               "reads where a clipping plane cuts it — hatch pattern, hatch spacing and " +
               "the weight of the cut line.",
        TextColor = Colors.Gray,
        Wrap = WrapMode.Word
      });
      layout.Add(_productGrid, yscale: true);
      layout.Add(new StackLayout
      {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        Items = { newProduct, duplicate, delete }
      });
      return layout;
    }

    void BuildProductGrid()
    {
      _productGrid.DataStore = _productRows;
      _productGrid.Columns.Add(Column("Category", 100, (ProductRow r) => r.Category, (r, v) => r.Category = v));
      _productGrid.Columns.Add(Column("Name", 230, (ProductRow r) => r.Name, (r, v) => r.Name = v));
      _productGrid.Columns.Add(Column("Manufacturer", 120, (ProductRow r) => r.Manufacturer, (r, v) => r.Manufacturer = v));
      _productGrid.Columns.Add(Column("SKU", 90, (ProductRow r) => r.Sku, (r, v) => r.Sku = v));
      _productGrid.Columns.Add(Column("Thick", 66, (ProductRow r) => r.Thickness, (r, v) => r.Thickness = v));
      _productGrid.Columns.Add(Column("R/in", 54, (ProductRow r) => r.RPerInch, (r, v) => r.RPerInch = v));
      _productGrid.Columns.Add(Column("R total", 58, (ProductRow r) => r.RTotal, (r, v) => r.RTotal = v));
      _productGrid.Columns.Add(Column("$/sf", 58, (ProductRow r) => r.Cost, (r, v) => r.Cost = v));
      _productGrid.Columns.Add(Column("Waste", 54, (ProductRow r) => r.Waste, (r, v) => r.Waste = v));
      _productGrid.Columns.Add(Column("pcf", 54, (ProductRow r) => r.Density, (r, v) => r.Density = v));
      _productGrid.Columns.Add(Column("perms", 54, (ProductRow r) => r.Perm, (r, v) => r.Perm = v));
      _productGrid.Columns.Add(Column("Colour", 76, (ProductRow r) => r.ColorHex, (r, v) => r.ColorHex = v));
      _productGrid.Columns.Add(Combo("Section hatch", 150, SectionPatternNames.All,
        (ProductRow r) => r.SectionHatch, (r, v) => r.SectionHatch = v));
      _productGrid.Columns.Add(Column("Hatch scale", 76, (ProductRow r) => r.SectionHatchScale,
        (r, v) => r.SectionHatchScale = v));
      _productGrid.Columns.Add(Column("Cut weight", 74, (ProductRow r) => r.SectionWeight,
        (r, v) => r.SectionWeight = v));

      _productGrid.CellEdited += (s, e) =>
      {
        if (_loading) return;
        if (e.Row >= 0 && e.Row < _productRows.Count) _productRows[e.Row].Apply();
        _model.Catalog.SyncLayerNames();
        RefreshSummary();
      };
    }

    // ---- helpers -----------------------------------------------------------

    static GridColumn Column<T>(string header, int width, Func<T, string> get, Action<T, string> set = null)
    {
      return new GridColumn
      {
        HeaderText = header,
        Width = width,
        Editable = set != null,
        DataCell = new TextBoxCell { Binding = Binding.Delegate(get, set) }
      };
    }

    static GridColumn Combo<T>(string header, int width, IEnumerable<string> values,
                               Func<T, string> get, Action<T, string> set)
    {
      return new GridColumn
      {
        HeaderText = header,
        Width = width,
        Editable = true,
        DataCell = new ComboBoxCell
        {
          DataStore = values.Cast<object>().ToList(),
          Binding = Binding.Delegate<T, object>(r => get(r), (r, v) => set(r, v as string))
        }
      };
    }

    List<string> ProductNames()
      => _model.Catalog.Products.OrderBy(p => p.Category).ThenBy(p => p.Name).Select(p => p.Name).ToList();

    List<string> CavityNames()
    {
      var names = new List<string> { "" };
      names.AddRange(_model.Catalog.Products
        .Where(p => p.Category == "Insulation")
        .OrderBy(p => p.Name)
        .Select(p => p.Name));
      return names;
    }

    // ---- loading -----------------------------------------------------------

    void ReloadAssemblies()
    {
      _loading = true;
      try
      {
        _assemblyRows.Clear();
        // Grouped by kind so walls, floors and roofs do not run together.
        foreach (var assembly in _model.Catalog.Assemblies
                                       .OrderBy(a => a.Kind)
                                       .ThenBy(a => a.Code))
          _assemblyRows.Add(new AssemblyRow(_doc, _model, assembly));

        _productRows.Clear();
        foreach (var product in _model.Catalog.Products.OrderBy(p => p.Category).ThenBy(p => p.Name))
          _productRows.Add(new ProductRow(_doc, product));

        if (_assemblyRows.Count > 0)
        {
          _assemblyGrid.SelectRow(0);
          _current = _assemblyRows[0].Assembly;
        }
      }
      finally { _loading = false; }

      LoadCurrent();
    }

    void OnAssemblySelected()
    {
      if (_loading) return;
      int row = _assemblyGrid.SelectedRow;
      if (row < 0 || row >= _assemblyRows.Count) return;
      _current = _assemblyRows[row].Assembly;
      LoadCurrent();
    }

    void LoadCurrent()
    {
      _loading = true;
      try
      {
        _layerRows.Clear();
        if (_current == null)
        {
          _code.Text = _name.Text = _description.Text = "";
          _fireRating.Text = _stc.Text = "";
          _summary.Text = "";
          return;
        }

        _code.Text = _current.Code;
        _name.Text = _current.Name;
        _description.Text = _current.Description;
        _fireRating.Text = _current.FireRatingHours.ToString("0.##", CultureInfo.CurrentCulture);
        _stc.Text = _current.StcRating.ToString(CultureInfo.CurrentCulture);

        foreach (var layer in _current.Layers)
          _layerRows.Add(new LayerEditRow(_doc, _model, layer));
      }
      finally { _loading = false; }

      RefreshSummary();
    }

    void RefreshSummary()
    {
      if (_current == null) { _summary.Text = ""; return; }
      _summary.Text = string.Format(CultureInfo.CurrentCulture,
        "{0} overall · R-{1:0.0} nominal · R-{2:0.0} effective · {3:0.0} psf · ${4:0.00}/sf",
        Units.FormatInches(_doc, _current.TotalThicknessIn),
        _current.RValue(_model.Catalog),
        _current.EffectiveRValue(_model.Catalog),
        _current.WeightPsf(_model.Catalog),
        _current.CostPerSqFt(_model.Catalog));
    }

    void RefreshAssemblyRow()
    {
      int row = _assemblyGrid.SelectedRow;
      if (row < 0 || row >= _assemblyRows.Count) return;
      _assemblyRows[row] = new AssemblyRow(_doc, _model, _assemblyRows[row].Assembly);
      _assemblyGrid.SelectRow(row);
    }

    void CommitIdentity()
    {
      if (_loading || _current == null) return;

      if (!string.IsNullOrWhiteSpace(_code.Text)) _current.Code = _code.Text.Trim();
      if (!string.IsNullOrWhiteSpace(_name.Text)) _current.Name = _name.Text.Trim();
      _current.Description = _description.Text ?? "";

      double fire;
      if (Units.TryParseNumber(_fireRating.Text, out fire))
        _current.FireRatingHours = fire;

      double stc;
      if (Units.TryParseNumber(_stc.Text, out stc)) _current.StcRating = (int)Math.Round(stc);

      RefreshAssemblyRow();
    }

    void CommitAll()
    {
      CommitIdentity();
      foreach (var row in _layerRows) row.Apply(_model);
      foreach (var row in _productRows) row.Apply();
      _model.Catalog.SyncLayerNames();
      foreach (var assembly in _model.Catalog.Assemblies) assembly.NormalizeSides();
    }

    void Restore()
    {
      var restored = AssemblyCatalog.FromDictionary(_snapshot);
      _model.Catalog.Products.Clear();
      _model.Catalog.Products.AddRange(restored.Products);
      _model.Catalog.Assemblies.Clear();
      _model.Catalog.Assemblies.AddRange(restored.Assemblies);
    }

    // ---- actions -----------------------------------------------------------

    void OnNewAssembly()
    {
      var product = _model.Catalog.Products.FirstOrDefault(p => p.StructuralCapable)
                    ?? _model.Catalog.Products.FirstOrDefault();

      var assembly = new LayeredAssembly
      {
        Code = NextCode(),
        Name = "New wall type"
      };
      if (product != null)
      {
        assembly.Layers.Add(new AssemblyLayer
        {
          ProductId = product.Id,
          ProductName = product.Name,
          ThicknessIn = product.ThicknessIn,
          Function = LayerFunction.Structure,
          IsCore = true
        });
      }
      assembly.NormalizeSides();

      _model.Catalog.Assemblies.Add(assembly);
      ReloadAssemblies();
      SelectAssembly(assembly);
    }

    void OnDuplicateAssembly()
    {
      if (_current == null) return;
      var copy = _current.Duplicate();
      copy.Code = NextCode();
      copy.Name = _current.Name + " (copy)";
      _model.Catalog.Assemblies.Add(copy);
      ReloadAssemblies();
      SelectAssembly(copy);
    }

    void OnDeleteAssembly()
    {
      if (_current == null) return;

      int inUse = _model.WallsUsing(_current.Id).Count() +
                  _model.ElementsUsing(_current.Id).Count();
      if (inUse > 0)
      {
        MessageBox.Show(this,
          string.Format("{0} wall{1} still use this type. Retype them first.",
                        inUse, inUse == 1 ? "" : "s"),
          "Stratum", MessageBoxButtons.OK, MessageBoxType.Warning);
        return;
      }

      _model.Catalog.Assemblies.Remove(_current);
      _current = null;
      ReloadAssemblies();
    }

    void SelectAssembly(LayeredAssembly assembly)
    {
      int index = _assemblyRows.ToList().FindIndex(r => r.Assembly.Id == assembly.Id);
      if (index < 0) return;
      _assemblyGrid.SelectRow(index);
      _current = assembly;
      LoadCurrent();
    }

    string NextCode()
    {
      for (int i = 1; i < 500; i++)
      {
        string candidate = "W" + i.ToString(CultureInfo.InvariantCulture);
        if (_model.Catalog.FindAssemblyByCode(candidate) == null) return candidate;
      }
      return "W" + Guid.NewGuid().ToString("N").Substring(0, 4);
    }

    void OnAddLayer()
    {
      if (_current == null) return;
      var product = _model.Catalog.Products.FirstOrDefault();
      int at = Math.Max(0, _layerGrid.SelectedRow + 1);

      _current.Layers.Insert(Math.Min(at, _current.Layers.Count), new AssemblyLayer
      {
        ProductId = product?.Id ?? Guid.Empty,
        ProductName = product?.Name ?? "",
        ThicknessIn = product?.ThicknessIn ?? 0.5,
        Function = LayerFunction.Finish
      });
      _current.NormalizeSides();
      LoadCurrent();
      RefreshAssemblyRow();
    }

    void OnRemoveLayer()
    {
      if (_current == null) return;
      int row = _layerGrid.SelectedRow;
      if (row < 0 || row >= _current.Layers.Count) return;
      _current.Layers.RemoveAt(row);
      _current.NormalizeSides();
      LoadCurrent();
      RefreshAssemblyRow();
    }

    void OnMoveLayer(int delta)
    {
      if (_current == null) return;
      int row = _layerGrid.SelectedRow;
      int target = row + delta;
      if (row < 0 || target < 0 || row >= _current.Layers.Count || target >= _current.Layers.Count) return;

      var layer = _current.Layers[row];
      _current.Layers.RemoveAt(row);
      _current.Layers.Insert(target, layer);
      _current.NormalizeSides();
      LoadCurrent();
      _layerGrid.SelectRow(target);
    }

    void OnNewProduct()
    {
      var product = new MaterialProduct { Name = "New product", Category = "General" };
      CatalogDefaults.ApplySectionDefaults(product);
      _model.Catalog.Products.Add(product);
      ReloadAssemblies();
    }

    void OnDuplicateProduct()
    {
      int row = _productGrid.SelectedRow;
      if (row < 0 || row >= _productRows.Count) return;
      var copy = _productRows[row].Product.Duplicate();
      copy.Name = copy.Name + " (copy)";
      _model.Catalog.Products.Add(copy);
      ReloadAssemblies();
    }

    void OnDeleteProduct()
    {
      int row = _productGrid.SelectedRow;
      if (row < 0 || row >= _productRows.Count) return;
      var product = _productRows[row].Product;

      bool used = _model.Catalog.Assemblies
        .SelectMany(a => a.Layers)
        .Any(l => l.ProductId == product.Id || l.CavityProductId == product.Id);

      if (used)
      {
        MessageBox.Show(this, "That product is used by a wall type. Replace it there first.",
                        "Stratum", MessageBoxButtons.OK, MessageBoxType.Warning);
        return;
      }

      _model.Catalog.Products.Remove(product);
      ReloadAssemblies();
    }

    void OnSaveLibrary()
    {
      CommitAll();
      string error;
      if (_model.Catalog.SaveToFile(AssemblyCatalog.DefaultLibraryPath, out error))
        MessageBox.Show(this, "Library saved to\n" + AssemblyCatalog.DefaultLibraryPath, "Stratum");
      else
        MessageBox.Show(this, "Could not save the library:\n" + error, "Stratum",
                        MessageBoxButtons.OK, MessageBoxType.Error);
    }

    void OnLoadLibrary()
    {
      string error;
      var library = AssemblyCatalog.LoadFromFile(AssemblyCatalog.DefaultLibraryPath, out error);
      if (library == null)
      {
        MessageBox.Show(this, "Could not load the library:\n" + error, "Stratum",
                        MessageBoxButtons.OK, MessageBoxType.Error);
        return;
      }

      _model.Catalog.Merge(library, overwrite: true);
      _model.Catalog.SyncLayerNames();
      ReloadAssemblies();
    }
  }

  // ---- row models ----------------------------------------------------------

  public class AssemblyRow
  {
    public LayeredAssembly Assembly { get; }
    public string Kind { get; }
    public string Code { get; }
    public string Name { get; }
    public string Thickness { get; }

    public AssemblyRow(RhinoDoc doc, BimModel model, LayeredAssembly assembly)
    {
      Assembly = assembly;
      Kind = assembly.Kind.ToString();
      Code = assembly.Code;
      Name = assembly.Name;
      Thickness = Units.FormatInches(doc, assembly.TotalThicknessIn);
    }
  }

  public class LayerEditRow
  {
    readonly RhinoDoc _doc;
    public AssemblyLayer Layer { get; }

    public bool IsCore { get; set; }
    public string FunctionName { get; set; }
    public string ProductName { get; set; }
    public string CavityName { get; set; }
    public string ThicknessText { get; set; }
    public string JambName { get; set; }
    public string HeadName { get; set; }
    public string SillName { get; set; }
    public string ReturnText { get; set; }

    string _originalProduct;

    public LayerEditRow(RhinoDoc doc, BimModel model, AssemblyLayer layer)
    {
      _doc = doc;
      Layer = layer;

      IsCore = layer.IsCore;
      FunctionName = layer.Function.ToString();
      ProductName = model.Catalog.FindProduct(layer.ProductId)?.Name ?? layer.ProductName;
      CavityName = model.Catalog.FindProduct(layer.CavityProductId)?.Name ?? "";
      ThicknessText = Units.FormatInches(doc, layer.ThicknessIn);
      JambName = layer.JambResolution.ToString();
      HeadName = layer.HeadResolution.ToString();
      SillName = layer.SillResolution.ToString();
      ReturnText = Units.FormatInches(doc, layer.JambReturnIn);
      _originalProduct = ProductName;
    }

    public void Apply(BimModel model)
    {
      Layer.IsCore = IsCore;

      LayerFunction function;
      if (Enum.TryParse(FunctionName, out function)) Layer.Function = function;

      EdgeResolution resolution;
      if (Enum.TryParse(JambName, out resolution)) Layer.JambResolution = resolution;
      if (Enum.TryParse(HeadName, out resolution)) Layer.HeadResolution = resolution;
      if (Enum.TryParse(SillName, out resolution)) Layer.SillResolution = resolution;

      var product = model.Catalog.FindProductByName(ProductName);
      if (product != null)
      {
        bool changed = !string.Equals(product.Name, _originalProduct, StringComparison.Ordinal);
        Layer.ProductId = product.Id;
        Layer.ProductName = product.Name;
        if (changed && product.ThicknessIn > 0)
        {
          Layer.ThicknessIn = product.ThicknessIn;
          ThicknessText = Units.FormatInches(_doc, Layer.ThicknessIn);
        }
        _originalProduct = product.Name;
      }

      var cavity = string.IsNullOrWhiteSpace(CavityName) ? null : model.Catalog.FindProductByName(CavityName);
      Layer.CavityProductId = cavity?.Id ?? Guid.Empty;
      Layer.CavityProductName = cavity?.Name ?? "";

      double inches;
      if (Units.TryParseInches(ThicknessText, out inches) && inches >= 0) Layer.ThicknessIn = inches;
      if (Units.TryParseInches(ReturnText, out inches) && inches >= 0)
      {
        Layer.JambReturnIn = inches;
        Layer.HeadReturnIn = inches;
        Layer.SillReturnIn = inches;
      }
    }
  }

  public class ProductRow
  {
    public MaterialProduct Product { get; }

    public string Category { get; set; }
    public string Name { get; set; }
    public string Manufacturer { get; set; }
    public string Sku { get; set; }
    public string Thickness { get; set; }
    public string RPerInch { get; set; }
    public string RTotal { get; set; }
    public string Cost { get; set; }
    public string Waste { get; set; }
    public string Density { get; set; }
    public string Perm { get; set; }
    public string ColorHex { get; set; }
    public string SectionHatch { get; set; }
    public string SectionHatchScale { get; set; }
    public string SectionWeight { get; set; }

    public ProductRow(RhinoDoc doc, MaterialProduct product)
    {
      Product = product;
      Category = product.Category;
      Name = product.Name;
      Manufacturer = product.Manufacturer;
      Sku = product.Sku;
      Thickness = Units.FormatInches(doc, product.ThicknessIn);
      RPerInch = product.RPerInch.ToString("0.##", CultureInfo.CurrentCulture);
      RTotal = product.RValueTotal.ToString("0.##", CultureInfo.CurrentCulture);
      Cost = product.CostPerSqFt.ToString("0.00", CultureInfo.CurrentCulture);
      Waste = product.WasteFactor.ToString("0.##", CultureInfo.CurrentCulture);
      Density = product.DensityPcf.ToString("0.##", CultureInfo.CurrentCulture);
      Perm = product.PermRating.ToString("0.###", CultureInfo.CurrentCulture);
      ColorHex = ToHex(product.Color);
      SectionHatch = product.SectionHatchPattern ?? "";
      SectionHatchScale = product.SectionHatchScale.ToString("0.##", CultureInfo.CurrentCulture);
      SectionWeight = product.SectionLineWeightScale.ToString("0.##", CultureInfo.CurrentCulture);
    }

    public void Apply()
    {
      if (!string.IsNullOrWhiteSpace(Category)) Product.Category = Category.Trim();
      if (!string.IsNullOrWhiteSpace(Name)) Product.Name = Name.Trim();
      Product.Manufacturer = Manufacturer ?? "";
      Product.Sku = Sku ?? "";

      double value;
      if (Units.TryParseInches(Thickness, out value) && value >= 0) Product.ThicknessIn = value;
      if (TryNum(RPerInch, out value)) Product.RPerInch = value;
      if (TryNum(RTotal, out value)) Product.RValueTotal = value;
      if (TryNum(Cost, out value)) Product.CostPerSqFt = value;
      if (TryNum(Waste, out value)) Product.WasteFactor = value;
      if (TryNum(Density, out value)) Product.DensityPcf = value;
      if (TryNum(Perm, out value)) Product.PermRating = value;

      var parsed = FromHex(ColorHex);
      if (parsed.HasValue) Product.Color = parsed.Value;

      Product.SectionHatchPattern = SectionHatch ?? "";
      if (TryNum(SectionHatchScale, out value) && value > 0) Product.SectionHatchScale = value;
      if (TryNum(SectionWeight, out value) && value > 0) Product.SectionLineWeightScale = value;
    }

    // Routed through Units so a comma-decimal locale cannot silently read
    // a cost of "2.10" as 210. See Units.TryParseNumber.
    static bool TryNum(string text, out double value) => Units.TryParseNumber(text, out value);

    static string ToHex(System.Drawing.Color color)
      => string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);

    static System.Drawing.Color? FromHex(string text)
    {
      if (string.IsNullOrWhiteSpace(text)) return null;
      var s = text.Trim().TrimStart('#');
      if (s.Length != 6) return null;

      int rgb;
      if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return null;

      return System.Drawing.Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }
  }
}
