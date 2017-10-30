﻿using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.ApplicationInsights.Extensibility;

using System.Linq;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Channel;

namespace Microsoft.ApplicationInsights.Metrics.Extensibility
{
    /// <summary />
    [TestClass]
    public class AggregationPeriodSummaryTests
    {
        /// <summary />
        [TestMethod]
        public void Ctor()
        {
            {
                var period = new AggregationPeriodSummary(null, null);
                Assert.IsNotNull(period);
            }
        }

        /// <summary />
        [TestMethod]
        public void PersistentAggregates()
        {
            {
                MetricAggregate[] p = new MetricAggregate[0];
                var period = new AggregationPeriodSummary(p, null);

                Assert.IsNull(period.NonpersistentAggregates);

                Assert.IsNotNull(period.PersistentAggregates);
                Assert.AreSame(p, period.PersistentAggregates);
                Assert.AreEqual(0, period.PersistentAggregates.Count);
            }
            {
                MetricAggregate[] p = new MetricAggregate[] { new MetricAggregate("mid1", "KindA"),
                                                              new MetricAggregate("mid2", "KindB"),
                                                              new MetricAggregate("mid3", "KindC") };
                var period = new AggregationPeriodSummary(p, null);

                Assert.IsNull(period.NonpersistentAggregates);

                Assert.IsNotNull(period.PersistentAggregates);
                Assert.AreSame(p, period.PersistentAggregates);
                Assert.AreEqual(3, period.PersistentAggregates.Count);

                Assert.AreEqual("mid1", period.PersistentAggregates[0].MetricId);
                Assert.AreEqual("KindA", period.PersistentAggregates[0].AggregationKindMoniker);

                Assert.AreEqual("mid2", period.PersistentAggregates[1].MetricId);
                Assert.AreEqual("KindB", period.PersistentAggregates[1].AggregationKindMoniker);

                Assert.AreEqual("mid3", period.PersistentAggregates[2].MetricId);
                Assert.AreEqual("KindC", period.PersistentAggregates[2].AggregationKindMoniker);
            }
            {
                MetricAggregate[] np = new MetricAggregate[] { new MetricAggregate("mid1", "KindA"),
                                                              new MetricAggregate("mid2", "KindB"),
                                                              new MetricAggregate("mid3", "KindC") };
                var period = new AggregationPeriodSummary(null, np);

                Assert.IsNull(period.PersistentAggregates);

                Assert.IsNotNull(period.NonpersistentAggregates);
                Assert.AreSame(np, period.NonpersistentAggregates);
                Assert.AreEqual(3, period.NonpersistentAggregates.Count);

                Assert.AreEqual("mid1", period.NonpersistentAggregates[0].MetricId);
                Assert.AreEqual("KindA", period.NonpersistentAggregates[0].AggregationKindMoniker);

                Assert.AreEqual("mid2", period.NonpersistentAggregates[1].MetricId);
                Assert.AreEqual("KindB", period.NonpersistentAggregates[1].AggregationKindMoniker);

                Assert.AreEqual("mid3", period.NonpersistentAggregates[2].MetricId);
                Assert.AreEqual("KindC", period.NonpersistentAggregates[2].AggregationKindMoniker);
            }

        }
    }
}
