namespace Fixie.Tests.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Assertions;
    using Cases;
    using Fixie.Internal;
    using static Utility;

    public class RunnerTests
    {
        static readonly string Self = FullName<RunnerTests>();

        public void ShouldExecuteAllCasesInAllDiscoveredTestClasses()
        {
            var listener = new StubListener();

            var candidateTypes = new[]
            {
                typeof(SampleIrrelevantClass), typeof(PassTestClass), typeof(int),
                typeof(PassFailTestClass), typeof(SkipTestClass)
            };
            var discovery = new SelfTestDiscovery();
            var execution = new CreateInstancePerClass();

            var bus = new Bus(listener);
            new Runner(GetType().Assembly, bus).Run(candidateTypes, discovery, execution);

            listener.Entries.ShouldBe(
                Self + "+PassTestClass.PassA passed",
                Self + "+PassTestClass.PassB passed",
                Self + "+PassFailTestClass.Fail failed: 'Fail' failed!",
                Self + "+PassFailTestClass.Pass passed",
                Self + "+SkipTestClass.SkipA skipped",
                Self + "+SkipTestClass.SkipB skipped");
        }

        public void ShouldExecuteAllCasesInAllDiscoveredTestClassesByConstructorsParameters()
        {
            var listener = new StubListener();

            var candidateTypes = new[]
            {
                typeof(MultiConstructorWithDataTestClass)
            };
            var discovery = new SelfTestDiscovery();
            discovery.Parameters.Add<ParameterizedCaseTests.InputAttributeParameterSource>();
            var execution = new CreateInstancePerClass();

            var bus = new Bus(listener);
            new Runner(GetType().Assembly, bus).Run(candidateTypes, discovery, execution);

            listener.Entries.ShouldBe(
                Self + "+MultiConstructorWithDataTestClass(1, 1).StringArg(\"abc\") passed",
                Self + "+MultiConstructorWithDataTestClass(1, 1).StringArg(\"def\") passed",
                Self + "+MultiConstructorWithDataTestClass(1, 1).ZeroArg passed",
                Self + "+MultiConstructorWithDataTestClass(2, 1).StringArg(\"abc\") passed",
                Self + "+MultiConstructorWithDataTestClass(2, 1).StringArg(\"def\") passed",
                Self + "+MultiConstructorWithDataTestClass(2, 1).ZeroArg passed",
                Self + "+MultiConstructorWithDataTestClass(3, 1).StringArg(\"abc\") passed",
                Self + "+MultiConstructorWithDataTestClass(3, 1).StringArg(\"def\") passed",
                Self + "+MultiConstructorWithDataTestClass(3, 1).ZeroArg passed");
        }

        public void ShouldAllowRandomShufflingOfCaseExecutionOrder()
        {
            var listener = new StubListener();

            var candidateTypes = new[]
            {
                typeof(SampleIrrelevantClass), typeof(PassTestClass), typeof(int),
                typeof(PassFailTestClass), typeof(SkipTestClass)
            };
            var discovery = new SelfTestDiscovery();
            var execution = new CreateInstancePerClass();

            discovery.Methods
                .Shuffle(new Random(1));

            var bus = new Bus(listener);
            new Runner(GetType().Assembly, bus).Run(candidateTypes, discovery, execution);

            listener.Entries.ShouldBe(
                Self + "+PassTestClass.PassB passed",
                Self + "+PassTestClass.PassA passed",
                Self + "+PassFailTestClass.Fail failed: 'Fail' failed!",
                Self + "+PassFailTestClass.Pass passed",
                Self + "+SkipTestClass.SkipB skipped",
                Self + "+SkipTestClass.SkipA skipped");
        }

        public void ShouldReportFailuresForAllAffectedCasesWithoutShortCircuitingTestExecutionWhenCaseOrderingThrows()
        {
            var listener = new StubListener();

            var candidateTypes = new[]
            {
                typeof(SampleIrrelevantClass), typeof(PassTestClass), typeof(int),
                typeof(PassFailTestClass), typeof(SkipTestClass), typeof(BuggyParameterGenerationTestClass)
            };
            var discovery = new SelfTestDiscovery();
            var execution = new CreateInstancePerClass();

            discovery.Methods
                .OrderBy((Func<MethodInfo, string>)(x => throw new Exception("OrderBy lambda expression threw!")));

            discovery.Parameters
                .Add<BuggyParameterSource>();

            var bus = new Bus(listener);
            new Runner(GetType().Assembly, bus).Run(candidateTypes, discovery, execution);

            //NOTE: Since the ordering of cases is deliberately failing, and since member order via reflection
            //      is undefined, we explicitly sort the listener Entries here to avoid making a brittle assertion.

            var strings = listener.Entries.OrderBy(x => x).ToArray();
            strings.ShouldBe(
                Self + "+BuggyParameterGenerationTestClass.ParameterizedA failed: Exception thrown while attempting to yield input parameters for method: ParameterizedA",
                Self + "+BuggyParameterGenerationTestClass.ParameterizedA failed: OrderBy lambda expression threw!",
                Self + "+BuggyParameterGenerationTestClass.ParameterizedB failed: Exception thrown while attempting to yield input parameters for method: ParameterizedB",
                Self + "+BuggyParameterGenerationTestClass.ParameterizedB failed: OrderBy lambda expression threw!",
                Self + "+PassFailTestClass.Fail failed: 'Fail' failed!",
                Self + "+PassFailTestClass.Fail failed: OrderBy lambda expression threw!",
                Self + "+PassFailTestClass.Pass failed: OrderBy lambda expression threw!",
                Self + "+PassFailTestClass.Pass passed",
                Self + "+PassTestClass.PassA failed: OrderBy lambda expression threw!",
                Self + "+PassTestClass.PassA passed",
                Self + "+PassTestClass.PassB failed: OrderBy lambda expression threw!",
                Self + "+PassTestClass.PassB passed",
                Self + "+SkipTestClass.SkipA failed: OrderBy lambda expression threw!",
                Self + "+SkipTestClass.SkipA skipped",
                Self + "+SkipTestClass.SkipB failed: OrderBy lambda expression threw!",
                Self + "+SkipTestClass.SkipB skipped");
        }

        class CreateInstancePerClass : Execution
        {
            public void Execute(TestClass testClass)
            {
                var instance = testClass.Construct();

                testClass.RunCases(@case =>
                {
                    if (@case.Method.Name.Contains("Skip"))
                        return;

                    @case.Execute(instance);
                });

                instance.Dispose();
            }
        }

        class SampleIrrelevantClass
        {
            public void PassA() { }
            public void PassB() { }
        }

        class PassTestClass
        {
            public void PassA() { }
            public void PassB() { }
        }

        class PassFailTestClass
        {
            public void Pass() { }
            public void Fail() { throw new FailureException(); }
        }

        class SkipTestClass
        {
            public void SkipA() { throw new ShouldBeUnreachableException(); }
            public void SkipB() { throw new ShouldBeUnreachableException(); }
        }

        class BuggyParameterGenerationTestClass
        {
            public void ParameterizedA(int i) { }
            public void ParameterizedB(int i) { }
        }

        class BuggyParameterSource : ParameterSource
        {
            public IEnumerable<object[]> GetParameters(MethodBase method)
            {
                if (method.GetParameters().Length == 0)
                    yield break;

                throw new Exception("Exception thrown while attempting to yield input parameters for method: " + method.Name);
            }
        }

        class MultiConstructorWithDataTestClass
        {
            [ParameterizedCaseTests.Input(1, 1)]
            [ParameterizedCaseTests.Input(2, 1)]
            [ParameterizedCaseTests.Input(3, 1)]
            public MultiConstructorWithDataTestClass(int a, int b)
            {
            }

            public void ZeroArg() { }

            [ParameterizedCaseTests.Input("abc")]
            [ParameterizedCaseTests.Input("def")]
            public void StringArg(string c) { }
        }
    }
}