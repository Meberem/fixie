﻿namespace Fixie.VisualStudio.TestAdapter
{
    using System;
    using System.IO;
    using System.Security;
    using System.Security.Permissions;
    using Execution;
    using Internal;

    public class ExecutionEnvironment : IDisposable
    {
        readonly string assemblyFullPath;
        readonly AppDomain appDomain;
        readonly string previousWorkingDirectory;

        public ExecutionEnvironment(string assemblyPath)
        {
            assemblyFullPath = Path.GetFullPath(assemblyPath);
            appDomain = CreateAppDomain(assemblyFullPath);

            previousWorkingDirectory = Directory.GetCurrentDirectory();
            var assemblyDirectory = Path.GetDirectoryName(assemblyFullPath);
            Directory.SetCurrentDirectory(assemblyDirectory);
        }

        public void DiscoverMethodGroups(Listener listeners, Options options)
        {
            using (var executionProxy = Create<ExecutionProxy>())
            using (var bus = new Bus(listeners))
                executionProxy.DiscoverMethodGroups(assemblyFullPath, options, bus);
        }

        public void RunAssembly(Listener listeners, Options options)
        {
            using (var executionProxy = Create<ExecutionProxy>())
            using (var bus = new Bus(listeners))
                executionProxy.RunAssembly(assemblyFullPath, options, bus);
        }

        public void RunMethods(Listener listeners, Options options, MethodGroup[] methodGroups)
        {
            using (var executionProxy = Create<ExecutionProxy>())
            using (var bus = new Bus(listeners))
                executionProxy.RunMethods(assemblyFullPath, options, bus, methodGroups);
        }

        T Create<T>() where T : LongLivedMarshalByRefObject
        {
            return (T)appDomain.CreateInstanceAndUnwrap(typeof(T).Assembly.FullName, typeof(T).FullName, false, 0, null, null, null, null);
        }

        public void Dispose()
        {
            AppDomain.Unload(appDomain);
            Directory.SetCurrentDirectory(previousWorkingDirectory);
        }

        static AppDomain CreateAppDomain(string assemblyFullPath)
        {
            var setup = new AppDomainSetup
            {
                ApplicationBase = Path.GetDirectoryName(assemblyFullPath),
                ApplicationName = Guid.NewGuid().ToString(),
                ConfigurationFile = GetOptionalConfigFullPath(assemblyFullPath)
            };

            return AppDomain.CreateDomain(setup.ApplicationName, null, setup, new PermissionSet(PermissionState.Unrestricted));
        }

        static string GetOptionalConfigFullPath(string assemblyFullPath)
        {
            var configFullPath = assemblyFullPath + ".config";

            return File.Exists(configFullPath) ? configFullPath : null;
        }
    }
}