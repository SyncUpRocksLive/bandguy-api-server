using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;
using SyncUpRocks.Data.Access.TypeHandlers;

namespace SyncUpRocks.Unit.Tests;

public static class TestInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // 1. Find all your local projects
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName?.StartsWith("SyncUp") == true)
            .ToArray();

        // 2. Register the Dapper handlers once for the whole test process
        DapperEntityMapper.RegisterHandlers(assemblies);

        // Optional: Log it so you know it's working on your workstation
        Console.WriteLine($"[TestSetup] Registered Dapper Handlers for {assemblies.Length} assemblies.");
    }
}