namespace Stratum.Core
{
  /// <summary>
  /// The names of the construction hatch patterns Stratum installs.
  ///
  /// They live in Core rather than beside the builder in Documents because the
  /// catalog refers to them by name and the catalog must not depend on anything
  /// that touches a Rhino document. Documents/SectionPatterns.cs is what actually
  /// creates them.
  /// </summary>
  public static class SectionPatternNames
  {
    public const string Concrete = "Stratum Concrete";
    public const string Masonry = "Stratum Masonry";
    public const string Brick = "Stratum Brick";
    public const string Earth = "Stratum Earth";
    public const string RigidInsulation = "Stratum Rigid Insulation";
    public const string BattInsulation = "Stratum Batt Insulation";
    public const string Wood = "Stratum Wood";
    public const string Steel = "Stratum Steel";
    public const string Gypsum = "Stratum Gypsum";
    public const string Solid = "Stratum Solid";

    /// <summary>Every name, for the catalog editor's dropdown. The empty entry
    /// means "poche only, no hatch".</summary>
    public static string[] All => new[]
    {
      "", Concrete, Masonry, Brick, Earth, RigidInsulation,
      BattInsulation, Wood, Steel, Gypsum, Solid
    };
  }
}
