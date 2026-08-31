using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class MKAYFXM1Scalper : Robot
    {
        // =====================================================
        // SETTINGS
        // =====================================================

        [Parameter("Volume (Lots)", DefaultValue = 0.01, MinValue = 0.01)]
        public double Lots { get; set; }

        [Parameter("EMA Fast", DefaultValue = 20)]
        public int FastEmaPeriod { get; set; }

        [Parameter("EMA Slow", DefaultValue = 50)]
        public int SlowEmaPeriod { get; set; }

        [Parameter("RSI Period", DefaultValue = 14)]
        public int RsiPeriod { get; set; }

        [Parameter("ATR Period", DefaultValue = 14)]
        public int AtrPeriod { get; set; }

        [Parameter("ATR SL Multiplier", DefaultValue = 1.5)]
        public double AtrMultiplier { get; set; }

        [Parameter("Minimum Profit", DefaultValue = 0.01)]
        public double MinimumProfit { get; set; }

        [Parameter("Max Open Positions", DefaultValue = 3)]
        public int MaxOpenPositions { get; set; }

        [Parameter("Max Trades Per Day", DefaultValue = 509)]
        public int MaxTradesPerDay { get; set; }

        [Parameter("Cooldown Seconds", DefaultValue = 60)]
        public int CooldownSeconds { get; set; }


        // =====================================================
        // VARIABLES
        // =====================================================

        private ExponentialMovingAverage _ema20;
        private ExponentialMovingAverage _ema50;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;

        private DateTime _lastTradeTime = DateTime.MinValue;

        private int _tradesToday = 0;
        private DateTime _tradeDay;

        private const string BotLabel = "MKAYFX_M1";


        // =====================================================
        // START
        // =====================================================

        protected override void OnStart()
        {
            _ema20 = Indicators.ExponentialMovingAverage(
                Bars.ClosePrices,
                FastEmaPeriod
            );

            _ema50 = Indicators.ExponentialMovingAverage(
                Bars.ClosePrices,
                SlowEmaPeriod
            );

            _rsi = Indicators.RelativeStrengthIndex(
                Bars.ClosePrices,
                RsiPeriod
            );

            _atr = Indicators.AverageTrueRange(
                AtrPeriod,
                MovingAverageType.Exponential
            );

            _tradeDay = Server.Time.Date;

            // Check profit every second
            Timer.Start(1);

            Print("====================================");
            Print("MKAYFX M1 SCALPER STARTED");
            Print("Strategy: EMA20 + EMA50 + RSI + Pullback");
            Print("Timeframe: 1 MINUTE");
            Print("Symbol: {0}", SymbolName);
            Print("Lots: {0}", Lots);
            Print("Max Positions: {0}", MaxOpenPositions);
            Print("====================================");
        }


        // =====================================================
        // NEW 1-MINUTE BAR
        // =====================================================

        protected override void OnBar()
        {
            ResetDailyCounter();

            if (Bars.Count < 60)
                return;

            if (_tradesToday >= MaxTradesPerDay)
                return;

            if ((Server.Time - _lastTradeTime).TotalSeconds < CooldownSeconds)
                return;

            var positions = Positions.FindAll(BotLabel, SymbolName);

            if (positions.Length >= MaxOpenPositions)
                return;


            // Last completed candle
            int index = Bars.Count - 2;

            double open = Bars.OpenPrices[index];
            double high = Bars.HighPrices[index];
            double low = Bars.LowPrices[index];
            double close = Bars.ClosePrices[index];

            double ema20 = _ema20.Result[index];
            double ema50 = _ema50.Result[index];

            double rsi = _rsi.Result[index];
            double atr = _atr.Result[index];


            // =================================================
            // CANDLE DIRECTION
            // =================================================

            bool bullishCandle = close > open;
            bool bearishCandle = close < open;


            // =================================================
            // TREND
            // =================================================

            bool bullishTrend =
                ema20 > ema50 &&
                close > ema20;

            bool bearishTrend =
                ema20 < ema50 &&
                close < ema20;


            // =================================================
            // EMA20 PULLBACK
            // =================================================

            bool bullishPullback =
                low <= ema20 &&
                close > ema20;

            bool bearishPullback =
                high >= ema20 &&
                close < ema20;


            // =================================================
            // RSI FILTER
            // =================================================

            bool buyRsi =
                rsi >= 52 &&
                rsi <= 68;

            bool sellRsi =
                rsi >= 32 &&
                rsi <= 48;


            // =================================================
            // BUY SETUP
            // =================================================

            bool buySignal =
                bullishTrend &&
                bullishPullback &&
                bullishCandle &&
                buyRsi;


            // =================================================
            // SELL SETUP
            // =================================================

            bool sellSignal =
                bearishTrend &&
                bearishPullback &&
                bearishCandle &&
                sellRsi;


            Print(
                "M1 | Close: {0} | EMA20: {1} | EMA50: {2} | RSI: {3:F2}",
                close,
                ema20,
                ema50,
                rsi
            );


            if (buySignal)
            {
                OpenTrade(
                    TradeType.Buy,
                    atr
                );
            }
            else if (sellSignal)
            {
                OpenTrade(
                    TradeType.Sell,
                    atr
                );
            }
            else
            {
                Print("NO TRADE - Waiting for M1 setup");
            }
        }


        // =====================================================
        // OPEN TRADE
        // =====================================================

        private void OpenTrade(
            TradeType tradeType,
            double atr
        )
        {
            double volume =
                Symbol.QuantityToVolumeInUnits(Lots);

            volume = Symbol.NormalizeVolumeInUnits(
                volume,
                RoundingMode.Down
            );


            // ATR converted into pips
            double atrPips =
                atr / Symbol.PipSize;

            double stopLossPips =
                atrPips * AtrMultiplier;


            // Safety minimum
            if (stopLossPips < 2)
                stopLossPips = 2;


            var result = ExecuteMarketOrder(
                tradeType,
                SymbolName,
                volume,
                BotLabel,
                stopLossPips,
                null
            );


            if (result.IsSuccessful)
            {
                _lastTradeTime = Server.Time;
                _tradesToday++;

                Print("====================================");

                Print(
                    "TRADE OPENED: {0}",
                    tradeType
                );

                Print(
                    "Entry: {0}",
                    result.Position.EntryPrice
                );

                Print(
                    "SL: {0:F1} pips",
                    stopLossPips
                );

                Print(
                    "Trades Today: {0}",
                    _tradesToday
                );

                Print("====================================");
            }
            else
            {
                Print(
                    "ORDER FAILED: {0}",
                    result.Error
                );
            }
        }


        // =====================================================
        // PROFIT CLOSER
        // =====================================================

        protected override void OnTimer()
        {
            var positions =
                Positions.FindAll(
                    BotLabel,
                    SymbolName
                );


            foreach (var position in positions)
            {
                if (position.NetProfit >= MinimumProfit)
                {
                    Print(
                        "CLOSING {0} | PROFIT: {1}",
                        position.TradeType,
                        position.NetProfit
                    );

                    ClosePosition(position);
                }
            }
        }


        // =====================================================
        // RESET TRADE COUNTER EACH DAY
        // =====================================================

        private void ResetDailyCounter()
        {
            if (Server.Time.Date != _tradeDay)
            {
                _tradeDay = Server.Time.Date;
                _tradesToday = 0;

                Print("DAILY TRADE COUNTER RESET");
            }
        }


        // =====================================================
        // STOP
        // =====================================================

        protected override void OnStop()
        {
            Timer.Stop();

            Print("MKAYFX M1 SCALPER STOPPED");
        }
    }
}