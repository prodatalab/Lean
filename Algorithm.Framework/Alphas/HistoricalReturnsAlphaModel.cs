﻿using System.Collections.Generic;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Securities;

namespace QuantConnect.Algorithm.Framework.Alphas
{
    /// <summary>
    /// Alpha model that uses historical returns to create insights
    /// </summary>
    public class HistoricalReturnsAlphaModel : IAlphaModel, INamedModel
    {
        private readonly int _lookback;
        private readonly Resolution _resolution;
        private readonly TimeSpan _predictionInterval;
        private readonly Dictionary<Symbol, SymbolData> _symbolDataBySymbol;

        /// <summary>
        /// Defines a name for a framework model
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HistoricalReturnsAlphaModel"/> class
        /// </summary>
        /// <param name="lookback">Historical return lookback period</param>
        /// <param name="resolution">The resolution of historical data</param>
        public HistoricalReturnsAlphaModel(
            int lookback = 1,
            Resolution resolution = Resolution.Daily
            )
        {
            _lookback = lookback;
            _resolution = resolution;
            _predictionInterval = _resolution.ToTimeSpan().Multiply(_lookback);
            _symbolDataBySymbol = new Dictionary<Symbol, SymbolData>();
            Name = $"{nameof(HistoricalReturnsAlphaModel)}({lookback},{resolution})";
        }

        /// <summary>
        /// Updates this alpha model with the latest data from the algorithm.
        /// This is called each time the algorithm receives data for subscribed securities
        /// </summary>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="data">The new data available</param>
        /// <returns>The new insights generated</returns>
        public IEnumerable<Insight> Update(QCAlgorithmFramework algorithm, Slice data)
        {
            var insights = new List<Insight>();
            foreach (var symbolData in _symbolDataBySymbol.Values)
            {
                if (symbolData.CanEmit())
                {
                    var direction = InsightDirection.Flat;
                    var magnitude = (double)symbolData.ROC.Current.Value;
                    if (magnitude > 0) direction = InsightDirection.Up;
                    if (magnitude < 0) direction = InsightDirection.Down;
                    insights.Add(Insight.Price(symbolData.Security.Symbol, _predictionInterval, direction, magnitude, null));
                }
            }
            return insights;
        }

        /// <summary>
        /// Event fired each time the we add/remove securities from the data feed
        /// </summary>
        /// <param name="algorithm">The algorithm instance that experienced the change in securities</param>
        /// <param name="changes">The security additions and removals from the algorithm</param>
        public void OnSecuritiesChanged(QCAlgorithmFramework algorithm, SecurityChanges changes)
        {
            // clean up data for removed securities
            foreach (var removed in changes.RemovedSecurities)
            {
                SymbolData data;
                if (_symbolDataBySymbol.TryGetValue(removed.Symbol, out data))
                {
                    _symbolDataBySymbol.Remove(removed.Symbol);
                    algorithm.SubscriptionManager.RemoveConsolidator(removed.Symbol, data.Consolidator);
                }
            }

            // initialize data for added securities
            foreach (var added in changes.AddedSecurities)
            {
                if (!_symbolDataBySymbol.ContainsKey(added.Symbol))
                {
                    var symbolData = new SymbolData(algorithm, added, _lookback, _resolution);
                    _symbolDataBySymbol.Add(added.Symbol, symbolData);
                    symbolData.WarmUpIndicators(algorithm, added, _lookback, _resolution);
                }
            }
        }

        /// <summary>
        /// Contains data specific to a symbol required by this model
        /// </summary>
        private class SymbolData
        {
            public Security Security;
            public IDataConsolidator Consolidator;
            public RateOfChange ROC;
            public long previous = 0;

            public SymbolData(QCAlgorithmFramework algorithm, Security security, int lookback, Resolution resolution)
            {
                Security = security;
                Consolidator = algorithm.ResolveConsolidator(security.Symbol, resolution);
                algorithm.SubscriptionManager.AddConsolidator(security.Symbol, Consolidator);
                ROC = new RateOfChange(security.Symbol, lookback);
                algorithm.RegisterIndicator(security.Symbol, ROC, Consolidator);
            }

            public void WarmUpIndicators(QCAlgorithmFramework algorithm, Security security, int lookback, Resolution resolution)
            {
                var history = algorithm.History(security.Symbol, lookback, resolution);
                foreach (var bar in history)
                {
                    ROC.Update(bar.EndTime, bar.Close);
                }
            }

            public bool CanEmit()
            {
                if (previous == ROC.Samples) return false;
                previous = ROC.Samples;
                return ROC.IsReady;
            }
        }
    }
}