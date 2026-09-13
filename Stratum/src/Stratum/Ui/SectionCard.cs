using System;
using Eto.Drawing;
using Eto.Forms;

namespace Stratum.Ui
{
  /// <summary>
  /// A titled, collapsible group of related controls.
  ///
  /// The panel is a long column of things a wall knows about itself. Without
  /// grouping it reads as one undifferentiated list and the eye has nowhere to
  /// land; with it, "Identity", "Placement" and "Assembly" are three shapes you
  /// can skip between. Collapsing matters because a docked Rhino panel is short:
  /// closing the two sections you are not using is what makes the third one fit.
  ///
  /// The open/closed state is remembered per key in <see cref="PanelState"/>, so
  /// a panel that is reloaded on every selection change - which this one is -
  /// does not spring back open and undo the user's arrangement.
  /// </summary>
  public class SectionCard : Panel
  {
    readonly Label _twisty = new Label { Font = PanelStyle.Small, TextColor = PanelStyle.Muted };
    readonly Label _title = new Label { Font = PanelStyle.SectionTitle, TextColor = PanelStyle.Muted };
    readonly Label _summary = new Label { Font = PanelStyle.Small, TextColor = PanelStyle.Faint };
    readonly Panel _bodyHost = new Panel();
    readonly string _key;

    bool _expanded = true;

    public SectionCard(string title, Control body, string stateKey = null, bool defaultExpanded = true)
    {
      _key = stateKey ?? title;
      _title.Text = (title ?? "").ToUpperInvariant();
      _bodyHost.Content = body;

      _expanded = PanelState.IsExpanded(_key, defaultExpanded);

      var headerRow = new StackLayout
      {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        VerticalContentAlignment = VerticalAlignment.Center,
        Items =
        {
          _twisty,
          _title,
          new StackLayoutItem(_summary, true)
        }
      };

      // The whole header is the hit target, not just the triangle - a 9px glyph
      // is not something to ask anyone to aim at.
      var header = new Panel
      {
        Content = headerRow,
        Padding = new Padding(8, 6),
        Cursor = Cursors.Pointer,
        BackgroundColor = PanelStyle.Blend(PanelStyle.CardBack, PanelStyle.Line, 0.35f)
      };
      header.MouseDown += (s, e) => { Toggle(); e.Handled = true; };

      var shell = new DynamicLayout { BackgroundColor = PanelStyle.CardBack };
      shell.Add(header);
      shell.Add(_bodyHost);

      // One hairline of border, drawn as padding over a line-coloured ground.
      Content = new Panel
      {
        BackgroundColor = PanelStyle.Line,
        Padding = new Padding(1),
        Content = shell
      };

      ApplyExpanded();
    }

    /// <summary>The grey note on the right of the header - used to keep the most
    /// important number of a collapsed section visible while it is closed.</summary>
    public string Summary
    {
      get { return _summary.Text; }
      set { _summary.Text = value ?? ""; }
    }

    public bool Expanded
    {
      get { return _expanded; }
      set
      {
        if (_expanded == value) return;
        _expanded = value;
        PanelState.SetExpanded(_key, value);
        ApplyExpanded();
      }
    }

    public event EventHandler ExpandedChanged;

    void Toggle()
    {
      Expanded = !Expanded;
      var handler = ExpandedChanged;
      if (handler != null) handler(this, EventArgs.Empty);
    }

    void ApplyExpanded()
    {
      _twisty.Text = _expanded ? "▾" : "▸";
      _bodyHost.Visible = _expanded;
    }
  }

  /// <summary>
  /// Which sections the user has open, kept alive across the panel reloads that
  /// every selection change triggers. Static because the panel object itself is
  /// rebuilt by Rhino when it is docked, floated or reopened, and the layout the
  /// user arranged should survive that.
  /// </summary>
  public static class PanelState
  {
    static readonly System.Collections.Generic.Dictionary<string, bool> _expanded =
      new System.Collections.Generic.Dictionary<string, bool>(StringComparer.Ordinal);

    public static bool IsExpanded(string key, bool fallback)
    {
      bool value;
      return _expanded.TryGetValue(key ?? "", out value) ? value : fallback;
    }

    public static void SetExpanded(string key, bool value)
    {
      _expanded[key ?? ""] = value;
    }
  }
}
