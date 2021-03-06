﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Microsoft.ApplicationInsights.Metrics;
using Microsoft.ApplicationInsights.Metrics.ConcurrentDatastructures;

namespace Microsoft.ApplicationInsights
{
    /// <summary>
    /// Represents a zero- or multi-dimensional metric.<br />
    /// Contains convenience methods to track, aggregate and send values.<br />
    /// A <c>Metric</c> instance groups one or more <c>MetricSeries</c> that actually track and aggregate values along with
    /// naming and configuration attributes that identify the metric and define how it will be aggregated. 
    /// </summary>
    public sealed class Metric
    {
#pragma warning disable SA1401 // Field must be private
        internal readonly MetricConfiguration _configuration;
#pragma warning restore SA1401 // Field must be private

        private const string NullMetricObjectId = "null";

        private readonly MetricSeries _zeroDimSeries;
        private readonly IReadOnlyList<KeyValuePair<string[], MetricSeries>> _zeroDimSeriesList;

        private readonly MultidimensionalCube2<MetricSeries> _metricSeries;

        private readonly MetricManager _metricManager;

        internal Metric(MetricManager metricManager, MetricIdentifier metricIdentifier, MetricConfiguration configuration)
        {
            Util.ValidateNotNull(metricManager, nameof(metricManager));
            Util.ValidateNotNull(metricIdentifier, nameof(metricIdentifier));
            EnsureConfigurationValid(metricIdentifier.DimensionsCount > 0, configuration);

            _metricManager = metricManager;
            Identifier = metricIdentifier;
            _configuration = configuration;

            _zeroDimSeries = _metricManager.CreateNewSeries(
                                                        new MetricIdentifier(Identifier.MetricNamespace, Identifier.MetricId),
                                                        dimensionNamesAndValues: null,
                                                        config: _configuration.SeriesConfig);

            if (metricIdentifier.DimensionsCount == 0)
            {
                _metricSeries = null;
                _zeroDimSeriesList = new KeyValuePair<string[], MetricSeries>[1] { new KeyValuePair<string[], MetricSeries>(new string[0], _zeroDimSeries) };
            }
            else
            {
                int[] dimensionValuesCountLimits = new int[metricIdentifier.DimensionsCount];
                for (int d = 0; d < metricIdentifier.DimensionsCount; d++)
                {
                    dimensionValuesCountLimits[d] = configuration.ValuesPerDimensionLimit;
                }

                _metricSeries = new MultidimensionalCube2<MetricSeries>(
                            totalPointsCountLimit: configuration.SeriesCountLimit - 1,
                            pointsFactory: CreateNewMetricSeries,
                            dimensionValuesCountLimits: dimensionValuesCountLimits);

                _zeroDimSeriesList = null;
            }
        }

        /// <summary>
        /// The identifier of a metric groups together the MetricNamespace, the MetricId, and the dimensions of the metric, if any.
        /// </summary>
        public MetricIdentifier Identifier { get; }

        /// <summary>
        /// The current number of metric series contained in this metric. 
        /// Each metric contains a special zero-dimension series, plus one series per unique dimension-values combination.
        /// </summary>
        public int SeriesCount { get { return 1 + (_metricSeries?.TotalPointsCount ?? 0); } }

        /// <summary>
        /// Gets the values known for dimension identified by the specified 1-based dimension index.
        /// </summary>
        /// <param name="dimensionNumber">1-based dimension number. Currently it can be <c>1</c> or <c>2</c>.</param>
        /// <returns>The values known for the specified dimension.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2233", Justification = "dimensionNumber is validated.")]
        public IReadOnlyCollection<string> GetDimensionValues(int dimensionNumber)
        {
            Identifier.ValidateDimensionNumberForGetter(dimensionNumber);

            int dimensionIndex = dimensionNumber - 1;
            return _metricSeries.GetDimensionValues(dimensionIndex);
        }
        
        /// <summary>
        /// Gets all metric series contained in this metric.
        /// Each metric contains a special zero-dimension series, plus one series per unique dimension-values combination.
        /// </summary>
        /// <returns></returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
                                                "Microsoft.Design",
                                                "CA1024: Use properties where appropriate",
                                                Justification = "Completes with non-trivial effort. Method is approproiate.")]
        public IReadOnlyList<KeyValuePair<string[], MetricSeries>> GetAllSeries()
        {
            if (Identifier.DimensionsCount == 0)
            {
                return _zeroDimSeriesList;
            }

            var series = new List<KeyValuePair<string[], MetricSeries>>(SeriesCount);
            series.Add(new KeyValuePair<string[], MetricSeries>(new string[0], _zeroDimSeries));
            _metricSeries.GetAllPoints(series);
            return series;
        }

        /// <summary>
        /// Gets a <c>MetricSeries</c> associated with this metric.<br />
        /// This overload gets the zero-dimensional <c>MetricSeries</c> associated with this metric.
        /// Every metric, regardless of its dimensionality, has such a zero-dimensional <c>MetricSeries</c>.
        /// </summary>
        /// <param name="series">Will be set to the zero-dimensional <c>MetricSeries</c> associated with this metric</param>
        /// <returns><c>True</c>.</returns>
        public bool TryGetDataSeries(out MetricSeries series)
        {
            series = _zeroDimSeries;
            return true;
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// This overload may only be used with 1-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(out MetricSeries series, string dimension1Value)
        {
            return TryGetDataSeries(out series, true, dimension1Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload may only be used with 2-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(out MetricSeries series, string dimension1Value, string dimension2Value)
        {
            return TryGetDataSeries(out series, true, dimension1Value, dimension2Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload may only be used with 3-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(out MetricSeries series, string dimension1Value, string dimension2Value, string dimension3Value)
        {
            return TryGetDataSeries(out series, true, dimension1Value, dimension2Value, dimension3Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload may only be used with 4-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(out MetricSeries series, string dimension1Value, string dimension2Value, string dimension3Value, string dimension4Value)
        {
            return TryGetDataSeries(out series, true, dimension1Value, dimension2Value, dimension3Value, dimension4Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload may only be used with 5-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(
                                out MetricSeries series, 
                                string dimension1Value, 
                                string dimension2Value, 
                                string dimension3Value, 
                                string dimension4Value,
                                string dimension5Value)
        {
            return TryGetDataSeries(out series, true, dimension1Value, dimension2Value, dimension3Value, dimension4Value, dimension5Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload may only be used with 6-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(
                                out MetricSeries series,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value)
        {
            return TryGetDataSeries(out series, true, dimension1Value, dimension2Value, dimension3Value, dimension4Value, dimension5Value, dimension6Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload may only be used with 7-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(
                                out MetricSeries series,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value)
        {
            return TryGetDataSeries(
                            out series,
                            true,
                            dimension1Value, 
                            dimension2Value, 
                            dimension3Value, 
                            dimension4Value, 
                            dimension5Value, 
                            dimension6Value,
                            dimension7Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload may only be used with 8-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <param name="dimension8Value">The value of the 8th dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(
                                out MetricSeries series,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value,
                                string dimension8Value)
        {
            return TryGetDataSeries(
                            out series,
                            true,
                            dimension1Value,
                            dimension2Value,
                            dimension3Value,
                            dimension4Value,
                            dimension5Value,
                            dimension6Value,
                            dimension7Value,
                            dimension8Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload may only be used with 9-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <param name="dimension8Value">The value of the 8th dimension.</param>
        /// <param name="dimension9Value">The value of the 9th dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(
                                out MetricSeries series,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value,
                                string dimension8Value,
                                string dimension9Value)
        {
            return TryGetDataSeries(
                            out series,
                            true,
                            dimension1Value,
                            dimension2Value,
                            dimension3Value,
                            dimension4Value,
                            dimension5Value,
                            dimension6Value,
                            dimension7Value,
                            dimension8Value,
                            dimension9Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload may only be used with 10-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <param name="dimension8Value">The value of the 8th dimension.</param>
        /// <param name="dimension9Value">The value of the 9th dimension.</param>
        /// <param name="dimension10Value">The value of the 9th dimension.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension name could be retrieved (or created);
        /// <c>False</c> if the indicated series could not be retrieved or created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryGetDataSeries(
                                out MetricSeries series,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value,
                                string dimension8Value,
                                string dimension9Value,
                                string dimension10Value)
        {
            return TryGetDataSeries(
                            out series,
                            true,
                            dimension1Value,
                            dimension2Value,
                            dimension3Value,
                            dimension4Value,
                            dimension5Value,
                            dimension6Value,
                            dimension7Value,
                            dimension8Value,
                            dimension9Value,
                            dimension10Value);
        }

        /// <summary>
        /// Gets or creates the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// This overload used metrics of any valid dimensionality:
        /// The number of elements in the specified <c>dimensionValues</c> array must exactly match the dimensionality of this metric,
        /// and that array may not contain nulls. Specify a null-array for zero-dimensional metrics.
        /// </summary>
        /// <param name="series">If this method returns <c>True</c>: Will be set to the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// Otherwise: Will be set to <c>null</c>.</param>
        /// <param name="createIfNotExists">Whether to attempt creating a metric series for the specified dimension values if it does not exist.</param>
        /// <param name="dimensionValues">The values of the dimensions for the required metric series.</param>
        /// <returns><c>True</c> if the <c>MetricSeries</c> indicated by the specified dimension names could be retrieved or created;
        /// <c>False</c> if the indicated series could not be retrieved or created because <c>createIfNotExists</c> is <c>false</c>
        /// or because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>

        public bool TryGetDataSeries(out MetricSeries series, bool createIfNotExists, params string[] dimensionValues)
        {
            if (dimensionValues == null || dimensionValues.Length == 0)
            {
                series = _zeroDimSeries;
                return true;
            }

            if (Identifier.DimensionsCount != dimensionValues.Length)
            {
                throw new ArgumentException($"Attempted to get a metric series by specifying {dimensionValues.Length} dimension(s),"
                                          + $" but this metric has {Identifier.DimensionsCount} dimensions.");
            }

            for (int d = 0; d < dimensionValues.Length; d++)
            {
                Util.ValidateNotNullOrWhitespace(dimensionValues[d], $"{nameof(dimensionValues)}[{d}]");
            }

            MultidimensionalPointResult<MetricSeries> result = createIfNotExists
                                                                    ? _metricSeries.TryGetOrCreatePoint(dimensionValues)
                                                                    : _metricSeries.TryGetPoint(dimensionValues);

            if (result.IsSuccess)
            {
                series = result.Point;
                return true;
            }
            else
            {
                series = null;
                return false;
            }
        }

        /// <summary>
        /// Tracks the specified value.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.<br />
        /// This method uses the zero-dimensional <c>MetricSeries</c> associated with this metric.
        /// Use <c>TryTrackValue(..)</c> to track values into <c>MetricSeries</c> associated with specific dimension-values in multi-dimensional metrics.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        public void TrackValue(double metricValue)
        {
            _zeroDimSeries.TrackValue(metricValue);
        }

        /// <summary>
        /// Tracks the specified value.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.<br />
        /// This method uses the zero-dimensional <c>MetricSeries</c> associated with this metric.
        /// Use <c>TryTrackValue(..)</c> to track values into <c>MetricSeries</c> associated with specific dimension-values in multi-dimensional metrics.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        public void TrackValue(object metricValue)
        {
            _zeroDimSeries.TrackValue(metricValue);
        }


        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 1-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(double metricValue, string dimension1Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(out series, dimension1Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension value.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 1-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(object metricValue, string dimension1Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(out series, dimension1Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 2-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(double metricValue, string dimension1Value, string dimension2Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(out series, dimension1Value, dimension2Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 2-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(object metricValue, string dimension1Value, string dimension2Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(out series, dimension1Value, dimension2Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 3-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(double metricValue, string dimension1Value, string dimension2Value, string dimension3Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(out series, dimension1Value, dimension2Value, dimension3Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 3-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(object metricValue, string dimension1Value, string dimension2Value, string dimension3Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(out series, dimension1Value, dimension2Value, dimension3Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 4-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(double metricValue, string dimension1Value, string dimension2Value, string dimension3Value, string dimension4Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(out series, dimension1Value, dimension2Value, dimension3Value, dimension4Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 4-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(object metricValue, string dimension1Value, string dimension2Value, string dimension3Value, string dimension4Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(out series, dimension1Value, dimension2Value, dimension3Value, dimension4Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 5-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                double metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value, 
                                string dimension4Value, 
                                string dimension5Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(out series, dimension1Value, dimension2Value, dimension3Value, dimension4Value, dimension5Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 5-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                object metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(out series, dimension1Value, dimension2Value, dimension3Value, dimension4Value, dimension5Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 6-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                double metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(out series, dimension1Value, dimension2Value, dimension3Value, dimension4Value, dimension5Value, dimension6Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 6-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                object metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(out series, dimension1Value, dimension2Value, dimension3Value, dimension4Value, dimension5Value, dimension6Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 7-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                double metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(
                                        out series, 
                                        dimension1Value, 
                                        dimension2Value, 
                                        dimension3Value, 
                                        dimension4Value, 
                                        dimension5Value, 
                                        dimension6Value,
                                        dimension7Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 7-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                object metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(
                            out series,
                            dimension1Value,
                            dimension2Value,
                            dimension3Value,
                            dimension4Value,
                            dimension5Value,
                            dimension6Value,
                            dimension7Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 8-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <param name="dimension8Value">The value of the 8th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                double metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value,
                                string dimension8Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(
                                        out series,
                                        dimension1Value,
                                        dimension2Value,
                                        dimension3Value,
                                        dimension4Value,
                                        dimension5Value,
                                        dimension6Value,
                                        dimension7Value,
                                        dimension8Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 8-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <param name="dimension8Value">The value of the 8th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                object metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value,
                                string dimension8Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(
                            out series,
                            dimension1Value,
                            dimension2Value,
                            dimension3Value,
                            dimension4Value,
                            dimension5Value,
                            dimension6Value,
                            dimension7Value,
                            dimension8Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 9-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <param name="dimension8Value">The value of the 8th dimension.</param>
        /// <param name="dimension9Value">The value of the 9th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                double metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value,
                                string dimension8Value,
                                string dimension9Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(
                                        out series,
                                        dimension1Value,
                                        dimension2Value,
                                        dimension3Value,
                                        dimension4Value,
                                        dimension5Value,
                                        dimension6Value,
                                        dimension7Value,
                                        dimension8Value,
                                        dimension9Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 9-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <param name="dimension8Value">The value of the 8th dimension.</param>
        /// <param name="dimension9Value">The value of the 9th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                object metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value,
                                string dimension8Value,
                                string dimension9Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(
                            out series,
                            dimension1Value,
                            dimension2Value,
                            dimension3Value,
                            dimension4Value,
                            dimension5Value,
                            dimension6Value,
                            dimension7Value,
                            dimension8Value,
                            dimension9Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 10-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <param name="dimension8Value">The value of the 8th dimension.</param>
        /// <param name="dimension9Value">The value of the 9th dimension.</param>
        /// <param name="dimension10Value">The value of the 10th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                double metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value,
                                string dimension8Value,
                                string dimension9Value,
                                string dimension10Value)
        {
            MetricSeries series;
            bool canTrack = TryGetDataSeries(
                                        out series,
                                        dimension1Value,
                                        dimension2Value,
                                        dimension3Value,
                                        dimension4Value,
                                        dimension5Value,
                                        dimension6Value,
                                        dimension7Value,
                                        dimension8Value,
                                        dimension9Value,
                                        dimension10Value);
            if (canTrack)
            {
                series.TrackValue(metricValue);
            }

            return canTrack;
        }

        /// <summary>
        /// Tracks the specified value using the <c>MetricSeries</c> associated with the specified dimension values.<br />
        /// An aggregate representing tracked values will be automatically sent to the cloud ingestion endpoint at the end of each aggregation period.
        /// This overload may only be used with 10-dimensional metrics. Use other overloads to specify a matching number of dimension values for this metric.
        /// </summary>
        /// <param name="metricValue">The value to be aggregated.</param>
        /// <param name="dimension1Value">The value of the 1st dimension.</param>
        /// <param name="dimension2Value">The value of the 2nd dimension.</param>
        /// <param name="dimension3Value">The value of the 3rd dimension.</param>
        /// <param name="dimension4Value">The value of the 4th dimension.</param>
        /// <param name="dimension5Value">The value of the 5th dimension.</param>
        /// <param name="dimension6Value">The value of the 6th dimension.</param>
        /// <param name="dimension7Value">The value of the 7th dimension.</param>
        /// <param name="dimension8Value">The value of the 8th dimension.</param>
        /// <param name="dimension9Value">The value of the 9th dimension.</param>
        /// <param name="dimension10Value">The value of the 10th dimension.</param>
        /// <returns><c>True</c> if the specified value was added to the <c>MetricSeries</c> indicated by the specified dimension name;
        /// <c>False</c> if the indicated series could not be created because a dimension cap or a metric series cap was reached.</returns>
        /// <exception cref="ArgumentException">If the number of specified dimension names does not match the dimensionality of this <c>Metric</c>.</exception>
        public bool TryTrackValue(
                                object metricValue,
                                string dimension1Value,
                                string dimension2Value,
                                string dimension3Value,
                                string dimension4Value,
                                string dimension5Value,
                                string dimension6Value,
                                string dimension7Value,
                                string dimension8Value,
                                string dimension9Value,
                                string dimension10Value)
        {
            MetricSeries series;
            if (TryGetDataSeries(
                            out series,
                            dimension1Value,
                            dimension2Value,
                            dimension3Value,
                            dimension4Value,
                            dimension5Value,
                            dimension6Value,
                            dimension7Value,
                            dimension8Value,
                            dimension9Value,
                            dimension10Value))
            {
                series.TrackValue(metricValue);
                return true;
            }

            return false;
        }
       
        private static void EnsureConfigurationValid(
                                    bool isMultidimensional,
                                    MetricConfiguration configuration)
        {
            Util.ValidateNotNull(configuration, nameof(configuration));
            Util.ValidateNotNull(configuration.SeriesConfig, nameof(configuration.SeriesConfig));

            if (isMultidimensional && configuration.ValuesPerDimensionLimit < 1)
            {
                throw new ArgumentException("Multidimensional metrics must allow at least one dimension-value per dimesion"
                                         + $" (but {configuration.ValuesPerDimensionLimit} was specified"
                                         + $" in {nameof(configuration)}.{nameof(configuration.ValuesPerDimensionLimit)}).");
            }

            if (configuration.SeriesCountLimit < 1)
            {
                throw new ArgumentException("Metrics must allow at least one data series"
                                         + $" (but {configuration.SeriesCountLimit} was specified)"
                                         + $" in {nameof(configuration)}.{nameof(configuration.SeriesCountLimit)}).");
            }

            if (isMultidimensional && configuration.SeriesCountLimit < 2)
            {
                throw new ArgumentException("Multidimensional metrics must allow at least two data series:"
                                         + " 1 for the basic (zero-dimensional) series and 1 additional series"
                                         + $" (but {configuration.SeriesCountLimit} was specified)"
                                         + $" in {nameof(configuration)}.{nameof(configuration.SeriesCountLimit)}).");
            }
        }


        private MetricSeries CreateNewMetricSeries(string[] dimensionValues)
        {
            KeyValuePair<string, string>[] dimensionNamesAndValues = null;
            
            if (dimensionValues != null)
            {
                dimensionNamesAndValues = new KeyValuePair<string, string>[dimensionValues.Length];

                for (int d = 0; d < dimensionValues.Length; d++)
                {
                    string dimensionName = Identifier.GetDimensionName(d + 1);
                    string dimensionValue = dimensionValues[d];

                    if (dimensionValue == null)
                    {
                        throw new ArgumentNullException($"{nameof(dimensionValues)}[{d}]");
                    }

                    if (String.IsNullOrWhiteSpace(dimensionValue))
                    {
                        throw new ArgumentException($"The value for dimension number {d} is empty or white-space.");
                    }

                    dimensionNamesAndValues[d] = new KeyValuePair<string, string>(dimensionName, dimensionValue);
                }
            }

            MetricSeries series = _metricManager.CreateNewSeries(
                                                        Identifier,
                                                        dimensionNamesAndValues,
                                                        _configuration.SeriesConfig);
            return series;
        }
    }
}