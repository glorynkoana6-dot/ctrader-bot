using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;

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

        [Parameter("Immediate Trade On Start", DefaultValue = true)]
        public bool ImmediateTradeOnStart { get; set; }

        [Parameter("Target Trades Per Day", DefaultValue = 500, MinValue = 1, MaxValue = 500)]
        public int TargetTradesPerDay { get; set; }

        [Parameter("Maximum Open Positions", DefaultValue = 5, MinValue = 1, MaxValue = 20)]
        public int MaxOpenPositions { get; set; }

        [Parameter("Minimum Net Profit", DefaultValue = 0.01, MinValue = 0)]
        public double MinimumNetProfit { get; set; }

        [Parameter("Maximum Spread (Pips)", DefaultValue = 2.0, MinValue = 0.1)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("EMA Fast", DefaultValue = 9, MinValue = 2)]
        public int FastEmaPeriod { get; set; }

        [Parameter("EMA Slow", DefaultValue = 21, MinValue = 3)]
        public int SlowEmaPeriod { get; set; }

        [Parameter("RSI Period", DefaultValue = 7, MinValue = 2)]
        public int RsiPeriod { get; set; }

        [Parameter("ATR Period", DefaultValue = 14, MinValue = 2)]
        public int AtrPeriod { get; set; }

        [Parameter("ATR Stop Multiplier", DefaultValue = 1.0, MinValue = 0.2)]
        public double AtrStopMultiplier { get; set; }

        [Parameter("Minimum SL Pips", DefaultValue = 3.0, MinValue = 1)]
        public double MinimumStopLossPips { get; set; }

        [Parameter("Minimum Signal Score", DefaultValue = 3, MinValue = 1, MaxValue = 8)]
        public int MinimumSignalScore { get; set; }

        [Parameter("Maximum Hold Minutes", DefaultValue = 12, MinValue = 1)]
        public int MaximumHoldMinutes { get; set; }

        [Parameter("Force Trade Each M1 Candle", DefaultValue = true)]
        public bool ForceTradeEachCandle { get; set; }

        private Bars _m1Bars;

        private ExponentialMovingAverage _fastEma;
        private ExponentialMovingAverage _slowEma;

        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;

        private int _lastM1BarCount;

        private int _tradesToday;
        private DateTime _tradeDay;

        private const string BotLabel = "AI_SCALPING_M1_500";


        protected override void OnStart()
        {
            _m1Bars = MarketData.GetBars(
                TimeFrame.Minute,
                SymbolName
            );

            _fastEma =
                Indicators.ExponentialMovingAverage(
                    _m1Bars.ClosePrices,
                    FastEmaPeriod
                );

            _slowEma =
                Indicators.ExponentialMovingAverage(
                    _m1Bars.ClosePrices,
                    SlowEmaPeriod
                );

            _rsi =
                Indicators.RelativeStrengthIndex(
                    _m1Bars.ClosePrices,
                    RsiPeriod
                );

            _atr =
                Indicators.AverageTrueRange(
                    _m1Bars,
                    AtrPeriod,
                    MovingAverageType.Exponential
                );

            _lastM1BarCount = _m1Bars.Count;

            _tradeDay = Server.Time.Date;
            _tradesToday = 0;

            Timer.Start(1);

            Print("======================================");
            Print("AI SCALPING STARTED");
            Print("SYMBOL: {0}", SymbolName);
            Print("TIMEFRAME: M1");
            Print("TARGET: {0} trades/day", TargetTradesPerDay);
            Print("======================================");

            if (ImmediateTradeOnStart)
            {
                Print("ANALYSING IMMEDIATE STARTUP TRADE...");

                TryImmediateTrade();
            }
        }


        protected override void OnTimer()
        {
            ResetDailyCounter();

            ManagePositions();

            DetectNewM1Candle();
        }


        private void DetectNewM1Candle()
        {
            if (_m1Bars.Count <= _lastM1BarCount)
                return;

            _lastM1BarCount = _m1Bars.Count;

            AnalyseAndTrade(false);
        }


        private void TryImmediateTrade()
        {
            if (_m1Bars.Count < SlowEmaPeriod + 20)
            {
                Print("NOT ENOUGH DATA FOR IMMEDIATE TRADE");
                return;
            }

            AnalyseAndTrade(true);
        }


        private void AnalyseAndTrade(bool immediate)
        {
            if (_tradesToday >= TargetTradesPerDay)
            {
                Print("DAILY TRADE LIMIT REACHED");
                return;
            }

            var positions =
                Positions.FindAll(
                    BotLabel,
                    SymbolName
                );

            if (positions.Length >= MaxOpenPositions)
            {
                Print("MAXIMUM OPEN POSITIONS REACHED");
                return;
            }


            double spreadPips =
                (Symbol.Ask - Symbol.Bid)
                / Symbol.PipSize;


            if (spreadPips > MaximumSpreadPips)
            {
                Print(
                    "SPREAD TOO HIGH: {0:F2} pips",
                    spreadPips
                );

                return;
            }


            int i = _m1Bars.Count - 2;


            double open =
                _m1Bars.OpenPrices[i];

            double high =
                _m1Bars.HighPrices[i];

            double low =
                _m1Bars.LowPrices[i];

            double close =
                _m1Bars.ClosePrices[i];


            double previousClose =
                _m1Bars.ClosePrices[i - 1];


            double fast =
                _fastEma.Result[i];

            double slow =
                _slowEma.Result[i];

            double rsi =
                _rsi.Result[i];

            double atr =
                _atr.Result[i];


            int buyScore = 0;
            int sellScore = 0;


            // ===============================================
            // EMA TREND
            // ===============================================

            if (fast > slow)
                buyScore += 2;

            if (fast < slow)
                sellScore += 2;


            // ===============================================
            // PRICE VS EMA
            // ===============================================

            if (close > fast)
                buyScore++;

            if (close < fast)
                sellScore++;


            // ===============================================
            // CANDLE DIRECTION
            // ===============================================

            if (close > open)
                buyScore++;

            if (close < open)
                sellScore++;


            // ===============================================
            // MOMENTUM
            // ===============================================

            if (close > previousClose)
                buyScore++;

            if (close < previousClose)
                sellScore++;


            // ===============================================
            // STRONG CANDLE
            // ===============================================

            double range = high - low;

            double body =
                Math.Abs(close - open);


            if (range > 0)
            {
                double strength =
                    body / range;


                if (
                    strength >= 0.55 &&
                    close > open
                )
                {
                    buyScore++;
                }


                if (
                    strength >= 0.55 &&
                    close < open
                )
                {
                    sellScore++;
                }
            }


            // ===============================================
            // RSI
            // ===============================================

            if (rsi > 50)
                buyScore++;

            if (rsi < 50)
                sellScore++;


            if (rsi >= 55 && rsi <= 75)
                buyScore++;


            if (rsi <= 45 && rsi >= 25)
                sellScore++;


            // ===============================================
            // BREAKOUT
            // ===============================================

            double recentHigh =
                HighestHigh(
                    i - 5,
                    i - 1
                );


            double recentLow =
                LowestLow(
                    i - 5,
                    i - 1
                );


            if (close > recentHigh)
                buyScore += 2;


            if (close < recentLow)
                sellScore += 2;


            Print(
                "M1 SIGNAL | BUY {0} | SELL {1} | RSI {2:F1}",
                buyScore,
                sellScore,
                rsi
            );


            TradeType? direction = null;


            if (
                buyScore >= MinimumSignalScore &&
                buyScore > sellScore
            )
            {
                direction = TradeType.Buy;
            }


            else if (
                sellScore >= MinimumSignalScore &&
                sellScore > buyScore
            )
            {
                direction = TradeType.Sell;
            }


            // ===============================================
            // FORCE DIRECTION
            //
            // Used for immediate startup and aggressive
            // M1 entry mode.
            // ===============================================

            if (
                direction == null &&
                (
                    immediate ||
                    ForceTradeEachCandle
                )
            )
            {
                if (buyScore > sellScore)
                {
                    direction =
                        TradeType.Buy;
                }

                else if (sellScore > buyScore)
                {
                    direction =
                        TradeType.Sell;
                }

                else
                {
                    direction =
                        close >= open
                        ? TradeType.Buy
                        : TradeType.Sell;
                }
            }


            if (direction == null)
            {
                Print("NO TRADE");
                return;
            }


            if (immediate)
            {
                Print(
                    "IMMEDIATE START TRADE: {0}",
                    direction.Value
                );
            }


            OpenTrade(
                direction.Value,
                atr
            );
        }


        private void OpenTrade(
            TradeType tradeType,
            double atr
        )
        {
            if (_tradesToday >= TargetTradesPerDay)
                return;


            var positions =
                Positions.FindAll(
                    BotLabel,
                    SymbolName
                );


            if (positions.Length >= MaxOpenPositions)
                return;


            double volume =
                Symbol.QuantityToVolumeInUnits(
                    Lots
                );


            volume =
                Symbol.NormalizeVolumeInUnits(
                    volume,
                    RoundingMode.Down
                );


            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print("VOLUME BELOW BROKER MINIMUM");
                return;
            }


            double atrPips =
                atr / Symbol.PipSize;


            double stopLossPips =
                atrPips *
                AtrStopMultiplier;


            if (
                stopLossPips <
                MinimumStopLossPips
            )
            {
                stopLossPips =
                    MinimumStopLossPips;
            }


            var result =
                ExecuteMarketOrder(
                    tradeType,
                    SymbolName,
                    volume,
                    BotLabel,
                    stopLossPips,
                    null
                );


            if (result.IsSuccessful)
            {
                _tradesToday++;


                Print("================================");

                Print(
                    "TRADE OPENED: {0}",
                    tradeType
                );

                Print(
                    "ENTRY: {0}",
                    result.Position.EntryPrice
                );

                Print(
                    "STOP: {0:F1} pips",
                    stopLossPips
                );

                Print(
                    "TRADE COUNT: {0}/{1}",
                    _tradesToday,
                    TargetTradesPerDay
                );

                Print("================================");
            }
            else
            {
                Print(
                    "ORDER FAILED: {0}",
                    result.Error
                );
            }
        }


        private void ManagePositions()
        {
            var positions =
                Positions.FindAll(
                    BotLabel,
                    SymbolName
                );


            foreach (var position in positions)
            {
                // ===========================================
                // CLOSE AS SOON AS NET PROFIT IS POSITIVE
                // ===========================================

                if (
                    position.NetProfit >=
                    MinimumNetProfit
                )
                {
                    double profit =
                        position.NetProfit;


                    var result =
                        ClosePosition(
                            position
                        );


                    if (result.IsSuccessful)
                    {
                        Print(
                            "PROFIT CLOSED: {0:F2}",
                            profit
                        );
                    }


                    continue;
                }


                // ===========================================
                // MAXIMUM HOLD TIME
                // ===========================================

                double minutesOpen =
                    (
                        Server.Time -
                        position.EntryTime
                    )
                    .TotalMinutes;


                if (
                    minutesOpen >=
                    MaximumHoldMinutes
                )
                {
                    double resultValue =
                        position.NetProfit;


                    var result =
                        ClosePosition(
                            position
                        );


                    if (result.IsSuccessful)
                    {
                        Print(
                            "TIME EXIT: {0:F2}",
                            resultValue
                        );
                    }
                }
            }
        }


        private double HighestHigh(
            int start,
            int end
        )
        {
            if (start < 0)
                start = 0;


            double highest =
                double.MinValue;


            for (
                int x = start;
                x <= end;
                x++
            )
            {
                if (
                    _m1Bars.HighPrices[x] >
                    highest
                )
                {
                    highest =
                        _m1Bars.HighPrices[x];
                }
            }


            return highest;
        }


        private double LowestLow(
            int start,
            int end
        )
        {
            if (start < 0)
                start = 0;


            double lowest =
                double.MaxValue;


            for (
                int x = start;
                x <= end;
                x++
            )
            {
                if (
                    _m1Bars.LowPrices[x] <
                    lowest
                )
                {
                    lowest =
                        _m1Bars.LowPrices[x];
                }
            }


            return lowest;
        }


        private void ResetDailyCounter()
        {
            if (
                Server.Time.Date ==
                _tradeDay
            )
            {
                return;
            }


            _tradeDay =
                Server.Time.Date;


            _tradesToday = 0;


            Print(
                "DAILY TRADE COUNTER RESET"
            );
        }


        protected override void OnStop()
        {
            Timer.Stop();

            Print(
                "AI SCALPING STOPPED"
            );
        }
    }
}