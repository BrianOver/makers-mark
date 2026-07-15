#if DEBUG
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Godot;

namespace GodotClient;

/// <summary>
/// Engine-test infrastructure, not game code (and compiled out of Release builds).
///
/// gdUnit4Net's Godot runtime runner resolves [RequireGodotRuntime] test classes with
/// a by-NAME <c>Assembly.Load</c> inside the Godot process. Godot's plugin
/// AssemblyLoadContext only resolves assemblies listed in GodotClient.deps.json, so a
/// SEPARATE test assembly (GodotClient.Tests.dll — KTD3's split test project) can
/// never resolve by name on its own. This module initializer adds a last-chance
/// <see cref="AssemblyLoadContext.Resolving"/> probe over the project's build output
/// tree (res://.godot/mono/temp/bin/**), which is where both GodotClient.dll and the
/// test assembly land. Loading through the SAME context keeps the test assembly's
/// view of GodotClient/GameSim types unified with the running game assembly.
/// No-op in production: the event only fires for assembly names the normal
/// resolution chain already failed to find.
/// </summary>
internal static class TestAssemblyResolver
{
    [ModuleInitializer]
    [SuppressMessage(
        "Usage",
        "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification = "Deliberate: the hook must install before gdUnit4's runner resolves the test assembly; Godot game assemblies are app-level, not redistributable libraries.")]
    internal static void Install()
    {
        var context = AssemblyLoadContext.GetLoadContext(typeof(TestAssemblyResolver).Assembly);
        if (context is not null)
        {
            context.Resolving += ResolveFromBuildOutput;
        }
    }

    private static Assembly? ResolveFromBuildOutput(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name is null)
        {
            return null;
        }

        try
        {
            // Godot loads plugin assemblies from streams (Assembly.Location is empty),
            // so anchor the probe on the project path instead.
            var binRoot = ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin");
            if (!Directory.Exists(binRoot))
            {
                return null;
            }

            var candidate = Directory
                .EnumerateFiles(binRoot, name.Name + ".dll", SearchOption.AllDirectories)
                .FirstOrDefault();
            return candidate is null ? null : context.LoadFromAssemblyPath(candidate);
        }
        catch (Exception)
        {
            return null; // resolution fallthrough — the runtime reports the original failure
        }
    }
}
#endif
