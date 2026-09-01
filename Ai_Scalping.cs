using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class XAUUSD_AI_599_BUY_BOT : Robot
    {
        [Parameter("Target Trades Per Day", DefaultValue = 599, MinValue = 1)]
        public int TargetTradesPerDay { get; set; }

        [Parameter("Volume In Units", DefaultValue = 1000, MinValue = 1)]
        public double VolumeInUnits { get; set; }

        [Parameter("Stop Loss (Pips)", DefaultValue = 60, MinValue = 1)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (Pips)", DefaultValue = 40, MinValue = 1)]
        public double TakeProfitPips { get; set; }

        [Parameter("AI Base Score", DefaultValue = 55, MinValue = 1, MaxValue = 100)]
        public double BaseAiScore { get; set; }

        [Parameter("Maximum Spread (Pips)", DefaultValue = 30, MinValue = 1)]
        public double MaxSpreadPips { get; set; }

        [Parameter("Maximum Open Trades", DefaultValue = 3, MinValue = 1)]
        public int MaxOpenPositions { get; set; }

        [Parameter("Maximum Hold Minutes", DefaultValue = 3, MinValue = 1)]
        public int MaxHoldMinutes { get; set; }

        [Parameter("Minimum Seconds Between Trades", DefaultValue = 20, MinValue = 5)]
        public int MinimumSecondsBetweenTrades { get; set; }

        [Parameter("Enter Immediately On Start", DefaultValue = true)]
        public bool EnterImmediately { get; set; }

        private const string BotLabel = "XAU_AI_599_BUY";

        private Bars _m1Bars;
        private DateTime _currentDay;
        private DateTime _lastEntryTime;

        protected override void OnStart()
        {
            _m1Bars = MarketData.GetBars(TimeFrame.Minute);

            _currentDay = Server.Time.Date;
            _lastEntryTime = Server.Time.AddDays(-1);

            Timer.Start(1);

            Print("======================================");
            Print("XAUUSD AI 599 BUY BOT");
            Print("BUY ONLY");
            Print("Target: {0} trades/day", TargetTradesPerDay);
            Print("======================================");

            if (EnterImmediately)
            {
                Print("Opening immediate startup BUY...");
                OpenBuy(true);
            }
        }

        protected override void OnTimer()
        {
            ResetIfNewDay();

            ManageOpenTrades();

            int tradesToday = GetTradesToday();

            if (tradesToday >= TargetTradesPerDay)
                return;

            if (Positions.FindAll(BotLabel, SymbolName).Length >= MaxOpenPositions)
                return;

            double spacing = CalculateDynamicSpacing();

            if ((Server.Time - _lastEntryTime).TotalSeconds < spacing)
                return;

            double spreadPips =
                (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;

            if (spreadPips > MaxSpreadPips)
            {
                Print(
                    "Spread too high: {0:F1} pips",
                    spreadPips
                );

                return;
            }

            double aiScore = CalculateAiScore();
            double requiredScore = CalculateDynamicThreshold();

            Print(
                "AI Score: {0:F1} | Required: {1:F1} | Trades: {2}/{3}",
                aiScore,
                requiredScore,
                tradesToday,
                TargetTradesPerDay
            );

            if (aiScore >= requiredScore)
            {
                OpenBuy(false);
            }
        }

        // ======================================================
        // OPEN BUY
        // ======================================================

        private void OpenBuy(bool force)
        {
            try
            {
                double volume =
                    Symbol.NormalizeVolumeInUnits(
                        VolumeInUnits,
                        RoundingMode.Down
                    );

                if (volume < Symbol.VolumeInUnitsMin)
                    volume = Symbol.VolumeInUnitsMin;

                double spreadPips =
                    (Symbol.Ask - Symbol.Bid) /
                    Symbol.PipSize;

                if (!force && spreadPips > MaxSpreadPips)
                    return;

                TradeResult result =
                    ExecuteMarketOrder(
                        TradeType.Buy,
                        SymbolName,
                        volume,
                        BotLabel,
                        StopLossPips,
                        TakeProfitPips
                    );

                if (result.IsSuccessful)
                {
                    _lastEntryTime = Server.Time;

                    Print("");
                    Print("🔥 BUY EXECUTED");
                    Print("Entry: {0}", result.Position.EntryPrice);
                    Print("Trades Today: {0}/{1}",
                        GetTradesToday(),
                        TargetTradesPerDay
                    );
                    Print("");
                }
                else
                {
                    Print(
                        "BUY FAILED: {0}",
                        result.Error
                    );
                }
            }
            catch (Exception ex)
            {
                Print(
                    "Order error: {0}",
                    ex.Message
                );
            }
        }

        // ======================================================
        // AI ENTRY SCORE
        // ======================================================

        private double CalculateAiScore()
        {
            if (_m1Bars.Count < 30)
                return 50;

            double score = 50;

            double close1 = _m1Bars.ClosePrices.Last(1);
            double open1 = _m1Bars.OpenPrices.Last(1);

            double close2 = _m1Bars.ClosePrices.Last(2);
            double open2 = _m1Bars.OpenPrices.Last(2);

            double close3 = _m1Bars.ClosePrices.Last(3);
            double open3 = _m1Bars.OpenPrices.Last(3);

            double close4 = _m1Bars.ClosePrices.Last(4);

            // ------------------------------------------
            // Candle direction
            // ------------------------------------------

            if (close1 > open1)
                score += 10;
            else
                score -= 8;

            if (close2 > open2)
                score += 7;
            else
                score -= 5;

            if (close3 > open3)
                score += 5;
            else
                score -= 3;

            // ------------------------------------------
            // Momentum
            // ------------------------------------------

            if (close1 > close4)
                score += 10;
            else
                score -= 10;

            // ------------------------------------------
            // 20 candle average
            // ------------------------------------------

            double sma20 = 0;

            for (int i = 1; i <= 20; i++)
            {
                sma20 += _m1Bars.ClosePrices.Last(i);
            }

            sma20 /= 20.0;

            if (close1 > sma20)
                score += 10;
            else
                score -= 10;

            // ------------------------------------------
            // Candle strength
            // ------------------------------------------

            double high1 = _m1Bars.HighPrices.Last(1);
            double low1 = _m1Bars.LowPrices.Last(1);

            double range = high1 - low1;

            if (range > 0)
            {
                double body =
                    Math.Abs(close1 - open1);

                double strength =
                    body / range;

                if (close1 > open1)
                {
                    if (strength >= 0.70)
                        score += 10;

                    else if (strength >= 0.50)
                        score += 6;
                }
            }

            // ------------------------------------------
            // Recent range location
            // ------------------------------------------

            double recentHigh = double.MinValue;
            double recentLow = double.MaxValue;

            for (int i = 1; i <= 10; i++)
            {
                recentHigh =
                    Math.Max(
                        recentHigh,
                        _m1Bars.HighPrices.Last(i)
                    );

                recentLow =
                    Math.Min(
                        recentLow,
                        _m1Bars.LowPrices.Last(i)
                    );
            }

            double midpoint =
                (recentHigh + recentLow) / 2.0;

            if (close1 > midpoint)
                score += 5;
            else
                score -= 5;

            // ------------------------------------------
            // 3 consecutive bullish candles
            // ------------------------------------------

            if (
                close1 > open1 &&
                close2 > open2 &&
                close3 > open3
            )
            {
                score += 10;
            }

            return Math.Max(
                0,
                Math.Min(100, score)
            );
        }

        // ======================================================
        // ADAPTIVE AI THRESHOLD
        // ======================================================

        private double CalculateDynamicThreshold()
        {
            int trades = GetTradesToday();

            double minutesPassed =
                Server.Time.TimeOfDay.TotalMinutes;

            double expectedTrades =
                TargetTradesPerDay *
                (minutesPassed / 1440.0);

            double difference =
                expectedTrades - trades;

            double threshold = BaseAiScore;

            // Bot is falling behind daily target
            if (difference > 20)
                threshold -= 20;

            else if (difference > 10)
                threshold -= 15;

            else if (difference > 5)
                threshold -= 10;

            else if (difference > 2)
                threshold -= 5;

            // Bot is ahead of target
            if (difference < -10)
                threshold += 15;

            else if (difference < -5)
                threshold += 10;

            return Math.Max(
                30,
                Math.Min(90, threshold)
            );
        }

        // ======================================================
        // DYNAMIC TRADE SPACING
        // ======================================================

        private double CalculateDynamicSpacing()
        {
            int trades = GetTradesToday();

            int remainingTrades =
                TargetTradesPerDay - trades;

            if (remainingTrades <= 0)
                return double.MaxValue;

            DateTime endOfDay =
                Server.Time.Date.AddDays(1);

            double secondsRemaining =
                (endOfDay - Server.Time).TotalSeconds;

            double requiredSpacing =
                secondsRemaining /
                remainingTrades;

            return Math.Max(
                MinimumSecondsBetweenTrades,
                requiredSpacing
            );
        }

        // ======================================================
        // CLOSE OLD POSITIONS
        // ======================================================

        private void ManageOpenTrades()
        {
            Position[] positions =
                Positions.FindAll(
                    BotLabel,
                    SymbolName
                );

            foreach (Position position in positions)
            {
                double minutesOpen =
                    (Server.Time -
                     position.EntryTime)
                    .TotalMinutes;

                if (minutesOpen >= MaxHoldMinutes)
                {
                    ClosePosition(position);

                    Print(
                        "⏱ Trade closed after {0:F1} minutes",
                        minutesOpen
                    );
                }
            }
        }

        // ======================================================
        // COUNT DAILY ENTRIES
        // ======================================================

        private int GetTradesToday()
        {
            DateTime today =
                Server.Time.Date;

            int closedTrades =
                History.Count(
                    trade =>
                        trade.Label == BotLabel &&
                        trade.SymbolName == SymbolName &&
                        trade.EntryTime.Date == today
                );

            int openTrades =
                Positions.Count(
                    position =>
                        position.Label == BotLabel &&
                        position.SymbolName == SymbolName &&
                        position.EntryTime.Date == today
                );

            return closedTrades + openTrades;
        }

        // ======================================================
        // NEW DAY
        // ======================================================

        private void ResetIfNewDay()
        {
            if (Server.Time.Date ==
                _currentDay)
                return;

            _currentDay =
                Server.Time.Date;

            _lastEntryTime =
                Server.Time.AddDays(-1);

            Print("");
            Print("NEW TRADING DAY");
            Print("Daily counter reset.");
            Print("");
        }

        protected override void OnStop()
        {
            Timer.Stop();

            Print(
                "Bot stopped. Trades today: {0}",
                GetTradesToday()
            );
        }
    }
}