﻿namespace Fixie.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Assertions;
    using Fixie.Internal;

    public class LifecycleTests
    {
        static string[] FailingMembers;

        readonly Discovery discovery;

        public LifecycleTests()
        {
            FailingMembers = null;
            discovery = new SelfTestDiscovery();
        }

        static void FailDuring(params string[] failingMemberNames)
        {
            FailingMembers = failingMemberNames;
        }

        Output Run<TSampleTestClass, TExecution>() where TExecution : Execution, new()
        {
            return Run<TExecution>(typeof(TSampleTestClass));
        }

        Output Run<TExecution>(Type testClass) where TExecution : Execution, new()
        {
            var listener = new StubListener();
            var execution = new TExecution();

            using (var console = new RedirectedConsole())
            {
                Utility.Run(listener, discovery, execution, testClass);

                return new Output(console.Lines().ToArray(), listener.Entries.ToArray());
            }
        }

        class Output
        {
            readonly string[] lifecycle;
            readonly string[] results;

            public Output(string[] lifecycle, string[] results)
            {
                this.lifecycle = lifecycle;
                this.results = results;
            }

            public void ShouldHaveLifecycle(params string[] expected)
            {
                lifecycle.ShouldBe(expected);
            }

            public void ShouldHaveResults(params string[] expected)
            {
                var namespaceQualifiedExpectation = expected.Select(x => "Fixie.Tests.LifecycleTests+" + x).ToArray();

                results.ShouldBe(namespaceQualifiedExpectation);
            }
        }

        class SampleTestClass : IDisposable
        {
            bool disposed;

            public SampleTestClass()
            {
                WhereAmI();
            }

            public void Pass()
            {
                WhereAmI();
            }

            public void Fail()
            {
                WhereAmI();
                throw new FailureException();
            }

            public void Skip()
            {
                WhereAmI();
                throw new ShouldBeUnreachableException();
            }

            public void Dispose()
            {
                if (disposed)
                    throw new ShouldBeUnreachableException();
                disposed = true;

                WhereAmI();
            }
        }

        class AllSkippedTestClass : IDisposable
        {
            bool disposed;

            public AllSkippedTestClass()
            {
                WhereAmI();
            }

            public void SkipA()
            {
                WhereAmI();
                throw new ShouldBeUnreachableException();
            }

            public void SkipB()
            {
                WhereAmI();
                throw new ShouldBeUnreachableException();
            }

            public void SkipC()
            {
                WhereAmI();
                throw new ShouldBeUnreachableException();
            }

            public void Dispose()
            {
                if (disposed)
                    throw new ShouldBeUnreachableException();
                disposed = true;

                WhereAmI();
            }
        }

        class ParameterizedSampleTestClass : IDisposable
        {
            bool disposed;

            public ParameterizedSampleTestClass()
            {
                WhereAmI();
            }

            public void IntArg(int i)
            {
                WhereAmI();
                if (i != 0)
                    throw new Exception("Expected 0, but was " + i);
            }

            public void BoolArg(bool b)
            {
                WhereAmI();
                if (!b)
                    throw new Exception("Expected true, but was false");
            }

            public void Dispose()
            {
                if (disposed)
                    throw new ShouldBeUnreachableException();
                disposed = true;

                WhereAmI();
            }
        }

        static class StaticTestClass
        {
            public static void Pass()
            {
                WhereAmI();
            }

            public static void Fail()
            {
                WhereAmI();
                throw new FailureException();
            }

            public static void Skip()
            {
                WhereAmI();
                throw new ShouldBeUnreachableException();
            }
        }

        static void WhereAmI([CallerMemberName] string member = null)
        {
            Console.WriteLine(member);

            if (FailingMembers != null && FailingMembers.Contains(member))
                throw new FailureException(member);
        }

        class CreateInstancePerCase : Execution
        {
            public void Execute(TestClass testClass)
            {
                testClass.RunCases(@case =>
                {
                    if (@case.Method.Name.Contains("Skip"))
                        return;

                    var instance = testClass.Construct();

                    @case.Execute(instance);

                    instance.Dispose();
                });
            }
        }

        class CreateInstancePerClass : Execution
        {
            public void Execute(TestClass testClass)
            {
                var instance = testClass.Construct();

                testClass.RunCases(@case =>
                {
                    if (@case.Method.Name.Contains("Skip"))
                    {
                        @case.Skip("skipped by naming convention");
                        return;
                    }

                    CaseSetUp(@case);
                    @case.Execute(instance);
                    CaseTearDown(@case);
                });

                instance.Dispose();
            }

            static void CaseSetUp(Case @case)
            {
                @case.Class.ShouldBe(typeof(SampleTestClass));
                WhereAmI();
            }

            static void CaseTearDown(Case @case)
            {
                @case.Class.ShouldBe(typeof(SampleTestClass));
                WhereAmI();
            }
        }

        class BuggyExecution : Execution
        {
            public void Execute(TestClass testClass)
                => throw new Exception("Unsafe custom execution threw!");
        }

        class ShortCircuitClassExecution : Execution
        {
            public void Execute(TestClass testClass)
            {
                //Class lifecycle chooses not to invoke testClass.RunCases(...).
                //Since the test cases never run, they are all considered
                //'skipped'.
            }
        }

        class ShortCircuitCaseExection : Execution
        {
            public void Execute(TestClass testClass)
            {
                testClass.RunCases(@case =>
                {
                    //Case lifecycle chooses not to invoke @case.Execute(instance).
                    //Since the test cases never run, they are all considered
                    //'skipped'.
                });
            }
        }

        class RunCasesTwice : Execution
        {
            public void Execute(TestClass testClass)
            {
                var instance = testClass.Construct();

                for (int i = 1; i <= 2; i++)
                {
                    testClass.RunCases(@case =>
                    {
                        if (@case.Method.Name.Contains("Skip"))
                            return;

                        @case.Execute(instance);
                    });
                }

                instance.Dispose();
            }
        }

        class RetryFailingCases : Execution
        {
            public void Execute(TestClass testClass)
            {
                var instance = testClass.Construct();

                testClass.RunCases(@case =>
                {
                    if (@case.Method.Name.Contains("Skip"))
                        return;

                    @case.Execute(instance);

                    if (@case.Exception != null)
                        @case.Execute(instance);
                });

                instance.Dispose();
            }
        }

        class BuggyParameterSource : ParameterSource
        {
            public IEnumerable<object[]> GetParameters(MethodBase method)
            {
                if (method is ConstructorInfo)
                {
                    return new object[0][];
                }

                throw new Exception("Exception thrown while attempting to yield input parameters for method: " + method.Name);
            }
        }

        public void ShouldConstructPerCaseByDefault()
        {
            discovery.Methods.Where(x => x.Name != "Skip");

            var output = Run<SampleTestClass, DefaultExecution>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: 'Fail' failed!",
                "SampleTestClass.Pass passed");

            output.ShouldHaveLifecycle(
                ".ctor", "Fail", "Dispose",
                ".ctor", "Pass", "Dispose");
        }

        public void ShouldAllowConstructingPerCase()
        {
            var output = Run<SampleTestClass, CreateInstancePerCase>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: 'Fail' failed!",
                "SampleTestClass.Pass passed",
                "SampleTestClass.Skip skipped");

            output.ShouldHaveLifecycle(
                ".ctor", "Fail", "Dispose",
                ".ctor", "Pass", "Dispose");
        }

        public void ShouldAllowConstructingPerClass()
        {
            var output = Run<SampleTestClass, CreateInstancePerClass>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: 'Fail' failed!",
                "SampleTestClass.Pass passed",
                "SampleTestClass.Skip skipped: skipped by naming convention");

            output.ShouldHaveLifecycle(
                ".ctor",
                "CaseSetUp", "Fail", "CaseTearDown",
                "CaseSetUp", "Pass", "CaseTearDown",
                "Dispose");
        }

        public void ShouldSkipAllCasesWhenShortCircuitingClassExecution()
        {
            var output = Run<SampleTestClass, ShortCircuitClassExecution>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail skipped",
                "SampleTestClass.Pass skipped",
                "SampleTestClass.Skip skipped");

            output.ShouldHaveLifecycle();
        }

        public void ShouldSkipAllCasesWhenShortCircuitingCaseExecution()
        {
            var output = Run<SampleTestClass, ShortCircuitCaseExection>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail skipped",
                "SampleTestClass.Pass skipped",
                "SampleTestClass.Skip skipped");

            output.ShouldHaveLifecycle();
        }

        public void ShouldFailCaseWhenConstructingPerCaseAndConstructorThrows()
        {
            FailDuring(".ctor");

            var output = Run<SampleTestClass, CreateInstancePerCase>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: '.ctor' failed!",
                "SampleTestClass.Pass failed: '.ctor' failed!",
                "SampleTestClass.Skip skipped");

            output.ShouldHaveLifecycle(".ctor", ".ctor");
        }

        public void ShouldFailAllMethodsWhenCustomExecutionThrows()
        {
            var output = Run<SampleTestClass, BuggyExecution>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: Unsafe custom execution threw!",
                "SampleTestClass.Pass failed: Unsafe custom execution threw!",
                "SampleTestClass.Skip failed: Unsafe custom execution threw!");

            output.ShouldHaveLifecycle();
        }

        public void ShouldFailAllMethodsWhenConstructingPerClassAndConstructorThrows()
        {
            FailDuring(".ctor");

            var output = Run<SampleTestClass, CreateInstancePerClass>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: '.ctor' failed!",
                "SampleTestClass.Pass failed: '.ctor' failed!",
                "SampleTestClass.Skip failed: '.ctor' failed!");

            output.ShouldHaveLifecycle(".ctor");
        }

        public void ShouldFailCasesWhenConstructingPerClassAndCaseSetUpThrows()
        {
            FailDuring("CaseSetUp");

            var output = Run<SampleTestClass, CreateInstancePerClass>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: 'CaseSetUp' failed!",
                "SampleTestClass.Pass failed: 'CaseSetUp' failed!",
                "SampleTestClass.Skip skipped: skipped by naming convention");

            output.ShouldHaveLifecycle(
                ".ctor",
                "CaseSetUp",
                "CaseSetUp",
                "Dispose");
        }

        public void ShouldFailCasesWhenConstructingPerClassAndCaseTearDownThrows()
        {
            FailDuring("CaseTearDown");

            var output = Run<SampleTestClass, CreateInstancePerClass>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: 'Fail' failed!",
                "SampleTestClass.Fail failed: 'CaseTearDown' failed!",
                "SampleTestClass.Pass failed: 'CaseTearDown' failed!",
                "SampleTestClass.Skip skipped: skipped by naming convention");

            output.ShouldHaveLifecycle(
                ".ctor",
                "CaseSetUp", "Fail", "CaseTearDown",
                "CaseSetUp", "Pass", "CaseTearDown",
                "Dispose");
        }

        public void ShouldFailCaseWhenConstructingPerCaseAndDisposeThrows()
        {
            FailDuring("Dispose");

            var output = Run<SampleTestClass, CreateInstancePerCase>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: 'Fail' failed!",
                "SampleTestClass.Fail failed: 'Dispose' failed!",
                "SampleTestClass.Pass failed: 'Dispose' failed!",
                "SampleTestClass.Skip skipped");

            output.ShouldHaveLifecycle(
                ".ctor", "Fail", "Dispose",
                ".ctor", "Pass", "Dispose");
        }

        public void ShouldFailAllMethodsAfterReportingPrimaryResultsWhenConstructingPerClassAndDisposeThrows()
        {
            FailDuring("Dispose");

            var output = Run<SampleTestClass, CreateInstancePerClass>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: 'Fail' failed!",
                "SampleTestClass.Pass passed",
                "SampleTestClass.Skip skipped: skipped by naming convention",

                "SampleTestClass.Fail failed: 'Dispose' failed!",
                "SampleTestClass.Pass failed: 'Dispose' failed!",
                "SampleTestClass.Skip failed: 'Dispose' failed!");

            output.ShouldHaveLifecycle(
                ".ctor",
                "CaseSetUp", "Fail", "CaseTearDown",
                "CaseSetUp", "Pass", "CaseTearDown",
                "Dispose");
        }

        public void ShouldSkipLifecycleWhenConstructingPerCaseAndAllCasesAreSkipped()
        {
            var output = Run<AllSkippedTestClass, CreateInstancePerCase>();

            output.ShouldHaveResults(
                "AllSkippedTestClass.SkipA skipped",
                "AllSkippedTestClass.SkipB skipped",
                "AllSkippedTestClass.SkipC skipped");

            output.ShouldHaveLifecycle();
        }

        public void ShouldNotSkipLifecycleWhenConstructingPerClassAndAllCasesAreSkipped()
        {
            var output = Run<AllSkippedTestClass, CreateInstancePerClass>();

            output.ShouldHaveResults(
                "AllSkippedTestClass.SkipA skipped: skipped by naming convention",
                "AllSkippedTestClass.SkipB skipped: skipped by naming convention",
                "AllSkippedTestClass.SkipC skipped: skipped by naming convention");

            output.ShouldHaveLifecycle(".ctor", "Dispose");
        }

        public void ShouldSkipLifecycleWhenConstructingPerCaseButAllCasesFailCustomParameterGeneration()
        {
            discovery.Parameters.Add<BuggyParameterSource>();

            var output = Run<ParameterizedSampleTestClass, CreateInstancePerCase>();

            output.ShouldHaveResults(
                "ParameterizedSampleTestClass.BoolArg failed: Exception thrown while attempting to yield input parameters for method: BoolArg",
                "ParameterizedSampleTestClass.IntArg failed: Exception thrown while attempting to yield input parameters for method: IntArg");

            output.ShouldHaveLifecycle();
        }

        public void ShouldNotSkipLifecycleWhenConstructingPerClassAndAllCasesFailCustomParameterGeneration()
        {
            discovery.Parameters.Add<BuggyParameterSource>();

            var output = Run<ParameterizedSampleTestClass, CreateInstancePerClass>();

            output.ShouldHaveResults(
                "ParameterizedSampleTestClass.BoolArg failed: Exception thrown while attempting to yield input parameters for method: BoolArg",
                "ParameterizedSampleTestClass.IntArg failed: Exception thrown while attempting to yield input parameters for method: IntArg");

            output.ShouldHaveLifecycle(".ctor", "Dispose");
        }

        public void ShouldAllowRunningAllCasesMultipleTimes()
        {
            var output = Run<SampleTestClass, RunCasesTwice>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: 'Fail' failed!",
                "SampleTestClass.Pass passed",
                "SampleTestClass.Skip skipped",

                "SampleTestClass.Fail failed: 'Fail' failed!",
                "SampleTestClass.Pass passed",
                "SampleTestClass.Skip skipped");

            output.ShouldHaveLifecycle(
                ".ctor", "Fail", "Pass", "Fail", "Pass", "Dispose");
        }

        public void ShouldAllowExecutingACaseMultipleTimesBeforeEmittingItsResult()
        {
            var output = Run<SampleTestClass, RetryFailingCases>();

            output.ShouldHaveResults(
                "SampleTestClass.Fail failed: 'Fail' failed!",
                "SampleTestClass.Pass passed",
                "SampleTestClass.Skip skipped");

            output.ShouldHaveLifecycle(
                ".ctor", "Fail", "Fail", "Pass", "Dispose");
        }

        public void ShouldAllowStaticTestClassesAndMethodsBypassingConstructionAttempts()
        {
            var output = Run<CreateInstancePerCase>(typeof(StaticTestClass));

            output.ShouldHaveResults(
                "StaticTestClass.Fail failed: 'Fail' failed!",
                "StaticTestClass.Pass passed",
                "StaticTestClass.Skip skipped");

            output.ShouldHaveLifecycle(
                "Fail", "Pass");
        }
    }
}