// include Fake lib
#r @"packages/FAKE/tools/FakeLib.dll"
open Fake
open Fake.FscHelper
open System
open System.IO

let krePath = ref ""
let isMono = not (System.Type.GetType("Mono.Runtime") = null)

let exec dir cmd args =
    let result = 
        ExecProcess (fun psi ->
            psi.WorkingDirectory <- dir
            let strArgs =
                args
                |> List.map (fun a -> sprintf "\"%s\"" a)
                |> Array.ofList
            let cmdArgs = String.Join (" ", strArgs)
            psi.Arguments <- cmdArgs
            psi.FileName <- cmd
            if not isMono then
                let strArgs = sprintf "/C %s %s" psi.FileName psi.Arguments
                psi.Arguments <- strArgs
                psi.FileName <- "cmd"
            ()) (TimeSpan.FromMinutes 10.0)

    match result with
    | 0 -> ()
    | _ -> failwithf "Process returned status code %d" result

let kvm = exec (Directory.GetCurrentDirectory ()) (Directory.GetCurrentDirectory () @@ "packages" @@ "KoreBuild" @@ "build" @@ "kvm")
let klr dir args =
    exec dir (!krePath @@ "klr") (["--appbase"; dir] @ args)
let kpm dir args =
    exec dir (!krePath @@ "kpm") args
let git dir args =
    exec dir "git" args

let nuget dir args =
    exec dir (Directory.GetCurrentDirectory () @@ ".nuget" @@ "nuget.exe") args
let nugetPush pkg source key =
    nuget (Directory.GetCurrentDirectory ()) ["push"; pkg; key; "-Source"; source]

let root = Path.GetFullPath "."
let obj = Path.GetFullPath "./obj/"
let proj = Path.GetFullPath "./src/YoloDev.FSharp.AspNet"
let testProj = Path.GetFullPath "./test/YoloDev.FSharp.AspNet.Test"
let lib = Path.GetFullPath "./lib"
let packages = Path.GetFullPath "./packages"
let artifacts = Path.GetFullPath "./artifacts/build"
let pass n = obj @@ (sprintf "pass%d" n)

Target "Install KRE" (fun _ ->
    kvm ["upgrade"; "-runtime"; "CLR"; "-x86"]
    let kreHome =
        let home = environVar "KRE_HOME"
        let home = 
            match String.IsNullOrWhiteSpace home with
            | false -> home
            | true ->
                (Environment.GetFolderPath (Environment.SpecialFolder.ProgramFiles) @@ "KRE") + ";%USERPROFILE%\\.k"

        let rec find globPaths paths =
            match paths with
            | [] ->
                match globPaths with
                | [] -> failwith "KRE not found"
                | h :: t ->
                    let path = (Environment.ExpandEnvironmentVariables h) @@ "runtimes"
                    match Directory.Exists path with
                    | true ->
                        let dirs = Directory.GetDirectories (path, "KRE-*", SearchOption.TopDirectoryOnly) |> List.ofArray
                        find t dirs
                    | false -> find t []
            | h :: t ->
                let path = h
                let binPath = path @@ "bin"
                match Directory.Exists binPath with
                | false -> find globPaths t
                | true ->
                    let PATH = Environment.GetEnvironmentVariable ("PATH", EnvironmentVariableTarget.User)
                    let active =
                        PATH.Split (';')
                        |> Array.exists (fun segment ->
                            segment.StartsWith (binPath, StringComparison.Ordinal))
                
                    if active then
                        binPath
                    else
                        find globPaths t
        find (home.Split (';') |> List.ofArray) []

    traceVerbose (sprintf "using krehome: %s" kreHome)
    krePath := kreHome
)

Target "Clean" (fun _ ->
    CleanDir obj
)

Target "Prepare" (fun _ ->
    Copy (pass 1) (!!"packages/YoloDev.FSharp.AspNet/lib/aspnet50/YoloDev.FSharp.AspNet.*")
    //Directory.CreateDirectory (proj @@ "bin" @@ "debug" @@ "aspnet50") |> ignore
    let path = sprintf "%s;%s;%s" !krePath (!krePath @@ "lib" @@ "Microsoft.Framework.PackageManager") (pass 1)
    klr proj ["--lib"; path; "Microsoft.Framework.PackageManager"; "restore"]
)

let build n =
    let path = sprintf "%s;%s;%s" !krePath (!krePath @@ "lib" @@ "Microsoft.Framework.PackageManager") (pass n)
    klr proj ["--lib"; path; "Microsoft.Framework.PackageManager"; "pack"]

let copy n =
    Copy (pass n) !!(proj @@ "bin" @@ "debug" @@ "aspnet50" @@ "YoloDev.FSharp.AspNet.*")

Target "Pass 1" (fun _ ->
    try
        build 1
        copy 2
    with
        | _ ->
            trace "Fowler broke my shit, bootstrapping"
            //nuget root ["install"; "YoloDev.UnpaK"; "-ExcludeVersion"; "-o"; "packages"; "-nocache"; "-pre"]
            //let unpakPath = packages @@ "YoloDev.UnpaK" @@ "lib" @@ "aspnet50"
            let bootstrap = obj @@ "bootstrap"
            let bin = bootstrap @@ "bin"
            CreateDir bootstrap
            //klr proj ["--lib"; !krePath; "YoloDev.UnpaK"; "-o"; bootstrap]
            exec proj "k" ["YoloDev.UnpaK"; "raw"; "-o"; bootstrap]
            CreateDir bin
            let sources = File.ReadAllLines (bootstrap @@ "sources.txt") |> List.ofArray
            let refs = File.ReadAllLines (bootstrap @@ "references.txt") |> List.ofArray
            let anis = File.ReadAllLines (bootstrap @@ "anis.txt") |> List.ofArray
            let out = bin @@ "YoloDev.FSharp.AspNet.dll"
            let args = ["--out:" + out; "--target:library"; "--debug"; "--noframework"] @ (refs |> List.map (sprintf "--reference:%s")) @ (anis |> List.map (sprintf "--reference:%s"))


            match sources |> fscList args with
            | 0 ->
                Copy (pass 0) !!(bin @@ "*")

                build 0
                copy 1

                build 1
                copy 2

            | _ as n -> failwithf "fsc.exe returned status code %d" n
)

Target "Pass 2" (fun _ ->
    build 2
    copy 3
)

Target "Pass 3" (fun _ ->
    build 3
)

Target "Test" (fun _ ->
    let path = sprintf "%s;%s;%s" !krePath (!krePath @@ "lib" @@ "Microsoft.Framework.PackageManager") (pass 3)
    klr testProj ["--lib"; path; "Microsoft.Framework.PackageManager"; "restore"]
    klr testProj ["--lib"; path; "Microsoft.Framework.ApplicationHost"; "test"]
)

Target "CopyToArtifacts" (fun _ ->
    Copy artifacts !!(proj @@ "bin" @@ "debug" @@ "*.nupkg")
)

Target "Default" id

"Install KRE"
    ==> "Clean"
    ==> "Prepare"
    ==> "Pass 1"
    ==> "Pass 2"
    ==> "Pass 3"
    //==> "Test"
    ==> "CopyToArtifacts"
    ==> "Default"

// start build
RunTargetOrDefault "Default"
