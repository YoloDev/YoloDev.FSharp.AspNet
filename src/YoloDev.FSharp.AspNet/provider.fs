namespace YoloDev.FSharp.AspNet

open System.IO
open System.Runtime.Versioning
open Microsoft.Framework.Runtime
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices
open Microsoft.FSharp.Compiler.SimpleSourceCodeServices
open Microsoft.FSharp.Compiler.AbstractIL.Internal.Library

module Helpers =
    let inline ni () = raise (new System.NotImplementedException ())

[<NoEquality; NoComparison>]
type internal MetadataReference =
| FileMetadataReference of string
| ImageMetadataReference of string * byte array
| ProjectMetadataReference of string * (Stream -> unit)
with
    member r.VirtPath 
        with get () =
            match r with
            | FileMetadataReference f -> f
            | ImageMetadataReference (p, _) -> p
            | ProjectMetadataReference (p, _) -> p

[<CompilationRepresentationAttribute(CompilationRepresentationFlags.ModuleSuffix)>]
module internal MetadataReference =
    let virtPath (p: MetadataReference) = p.VirtPath

open Helpers
module internal Compiler =
    let lock fn =
        let l = new obj ()
        lock l fn

    //let tprintf fmt = Printf.ksprintf System.Diagnostics.Trace.Write fmt
    let tprintf _ = ignore

    //let tprintfn fmt = Printf.ksprintf System.Console.Out.WriteLine fmt
    let tprintfn _ = ignore

    let getReferences _ _ incomingReferences =
        //let name = project.Name

        let makePath p = "C:\\" + p + ".dll"

        let (|>|) a b =
            a, (b a)

        incomingReferences
        |> List.ofSeq
        |> List.choose (fun (r: IMetadataReference) ->
            match r with
            // Skip this project
            | _ when r.Name = (typeof<MetadataReference>.Assembly.GetName ()).Name -> None
                
            // NuGet references
            | :? IMetadataFileReference as r -> Some (FileMetadataReference r.Path)

            // Assembly neutral references
            | :? IMetadataEmbeddedReference as r -> Some (ImageMetadataReference ((makePath r.Name), r.Contents))

            // Project references
            | :? IMetadataProjectReference as r -> Some (ProjectMetadataReference ((makePath r.Name), r.EmitReferenceAssembly))

            // Other
            | _ -> failwith "Invalid reference type")
        |>| (List.map (fun r -> r.VirtPath, r) >> Map.ofList)

    let makeStream = function
        | FileMetadataReference f -> File.OpenRead f :> Stream
        | ImageMetadataReference (_, a) -> new MemoryStream (a) :> Stream
        | ProjectMetadataReference (_, a) -> let ms = new MemoryStream () in a ms; ms :> Stream

    let makeByteArray = function
        | FileMetadataReference f -> File.ReadAllBytes f
        | ImageMetadataReference (_, a) -> a
        | ProjectMetadataReference (_, a) -> use ms = new MemoryStream () in a ms; ms.ToArray ()

    let makeFs refs =
        let defaultFileSystem = Shim.FileSystem
        { new IFileSystem with
            // Implement the service to open files for reading and writing
            member fs.FileStreamReadShim name =
                tprintfn "FileStreamReadShim: %s" name
                match Map.tryFind name refs with
                | Some s -> makeStream s
                | _ -> defaultFileSystem.FileStreamReadShim name

            member fs.FileStreamCreateShim name =
                tprintfn "FileStreamCreateShim: %s" name
                defaultFileSystem.FileStreamCreateShim name

            member fs.FileStreamWriteExistingShim name =
                tprintfn "FileStreamWriteExistingShim: %s" name
                defaultFileSystem.FileStreamWriteExistingShim name

            member fs.ReadAllBytesShim name =
                tprintfn "ReadAllBytesShim: %s" name
                match Map.tryFind name refs with
                | Some s -> makeByteArray s
                | _ -> defaultFileSystem.ReadAllBytesShim name

            // Implement the service related to temporary paths and file time stamps
            member fs.GetTempPathShim () = 
                tprintfn "GetTempPathShim: ()%s" ""
                defaultFileSystem.GetTempPathShim ()

            member fs.GetLastWriteTimeShim name = 
                tprintfn "GetLastWriteTimeShim: %s" name
                defaultFileSystem.GetLastWriteTimeShim name

            member fs.GetFullPathShim name = 
                tprintfn "GetFullPathShim: %s" name
                defaultFileSystem.GetFullPathShim name

            member fs.IsInvalidPathShim name = 
                tprintfn "IsInvalidPathShim: %s" name
                defaultFileSystem.IsInvalidPathShim name

            member fs.IsPathRootedShim name = 
                tprintfn "IsPathRootedShim: %s" name
                defaultFileSystem.IsPathRootedShim name

            // Implement the service related to file existence and deletion
            member fs.SafeExists name =
                tprintfn "SafeExists: %s" name
                Map.containsKey name refs || defaultFileSystem.SafeExists name

            member fs.FileDelete name =
                tprintfn "FileDelete: %s" name
                defaultFileSystem.FileDelete name

            // Implement the service related to assembly loading, used to load type providers
            // and for F# interactive.
            member fs.AssemblyLoadFrom name =
                tprintfn "AssemblyLoadFrom: %s" name
                defaultFileSystem.AssemblyLoadFrom name

            member fs.AssemblyLoad name =
                tprintfn "AssemblyLoad: %s" name.FullName
                defaultFileSystem.AssemblyLoad name
        }

    let param name value = sprintf "--%s:%s" name value

    let emit name sources refs outputPath emitPdb emitDocFile emitExe _ =
        Directory.CreateDirectory outputPath |> ignore
        let outExt ext = Path.Combine [|outputPath; name + ext|]
        let outputDll = outExt (if emitExe then ".exe" else ".dll")

        let compilerArgs = [(param "out" outputDll); "--target:" + (if emitExe then "exe" else "library"); "--noframework"]

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
        let compilerArgs = (sources |> List.ofSeq |> List.rev) @ compilerArgs

        // These are the metadata references being used by your project.
        // Everything in your project.json is resolved and normailzed here:
        // - Project references
        // - Package references are turned into the appropriate assemblies
        // - Assembly neutral references
        // Each IMetadaReference maps to an assembly
        let compilerArgs =
            let refs = refs |> List.map MetadataReference.virtPath
            let refs = refs |> List.map (fun r -> "-r:" + r)

            refs @ compilerArgs

        let compilerArgsArr = ("fsc.exe" :: (compilerArgs |> List.rev)) |> Array.ofList

        let scs = SimpleSourceCodeServices()
        let errors, exitCode = scs.Compile compilerArgsArr

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

    let getProjectReference (project: Project) targetFramework _ incomingReferences (watcher: IFileWatcher) =
        let refs, refsMap = getReferences project targetFramework incomingReferences
        let fs = makeFs refsMap

        let run name fn =
            //System.Diagnostics.Debugger.Launch () |> ignore
            lock (fun () ->
                tprintfn "%s" name
                let defaultFileSystem = Shim.FileSystem
                Shim.FileSystem <- fs
                let result = fn ()
                Shim.FileSystem <- defaultFileSystem
                result)

        let getTempPath () =
            let path = Path.GetTempPath() // typecheck warning, compiler thinks it might be mutated
            Path.Combine(path, project.Name, (System.Guid.NewGuid ()).ToString ())

        let withTemp fn = fun () ->
            let path = getTempPath ()
            Directory.CreateDirectory path |> ignore
            let result = fn path
            Directory.Delete (path, true)
            result

        { new IMetadataProjectReference with
            member x.Name with get () = project.Name
            member x.ProjectPath with get () = project.ProjectDirectory

            member x.GetDiagnostics () = 
                run "GetDiagnostics" (withTemp (fun path ->
                    emit project.Name project.SourceFiles refs path true true false true))

            member x.Load loaderEngine = 
                run "Load" (fun () ->
                    let path = getTempPath ()
                    emit project.Name project.SourceFiles refs path true false false true |> ignore
                    let assemblyPath = Path.Combine [|path; project.Name + ".dll"|]
                    loaderEngine.LoadFile assemblyPath)

            member x.EmitReferenceAssembly stream = 
                run "EmitReferenceAssembly" (withTemp (fun path ->
                    emit project.Name project.SourceFiles refs path false false false false |> ignore
                    let assemblyPath = Path.Combine [|path; project.Name + ".dll"|]
                    use fs = File.OpenRead assemblyPath
                    fs.CopyTo stream))

            member x.EmitAssembly path =
                run "EmitAssembly" (fun () ->
                    project.SourceFiles
                    |> Seq.iter (fun f -> watcher.WatchFile f |> ignore)
                    emit project.Name project.SourceFiles refs path true true false false)

            member x.GetSources () =
                project.SourceFiles
                |> List.ofSeq
                |> List.map (fun p -> new SourceFileReference (p) :> ISourceReference)
                |> Array.ofList
                :> System.Collections.Generic.IList<ISourceReference>
        }

type public FSharpProjectReferenceProvider(watcher: IFileWatcher) =
    
    interface IProjectReferenceProvider with

        member x.GetProjectReference (project, target, referenceResolver, _) =
            let export = referenceResolver.Invoke ()
            let incomingReferences = export.MetadataReferences

            Compiler.getProjectReference project target.TargetFramework target.Configuration incomingReferences watcher
