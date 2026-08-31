using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(
        TimeZone = TimeZones.UTC,
        AccessRights = AccessRights.None
    )]
    public class Ai_Scalping : Robot
    {
        [Parameter("Volume (Lots)", DefaultValue = 0.01, MinValue = 0.01)]
        public double Lots { get; set; }

        [Parameter("Seconds Between Entries", DefaultValue = 170, MinValue = 10)]
        public int TradeIntervalSeconds { get; set; }

        [Parameter("Maximum Open Trades", DefaultValue = 3, MinValue = 1)]
        public int MaximumOpenTrades { get; set; }

        [Parameter("Maximum Daily Entries", DefaultValue = 509, MinValue = 1)]
        public int MaximumDailyEntries { get; set; }

        [Parameter("Close Profit", DefaultValue = 0.01, MinValue = 0)]
        public double CloseProfit { get; set; }

        [Parameter("Stop Loss (Pips)", DefaultValue = 10, MinValue = 1)]
        public double StopLossPips { get; set; }

        private const string Label = "AI_SCALPING";

        private DateTime _lastEntryTime;
        private DateTime _tradeDay;
        private int _dailyEntries;

        protected override void OnStart()
        {
            _tradeDay = Server.Time.Date;

            _lastEntryTime =
                Server.Time.AddSeconds(-TradeIntervalSeconds);

            _dailyEntries = 0;

            Timer.Start(1);

            Print("AI Scalping started");
            Print("Symbol: {0}", SymbolName);
        }

        protected override void OnTimer()
        {
            ResetDailyCounter();

            CloseProfitableTrades();

            if (_dailyEntries >= MaximumDailyEntries)
                return;

            if ((Server.Time - _lastEntryTime).TotalSeconds <
                TradeIntervalSeconds)
                return;

            var openTrades = Positions
                .Where(p =>
                    p.SymbolName == SymbolName &&
                    p.Label == Label)
                .ToArray();

            if (openTrades.Length >= MaximumOpenTrades)
                return;

            OpenTrade();
        }

        private void OpenTrade()
        {
            TradeType direction = GetDirection();

            double volume =
                Symbol.QuantityToVolumeInUnits(Lots);

            volume = Symbol.NormalizeVolumeInUnits(
                volume,
                RoundingMode.Down
            );

            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print("Volume below broker minimum.");
                return;
            }

            var result = ExecuteMarketOrder(
                direction,
                SymbolName,
                volume,
                Label,
                StopLossPips,
                null
            );

            if (result.IsSuccessful)
            {
                _lastEntryTime = Server.Time;
                _dailyEntries++;

                Print(
                    "OPENED {0} | Trade {1}/{2} | Entry {3}",
                    direction,
                    _dailyEntries,
                    MaximumDailyEntries,
                    result.Position.EntryPrice
                );
            }
            else
            {
                Print(
                    "Order failed: {0}",
                    result.Error
                );
            }
        }

        private TradeType GetDirection()
        {
            if (Bars.Count < 3)
                return TradeType.Buy;

            var candle = Bars.Last(1);

            if (candle.Close > candle.Open)
                return TradeType.Buy;

            if (candle.Close < candle.Open)
                return TradeType.Sell;

            if (Symbol.Bid >= candle.Close)
                return TradeType.Buy;

            return TradeType.Sell;
        }

        private void CloseProfitableTrades()
        {
            var trades = Positions
                .Where(p =>
                    p.SymbolName == SymbolName &&
                    p.Label == Label)
                .ToArray();

            foreach (var position in trades)
            {
                if (position.NetProfit >= CloseProfit)
                {
                    double profit = position.NetProfit;

                    var result =
                        ClosePosition(position);

                    if (result.IsSuccessful)
                    {
                        Print(
                            "CLOSED PROFIT | {0}",
                            profit
                        );
                    }
                }
            }
        }

        private void ResetDailyCounter()
        {
            if (Server.Time.Date == _tradeDay)
                return;

            _tradeDay = Server.Time.Date;
            _dailyEntries = 0;

            Print("Daily trade counter reset.");
        }

        protected override void OnStop()
        {
            Timer.Stop();

            Print("AI Scalping stopped.");
        }
    }
}