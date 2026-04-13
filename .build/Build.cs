using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Components;
using System.Collections.Generic;
using Serilog;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.NuGet;

using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions("ci_build",
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = false,
    OnPushBranches = ["4.0.x"],
    InvokedTargets = [nameof(IPublish.Publish)],
    ImportSecrets = ["NUGET_API_KEY"])]
class Build : NukeBuild, IHazSolution, ITest, IPack, ICompile, IRestore, IPublish {
    private readonly AbsolutePath _output = RootDirectory / "artifacts";

    [Parameter, Secret] private readonly string NugetApiKey;

    [Solution(GenerateProjects = true)]
    private readonly Solution Solution;

    private readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    string IPublish.NuGetApiKey => NugetApiKey;

    Nuke.Common.ProjectModel.Solution IHazSolution.Solution => Solution;

    AbsolutePath IPack.PackagesDirectory => _output;

    Configure<DotNetNuGetPushSettings> IPublish.PushSettings => _ => _
        .EnableSkipDuplicate();

    public IEnumerable<Project> TestProjects => Solution.AllProjects;

    public static int Main() => Execute<Build>(x => ((IPack)x).Pack);

    private T From<T>() where T : INukeBuild => (T)(object)this;

    #region Targets

    Target Clean => _ => _
        .Executes(() => {
            _output.CreateOrCleanDirectory();
            Log.Information("clear over!");
        });

    Target IRestore.Restore => _ => _
        .DependsOn(Clean)
        .Executes(() => {
            DotNetRestore(s => s.SetProjectFile(Solution));
        });

    Target ICompile.Compile => _ => _
        .TryDependsOn<IRestore>()
        .Executes(() => {
            DotNetBuild(x => x
                .SetProjectFile(Solution.MinecraftLaunch)
                .SetConfiguration(Configuration.Release)
                .EnableNoRestore());
        });

    Target ITest.Test => _ => _
        .Inherit<ITest>()
        .TryDependsOn<ICompile>();

    Target IPack.Pack => _ => _
        .TryDependsOn<ITest>()
        .Executes(() => {
            DotNetPack(x => x
                .SetConfiguration(Configuration.Release)
                .SetOutputDirectory(_output)
                .CombineWith([Solution.MinecraftLaunch, Solution.MinecraftLaunch_Base], (s, proj) =>s
                    .EnableNoBuild()
                    .SetProject(proj)));
        });

    Target IPublish.Publish => _ => _
        .Inherit<IPublish>()
        .Consumes(From<IPack>().Pack)
        .OnlyWhenStatic(() => IsServerBuild);

    #endregion
}
//Clean → Restore → Compile → Pack → Push