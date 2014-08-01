namespace FSharpSupport

open System.IO
open System.Runtime.Versioning
open System.Collections.Generic
open Microsoft.Framework.Runtime
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SimpleSourceCodeServices

module Helpers =
    let inline ni () = raise (new System.NotImplementedException ())

open Helpers

type internal FSharpProjectReference(project: Project, targetFramework: FrameworkName, configuration: string, incomingReferences: IMetadataReference seq, watcher: IFileWatcher) =
    
    let param name value = sprintf "--%s:%s" name value

    let emit outputPath emitPdb emitDocFile emitExe inMemory =
        //System.Diagnostics.Debugger.Launch () |> ignore
        let tempBasePath = Path.Combine [|outputPath; project.Name; "obj"|]

        let convertRef (r: IMetadataReference) =
            match r with
            // Skip this project
            | _ when r.Name = (typeof<FSharpProjectReference>.Assembly.GetName ()).Name -> None, None

            // NuGet references
            | :? IMetadataFileReference as r -> Some r.Path, None

            // Assembly neutral references
            | :? IMetadataEmbeddedReference as r ->
                let tempEmbeddedPath = Path.Combine [|tempBasePath; r.Name + ".dll"|]
                Directory.CreateDirectory (Path.GetDirectoryName tempEmbeddedPath) |> ignore

                // Write the ANI to disk
                File.WriteAllBytes (tempEmbeddedPath, r.Contents)
                Some tempEmbeddedPath, Some tempEmbeddedPath

            // Project references
            | :? IMetadataProjectReference as r ->
                let path = 
                    let path = Path.Combine [|tempBasePath; r.Name + ".dll"|]
                    Directory.CreateDirectory (Path.GetDirectoryName path) |> ignore

                    // Write metadata to disk
                    use fs = File.OpenWrite path
                    r.EmitReferenceAssembly fs
                    path

                Some path, Some path

            | _ -> None, None


        let outExt ext = Path.Combine [|outputPath; project.Name + ext|]
        let outputDll = outExt (if emitExe then ".exe" else ".dll")

        let compilerArgs = [(param "out" outputDll); "--target:" + (if emitExe then "exe" else "library"); "--noframework"]
        
        if not inMemory then
            Directory.CreateDirectory tempBasePath |> ignore

        let compilerArgs = 
            match emitPdb with
            | false -> compilerArgs
            | true ->
                let pdb = outExt ".pdb"
                (param "pdb" pdb) :: "--debug" :: compilerArgs

        let compilerArgs =
            match emitDocFile with
            | false -> compilerArgs
            | true ->
                let doc = outExt ".xml"
                (param "doc" doc) :: compilerArgs

        // F# cares about order so assume that the files were listed in order
        let compilerArgs = (project.SourceFiles |> List.ofSeq |> List.rev) @ compilerArgs
        project.SourceFiles
        |> Seq.iter (fun f -> watcher.WatchFile f |> ignore)

        // These are the metadata references being used by your project.
        // Everything in your project.json is resolved and normailzed here:
        // - Project references
        // - Package references are turned into the appropriate assemblies
        // - Assembly neutral references
        // Each IMetadaReference maps to an assembly
        let compilerArgs, tempFiles =
            let refs, tempFiles =
                incomingReferences
                |> List.ofSeq
                |> List.map convertRef
                |> List.unzip

            let refs = refs |> List.choose id
            let tempFiles = tempFiles |> List.choose id

            let refs =
                refs
                |> List.map (fun r -> "-r:" + r)

            refs @ compilerArgs, tempFiles
        
        let compilerArgsArr = ("fsc.exe" :: (compilerArgs |> List.rev)) |> Array.ofList

        let scs = SimpleSourceCodeServices()
        let errors, exitCode = scs.Compile compilerArgsArr
        tempFiles |> Seq.iter (fun f -> File.Delete f |> ignore)
        Directory.Delete tempBasePath
        let warnings =
            errors
            |> Array.choose (fun e ->
                match e.Severity with
                | Severity.Warning -> Some e
                | _ -> None)
            |> Array.map (fun e -> e.ToString ())
        
        let errors =
            errors
            |> Array.choose (fun e ->
                match e.Severity with
                | Severity.Error -> Some e
                | _ -> None)
            |> Array.map (fun e -> e.ToString ())

        let success = match exitCode with | 0 -> true | _ -> false
        new DiagnosticResult (success, warnings, errors) :> IDiagnosticResult

    interface IMetadataReference with
        member x.Name with get () = project.Name
    
    interface IMetadataProjectReference with
        member x.ProjectPath with get () = project.ProjectFilePath
    
        member x.GetDiagnostics () =
            let outputDir = Path.Combine [|Path.GetTempPath (); "diagnostics-" + (System.Guid.NewGuid ()).ToString ()|]

            try
                emit outputDir false false false true
            finally
                Directory.Delete (outputDir, true)

        member x.Load (loaderEngine) =
            let outputDir = Path.Combine [|Path.GetTempPath (); "dynamic-assemblies"|]

            let result = emit outputDir true false false true

            match result.Success with
            | false -> raise (new CompilationException (result.Errors |> System.Linq.Enumerable.ToList))
            | true ->
                let assemblyPath = Path.Combine [|outputDir; project.Name + ".dll"|]

                loaderEngine.LoadFile assemblyPath

        member x.EmitReferenceAssembly (stream) =
            let outputDir = Path.Combine [|Path.GetTempPath (); "reference-assembly-" + (System.Guid.NewGuid ()).ToString ()|]

            try
                let result = emit outputDir false false false false

                match result.Success with
                | false -> ()
                | true ->
                    use fs = File.OpenRead (Path.Combine [|outputDir; project.Name + ".dll"|])
                    fs.CopyTo stream
                    ()
            
            finally
                Directory.Delete (outputDir, true)

        member x.EmitAssembly (path) =
            emit path true true false false

        member x.GetSources () =
            project.SourceFiles
            |> List.ofSeq
            |> List.map (fun p -> new SourceFileReference (p) :> ISourceReference)
            |> Array.ofList
            :> IList<ISourceReference>

type public FSharpProjectReferenceProvider(watcher: IFileWatcher) =

    interface IProjectReferenceProvider with

        member x.GetProjectReference (project, targetFramework, configuration, incomingReferences, incomingSourceReferences, outgoingReferences) =
            new FSharpProjectReference (project, targetFramework, configuration, incomingReferences, watcher) :> IMetadataProjectReference