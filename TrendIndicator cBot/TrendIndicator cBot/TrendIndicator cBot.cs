using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using cAlgo.API;
using cAlgo.API.Extensions;
using cAlgo.API.Extensions.Enums;
using cAlgo.API.Extensions.Models;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class TrendIndicatorCBot : CBotBase
    {
        [Parameter("Trend indicator period", Group = "TrendIndicator", DefaultValue = 24, MinValue = 1, Step = 1)]
        public int TrendIndicatorPeriod { get; set; }

        [Parameter("Atr period", Group = "ATR", DefaultValue = 14, MinValue = 1, Step = 1)]
        public int AtrPeriod { get; set; }

        [Parameter("TP factor", Group = "Money", DefaultValue = 5, MinValue = 1)]
        public double TpFactor { get; set; }

        [Parameter("Std dev Factor", Group = "STDDEV", DefaultValue = 2, MinValue = 0)]
        public double StdDevFactor { get; set; }

        [Parameter("Std dev SL Factor", Group = "STDDEV", DefaultValue = 0.5, MinValue = 0)]
        public double StdDevSlFactor { get; set; }

        [Parameter("Atr multiplier", Group = "Money", DefaultValue = 8, MinValue = 0.001)]
        public double AtrMultiplier { get; set; }

        [Parameter("Risk", Group = "Money", DefaultValue = 1)]
        public double Risk { get; set; }

        [Parameter("Min Risk", Group = "Risk MM", DefaultValue = 0.1)]
        public double MinRisk { get; set; }

        [Parameter("Max Risk", Group = "Risk MM", DefaultValue = 1)]
        public double MaxRisk { get; set; }

        [Parameter("Risk factor", Group = "Risk MM", DefaultValue = 2)]
        public double RiskFactor { get; set; }

        [Parameter("Balance Avg N", Group = "Risk MM", DefaultValue = 10)]
        public int BalanceAvgN { get; set; }

        [Parameter("Balance Check N", Group = "Risk MM", DefaultValue = 5)]
        public int BalanceCheckN { get; set; }

        [Parameter("StdDev Risk Threshold", Group = "Money", DefaultValue = 0.002)]
        public double StdDevRiskThreshold { get; set; }

        [Parameter("Check Ema StdDev", Group = "Money", DefaultValue = false)]
        public bool CheckEmaStdDev { get; set; }

        private IndicatorDataSeries _ema;
        private IndicatorDataSeries _emaD;
        private StandardDeviation _stdDev;
        private AverageTrueRange _atr;
        private TrendIndicator _trendIndicator;

        private bool _isRising;

        private readonly List<double> _balance = new List<double>();

        protected override void OnStart()
        {
            _ema = Indicators.ExponentialMovingAverage(Bars.ClosePrices, TrendIndicatorPeriod * 8).Result;
            _emaD = Indicators.ExponentialMovingAverage(MarketData.GetBars(TimeFrame.Daily).ClosePrices, TrendIndicatorPeriod * 4).Result;
            _stdDev = Indicators.StandardDeviation(Bars.ClosePrices, TrendIndicatorPeriod * 8, MovingAverageType.Exponential);
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
            _trendIndicator = Indicators.GetIndicator<TrendIndicator>(TrendIndicatorPeriod, Bars.ClosePrices);

            Positions.Closed += PositionsOnClosed;

            var balances = History.Where(x => x.EntryTime < Time).OrderBy(x => x.EntryTime).Select(x => x.Balance).ToList();
            if (balances.Any())
            {
                Print(string.Join(",", balances));
            }
            _balance.AddRange(balances);
        }

        private void PositionsOnClosed(PositionClosedEventArgs positionClosedEventArgs)
        {
            _balance.Add(Account.Balance);
        }

        protected override void OnBar()
        {
//            var pos = Positions.FirstOrDefault();
//            if (pos != null)
//            {
//                if (pos.NetProfit > this.Account.Equity * Risk / 100 * TpFactor * 0.5)
//                {
//                    pos.ModifyStopLossPrice(pos.EntryPrice);
//                }
//            }
            OnTrade(Risk, TpFactor);
        }

        public TrendIndicatorCBot() : base("TrendIndicatorCBot")
        {
        }

//        protected override void OnStop()
//        {
//            var longPositions = Positions.FindAll(_label, SymbolName, TradeType.Buy);
//            var shortPositions = Positions.FindAll(_label, SymbolName, TradeType.Sell);
//            foreach (var position in longPositions)
//            {
//                position.Close();
//            }
//
//            foreach (var position in shortPositions)
//            {
//                position.Close();
//            }
//        }

        protected override TradeType? GetSignal()
        {
            if (!_trendIndicator.Result.IsRising())
            {
                _isRising = false;
                return null;
            }

            if (_stdDev.Result.LastValue > StdDevRiskThreshold)
            {
                _isRising = true;
                return null;
            }

            if (_isRising)
                return null;

            var buy = _ema.IsRising();
            var sell = _ema.IsFalling();

            var sig = (buy ? 1 : 0) + (sell ? -1 : 0);

            return GetTradeType(sig);
        }

        protected override double GetStopLoss()
        {
            var atrVal = _atr.Result.Last(0);
            var atrSl = Symbol.ToPips(atrVal) * AtrMultiplier;
            return atrSl;
        }

        private double GetAvgBalance()
        {
            if (_balance.Count < BalanceAvgN)
                return -1;
            return _balance.Skip(_balance.Count - BalanceAvgN).Average();
        }

        private double GetOverAvgBalance()
        {
            var avg = GetAvgBalance();
            if (avg < 0)
                return -1;
            return _balance.Skip(_balance.Count - BalanceCheckN).Count(x => x >= avg);
        }

        protected override TradeType Filter(TradeType tradeType, ref double stopLossInPips, ref double risk, ref double tpFactor)
        {
            if (CheckEmaStdDev)
            {
                if (Bars.LastBar.Close < _ema.LastValue - _stdDev.Result.LastValue * StdDevFactor || Bars.LastBar.Close > _ema.LastValue + _stdDev.Result.LastValue * StdDevFactor)
                {
                    tradeType = tradeType == TradeType.Buy ? TradeType.Sell : TradeType.Buy;
                    stopLossInPips *= StdDevSlFactor;
                }
            }

            var over = GetOverAvgBalance();
            if (over >= 0)
            {
                var m = Math.Max(MinRisk, Math.Min(RiskFactor * over / BalanceCheckN, MaxRisk));
                Print(m);
                risk *= m;
            }

            return tradeType;
        }

        #region Fit

        protected override double GetFitness(GetFitnessArgs args)
        {
            if (args.History.Count < 10)
                return -100;
            var balance0 = args.History[0].Balance;
            var balanceData = args.History.Select(x => x.Balance / balance0 - 1).ToList();
            var ls = GetLeastSquaresRegression(balanceData);
            var balanceX2X = balanceData.Count * 2 * ls.Slope + ls.Intercept;
            double dev = 0;
            for (int i = 0; i < balanceData.Count; i++)
            {
                var y = i * ls.Slope + ls.Intercept;
                var b = balanceData[i];
                dev += (y - b) * (y - b);
            }

            dev /= balanceData.Count;
            dev = Math.Sqrt(dev);
            if (Math.Abs(dev) < 1E-09)
                dev = 1;
            dev = 1 / dev;
            dev = dev < 1E-07 ? 1 : dev;
            return balanceX2X * dev * Math.Log(balanceData.Count);
        }

        private static LeastSquares GetLeastSquaresRegression(IList<double> data)
        {
            var xValues = new List<int>();
            var yValues = new List<double>();

            for (var x = 0; x < data.Count; x++)
            {
                xValues.Add(x);
                yValues.Add(data[x]);
            }

            var xSquared = xValues.Select(x => Math.Pow(x, 2)).ToList();
            var xyProducts = xValues.Zip(yValues, (x, y) => x * y).ToList();

            double xSum = xValues.Sum();
            var ySum = yValues.Sum();

            var xSqauredSum = xSquared.Sum();

            var xyProductsSum = xyProducts.Sum();

            var n = xValues.Count;

            var slope = (n * xyProductsSum - xSum * ySum) / (n * xSqauredSum - Math.Pow(xSum, 2));
            var intercept = (ySum - slope * xSum) / n;

            return new LeastSquares 
            {
                Slope = slope,
                Intercept = intercept
            };
        }

        #endregion
    }
}
