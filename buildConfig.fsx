// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------

(*
    This file handles the complete build process of Yaaf.Logging

    The first step is handled in build.sh and build.cmd by bootstrapping a NuGet.exe and 
    executing NuGet to resolve all build dependencies (dependencies required for the build to work, for example FAKE)

    The secound step is executing this file which resolves all dependencies, builds the solution and executes all unit tests
*)

#if FAKE
#else
// Support when file is opened in Visual Studio
#load "packages/Yaaf.AdvancedBuilding/content/buildConfigDef.fsx"
#endif

open BuildConfigDef
open System.Collections.Generic
open System.IO

open Fake
open Fake.Git
open Fake.FSharpFormatting
open AssemblyInfoFile

if isMono then
    monoArguments <- "--runtime=v4.0 --debug"

let buildConfig =
 // Read release notes document
 let release = ReleaseNotesHelper.parseReleaseNotes (File.ReadLines "doc/ReleaseNotes.md")
 { BuildConfiguration.Defaults with
    ProjectName = "Yaaf.Shell"
    CopyrightNotice = "Yaaf.Shell Copyright © Matthias Dittrich 2011-2015"
    ProjectSummary = "Yaaf.Shell some helpers for F# shell scripting."
    ProjectDescription = "Yaaf.Shell some helpers for F# shell scripting."
    ProjectAuthors = ["Matthias Dittrich"]
    NugetTags =  "C# F# shell script .net"
    PageAuthor = "Matthias Dittrich"
    GithubUser = "matthid"
    Version = release.NugetVersion
    RestrictReleaseToWindows = false
    NugetPackages =
      [ "Yaaf.Shell.nuspec", (fun config p ->
          { p with
              Version = config.Version
              ReleaseNotes = toLines release.Notes
              Dependencies = [ ] }) ]
    UseNuget = true
    SetAssemblyFileVersions = (fun config ->
      let info =
        [ Attribute.Company config.ProjectName
          Attribute.Product config.ProjectName
          Attribute.Copyright config.CopyrightNotice
          Attribute.Version config.Version
          Attribute.FileVersion config.Version
          Attribute.InformationalVersion config.Version]
      CreateFSharpAssemblyInfo "./src/SharedAssemblyInfo.fs" info)
    BuildTargets =
     [ { BuildParams.WithSolution with
          // The generated templates
          PlatformName = "Net45"
          SimpleBuildName = "net45" } ]
  }

