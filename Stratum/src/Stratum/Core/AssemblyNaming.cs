using System;
using System.Collections.Generic;
using System.Linq;

namespace Stratum.Core
{
  /// <summary>
  /// Translates the side-neutral model into the words the element actually uses.
  ///
  /// The data model says "first" and "last" so that one layer stack can describe a
  /// wall, a floor or a roof. Nobody detailing a building says that. A wall has an
  /// exterior and an interior; a floor has a top and a bottom. Everything the user
  /// reads — the panel, the catalog editor, the command line — goes through here, so
  /// the generalisation costs the model nothing in clarity.
  /// </summary>
  public static class AssemblyNaming
  {
    /// <summary>The side whose layers are listed first.</summary>
    public static string FirstSide(AssemblyKind kind)
    {
      switch (kind)
      {
        case AssemblyKind.Floor: return "Top";
        case AssemblyKind.Roof: return "Upper";
        case AssemblyKind.Ceiling: return "Upper";
        default: return "Exterior";
      }
    }

    /// <summary>The side whose layers are listed last.</summary>
    public static string LastSide(AssemblyKind kind)
    {
      switch (kind)
      {
        case AssemblyKind.Floor: return "Bottom";
        case AssemblyKind.Roof: return "Lower";
        case AssemblyKind.Ceiling: return "Lower";
        default: return "Interior";
      }
    }

    public static string SideName(AssemblyKind kind, LayerSide side)
    {
      switch (side)
      {
        case LayerSide.Outer: return FirstSide(kind);
        case LayerSide.Inner: return LastSide(kind);
        default: return "Core";
      }
    }

    /// <summary>A justification named the way this kind of element talks, with no
    /// spaces — safe to use as a Rhino command-line option value.</summary>
    public static string OptionName(AssemblyKind kind, AssemblyJustification justification)
    {
      string first = FirstSide(kind), last = LastSide(kind);
      switch (justification)
      {
        case AssemblyJustification.FirstFace: return first + "Face";
        case AssemblyJustification.FirstCore: return first + "Core";
        case AssemblyJustification.LastCore: return last + "Core";
        case AssemblyJustification.LastFace: return last + "Face";
        case AssemblyJustification.Center: return "Center";
        case AssemblyJustification.CoreCenter:
        default: return "CoreCenter";
      }
    }

    /// <summary>The same thing written out for a panel, where spaces are fine.</summary>
    public static string DisplayName(AssemblyKind kind, AssemblyJustification justification)
    {
      string first = FirstSide(kind).ToLowerInvariant(), last = LastSide(kind).ToLowerInvariant();
      switch (justification)
      {
        case AssemblyJustification.FirstFace: return Capitalise(first) + " face";
        case AssemblyJustification.FirstCore: return Capitalise(first) + " face of core";
        case AssemblyJustification.LastCore: return Capitalise(last) + " face of core";
        case AssemblyJustification.LastFace: return Capitalise(last) + " face";
        case AssemblyJustification.Center: return "Centre of assembly";
        case AssemblyJustification.CoreCenter:
        default: return "Centre of core";
      }
    }

    public static IEnumerable<AssemblyJustification> All
      => Enum.GetValues(typeof(AssemblyJustification)).Cast<AssemblyJustification>();

    public static string[] OptionNames(AssemblyKind kind)
      => All.Select(j => OptionName(kind, j)).ToArray();

    public static string[] DisplayNames(AssemblyKind kind)
      => All.Select(j => DisplayName(kind, j)).ToArray();

    static string Capitalise(string value)
      => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);
  }
}
