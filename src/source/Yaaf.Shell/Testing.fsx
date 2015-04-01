#light (*
    exec fsharpi --quiet --exec $0 "$@"
*)
// The above lines allows us to call ./script directly in a shell
// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------

#r @"C:\Program Files (x86)\Mono-3.2.3\lib\mono\4.5\Mono.Posix.dll"
#r @"C:\projects\pam-hook-fsharp\lib\FSharpx\FSharpx.Core.dll" 
#load "Event.fs"
open Yaaf.Shell.Event
#load "SimpleOptions.fs"
#load "IO.fs"
#load "ToolProcess.fs"
#load "Scripting.fs"
open Mono.Unix
open System
open Yaaf.Shell.Scripting
Environment.CurrentDirectory <- @"C:\Users\Matthias\testing"

open System.Runtime.InteropServices


getCurrentUser()
ln "testfolder" "blub"



