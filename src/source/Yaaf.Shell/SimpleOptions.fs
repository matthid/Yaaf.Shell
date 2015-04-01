// ----------------------------------------------------------------------------
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.
// ----------------------------------------------------------------------------
[<AutoOpen>]
module Yaaf.Shell.Options

open Microsoft.FSharp.Reflection
type SimpleOptions<'a> private (opts:'a list, names :  System.Collections.Generic.HashSet<_>) =
    let unionType = typeof<'a>
    let checkItem (names:System.Collections.Generic.HashSet<_>) value = 
        let unionCase,infos = FSharpValue.GetUnionFields(value, unionType)
        if names.Contains unionCase.Name then
            failwithf "duplicate entry %s" unionCase.Name 
        unionCase.Name
    let doDataChecks () =
        if not <| FSharpType.IsUnion unionType then
            failwith "Options class is only working with unions"
        let foundNames = new System.Collections.Generic.HashSet<_>()
        for o in opts do
            let unionCaseName = checkItem foundNames o                
            foundNames.Add unionCaseName |> ignore
        foundNames

    let names = 
        if names = null then
           doDataChecks()
        else names
    let combineWith (other:'a) = 
        let name = checkItem names other
        let newSet = System.Collections.Generic.HashSet<_>(names)
        newSet.Add name |> ignore
        SimpleOptions(other :: opts, newSet)
    let combineWhen (other:'a) =         
        let unionCase,infos = FSharpValue.GetUnionFields(other, unionType)
        if not <| names.Contains unionCase.Name then
            Some <| combineWith other
        else
            None
    let findNameFromConstrFunc (construct:'b -> 'a) = 
        // I know this is evil, but this seems to be the cleanest way!
        let constrArgs, _ = FSharpType.GetFunctionElements (construct.GetType())
        let emptyArgs = 
            let rec createEmpty typeObj =
                if FSharpType.IsUnion typeObj then
                    let cases = FSharpType.GetUnionCases(typeObj)
                    let selectedCase, paramTypes =
                        cases 
                        |> Seq.map 
                            (fun c -> c, FSharpValue.PreComputeUnionConstructorInfo(c))
                        |> Seq.map (fun (c,m) -> c, m.GetParameters() |> Array.map (fun p -> p.ParameterType))
                        |> Seq.sortBy (fun (c,typse) ->  typse.Length)
                        |> Seq.head

                    FSharpValue.MakeUnion(selectedCase, paramTypes |> Array.map createEmpty)
                else if FSharpType.IsRecord typeObj then
                    let recordArgs = FSharpType.GetRecordFields(typeObj)
                    FSharpValue.MakeRecord(
                        typeObj, 
                        recordArgs 
                            |> Array.map(fun info -> info.PropertyType)
                            |> Array.map createEmpty)
                else if FSharpType.IsFunction typeObj then
                   FSharpValue.MakeFunction(typeObj, (fun o -> o))
                else if FSharpType.IsTuple typeObj then
                    let tupleArgs = FSharpType.GetTupleElements typeObj
                    FSharpValue.MakeTuple(tupleArgs |> Array.map createEmpty, typeObj)
                else
                    Unchecked.defaultof<_>
            createEmpty constrArgs
        let createdTmp = construct (emptyArgs:?>'b)
        let givenCase, _ = FSharpValue.GetUnionFields(createdTmp, unionType)
        givenCase.Name
    let hasFlagName (findUnionCaseName) = 
        opts
            |> List.exists
                (fun o ->
                    let unionCase,infos = FSharpValue.GetUnionFields(o, unionType)
                    findUnionCaseName = unionCase.Name)
    let hasFlag (d:'a) = 
        let findUnionCase, _ = FSharpValue.GetUnionFields(d, unionType)
        hasFlagName findUnionCase.Name
    let hasFlagFunc (construct:'b -> 'a) = 
        findNameFromConstrFunc construct |> hasFlagName

    let findFlagName (findUnionCase) =
        opts
            |> List.find
                (fun o ->
                    let unionCase,infos = FSharpValue.GetUnionFields(o, unionType)
                    findUnionCase = unionCase.Name)
    let findFlag (d:'a) =
        let findUnionCase, _ = FSharpValue.GetUnionFields(d, unionType)
        findFlagName findUnionCase.Name
    let findFlagFunc (construct:'b -> 'a) =        
        findNameFromConstrFunc construct |> findFlagName

    let tryFindFlag (d:'a) =
        let findUnionCase, _ = FSharpValue.GetUnionFields(d, unionType)
        opts
            |> List.tryFind
                (fun o ->
                    let unionCase,infos = FSharpValue.GetUnionFields(o, unionType)
                    findUnionCase.Name = unionCase.Name)
    member x.Append other = combineWith other
    member x.AppendSafe other = 
        match combineWhen other with
        | Some v -> v
        | None -> x
    member x.HasFlag f = hasFlag f
    member x.HasFlagF f = hasFlagFunc f
    member x.HasFlagN f = hasFlagName f
    member x.GetFlag f = findFlag f
    member x.GetFlagF f = findFlagFunc f
    member x.GetFlagName name = findFlagName name
    static member Empty = SimpleOptions( [],  new System.Collections.Generic.HashSet<_>())

let NO<'a> : SimpleOptions<'a> = SimpleOptions.Empty
let (|+) (left:SimpleOptions<'a>) (right:'a) =
    left.AppendSafe right