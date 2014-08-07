// include Fake lib
#r @"packages/FAKE/tools/FakeLib.dll"
open Fake
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
            ()) TimeSpan.MaxValue

    match result with
    | 0 -> ()
    | _ -> failwithf "Process returned status code %d" result

let kvm = exec (Directory.GetCurrentDirectory ()) (Directory.GetCurrentDirectory () @@ "packages" @@ "KoreBuild" @@ "build" @@ "kvm")
let klr dir args =
    exec dir (!krePath @@ "klr") args

let nugetPush pkg source key =
    exec (Directory.GetCurrentDirectory ()) (Directory.GetCurrentDirectory () @@ ".nuget" @@ "nuget.exe") ["push"; pkg; key; "-Source"; source]

let obj = Path.GetFullPath "./obj/"
let proj = Path.GetFullPath "./src/FSharpSupport"
let pass n = obj @@ (sprintf "pass%d" n)

Target "Install KRE" (fun _ ->
    kvm ["upgrade"; "-svr50"; "-x86"]
    let kreHome =
        let home = environVar "KRE_HOME"
        let home = 
            match String.IsNullOrWhiteSpace home with
            | false -> home
            | true ->
                (Environment.GetFolderPath (Environment.SpecialFolder.ProgramFiles) @@ "KRE") + ";%USERPROFILE%\\.kre"

        let rec find globPaths paths =
            match paths with
            | [] ->
                match globPaths with
                | [] -> failwith "KRE not found"
                | h :: t ->
                    let path = (Environment.ExpandEnvironmentVariables h) @@ "packages"
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
    Copy (pass 1) (!!"packages/FSharpSupport/lib/net45/FSharpSupport.*")
    Directory.CreateDirectory (proj @@ "bin" @@ "debug" @@ "net45") |> ignore
    let path = sprintf "%s;%s;%s" !krePath (!krePath @@ "lib" @@ "Microsoft.Framework.PackageManager") (pass 1)
    klr proj ["--lib"; path; "Microsoft.Framework.PackageManager"; "restore"]
)

let build n =
    let path = sprintf "%s;%s;%s" !krePath (!krePath @@ "lib" @@ "Microsoft.Framework.PackageManager") (pass n)
    klr proj ["--lib"; path; "Microsoft.Framework.PackageManager"; "build"]

let copy n =
    Copy (pass n) !!(proj @@ "bin" @@ "debug" @@ "net45" @@ "FSharpSupport.*")

Target "Pass 1" (fun _ ->
    try
        build 1
        copy 2
    with
        | _ ->
            failwith "TODO: Bootstrap"
)

Target "Pass 2" (fun _ ->
    build 2
    copy 3
)

Target "Pass 3" (fun _ ->
    build 3
)

let kBuildVersion = environVar "K_BUILD_VERSION"
let avRepoBranch = environVar "APPVEYOR_REPO_BRANCH"
let avPrn = environVar "APPVEYOR_PULL_REQUEST_NUMBER"
let nugetSource = environVar "NUGET_SOURCE"
let nugetApiKey = environVar "NUGET_API_KEY"
let symbolSource = environVar "SYMBOL_SOURCE"
let symbolApiKey = environVar "SYMBOL_API_KEY"

Target "Publish" (fun _ ->
    nugetPush (proj @@ "bin" @@ "debug" @@ (sprintf "FSharpSupport.0.1-alpha-%s.nupkg" kBuildVersion)) nugetSource nugetApiKey
    nugetPush (proj @@ "bin" @@ "debug" @@ (sprintf "FSharpSupport.0.1-alpha-%s.symbols.nupkg" kBuildVersion)) symbolSource symbolApiKey
)

let shouldPublish =
    let anyEmpty = 
        [kBuildVersion; avRepoBranch; nugetSource; nugetApiKey; symbolSource; symbolApiKey]
        |> List.exists String.IsNullOrWhiteSpace
    
    match anyEmpty with
    | true -> false
    | false ->
        let avPrn = if avPrn = null then String.Empty else avPrn
        match avRepoBranch, avPrn with
        | "master", "" -> true
        | _ -> false

Target "Default" id

"Install KRE"
    ==> "Clean"
    ==> "Prepare"
    ==> "Pass 1"
    ==> "Pass 2"
    ==> "Pass 3"
    =?> ("Publish", shouldPublish)
    ==> "Default"

// start build
RunTargetOrDefault "Default"