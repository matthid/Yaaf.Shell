// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Yaaf.ShellTest

open NUnit.Framework
open FsUnit
open Yaaf.Shell
open Yaaf.Shell.StreamModule

type SimpleOption =
    | Option1
    | Option2
type AdvancedOption = 
    | Test1 of string
    | Test2 of int * bool
    | Test3
type DebugRecord = {
    Item : string }
exception DebugExn of string
type DebugUnion = 
    | Function of (string -> bool)
    | Record of DebugRecord
    | Union of SimpleOption
    | Exception of exn

[<TestFixture>]
type ``Given the SimpleOptions Module``() = 
    [<Test>]
    member this.``check if we can check simple options``  () = 
        let flag = NO |+ Option1
        flag.HasFlag Option1 |> should be True
        flag.HasFlag Option2 |> should be False
    [<Test>]
    member this.``check if we can check advanced options``  () = 
        let flag = NO |+ Test1 "somedata" |+ (Test2(4, true))
        flag.HasFlagF Test1 |> should be True
        flag.HasFlagF Test2 |> should be True
        flag.HasFlag Test3 |> should be False
        
    [<Test>]
    member this.``check if we can get advanced options``  () = 
        let flag = NO |+ Test1 "somedata" |+ (Test2(4, true))
        match flag.GetFlagF Test1 with
        | Test1 (s) -> s |> should be (equal "somedata")
        | _ -> failwith "Test1 data not found!"
        
        
    [<Test>]
    member this.``check if we can handle special fsharp types``  () = 
        let flag = NO |+ Function (fun s -> s = "somedata") |+ (Record {Item = "data" }) |+ Union (Option2)
        flag.HasFlagF Function |> should be True
        flag.HasFlagF Record |> should be True
        flag.HasFlagF Union |> should be True
        match flag.GetFlagF Function with
        | Function (f) -> f "somedata" |> should be True
        | _ -> failwith "Function data not found!"
        match flag.GetFlagF Record with
        | Record (item) -> item.Item |> should be (equal "data")
        | _ -> failwith "Record data not found!"
        match flag.GetFlagF Union with
        | Union (item) -> item |> should be (equal Option2)
        | _ -> failwith "Union data not found!"

    [<Test>]
    member this.``check if we can handle even more special fsharp types``  () = 
        let flag = NO |+ Exception (DebugExn "blub") |+ (Record {Item = "data" })
        flag.HasFlagF Exception |> should be True
        match flag.GetFlagF Exception with
        | Exception (DebugExn data) -> data |> should be (equal "blub")
        | _ -> failwith "Exception data not found!"