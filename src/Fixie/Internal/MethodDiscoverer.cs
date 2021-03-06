﻿namespace Fixie.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    class MethodDiscoverer
    {
        readonly IReadOnlyList<Func<MethodInfo, bool>> testMethodConditions;

        public MethodDiscoverer(Discovery discovery)
            => testMethodConditions = discovery.Config.TestMethodConditions;

        public IReadOnlyList<MethodInfo> TestMethods(Type testClass)
        {
            try
            {
                bool testClassIsDisposable = IsDisposable(testClass);

                return testClass
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(method => method.DeclaringType != typeof(object))
                    .Where(method => !(testClassIsDisposable && HasDisposeSignature(method)))
                    .Where(IsMatch)
                    .ToList();
            }
            catch (Exception exception)
            {
                throw new Exception(
                    "Exception thrown while attempting to run a custom method-discovery predicate. " +
                    "Check the inner exception for more details.", exception);
            }
        }

        bool IsMatch(MethodInfo candidate)
            => testMethodConditions.All(condition => condition(candidate));

        static bool IsDisposable(Type type)
            => type.GetInterfaces().Contains(typeof(IDisposable));

        static bool HasDisposeSignature(MethodInfo method)
            => method.Name == "Dispose" && method.IsVoid() && method.GetParameters().Length == 0;
    }
}