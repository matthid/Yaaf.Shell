// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
namespace Yaaf.ShellTest

open NUnit.Framework
open FsUnit
open Yaaf.Shell
open Yaaf.Shell.StreamModule

[<TestFixture>]
type ``Given the Shell IO Module``() = 
    [<Test>]
    member this.``check if redirection works``  () = 
        let firstClose, firstStream = StreamModule.limitedStream()
        let secoundClose, secoundStream = StreamModule.limitedStream()

        let closeRedirect = firstStream |> StreamModule.redirect 1024 secoundStream

        firstStream.WriteWait "TEst"
        let result = secoundStream.ReadWait()
        result |> should be (equal (Some "TEst"))
        ()
        
    [<Test>]
    member this.``check if stream works`` () = 
        
        let firstClose, firstStream = StreamModule.limitedStream()

        firstStream.WriteWait "Blub"
        firstStream.WriteWait "Test"
        firstStream.ReadWait() |> should be (equal (Some "Blub"))
        firstStream.ReadWait() |> should be (equal (Some "Test"))

    [<Test>]
    member this.``check if async canceling works`` () = 
        
        let firstClose, firstStream = StreamModule.limitedStream()
        let cts = new System.Threading.CancellationTokenSource()
        Async.Start(firstStream.Read() |> Async.Ignore, cts.Token)
        cts.Cancel()
        System.Threading.Thread.Sleep 1000

        firstStream.WriteWait "Blub"
        firstStream.WriteWait "Test"
        firstStream.ReadWait() |> should be (equal (Some "Blub"))
        firstStream.ReadWait() |> should be (equal (Some "Test"))

    [<Test>]
    member this.``check if async canceling works 2`` () = 
        
        let firstClose, firstStream = StreamModule.limitedStream()
        let cts = new System.Threading.CancellationTokenSource()
        let tempData = ref ""
        Async.Start(async {
            let! data = firstStream.Read()
            tempData := data.Value
            do! firstStream.Read() |> Async.Ignore
            return () }, cts.Token)
            
        firstStream.WriteWait "First"
        System.Threading.Thread.Sleep 40
        cts.Cancel()
        System.Threading.Thread.Sleep 40
        !tempData |> should be (equal "First")
        firstStream.WriteWait "Blub"
        firstStream.WriteWait "Test"
        firstStream.ReadWait() |> should be (equal (Some "Blub"))
        firstStream.ReadWait() |> should be (equal (Some "Test"))  

    [<Test>]
    member this.``check if redirection can be canceled``  () = 
        let firstClose, firstStream = StreamModule.limitedStream()
        let secoundClose, secoundStream = StreamModule.limitedStream()

        let closeRedirect = firstStream |> StreamModule.redirect 1024 secoundStream

        firstStream.WriteWait "TEst"
        let result = secoundStream.ReadWait()
        result |> should be (equal (Some "TEst"))

        closeRedirect 1000 true

        firstStream.WriteWait "Blub"
        secoundStream.WriteWait "Test2"
        let result = secoundStream.ReadWait()
        result |> should FsUnit.TopLevelOperators.not' (equal (Some "Blub"))
        result |> should be (equal (Some "Test2"))
        let resultFirst = firstStream.ReadWait()        
        resultFirst |> should be (equal (Some "Blub"))
        ()