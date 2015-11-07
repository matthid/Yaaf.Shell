namespace System
open System.Reflection

[<assembly: AssemblyCompanyAttribute("Yaaf.Shell")>]
[<assembly: AssemblyProductAttribute("Yaaf.Shell")>]
[<assembly: AssemblyCopyrightAttribute("Yaaf.Shell Copyright © Matthias Dittrich 2011-2015")>]
[<assembly: AssemblyVersionAttribute("0.0.2")>]
[<assembly: AssemblyFileVersionAttribute("0.0.2")>]
[<assembly: AssemblyInformationalVersionAttribute("0.0.2")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.0.2"
