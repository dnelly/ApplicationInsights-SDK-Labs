﻿using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Metrics.Extensibility;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Metrics.TestUtil
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public static class Util

    {
        public const string AggregationIntervalMonikerPropertyKey = "_MS.AggregationIntervalMs";
        public const double MaxAllowedPrecisionError = 0.00001;

        //public const bool WaitForDefaultAggregationCycleCompletion = true;
        public const bool WaitForDefaultAggregationCycleCompletion = false;

        public static void AssertAreEqual<T>(T[] array1, T[] array2)
        {
            if (array1 == array2)
            {
                return;
            }

            Assert.IsNotNull(array1);
            Assert.IsNotNull(array2);

            Assert.AreEqual(array1.Length, array1.Length);

            for(int i = 0; i < array1.Length; i++)
            {
                Assert.AreEqual(array1[i], array2[i], message: $" at index {i}");
            }
        }

        public static bool AreEqual<T>(T[] array1, T[] array2)
        {
            if (array1 == array2)
            {
                return true;
            }

            if (array1 == null)
            {
                return false;
            }

            if (array2 == null)
            {
                return false;
            }

            if (array1.Length != array1.Length)
            {
                return false;
            }

            for (int i = 0; i < array1.Length; i++)
            {
                if (array1[i] == null && array2[i] == null)
                {
                    continue;
                }

                if (array1 == null)
                {
                    return false;
                }

                if (array2 == null)
                {
                    return false;
                }

                if (! array1[i].Equals(array2[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static TelemetryConfiguration CreateAITelemetryConfig()
        {
            IList<ITelemetry> telemetrySentToChannel;
            return CreateAITelemetryConfig(out telemetrySentToChannel);
        }

        public static TelemetryConfiguration CreateAITelemetryConfig(out IList<ITelemetry> telemetrySentToChannel)
        {
            StubTelemetryChannel channel = new StubTelemetryChannel();
            string iKey = Guid.NewGuid().ToString("D");
            TelemetryConfiguration telemetryConfig = new TelemetryConfiguration(iKey, channel);

            var channelBuilder = new TelemetryProcessorChainBuilder(telemetryConfig);
            channelBuilder.Build();

            foreach (ITelemetryProcessor initializer in telemetryConfig.TelemetryInitializers)
            {
                ITelemetryModule m = initializer as ITelemetryModule;
                if (m != null)
                {
                    m.Initialize(telemetryConfig);
                }
            }

            foreach (ITelemetryProcessor processor in telemetryConfig.TelemetryProcessors)
            {
                ITelemetryModule m = processor as ITelemetryModule;
                if (m != null)
                {
                    m.Initialize(telemetryConfig);
                }
            }

            telemetrySentToChannel = channel.TelemetryItems;
            return telemetryConfig;
        }

        public static void ValidateNumericAggregateValues(ITelemetry aggregate, string name, int count, double sum, double max, double min, double stdDev, DateTimeOffset timestamp, string periodMs)
        {
            ValidateNumericAggregateValues(aggregate, name, count, sum, max, min, stdDev);

            var metricAggregate = (MetricTelemetry) aggregate;
            Assert.AreEqual(timestamp, metricAggregate.Timestamp, "metricAggregate.Timestamp mismatch");
            Assert.AreEqual(periodMs, metricAggregate?.Properties?[Util.AggregationIntervalMonikerPropertyKey], "metricAggregate.Properties[AggregationIntervalMonikerPropertyKey] mismatch");
        }

        public static void ValidateNumericAggregateValues(ITelemetry aggregate, string name, int count, double sum, double max, double min, double stdDev)
        {
            Assert.IsNotNull(aggregate);

            MetricTelemetry metricAggregate = aggregate as MetricTelemetry;

            Assert.IsNotNull(metricAggregate);

            Assert.AreEqual(name, metricAggregate.Name, "metricAggregate.Name mismatch");
            Assert.AreEqual(count, metricAggregate.Count, "metricAggregate.Count mismatch");
            Assert.AreEqual(sum, metricAggregate.Sum, Util.MaxAllowedPrecisionError, "metricAggregate.Sum mismatch");
            Assert.AreEqual(max, metricAggregate.Max.Value, Util.MaxAllowedPrecisionError, "metricAggregate.Max mismatch");
            Assert.AreEqual(min, metricAggregate.Min.Value, Util.MaxAllowedPrecisionError, "metricAggregate.Min mismatch");

            // For very large numbers we perform an approx comparison.
            if (Math.Abs(stdDev) > Int64.MaxValue)
            {
                double expectedStdDevScale = Math.Floor(Math.Log10(Math.Abs(stdDev)));
                double actualStdDevScale = Math.Floor(Math.Log10(Math.Abs(metricAggregate.StandardDeviation.Value)));
                Assert.AreEqual(expectedStdDevScale, actualStdDevScale, "metricAggregate.StandardDeviation (exponent) mismatch");
                Assert.AreEqual(
                            stdDev / Math.Pow(10, expectedStdDevScale),
                            metricAggregate.StandardDeviation.Value / Math.Pow(10, actualStdDevScale),
                            Util.MaxAllowedPrecisionError,
                            "metricAggregate.StandardDeviation (significant part) mismatch");
            }
            else
            {
                Assert.AreEqual(stdDev, metricAggregate.StandardDeviation.Value, Util.MaxAllowedPrecisionError, "metricAggregate.StandardDeviation mismatch");
            }
        }

        public static void ValidateNumericAggregateValues(MetricAggregate aggregate, string name, int count, double sum, double max, double min, double stdDev, DateTimeOffset timestamp, long periodMs)
        {
            ValidateNumericAggregateValues(aggregate, name, count, sum, max, min, stdDev);

            Assert.AreEqual(timestamp, aggregate.AggregationPeriodStart, "metricAggregate.Timestamp mismatch");
            Assert.AreEqual(periodMs, (long) aggregate.AggregationPeriodDuration.TotalMilliseconds, "metricAggregate.Properties[AggregationIntervalMonikerPropertyKey] mismatch");
        }

        public static void ValidateNumericAggregateValues(MetricAggregate aggregate, string name, int count, double sum, double max, double min, double stdDev)
        {
            Assert.IsNotNull(aggregate);

            Assert.AreEqual("Microsoft.ApplicationInsights.SimpleMeasurement", aggregate.AggregationKindMoniker);

            Assert.AreEqual(name, aggregate.MetricId, "aggregate.Name mismatch");
            Assert.AreEqual(count, aggregate.AggregateData["Count"], "aggregate.Count mismatch");
            Assert.AreEqual(sum, (double) aggregate.AggregateData["Sum"], Util.MaxAllowedPrecisionError, "aggregate.Sum mismatch");
            Assert.AreEqual(max, (double) aggregate.AggregateData["Max"], Util.MaxAllowedPrecisionError, "aggregate.Max mismatch");
            Assert.AreEqual(min, (double) aggregate.AggregateData["Min"], Util.MaxAllowedPrecisionError, "aggregate.Min mismatch");

            // For very large numbers we perform an approx comparison.
            if (Math.Abs(stdDev) > Int64.MaxValue)
            {
                double expectedStdDevScale = Math.Floor(Math.Log10(Math.Abs(stdDev)));
                double actualStdDevScale = Math.Floor(Math.Log10(Math.Abs((double) aggregate.AggregateData["StdDev"])));
                Assert.AreEqual(expectedStdDevScale, actualStdDevScale, "aggregate.StandardDeviation (exponent) mismatch");
                Assert.AreEqual(
                            stdDev / Math.Pow(10, expectedStdDevScale),
                            ((double) aggregate.AggregateData["StdDev"]) / Math.Pow(10, actualStdDevScale),
                            Util.MaxAllowedPrecisionError,
                            "aggregate.StandardDeviation (significant part) mismatch");
            }
            else
            {
                Assert.AreEqual(stdDev, (double) aggregate.AggregateData["StdDev"], Util.MaxAllowedPrecisionError, "aggregate.StandardDeviation mismatch");
            }
        }

        /// <summary />
        /// <param name="versionMoniker"></param>
        public static void ValidateSdkVersionString(string versionMoniker)
        {
            Assert.IsNotNull(versionMoniker);

            // Expected result example: // "msdk-0.1.0-371:2.3.0-41907"

            const string expectedPrefix = "msdk-0.1.0-";
            const string expectedPostfix = ":2.3.0-41907";

            Assert.IsTrue(versionMoniker.StartsWith(expectedPrefix));
            Assert.IsTrue(versionMoniker.EndsWith(expectedPostfix));

            string metricsSdkRevisionStr = versionMoniker.Substring(expectedPrefix.Length);
            metricsSdkRevisionStr = metricsSdkRevisionStr.Substring(0, metricsSdkRevisionStr.Length - expectedPostfix.Length);

            Assert.IsNotNull(metricsSdkRevisionStr);
            Assert.IsTrue(metricsSdkRevisionStr.Length > 0);

            int metricsSdkRevision;
            Assert.IsTrue(Int32.TryParse(metricsSdkRevisionStr, out metricsSdkRevision));
            Assert.IsTrue(metricsSdkRevision > 0);
        }

        /// <summary>
        /// The MetricManager contains an instance of DefaultAggregationPeriodCycle which encapsulates a managed thread.
        /// That tread sleeps for most of the time, and once per minute it wakes up to cycle aggregators and send aggregates.
        /// Accordingly, it can only stop itself once per minute by checking a flag.
        /// In tests we do not really need to wait for that thread to exit, but then we will get a message like:
        /// ----
        /// System.AppDomainUnloadedException:
        ///  Attempted to access an unloaded AppDomain. This can happen if the test(s) started a thread but did not stop it.
        ///  Make sure that all the threads started by the test(s) are stopped before completion.
        /// ----
        /// However, if we wait for the completion, than each test involving the MetricManager will take a minute.
        /// So we will call this method at the end of each test involving the MetricManager and then we will use a flag to switch
        /// between waiting and not waiting. This will let us run test quickly most of the time, but we can switch the flag to get
        /// a clean test run.
        /// </summary>
        /// <param name="metricManagers"></param>
        public static void CompleteDefaultAggregationCycle(params MetricManager[] metricManagers)
        {
            if (metricManagers == null)
            {
                return;
            }

            List<Task> completionTasks = new List<Task>();
            foreach (MetricManager manager in metricManagers)
            {
                if (manager != null)
                {
                    Task cycleCompletionTask = manager.StopDefaultAggregationCycleAsync();
                    completionTasks.Add(cycleCompletionTask);
                }
            }
            

            if (WaitForDefaultAggregationCycleCompletion)
            {
#pragma warning disable CS0162 // Unreachable code detected
                Task.WhenAll(completionTasks).GetAwaiter().GetResult();
#pragma warning restore CS0162 // Unreachable code detected
            }
        }
    }
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
