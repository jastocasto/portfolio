using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Stratum.Core;

namespace Stratum.Ui
{
  /// <summary>
  /// A to-scale cross-section of the wall type, drawn exterior (left) to interior
  /// (right), with the reference line shown where the justification actually puts
  /// it and the structural core called out.
  ///
  /// This is the control that turns a table of numbers into something a person can
  /// check at a glance: if the reference line is not through the core, or the
  /// rainscreen cavity ended up on the inside, you see it here before you draw
  /// three hundred feet of wall with it.
  /// </summary>
  public class AssemblyPreview : Drawable
  {
    class Band
    {
      public int LayerIndex;
      public float X0, X1;
      public Color Fill;
      public bool IsCore;
      public string Label;
    }

    readonly List<Band> _bands = new List<Band>();

    RhinoDoc _doc;
    BimModel _model;
    WallAssembly _assembly;
    WallJustification _justification = WallJustification.CoreCenter;
    bool _flipped;

    /// <summary>Raised when the user clicks a layer in the section.</summary>
    public event EventHandler<int> LayerClicked;

    public AssemblyPreview()
    {
      Height = 86;
      Paint += OnPaintSection;
      MouseDown += OnMouseDownSection;
    }

    public void Update(RhinoDoc doc, BimModel model, WallAssembly assembly,
                       WallJustification justification, bool flipped)
    {
      _doc = doc;
      _model = model;
      _assembly = assembly;
      _justification = justification;
      _flipped = flipped;
      Invalidate();
    }

    // ------------------------------------------------------------------------

    static Color ToEto(System.Drawing.Color c) => Color.FromArgb(c.R, c.G, c.B, 255);

    static Color Darken(Color c, float f)
      => Color.FromArgb((int)(c.Rb * f), (int)(c.Gb * f), (int)(c.Bb * f), 255);

    void OnPaintSection(object sender, PaintEventArgs e)
    {
      var g = e.Graphics;
      _bands.Clear();

      var ink = Colors.Gray;
      var small = SystemFonts.Default(7.5f);

      if (_assembly == null || _assembly.TotalThicknessIn <= 0.0)
      {
        g.DrawText(small, ink, 6, 6, "No wall type selected.");
        return;
      }

      float left = 8f, right = Math.Max(left + 40f, Width - 8f);
      float top = 20f, bottom = 58f;
      float span = right - left;
      double total = _assembly.TotalThicknessIn;

      // Exterior is on the left of the drawing. Flipping a wall swaps which way it
      // faces in the model, so the section is mirrored to match what is on screen.
      bool mirror = _flipped;

      float Map(double stationInches)
      {
        double f = stationInches / total;
        if (mirror) f = 1.0 - f;
        return left + (float)(f * span);
      }

      int core = _assembly.CoreIndex;
      double u = 0.0;

      for (int i = 0; i < _assembly.Layers.Count; i++)
      {
        var layer = _assembly.Layers[i];
        if (!layer.Enabled) continue;

        double t = Math.Max(0.0, layer.ThicknessIn);
        if (t <= 1e-9) continue;

        float a = Map(u), b = Map(u + t);
        float x0 = Math.Min(a, b), x1 = Math.Max(a, b);

        // Membranes are real but hairline thin. Keep the drawing honest by leaving
        // them at true scale, with a floor of one visible pixel.
        if (x1 - x0 < 1.5f) x1 = x0 + 1.5f;

        var product = _model?.Catalog.FindProduct(layer.ProductId);
        var fill = product != null ? ToEto(product.Color) : Color.FromArgb(170, 170, 170, 255);

        _bands.Add(new Band
        {
          LayerIndex = i,
          X0 = x0,
          X1 = x1,
          Fill = fill,
          IsCore = (i == core),
          Label = product?.Name ?? layer.Function.ToString()
        });

        u += t;
      }

      foreach (var band in _bands)
      {
        var rect = new RectangleF(band.X0, top, band.X1 - band.X0, bottom - top);
        g.FillRectangle(band.Fill, rect);
        g.DrawRectangle(Darken(band.Fill, 0.55f), rect);

        if (band.IsCore)
        {
          // Call the core out: a heavier frame and a centre tick.
          g.DrawRectangle(new Pen(Color.FromArgb(30, 30, 30, 255), 2f), rect);
          float mid = (band.X0 + band.X1) * 0.5f;
          g.DrawLine(Color.FromArgb(30, 30, 30, 255), mid, top + 3, mid, bottom - 3);
        }
      }

      // ---- the reference line ------------------------------------------------
      double baselineStation = _assembly.BaselineStation(_justification);
      float bx = Map(baselineStation);
      var accent = Color.FromArgb(40, 120, 215, 255);

      g.DrawLine(new Pen(accent, 2f), bx, top - 9, bx, bottom + 9);
      g.FillRectangle(accent, bx - 3f, top - 12f, 6f, 4f);

      // ---- annotation --------------------------------------------------------
      string extLabel = "EXTERIOR", intLabel = "INTERIOR";
      g.DrawText(small, ink, left, 4, mirror ? intLabel : extLabel);

      string rightLabel = mirror ? extLabel : intLabel;
      float rightWidth = small.MeasureString(rightLabel).Width;
      g.DrawText(small, ink, right - rightWidth, 4, rightLabel);

      string footer = string.Format("{0} overall · reference line at {1} from the exterior face",
        Units.FormatInches(_doc, total),
        Units.FormatInches(_doc, baselineStation));
      g.DrawText(small, ink, left, bottom + 10, footer);
    }

    void OnMouseDownSection(object sender, MouseEventArgs e)
    {
      float x = e.Location.X;
      foreach (var band in _bands)
      {
        if (x < band.X0 || x > band.X1) continue;
        var handler = LayerClicked;
        if (handler != null) handler(this, band.LayerIndex);
        return;
      }
    }
  }
}
