using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Extensions;
using cAlgo.API.Extensions.Enums;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class RsiStochasticDivergencecBot : CBotBase
    {
        [Parameter("Rsi period", Group = "Rsi", DefaultValue = 14, MinValue = 10, MaxValue = 20, Step = 1)]
        public int RsiPeriod { get; set; }

        [Parameter("Stochastic %K period", Group = "Stochastic", DefaultValue = 9, MinValue = 5, MaxValue = 20, Step = 1)]
        public int StochasticKPeriod { get; set; }

        [Parameter("Stochastic %K slowing", Group = "Stochastic", DefaultValue = 3, MinValue = 1, MaxValue = 8, Step = 1)]
        public int StochasticKSlowing { get; set; }

        [Parameter("Stochastic %D period", Group = "Stochastic", DefaultValue = 9, MinValue = 5, MaxValue = 20, Step = 1)]
        public int StochasticDPeriod { get; set; }

        [Parameter("Divergence period", Group = "Div", DefaultValue = 100, MinValue = 10, MaxValue = 300, Step = 5)]
        public int DivPeriod { get; set; }

        [Parameter("Divergence min distance", Group = "Div", DefaultValue = 10, MinValue = 1, MaxValue = 50, Step = 1)]
        public int DivMinDist { get; set; }

        [Parameter("Atr period", Group = "ATR", DefaultValue = 14, MinValue = 8, MaxValue = 20, Step = 1)]
        public int AtrPeriod { get; set; }

        [Parameter("TP factor", Group = "Money", DefaultValue = 2, MinValue = 1, MaxValue = 2.5)]
        public double TpFactor { get; set; }

        [Parameter("Atr multiplier", Group = "Money", DefaultValue = 2, MinValue = 1, MaxValue = 10)]
        public double AtrMultiplier { get; set; }

        [Parameter("Risk", Group = "Money", DefaultValue = 1)]
        public double Risk { get; set; }

        private IndicatorDataSeries _ema200;
        private StochasticOscillator _stochastic;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;

        protected override void OnStart()
        {
            _ema200 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 200).Result;
            _stochastic = Indicators.StochasticOscillator(StochasticKPeriod, StochasticKSlowing, StochasticDPeriod, MovingAverageType.Exponential);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
        }

        protected override void OnBar()
        {
            OnTrade(Risk, TpFactor);
        }

        protected override void OnStop()
        {
            // Put your deinitialization logic here
        }

        public RsiStochasticDivergencecBot() : base("RsiStochasticDivergencecBot")
        {
        }

        protected override TradeType? GetSignal()
        {
            var buy1 = _ema200.LastValue < Bars.LastBar.Close;
            var buy2 = _rsi.Result.GetDivergence(Bars.ClosePrices, Bars.Count - 1, DivPeriod, DivMinDist).Any(x => x.Type == DivergenceType.Down);
            var buy3 = _stochastic.PercentK.HasCrossedAbove(_stochastic.PercentD, 1);

            var buy = buy1 && buy2 && buy3;

            var sell1 = _ema200.LastValue >= Bars.LastBar.Close;
            var sell2 = _rsi.Result.GetDivergence(Bars.ClosePrices, Bars.Count - 1, DivPeriod, DivMinDist).Any(x => x.Type == DivergenceType.Up);
            var sell3 = _stochastic.PercentK.HasCrossedBelow(_stochastic.PercentD, 1);

//            if (buy2)
//                Print("buy2 " + Bars.LastBar.OpenTime);
//            if (sell2)
//                Print("sell2 " + Bars.LastBar.OpenTime);

            var sell = sell1 && sell2 && sell3;
            var sig = (buy ? 1 : 0) + (sell ? -1 : 0);
            return GetTradeType(sig);
        }

        protected override double GetStopLoss()
        {
            var atrVal = _atr.Result.LastValue;
            var atrSl = Symbol.ToPips(atrVal) * AtrMultiplier;
            return atrSl;
        }

        protected override TradeType Filter(TradeType tradeType, ref double stopLossInPips, ref double risk)
        {
            return tradeType;
        }
    }
}
