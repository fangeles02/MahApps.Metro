
//////////////////////////////////////////////////////////////////////
// TOOLS / ADDINS
//////////////////////////////////////////////////////////////////////

#tool paket:?package=GitVersion.CommandLine
#tool paket:?package=gitreleasemanager
#tool paket:?package=xunit.runner.console
#addin paket:?package=Cake.Figlet
#addin paket:?package=Cake.Paket

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
if (string.IsNullOrWhiteSpace(target))
{
    target = "Default";
}

var configuration = Argument("configuration", "Release");
if (string.IsNullOrWhiteSpace(configuration))
{
    configuration = "Release";
}

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Set build version
GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.BuildServer });
GitVersion gitVersion;

var local = BuildSystem.IsLocalBuild;
var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;
var isDevelopBranch = StringComparer.OrdinalIgnoreCase.Equals("develop", AppVeyor.Environment.Repository.Branch);
var isReleaseBranch = StringComparer.OrdinalIgnoreCase.Equals("master", AppVeyor.Environment.Repository.Branch);
var isTagged = AppVeyor.Environment.Repository.Tag.IsTag;

// Define directories.
var publishDir = "./Publish";

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Setup(context =>
{
    context.Tools.RegisterFile("./packages/NuGet.CommandLine/tools/NuGet.exe");

    gitVersion = GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.Json });
    Information("Informational Version  : {0}", gitVersion.InformationalVersion);
    Information("SemVer Version         : {0}", gitVersion.SemVer);
    Information("AssemblySemVer Version : {0}", gitVersion.AssemblySemVer);
    Information("MajorMinorPatch Version: {0}", gitVersion.MajorMinorPatch);
    Information("NuGet Version          : {0}", gitVersion.NuGetVersion);
    Information("IsLocalBuild           : {0}", local);

    Information(Figlet("MahApps.Metro"));
});

Task("Clean")
    .Does(() =>
{
	CleanDirectories("./**/bin");
	CleanDirectories("./**/obj");
});

Task("NuGet-Paket-Restore")
    .IsDependentOn("Clean")
    .Does(() =>
{
    PaketRestore();

    // Restore all NuGet packages.
    var solutions = GetFiles("./**/*.sln");
    foreach(var solution in solutions)
    {
    	Information("Restoring {0}", solution);
        MSBuild(solution, settings => settings.SetMaxCpuCount(0).SetConfiguration(configuration).WithTarget("restore"));
    }
});

Task("Update-SolutionInfo")
    .Does(() =>
{
	var solutionInfo = "./MahApps.Metro/MahApps.Metro/Properties/AssemblyInfo.cs";
	GitVersion(new GitVersionSettings { UpdateAssemblyInfo = true, UpdateAssemblyInfoFilePath = solutionInfo});
});

Task("Build")
    .IsDependentOn("NuGet-Paket-Restore")
    .Does(() =>
{
    var solutions = GetFiles("./**/*.sln");
    foreach(var solution in solutions)
    {
    	Information("Building {0}", solution);
        MSBuild(solution, settings => settings.SetMaxCpuCount(0).SetConfiguration(configuration));
    }
});

Task("Paket-Pack")
    //.WithCriteria(ShouldRunRelease())
    .Does(() =>
{
	EnsureDirectoryExists(Directory(publishDir));
	PaketPack(publishDir, new PaketPackSettings { Version = isReleaseBranch ? gitVersion.MajorMinorPatch : gitVersion.NuGetVersion });
});

Task("Zip-Demos")
    //.WithCriteria(ShouldRunRelease())
    .Does(() =>
{
	EnsureDirectoryExists(Directory(publishDir));
    Zip("./MahApps.Metro.Samples/MahApps.Metro.Demo/bin/" + configuration, publishDir + "/MahApps.Metro.Demo-v" + gitVersion.NuGetVersion + ".zip");
    Zip("./MahApps.Metro.Samples/MahApps.Metro.Caliburn.Demo/bin/" + configuration, publishDir + "/MahApps.Metro.Caliburn.Demo-v" + gitVersion.NuGetVersion + ".zip");
});

Task("Unit-Tests")
    //.WithCriteria(ShouldRunRelease())
    .Does(() =>
{
    XUnit(
        "./Mahapps.Metro.Tests/bin/" + configuration + "/**/*.Tests.dll",
        new XUnitSettings { ToolPath = "./packages/cake/xunit.runner.console/tools/net452/xunit.console.exe" }
    );
});

Task("CreateRelease")
    .WithCriteria(() => !isTagged)
    .Does(() =>
{
    var username = EnvironmentVariable("GITHUB_USERNAME");
    if (string.IsNullOrEmpty(username))
    {
        throw new Exception("The GITHUB_USERNAME environment variable is not defined.");
    }

    var token = EnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The GITHUB_TOKEN environment variable is not defined.");
    }

    GitReleaseManagerCreate(username, token, "MahApps", "MahApps.Metro", new GitReleaseManagerCreateSettings {
        Milestone         = gitVersion.MajorMinorPatch,
        Name              = gitVersion.MajorMinorPatch,
        Prerelease        = false,
        TargetCommitish   = "master",
        WorkingDirectory  = "../"
    });
});

Task("ExportReleaseNotes")
    .Does(() =>
{
    var username = EnvironmentVariable("GITHUB_USERNAME");
    if (string.IsNullOrEmpty(username))
    {
        throw new Exception("The GITHUB_USERNAME environment variable is not defined.");
    }

    var token = EnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The GITHUB_TOKEN environment variable is not defined.");
    }

    EnsureDirectoryExists(Directory(publishDir));
    GitReleaseManagerExport(username, token, "MahApps", "MahApps.Metro", publishDir + "/releasenotes.md", new GitReleaseManagerExportSettings {
        // TagName         = gitVersion.SemVer,
        TagName         = "1.5.0",
        TargetDirectory = publishDir,
        LogFilePath     = publishDir + "/grm.log"
    });
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Build");

Task("appveyor")
    .IsDependentOn("Update-SolutionInfo")
    .IsDependentOn("Build")
    .IsDependentOn("Unit-Tests")
    .IsDependentOn("Paket-Pack")
    .IsDependentOn("Zip-Demos");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
