using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;

namespace Stratum.Ui
{
  /// <summary>
  /// The headline numbers of an assembly - thickness, R-value, cost, weight -
  /// shown as a row of chips rather than a list of label/value rows.
  ///
  /// These are the four figures a person checks most often and they used to sit
  /// at the very bottom of a tab, underneath a grid tall enough to push them off
  /// the panel. As chips they stay near the top, read in one glance, and each one
  /// carries its own detail line underneath for the qualifier that would
  /// otherwise make the value unreadable ("R-19.4 nominal · R-16.8 effective").
  /// </summary>
  public class MetricStrip : Panel
  {
    /// <summary>One chip. Held so the values can be refreshed without rebuilding
    /// the layout on every selection change.</summary>
    class Chip
    {
      public Label Caption;
      public Label Value;
      public Label Detail;
      public Panel Host;
    }

    readonly Dictionary<string, Chip> _chips = new Dictionary<string, Chip>();
    readonly DynamicLayout _grid = new DynamicLayout { Spacing = new Size(6, 6) };

    /// <summary>Chips per row. Two keeps each chip wide enough for "$12.40/sf"
    /// in the narrow docked panel most people leave this in.</summary>
    readonly int _columns;

    public MetricStrip(int columns = 2)
    {
      _columns = columns;
      Content = _grid;
    }

    /// <summary>Declares the chips, in order. Call once at build time.</summary>
    public void Define(params string[] captions)
    {
      _grid.Clear();
      _chips.Clear();

      var row = new List<Control>();
      foreach (var caption in captions)
      {
        var chip = MakeChip(caption);
        _chips[caption] = chip;
        row.Add(chip.Host);

        if (row.Count != _columns) continue;
        AddRow(row);
        row = new List<Control>();
      }

      // A trailing partial row still has to fill the width, or the last chip
      // stretches to twice the size of its neighbours.
      if (row.Count > 0)
      {
        while (row.Count < _columns) row.Add(new Panel());
        AddRow(row);
      }

      _grid.Create();
    }

    /// <summary>Lays one row of chips out in a table whose columns all scale, so
    /// the chips share the panel width evenly however narrow it is docked.</summary>
    void AddRow(List<Control> row)
    {
      var table = new TableLayout { Spacing = new Size(6, 0) };
      var cells = new TableRow();
      foreach (var control in row)
        cells.Cells.Add(new TableCell(control, true));
      table.Rows.Add(cells);

      _grid.Add(table);
    }

    Chip MakeChip(string caption)
    {
      var captionLabel = new Label
      {
        Text = caption.ToUpperInvariant(),
        Font = PanelStyle.Small,
        TextColor = PanelStyle.Faint
      };

      var valueLabel = new Label
      {
        Text = "—",
        Font = PanelStyle.Strong,
        TextColor = PanelStyle.Ink
      };

      var detailLabel = new Label
      {
        Text = "",
        Font = PanelStyle.Small,
        TextColor = PanelStyle.Muted,
        Wrap = WrapMode.Word,
        Visible = false
      };

      var stack = new DynamicLayout { Spacing = new Size(0, 1), Padding = new Padding(8, 6) };
      stack.Add(captionLabel);
      stack.Add(valueLabel);
      stack.Add(detailLabel);

      var host = new Panel
      {
        BackgroundColor = PanelStyle.Blend(PanelStyle.CardBack, PanelStyle.Line, 0.25f),
        Content = stack
      };

      return new Chip { Caption = captionLabel, Value = valueLabel, Detail = detailLabel, Host = host };
    }

    /// <summary>Sets a chip's value and its optional second line.</summary>
    public void Set(string caption, string value, string detail = null)
    {
      Chip chip;
      if (!_chips.TryGetValue(caption, out chip)) return;

      chip.Value.Text = string.IsNullOrEmpty(value) ? "—" : value;
      chip.Detail.Text = detail ?? "";
      chip.Detail.Visible = !string.IsNullOrEmpty(detail);
    }

    /// <summary>Blanks every chip - the empty state, when nothing is selected.</summary>
    public void Clear()
    {
      foreach (var chip in _chips.Values)
      {
        chip.Value.Text = "—";
        chip.Detail.Text = "";
        chip.Detail.Visible = false;
      }
    }
  }
}
