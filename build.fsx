// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

//#r "paket: group DotNetCoreBuild //"

#r "paket:
nuget Fake.Core
nuget Fake.DotNet.Cli
nuget Fake.IO.FileSystem
nuget Fake.Core.Target
nuget Fake.Core.Process//"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core.TargetOperators
open Fake.IO.Globbing.Operators
open Fake.Core
open Fake.DotNet

// --------------------------------------------------------------------------------------
// Build variables
// --------------------------------------------------------------------------------------

//let buildDir  = ".src//build/"

let project = !! "./src/**/*.fsproj" |> Seq.head
let testProject = !! "./test/**/*.fsproj" |> Seq.head
let allProjects = [project; testProject]
let dotnetcliVersion = "2.0.2"
let dotnetExePath = "dotnet"
let paketPath = "./.paket/paket.exe"
let versionFile = ".version"

// --------------------------------------------------------------------------------------
// Helpers
// --------------------------------------------------------------------------------------

let path = System.IO.Path.GetDirectoryName

let versionRegex = System.Text.RegularExpressions.Regex(@"(\d+)\.(\d+).(\d+)(-\w+\d*)?")

let readVersion () = 
    let rawVersion = System.IO.File.ReadLines versionFile |> Seq.head
    let result = versionRegex.Match rawVersion
    if result.Success then
        int result.Groups.[1].Value, int result.Groups.[2].Value, int result.Groups.[3].Value, result.Groups.[4].Value
    else
        failwith "Incorrect version format"

let increaseVersion (major, minor, patch, extra) = (major, minor, patch + 1, extra)

let strVersion (major, minor, patch, extra) = sprintf "%d.%d.%d%s" major minor patch extra

let writeVersion version = System.IO.File.WriteAllText (versionFile, strVersion version)

let run' timeout args dir cmd =
    if Process.execSimple (fun info ->
        { info with 
            FileName = cmd
            Arguments = args
            WorkingDirectory = if not (System.String.IsNullOrWhiteSpace dir) then dir else info.WorkingDirectory}
    ) timeout <> 0 then
        failwithf "Error while running '%s' with args: %s" cmd args

let run = run' System.TimeSpan.MaxValue

let runDotnet workingDir args =
    let result =
        Process.execSimple (fun info ->
            { info with 
                FileName = dotnetExePath
                Arguments = args
                WorkingDirectory = workingDir}) System.TimeSpan.MaxValue
    if result <> 0 then failwithf "dotnet %s failed" args

// --------------------------------------------------------------------------------------
// Targets
// --------------------------------------------------------------------------------------

Target.create "Install" (fun _ -> 
    if (sprintf "%A" Fake.SystemHelper.Environment.Environment.OSVersion).Contains("Windows") 
        then paketPath
        else "mono " + paketPath
    |> run "install" ".")

Target.create "Clean" (fun _ ->
    allProjects
    |> Seq.iter (fun p ->
        let dir = path p
        let k = !! (dir + "/bin") |> Seq.append !! (dir + "/obj") |> Seq.toArray
        printfn "Cleaning build dirs..."
        k |> Seq.iter (fun dir -> 
            printf "Cleaning dir %s" dir
            Fake.IO.Shell.cleanDir dir
            Fake.IO.Shell.deleteDir dir
            printfn " => Done")
    )
)

Target.create "InstallDotNetCLI" (fun _ ->
    DotNet.install 
        (fun opts -> { opts with Version = DotNet.CliVersion.Version dotnetcliVersion }) 
        (DotNet.Options.Create ()) 
    |> ignore
)

Target.create "Build" (fun param ->
    let cmd =
        match param.Context.Arguments with
        | []
        | "Release"::_ -> " -c Release"
        | "Debug"::_ -> " -c Debug"
        | x::_ -> failwith (sprintf "Unkown Build argument %s" x)
        |> sprintf "build%s"
    allProjects
    |> Seq.iter (fun p ->
        let dir = path p
        runDotnet dir cmd
    )
)

Target.create "Test" (fun param -> runDotnet (path testProject)  "run")

Target.create "Pack" (fun param ->
    let version = () |> readVersion
    match param.Context.Arguments with
    | ["Debug"]
    | ["Debug"; _] -> " -c Debug"
    | []
    | [_]
    | ["Release"]
    | ["Release"; _] -> " -c Release"
    | _ -> failwith "Invalid arguments. Usage: Publish [Debug|Release] [ApiKey]"
    |> sprintf "pack -p:Version=%s%s" (strVersion version)
    |> runDotnet (path project)
)

Target.create "Publish" (fun param ->
    let key = 
        match param.Context.Arguments with
        | [key] -> key
        | _ -> failwith "Invalid arguments. Usage: Publish ApiKey"
    let package = !! ((path project) + "/bin/Release/*.nupkg") |> Seq.head
    runDotnet "." (sprintf "nuget push %s -k %s -s https://api.nuget.org/v3/index.json" package key)
    ()
    |> readVersion
    |> increaseVersion
    |> writeVersion
    run (sprintf "add %s" versionFile) "." "git"
    run "commit -m \"Version bump\"" "." "git"
    run "push" "." "git"
)

// --------------------------------------------------------------------------------------
// Build order
// --------------------------------------------------------------------------------------

"InstallDotNetCLI"
==> "Build"
==> "Test"

"InstallDotNetCLI"
==> "Pack"
==> "Publish"

Target.runOrDefaultWithArguments "Build"
