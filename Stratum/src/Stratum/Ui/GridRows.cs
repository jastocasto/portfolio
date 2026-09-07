using System;
using System.Globalization;
using System.Linq;
using Rhino;
using Stratum.Core;

namespace Stratum.Ui
{
  /// <summary>
  /// One editable line of the layer grid. The row holds display strings; Apply
  /// parses them back onto the real <see cref="AssemblyLayer"/>. Keeping the
  /// parsing here means the grid accepts what a builder would actually type -
  /// 3/4, 0.75, 5-1/2, 140mm - rather than demanding decimals.
  /// </summary>
  public class LayerRow
  {
    readonly RhinoDoc _doc;
    readonly BimModel _model;

    public AssemblyLayer Layer { get; }

    public bool Enabled { get; set; }
    public bool IsCore { get; set; }
    public string FunctionName { get; set; }
    public string ProductName { get; set; }
    public string ThicknessText { get; set; }

    string _originalProductName;

    public LayerRow(RhinoDoc doc, BimModel model, AssemblyLayer layer)
    {
      _doc = doc;
      _model = model;
      Layer = layer;

      Enabled = layer.Enabled;
      IsCore = layer.IsCore;
      FunctionName = layer.Function.ToString();

      var product = model.Catalog.FindProduct(layer.ProductId);
      ProductName = product?.Name ?? layer.ProductName;
      _originalProductName = ProductName;

      ThicknessText = Units.FormatInches(doc, layer.ThicknessIn);
    }

    public string RText
    {
      get
      {
        var product = _model.Catalog.FindProduct(Layer.ProductId);
        double r = product?.RValueAt(Layer.ThicknessIn) ?? 0.0;

        var cavity = _model.Catalog.FindProduct(Layer.CavityProductId);
        if (cavity != null) r += cavity.RValueAt(Layer.ThicknessIn);

        return r > 0.001 ? r.ToString("0.0", CultureInfo.CurrentCulture) : "—";
      }
    }

    public string CostText
    {
      get
      {
        var product = _model.Catalog.FindProduct(Layer.ProductId);
        double cost = product == null ? 0.0 : product.CostPerSqFt * (1.0 + product.WasteFactor);

        var cavity = _model.Catalog.FindProduct(Layer.CavityProductId);
        if (cavity != null) cost += cavity.CostPerSqFt * (1.0 + cavity.WasteFactor);

        return cost > 0.001 ? cost.ToString("0.00", CultureInfo.CurrentCulture) : "—";
      }
    }

    /// <summary>Writes the edited values back onto the assembly layer.</summary>
    public void Apply(BimModel model)
    {
      Layer.Enabled = Enabled;
      Layer.IsCore = IsCore;

      LayerFunction function;
      if (Enum.TryParse(FunctionName, out function)) Layer.Function = function;

      bool productChanged = !string.Equals(ProductName, _originalProductName, StringComparison.Ordinal);
      if (productChanged)
      {
        var product = model.Catalog.FindProductByName(ProductName);
        if (product != null)
        {
          Layer.ProductId = product.Id;
          Layer.ProductName = product.Name;

          // Swapping 1/2" plywood for 3/4" plywood should change the thickness.
          // Swapping it for a cavity-filling product should not: those are sized
          // by the assembly, not by the product.
          if (product.ThicknessIn > 0.0) Layer.ThicknessIn = product.ThicknessIn;

          ThicknessText = Units.FormatInches(_doc, Layer.ThicknessIn);
          _originalProductName = product.Name;
        }
      }
      else
      {
        double inches;
        if (Units.TryParseInches(ThicknessText, out inches) && inches >= 0.0)
          Layer.ThicknessIn = inches;
      }
    }
  }

  /// <summary>One editable line of the openings grid.</summary>
  public class OpeningRow
  {
    readonly RhinoDoc _doc;

    public Opening Opening { get; }

    public string Name { get; set; }
    public string KindName { get; set; }
    public string WidthText { get; set; }
    public string HeightText { get; set; }
    public string SillText { get; set; }
    public string StationText { get; set; }
    public string UnitName { get; set; }

    public OpeningRow(RhinoDoc doc, Opening opening, AssemblyCatalog catalog = null)
    {
      _doc = doc;
      Opening = opening;

      UnitName = catalog?.FindUnit(opening.UnitId)?.Name ?? "";
      Name = opening.Name;
      KindName = opening.Kind.ToString();
      WidthText = Units.FormatInches(doc, opening.WidthIn);
      HeightText = Units.FormatInches(doc, opening.HeightIn);
      SillText = Units.FormatInches(doc, opening.SillHeightIn);
      StationText = Units.FormatInches(doc, opening.StationAlongWall * Units.ModelToInch(doc));
    }

    public void Apply(AssemblyCatalog catalog = null)
    {
      Opening.Name = string.IsNullOrWhiteSpace(Name) ? Opening.Name : Name.Trim();

      // Re-typing an opening pulls its sizes from the unit, so the schedule and the
      // hole stay in step.
      if (catalog != null)
      {
        var unit = string.IsNullOrWhiteSpace(UnitName)
          ? null
          : catalog.OpeningUnits.FirstOrDefault(u =>
              string.Equals(u.Name, UnitName, StringComparison.OrdinalIgnoreCase));

        Opening.UnitId = unit?.Id ?? Guid.Empty;
        if (unit != null)
        {
          Opening.Resolve(catalog);
          WidthText = Units.FormatInches(_doc, Opening.WidthIn);
          HeightText = Units.FormatInches(_doc, Opening.HeightIn);
        }
      }

      OpeningKind kind;
      if (Enum.TryParse(KindName, out kind)) Opening.Kind = kind;

      double value;
      if (Units.TryParseInches(WidthText, out value) && value > 0) Opening.WidthIn = value;
      if (Units.TryParseInches(HeightText, out value) && value > 0) Opening.HeightIn = value;
      if (Units.TryParseInches(SillText, out value) && value >= 0) Opening.SillHeightIn = value;
      if (Units.TryParseInches(StationText, out value) && value >= 0)
        Opening.StationAlongWall = value * Units.InchToModel(_doc);
    }
  }
}
