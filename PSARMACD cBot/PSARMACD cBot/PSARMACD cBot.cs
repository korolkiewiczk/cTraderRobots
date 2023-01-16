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
    public class PSARMACDCBot : CBotBase
    {
        [Parameter("Max Check Back", Group = "Check", DefaultValue = 5)]
        public int MaxCheckBack { get; set; }

        [Parameter("Min pips", Group = "Check", DefaultValue = 15)]
        public int MinPips { get; set; }

        [Parameter("Max pips", Group = "Check", DefaultValue = 100)]
        public int MaxPips { get; set; }

        [Parameter("TP factor", Group = "Money", DefaultValue = 1, MinValue = 1)]
        public double TpFactor { get; set; }

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

        private IndicatorDataSeries _ema;
        private MacdHistogram _macd;
        private ParabolicSAR _psar;

        private readonly List<double> _balance = new List<double>();

        protected override void OnStart()
        {
            _ema = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 200).Result;
            _macd = Indicators.MacdHistogram(26, 12, 9);
            _psar = Indicators.ParabolicSAR(0.02, 0.2);

            Positions.Closed += PositionsOnClosed;
        }

        private void PositionsOnClosed(PositionClosedEventArgs positionClosedEventArgs)
        {
            _balance.Add(Account.Balance);
        }

        protected override void OnBar()
        {
            OnTrade(Risk, TpFactor);
        }

        protected override void OnStop()
        {
            var longPositions = Positions.FindAll(_label, SymbolName, TradeType.Buy);
            var shortPositions = Positions.FindAll(_label, SymbolName, TradeType.Sell);
            foreach (var position in longPositions)
            {
                position.Close();
            }

            foreach (var position in shortPositions)
            {
                position.Close();
            }
        }

        public PSARMACDCBot() : base("PSARMACD")
        {
        }

        protected override TradeType? GetSignal()
        {
            if (Symbol.ToPips(Math.Abs(Bars.ClosePrices.LastValue - _psar.Result.LastValue)) < MinPips)
            {
                return null;
            }

            var buy1 = Bars.HighPrices.LastValue >= _ema.LastValue;
            var buy2 = Check(x => _macd.Histogram.Last(x) > 0, MaxCheckBack);
            var buy3 = Check(x => Bars.ClosePrices.Last(x) > _psar.Result.Last(x), MaxCheckBack);
            var sell1 = Bars.LowPrices.LastValue <= _ema.LastValue;
            var sell2 = Check(x => _macd.Histogram.Last(x) < 0, MaxCheckBack);
            var sell3 = Check(x => Bars.ClosePrices.Last(x) < _psar.Result.Last(x), MaxCheckBack);

            var buy = buy1 && buy2 && buy3;
            var sell = sell1 && sell2 && sell3;

            var sig = (buy ? 1 : 0) + (sell ? -1 : 0);

            return GetTradeType(sig);
        }

        protected override double GetStopLoss()
        {
            return Symbol.ToPips(Math.Abs(Bars.ClosePrices.LastValue - _psar.Result.LastValue));
        }

        protected override TradeType Filter(TradeType tradeType, ref double stopLossInPips, ref double risk, ref double tpFactor)
        {
            if (Symbol.ToPips(Math.Abs(Bars.ClosePrices.LastValue - _psar.Result.LastValue)) > MaxPips)
            {
                tradeType = tradeType == TradeType.Buy ? TradeType.Sell : TradeType.Buy;
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

        private static bool Check(Func<int, bool> f, int c)
        {
            bool isAbove = false;
            bool isBelow = false;
            for (int i = 0; i < c; i++)
            {
                if (f(i))
                {
                    if (isBelow)
                    {
                        return false;
                    }
                    isAbove = true;
                }
                else
                {
                    if (!isAbove)
                    {
                        return false;
                    }
                    isBelow = true;
                }
            }

            return isAbove && isBelow;
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
