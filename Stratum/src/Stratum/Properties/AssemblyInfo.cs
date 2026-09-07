using System.Reflection;
using System.Runtime.InteropServices;
using Rhino.PlugIns;

// ---------------------------------------------------------------------------
//  Plug-in description. Rhino reads these attributes for the Plug-in Manager
//  and for the "About" information shown to the user.
// ---------------------------------------------------------------------------
[assembly: PlugInDescription(DescriptionType.Address, "")]
[assembly: PlugInDescription(DescriptionType.Country, "United States")]
[assembly: PlugInDescription(DescriptionType.Email, "")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.Fax, "")]
[assembly: PlugInDescription(DescriptionType.Organization, "Stratum")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "")]
[assembly: PlugInDescription(DescriptionType.WebSite, "")]

[assembly: AssemblyTitle("Stratum BIM")]
[assembly: AssemblyDescription("Parametric layered wall assemblies for Rhino 8")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Stratum")]
[assembly: AssemblyProduct("Stratum BIM")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

// IMPORTANT: This GUID *is* the plug-in id. Rhino identifies the plug-in by it
// forever - never change it once the plug-in has shipped, or existing documents
// will not be able to find their plug-in data.
[assembly: Guid("bc93f43b-764a-41dc-8cd4-edfc72500fbf")]

[assembly: AssemblyVersion("0.9.0.0")]
[assembly: AssemblyFileVersion("0.9.0.0")]
[assembly: AssemblyInformationalVersion("0.9.0")]
