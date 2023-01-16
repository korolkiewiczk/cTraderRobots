using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo
{
    public abstract class CBotBase : Robot
    {
        protected CBotSettings Settings = new CBotSettings();
        private readonly string _label;

        protected CBotBase(string label)
        {
            _label = label;
        }

        protected abstract TradeType? GetSignal();

        protected abstract double GetStopLoss();

        protected abstract TradeType Filter(TradeType tradeType, ref double stopLossInPips, ref double risk);

        protected virtual void OnTrade(double tradeRisk, double tpFactor)
        {
            TradeType? tradeType = GetSignal();
            if (tradeType == null)
                return;

            var stopLossInPips = GetStopLoss();
            var risk = tradeRisk;

            tradeType = Filter(tradeType.Value, ref stopLossInPips, ref risk);
            
            if (HandleOpenPositions(tradeType.Value)) return;

            ExecuteOrder(tradeType.Value, risk, stopLossInPips, tpFactor);
        }

        protected virtual void ExecuteOrder(TradeType tradeType, double risk, double stopLossInPips, double tpFactor)
        {
            var takeProfitInPips = stopLossInPips * tpFactor;
            if (Settings.IsReverseStrategy)
            {
                tradeType = Reverse(tradeType, ref stopLossInPips, ref takeProfitInPips);
            }

            var volumeInUnits = Symbol.GetVolume(risk, Account.Balance, stopLossInPips);
            ExecuteMarketOrder(tradeType, SymbolName, volumeInUnits, _label, stopLossInPips, takeProfitInPips);
        }

        protected static TradeType? GetTradeType(double signal)
        {
            TradeType? tradeType = null;
            if (signal > 0)
            {
                tradeType = TradeType.Buy;
            }
            else if (signal < 0)
            {
                tradeType = TradeType.Sell;
            }

            return tradeType;
        }

        private bool HandleOpenPositions(TradeType tradeType)
        {
            var longPositions = Positions.FindAll(_label, SymbolName, TradeType.Buy);
            var shortPositions = Positions.FindAll(_label, SymbolName, TradeType.Sell);
            switch (Settings.PositionMode)
            {
                case PositionMode.OnePosition:
                    return longPositions.Length > 0 || shortPositions.Length > 0;
                case PositionMode.MultiPosition:
                    return longPositions.Length + shortPositions.Length > Settings.MaxOpenPositions;
                case PositionMode.CloseExistingOnSignal:
                    if (tradeType == TradeType.Sell)
                    {
                        foreach (var position in longPositions)
                        {
                            position.Close();
                        }

                        return shortPositions.Any();
                    }

                    if (tradeType == TradeType.Buy)
                    {
                        foreach (var position in shortPositions)
                        {
                            position.Close();
                        }

                        return longPositions.Any();
                    }

                    return false;
            }

            throw new InvalidOperationException("Invalid positionMode");
        }

        private static TradeType Reverse(TradeType tradeType, ref double stopLossInPips, ref double takeProfitInPips)
        {
            tradeType = (TradeType) (((int) tradeType + 1) % 2);
            var tmp = stopLossInPips;
            stopLossInPips = takeProfitInPips;
            takeProfitInPips = tmp;
            return tradeType;
        }
    }
}