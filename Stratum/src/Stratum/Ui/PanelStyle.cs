using Eto.Drawing;
using Eto.Forms;

namespace Stratum.Ui
{
  /// <summary>
  /// The small vocabulary of colours, fonts and spacings the panels are built
  /// from. It exists so that "a secondary label" is one decision made once,
  /// rather than a <c>Colors.Gray</c> repeated in thirty places and drifting.
  ///
  /// Every colour is derived from the host's own theme rather than hard-coded,
  /// because Rhino 8 ships a light and a dark scheme and a panel that assumes
  /// either one is unreadable in the other.
  /// </summary>
  public static class PanelStyle
  {
    /// <summary>True when Rhino is running a dark colour scheme. Decided from the
    /// theme's own window background, so it follows the host rather than a
    /// setting this plug-in would have to keep in sync.</summary>
    public static bool IsDark
    {
      get
      {
        var back = SystemColors.ControlBackground;
        // Rec. 601 luma. Anything below the midpoint is a dark theme.
        double luma = 0.299 * back.Rb + 0.587 * back.Gb + 0.114 * back.Bb;
        return luma < 128.0;
      }
    }

    /// <summary>Body text.</summary>
    public static Color Ink => SystemColors.ControlText;

    /// <summary>Labels, units, and anything that names a value rather than being
    /// one. Readable, but visibly quieter than the value beside it.</summary>
    public static Color Muted => Blend(SystemColors.ControlText, SystemColors.ControlBackground, 0.45f);

    /// <summary>Captions and hints - the quietest text that is still text.</summary>
    public static Color Faint => Blend(SystemColors.ControlText, SystemColors.ControlBackground, 0.62f);

    /// <summary>The selection/reference accent, matched to the blue the section
    /// preview draws its reference line with.</summary>
    public static Color Accent => Color.FromArgb(40, 120, 215, 255);

    /// <summary>Problems that block a rebuild.</summary>
    public static Color Danger => IsDark ? Color.FromArgb(255, 120, 110, 255) : Color.FromArgb(190, 30, 20, 255);

    /// <summary>Conditions worth knowing about that are not errors.</summary>
    public static Color Caution => IsDark ? Color.FromArgb(240, 185, 80, 255) : Color.FromArgb(150, 100, 0, 255);

    /// <summary>The surface a card sits on: a slight lift away from the panel
    /// background in light themes, a slight recess in dark ones.</summary>
    public static Color CardBack
      => IsDark ? Lighten(SystemColors.ControlBackground, 0.06f)
                : Blend(SystemColors.ControlBackground, Colors.White, 0.55f);

    /// <summary>Hairline separators and card edges.</summary>
    public static Color Line => Blend(SystemColors.ControlText, SystemColors.ControlBackground, 0.78f);

    // ---- type ---------------------------------------------------------------

    public static Font Heading => SystemFonts.Bold(SystemFonts.Default().Size + 0.5f);
    public static Font Strong => SystemFonts.Bold();
    public static Font Body => SystemFonts.Default();
    public static Font Small => SystemFonts.Default(SystemFonts.Default().Size - 1.0f);
    public static Font SmallBold => SystemFonts.Bold(SystemFonts.Default().Size - 1.0f);

    /// <summary>Section titles: small, bold and tracked out, so a card announces
    /// itself without shouting over the values inside it.</summary>
    public static Font SectionTitle => SystemFonts.Bold(SystemFonts.Default().Size - 1.0f);

    // ---- helpers ------------------------------------------------------------

    /// <summary>Mixes <paramref name="a"/> toward <paramref name="b"/>. A fraction
    /// of 0 is all <paramref name="a"/>, 1 is all <paramref name="b"/>.</summary>
    public static Color Blend(Color a, Color b, float f)
    {
      return Color.FromArgb(
        (int)(a.Rb + (b.Rb - a.Rb) * f),
        (int)(a.Gb + (b.Gb - a.Gb) * f),
        (int)(a.Bb + (b.Bb - a.Bb) * f),
        255);
    }

    public static Color Lighten(Color c, float f) => Blend(c, Colors.White, f);

    /// <summary>A label that names a value.</summary>
    public static Label Caption(string text)
      => new Label { Text = text, Font = Small, TextColor = Muted, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A wrapping hint, for the one-line explanations under a control.</summary>
    public static Label Hint(string text)
      => new Label { Text = text, Font = Small, TextColor = Faint, Wrap = WrapMode.Word };
  }
}
