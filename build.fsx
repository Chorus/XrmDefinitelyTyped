// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r @"packages/FAKE/tools/FakeLib.dll"
open Fake
open Fake.Git
open Fake.NpmHelper
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open System
open System.IO
#if MONO
#else
#load "packages/SourceLink.Fake/tools/Fake.fsx"
open SourceLink
#endif


// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "XrmDefinitelyTyped"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "Tool to generate TypeScript declaration files for MS Dynamics 365/CRM client-side coding."

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "Tool to generate TypeScript declaration files for MS Dynamics 365/CRM client-side coding."

// List of author names (for NuGet package)
let authors = [ "Delegate A/S"; "Martin Kasban Tange" ]

// Tags for your project (for NuGet package)
let tags = "microsoft crm xrm dynamics xrmdefinitelytyped typescript ts definitelytyped javascript sdk rest odata fsharp f# delegate D365 Dynamics365 365"

let company = "Delegate A/S"
let copyright = @"Copyright (c) Delegate A/S 2017"

// File system information 
let solutionFile  = "XrmDefinitelyTyped.sln"


// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "delegateas" 
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "XrmDefinitelyTyped"

// The profile where the docs project is posted 
let docsGitHome = "https://github.com/delegateas"
// The name of the project on GitHub
let docsGitName = "delegateas.github.io"
// The name of the subfolder
let fullProjectName = "Delegate.XrmDefinitelyTyped"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
let release = LoadReleaseNotes "RELEASE_NOTES.md"

// Helper active pattern for project types
let (|Fsproj|Csproj|Vbproj|) (projFileName:string) = 
  match projFileName with
  | f when f.EndsWith("fsproj") -> Fsproj
  | f when f.EndsWith("csproj") -> Csproj
  | f when f.EndsWith("vbproj") -> Vbproj
  | _                           -> failwith (sprintf "Project file %s not supported. Unknown project type." projFileName)

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
  let getAssemblyInfoAttributes projectName =
    [ Attribute.Title (projectName)
      Attribute.Product project
      Attribute.Description summary
      Attribute.Company company
      Attribute.Copyright copyright
      Attribute.Version release.AssemblyVersion
      Attribute.FileVersion release.AssemblyVersion ]

  let getProjectDetails projectPath =
    let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
    ( projectPath, 
      projectName,
      System.IO.Path.GetDirectoryName(projectPath),
      (getAssemblyInfoAttributes projectName)
    )

  !! "src/**/*.??proj"
  |> Seq.map getProjectDetails
  |> Seq.iter (fun (projFileName, projectName, folderName, attributes) ->
    match projFileName with
    | Fsproj -> CreateFSharpAssemblyInfo (folderName @@ "AssemblyInfo.fs") attributes
    | Csproj -> CreateCSharpAssemblyInfo ((folderName @@ "Properties") @@ "AssemblyInfo.cs") attributes
    | Vbproj -> CreateVisualBasicAssemblyInfo ((folderName @@ "My Project") @@ "AssemblyInfo.vb") attributes
      )
)

// Copies binaries from default VS location to expected bin folder
// But keeps a subdirectory structure for each project in the 
// src folder to support multiple project outputs
Target "CopyBinaries" (fun _ ->
  !! "src/**/*.??proj"
  |>  Seq.map (fun f -> ((Path.GetDirectoryName f) @@ "bin/Release", "bin" @@ (Path.GetFileNameWithoutExtension f)))
  |>  Seq.iter (fun (fromDir, toDir) -> CopyDir toDir fromDir (fun _ -> true))
)

// --------------------------------------------------------------------------------------
// Clean build results

Target "Clean" (fun _ ->
  CleanDirs ["bin"; "temp"]
)

Target "CleanDocs" (fun _ ->
  CleanDirs ["docs/output"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target "BuildSetup" (fun _ ->

  // Setup EnvInfo.config file if it does not exist
  let envInfoPath = Path.GetFullPath @"src/XrmDefinitelyTyped/EnvInfo.config"

  if not(fileExists envInfoPath) then
    File.WriteAllLines(envInfoPath, 
      [
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
        "<appSettings>"
        "</appSettings>"
      ]
    )
)


Target "Build" (fun _ ->
  !! solutionFile
  |> MSBuildRelease "" "Rebuild"
  |> ignore
)


Target "Test" (fun _ ->
  // Run XrmDefinitelyTyped
  let result = 
    ExecProcess 
      (fun info -> 
        let pathToRelease = Path.GetFullPath @"src/XrmDefinitelyTyped/bin/Release"
        info.FileName <- pathToRelease @@ @"XrmDefinitelyTyped.exe"
        info.WorkingDirectory <- pathToRelease
        info.Arguments <-
          [
            "load", "../../XdtData.json"
            "out", "../../../../test/typings/XRM"
            "jsLib", "../../../../test/lib"
            "rest", "RestNs"
            "web", "WebNs"
            "entities", "account,contact"
          ]
          |> List.map (fun (k,v) -> sprintf "-%s:%s" k v)
          |> String.concat " "
      )
      (TimeSpan.FromMinutes 5.0)

  if result <> 0 then 
    failwithf "XrmDefinitelyTyped.exe returned with a non-zero exit code"


  // Install necessary npm packages in test folder
  Npm 
    (fun p ->
      { p with
            Command = Install Standard
            WorkingDirectory = @"test"
      }
    )

  // Run tests
  Npm 
    (fun p ->
      { p with
            Command = (Run "test")
            WorkingDirectory = @"test"
      }
    )
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "NuGet" (fun _ ->
  NuGetHelper.NuGet (fun p -> 
    { p with
        Title = project
        Authors = authors
        Project = fullProjectName
        Summary = summary
        Description = description
        Copyright = copyright
        Tags = tags
        NoDefaultExcludes = true
        AccessKey = getBuildParamOrDefault "delegateas-nugetkey" ""
        Dependencies = []
        References = [] 

        OutputPath = "bin"
        Version = release.NugetVersion
        ReleaseNotes = toLines release.Notes }) 
    (@"nuget/" + fullProjectName + ".nuspec"))
//  Paket.Pack(fun p ->
//        { p with
//            OutputPath = "bin"
//            Version = release.NugetVersion            
//            ReleaseNotes = toLines release.Notes})
//)        

Target "PublishNuget" (fun _ ->
  Paket.Push(fun p -> 
    { p with
        ApiKey = getBuildParamOrDefault "delegateas-nugetkey" ""
        WorkingDir = "bin" })
)


// --------------------------------------------------------------------------------------
// Generate the documentation

Target "GenerateReferenceDocs" (fun _ ->
  if not <| executeFSIWithArgs "docs/tools" "generate.fsx" ["--define:RELEASE"; "--define:REFERENCE"] [] then
    failwith "generating reference documentation failed"
)

let generateHelp' fail debug =
  let args =
    if debug then ["--define:HELP"]
    else ["--define:RELEASE"; "--define:HELP"]
  if executeFSIWithArgs "docs/tools" "generate.fsx" args [] then
    traceImportant (sprintf "Help generated with args: %A" args)
  else
    if fail then
      failwith "generating help documentation failed"
    else
      traceImportant "generating help documentation failed"

let generateHelp fail =
  generateHelp' fail false

let copySpecialFiles () =
  DeleteFile "docs/content/release-notes.md"
  CopyFile "docs/content/" "RELEASE_NOTES.md"
  Rename "docs/content/release-notes.md" "docs/content/RELEASE_NOTES.md"

  DeleteFile "docs/content/license.md"
  CopyFile "docs/content/" "LICENSE.md"
  Rename "docs/content/license.md" "docs/content/LICENSE.md"

let copyExtraFiles () =
  let path = "docs/output/files"
  CleanDir path
  // Copy additional files
  CopyRecursive "files" path true |> tracefn "%A"
  // Copy over compiled javascript resources
  CopyRecursive "bin/XrmDefinitelyTyped/Resources" path true |> tracefn "%A"


Target "GenerateHelp" (fun _ ->
  copySpecialFiles()
  generateHelp true
  copyExtraFiles()
)

Target "GenerateHelpDebug" (fun _ ->
  copySpecialFiles()
  generateHelp' true true
  copyExtraFiles()
)

Target "KeepRunning" (fun _ ->    
  use watcher = new FileSystemWatcher(DirectoryInfo("docs/content").FullName,"*.*")
  watcher.EnableRaisingEvents <- true
  watcher.Changed.Add(fun e -> generateHelp' false true)
  watcher.Created.Add(fun e -> generateHelp' false true)
  watcher.Renamed.Add(fun e -> generateHelp' false true)
  watcher.Deleted.Add(fun e -> generateHelp' false true)

  traceImportant "Waiting for help edits. Press any key to stop."

  System.Console.ReadKey() |> ignore

  watcher.EnableRaisingEvents <- false
  watcher.Dispose()
)

Target "GenerateDocs" DoNothing

let createIndexFsx lang =
  let content = """(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../../bin"

(**
F# Project Scaffold ({0})
=========================
*)
"""
  let targetDir = "docs/content" @@ lang
  let targetFile = targetDir @@ "index.fsx"
  ensureDirectory targetDir
  System.IO.File.WriteAllText(targetFile, System.String.Format(content, lang))

Target "AddLangDocs" (fun _ ->
  let args = System.Environment.GetCommandLineArgs()
  if args.Length < 4 then
    failwith "Language not specified."

  args.[3..]
  |> Seq.iter (fun lang ->
    if lang.Length <> 2 && lang.Length <> 3 then
      failwithf "Language must be 2 or 3 characters (ex. 'de', 'fr', 'ja', 'gsw', etc.): %s" lang

    let templateFileName = "template.cshtml"
    let templateDir = "docs/tools/templates"
    let langTemplateDir = templateDir @@ lang
    let langTemplateFileName = langTemplateDir @@ templateFileName

    if System.IO.File.Exists(langTemplateFileName) then
      failwithf "Documents for specified language '%s' have already been added." lang

    ensureDirectory langTemplateDir
    Copy langTemplateDir [ templateDir @@ templateFileName ]

    createIndexFsx lang)
)

// --------------------------------------------------------------------------------------
// Release Scripts

Target "ReleaseDocs" (fun _ ->
  let tempDocsDir = "temp/docs"
  let tempDocsSubDir = tempDocsDir @@ fullProjectName
  CleanDir tempDocsDir
  Repository.cloneSingleBranch "" (docsGitHome + "/" + docsGitName + ".git") 
    "master" tempDocsDir
  fullclean tempDocsSubDir
  CopyRecursive "docs/output" tempDocsSubDir true |> tracefn "%A"
  StageAll tempDocsSubDir
  Commit tempDocsSubDir 
    (sprintf "Update generated documentation for version %s" 
      release.NugetVersion)
  Branches.push tempDocsDir)

#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

Target "Release" (fun _ ->
  StageAll ""
  Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
  Branches.push ""

  Branches.tag "" release.NugetVersion
  Branches.pushTag "" "origin" release.NugetVersion
    
  // release on github
  createClient (getBuildParamOrDefault "github-user" "") (getBuildParamOrDefault "github-pw" "")
  |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
  // TODO: |> uploadFile "PATH_TO_FILE"    
  |> releaseDraft
  |> Async.RunSynchronously
)

Target "BuildPackage" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "CopyBinaries"
//  =?> ("GenerateReferenceDocs",isLocalBuild)
  ==> "Test"
  ==> "All"

"BuildSetup"
  ==> "Build" 
  ==> "Test"
  ==> "NuGet"
  ==> "BuildPackage"
  ==> "PublishNuget"
  ==> "Release"

  
"GenerateHelp"
  //  ==> "GenerateReferenceDocs"
  ==> "GenerateDocs"


[ "GenerateHelp"; "GenerateHelpDebug" ]
|> List.iter 
  (fun genHelp ->
    genHelp <== [ "CopyBinaries"; "CleanDocs" ] |> ignore
  )

"GenerateHelpDebug"
  ==> "KeepRunning"


"GenerateDocs"
  ==> "ReleaseDocs"
  ==> "Release"


RunTargetOrDefault "All"
