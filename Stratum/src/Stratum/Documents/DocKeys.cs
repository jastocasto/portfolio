namespace Stratum.Documents
{
  /// <summary>
  /// Attribute user-string keys written onto every solid Stratum bakes.
  ///
  /// They are the link back from a piece of geometry to the parametric record
  /// that produced it. Because they are plain user strings they survive Copy,
  /// Paste, Export, Import, WorkSession attach and a round trip through any
  /// other application that respects 3dm user text - which is a great deal more
  /// robust than storing the link in transient runtime state.
  /// </summary>
  public static class DocKeys
  {
    public const string Wall = "Stratum:Wall";
    public const string WallName = "Stratum:WallName";
    public const string Assembly = "Stratum:Assembly";
    public const string AssemblyCode = "Stratum:AssemblyCode";
    public const string LayerIndex = "Stratum:LayerIndex";
    public const string LayerFunction = "Stratum:LayerFunction";
    public const string Product = "Stratum:Product";
    public const string ProductName = "Stratum:ProductName";
    public const string Manufacturer = "Stratum:Manufacturer";
    public const string Sku = "Stratum:SKU";
    public const string ThicknessIn = "Stratum:ThicknessIn";
    public const string RValue = "Stratum:RValue";
    public const string CostPerSqFt = "Stratum:CostPerSF";
    public const string Side = "Stratum:Side";

    public const string Opening = "Stratum:Opening";
    public const string OpeningMark = "Stratum:OpeningMark";
    public const string OpeningUnit = "Stratum:OpeningUnit";

    public const string RootLayer = "Stratum";
    public const string WallsLayer = "Walls";
  }
}
