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
        // =====================================================
        // SETTINGS
        // =====================================================

        [Parameter(
            "Volume (Lots)",
            DefaultValue = 0.01,
            MinValue = 0.01
        )]
        public double Lots { get; set; }


        [Parameter(
            "Target Trades Per Day",
            DefaultValue = 500,
            MinValue = 1,
            MaxValue = 500
        )]
        public int TargetTradesPerDay { get; set; }


        [Parameter(
            "Maximum Open Positions",
            DefaultValue = 5,
            MinValue = 1,
            MaxValue = 20
        )]
        public int MaxOpenPositions { get; set; }


        [Parameter(
            "Minimum Net Profit",
            DefaultValue = 0.01,
            MinValue = 0
        )]
        public double MinimumNetProfit { get; set; }


        [Parameter(
            "Maximum Spread (Pips)",
            DefaultValue = 2.0,
            MinValue = 0.1
        )]
        public double MaximumSpreadPips { get; set; }


        [Parameter(
            "EMA Fast",
            DefaultValue = 9,
            MinValue = 2
        )]
        public int FastEmaPeriod { get; set; }


        [Parameter(
            "EMA Slow",
            DefaultValue = 21,
            MinValue = 3
        )]
        public int SlowEmaPeriod { get; set; }


        [Parameter(
            "RSI Period",
            DefaultValue = 7,
            MinValue = 2
        )]
        public int RsiPeriod { get; set; }


        [Parameter(
            "ATR Period",
            DefaultValue = 14,
            MinValue = 2
        )]
        public int AtrPeriod { get; set; }


        [Parameter(
            "ATR Stop Multiplier",
            DefaultValue = 1.0,
            MinValue = 0.2
        )]
        public double AtrStopMultiplier { get; set; }


        [Parameter(
            "Minimum SL Pips",
            DefaultValue = 3.0,
            MinValue = 1
        )]
        public double MinimumStopLossPips { get; set; }


        [Parameter(
            "Normal Minimum Score",
            DefaultValue = 3,
            MinValue = 1,
            MaxValue = 8
        )]
        public int NormalMinimumScore { get; set; }


        [Parameter(
            "Maximum Hold Minutes",
            DefaultValue = 12,
            MinValue = 1
        )]
        public int MaximumHoldMinutes { get; set; }


        [Parameter(
            "Force Catch-Up",
            DefaultValue = true
        )]
        public bool ForceCatchUp { get; set; }


        // =====================================================
        // VARIABLES
        // =====================================================

        private Bars _m1Bars;

        private ExponentialMovingAverage _fastEma;
        private ExponentialMovingAverage _slowEma;

        private RelativeStrengthIndex _rsi;

        private AverageTrueRange _atr;

        private int _lastM1BarCount;

        private int _tradesToday;

        private DateTime _tradeDay;

        private const string BotLabel =
            "AI_SCALPING_M1_500";


        // =====================================================
        // START
        // =====================================================

        protected override void OnStart()
        {
            // Always use M1 data even if the cBot instance
            // is accidentally attached to H1/M5/etc.
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


            // Check positions every second
            Timer.Start(1);


            Print("========================================");

            Print("AI SCALPING M1 500 STARTED");

            Print("Symbol: {0}", SymbolName);

            Print(
                "Daily Target: {0}",
                TargetTradesPerDay
            );

            Print(
                "Maximum Open Trades: {0}",
                MaxOpenPositions
            );

            Print(
                "Minimum Net Profit: {0}",
                MinimumNetProfit
            );

            Print("========================================");
        }


        // =====================================================
        // TIMER
        // =====================================================

        protected override void OnTimer()
        {
            ResetDailyCounter();

            ManageOpenPositions();

            DetectNewM1Candle();
        }


        // =====================================================
        // DETECT NEW M1 CANDLE
        // =====================================================

        private void DetectNewM1Candle()
        {
            if (_m1Bars.Count <= _lastM1BarCount)
                return;


            _lastM1BarCount = _m1Bars.Count;


            // A new M1 candle has opened.
            // Analyse the candle that just closed.
            AnalyseClosedM1Candle();
        }


        // =====================================================
        // ANALYSE EACH COMPLETED M1 CANDLE
        // =====================================================

        private void AnalyseClosedM1Candle()
        {
            if (_tradesToday >= TargetTradesPerDay)
            {
                Print("DAILY TARGET REACHED");
                return;
            }


            if (_m1Bars.Count <
                SlowEmaPeriod + 20)
            {
                return;
            }


            // -------------------------------------------------
            // SPREAD FILTER
            // -------------------------------------------------

            double spreadPips =
                (Symbol.Ask - Symbol.Bid)
                / Symbol.PipSize;


            if (spreadPips >
                MaximumSpreadPips)
            {
                Print(
                    "SKIP | Spread {0:F2} pips",
                    spreadPips
                );

                return;
            }


            // -------------------------------------------------
            // OPEN POSITION LIMIT
            // -------------------------------------------------

            var positions =
                Positions.FindAll(
                    BotLabel,
                    SymbolName
                );


            if (positions.Length >=
                MaxOpenPositions)
            {
                Print(
                    "SKIP | Maximum open positions"
                );

                return;
            }


            // Last completed M1 candle
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


            double fastEma =
                _fastEma.Result[i];

            double slowEma =
                _slowEma.Result[i];

            double rsi =
                _rsi.Result[i];

            double atr =
                _atr.Result[i];


            // =================================================
            // SCORE BUY / SELL
            // =================================================

            int buyScore = 0;

            int sellScore = 0;


            // -------------------------------------------------
            // 1. EMA TREND
            // -------------------------------------------------

            if (fastEma > slowEma)
                buyScore += 2;

            if (fastEma < slowEma)
                sellScore += 2;


            // -------------------------------------------------
            // 2. PRICE VS FAST EMA
            // -------------------------------------------------

            if (close > fastEma)
                buyScore++;

            if (close < fastEma)
                sellScore++;


            // -------------------------------------------------
            // 3. CANDLE DIRECTION
            // -------------------------------------------------

            if (close > open)
                buyScore++;

            if (close < open)
                sellScore++;


            // -------------------------------------------------
            // 4. SHORT-TERM MOMENTUM
            // -------------------------------------------------

            if (close > previousClose)
                buyScore++;

            if (close < previousClose)
                sellScore++;


            // -------------------------------------------------
            // 5. STRONG CANDLE
            // -------------------------------------------------

            double candleRange =
                high - low;

            double body =
                Math.Abs(close - open);


            if (candleRange > 0)
            {
                double bodyStrength =
                    body / candleRange;


                if (
                    bodyStrength >= 0.55 &&
                    close > open
                )
                {
                    buyScore++;
                }


                if (
                    bodyStrength >= 0.55 &&
                    close < open
                )
                {
                    sellScore++;
                }
            }


            // -------------------------------------------------
            // 6. RSI MOMENTUM
            // -------------------------------------------------

            if (
                rsi >= 52 &&
                rsi <= 78
            )
            {
                buyScore++;
            }


            if (
                rsi <= 48 &&
                rsi >= 22
            )
            {
                sellScore++;
            }


            // -------------------------------------------------
            // 7. MICRO BREAKOUT
            // -------------------------------------------------

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


            // =================================================
            // ADAPTIVE TRADE FREQUENCY
            // =================================================

            int requiredScore =
                GetRequiredScore();


            Print(
                "M1 | BUY {0} | SELL {1} | Required {2} | Trades {3}/{4}",
                buyScore,
                sellScore,
                requiredScore,
                _tradesToday,
                TargetTradesPerDay
            );


            // =================================================
            // TRADE DIRECTION
            // =================================================

            TradeType? direction = null;


            if (
                buyScore >= requiredScore &&
                buyScore > sellScore
            )
            {
                direction = TradeType.Buy;
            }


            else if (
                sellScore >= requiredScore &&
                sellScore > buyScore
            )
            {
                direction = TradeType.Sell;
            }


            // =================================================
            // CATCH-UP MODE
            //
            // If the bot is behind the pace needed for
            // 500 trades/day, become more aggressive.
            // =================================================

            if (
                direction == null &&
                ForceCatchUp &&
                IsBehindDailyTarget()
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
                    // Tie-break using candle direction
                    direction =
                        close >= open
                        ? TradeType.Buy
                        : TradeType.Sell;
                }


                Print(
                    "CATCH-UP ENTRY"
                );
            }


            if (direction == null)
            {
                Print("NO TRADE");
                return;
            }


            OpenTrade(
                direction.Value,
                atr
            );
        }


        // =====================================================
        // REQUIRED SCORE
        // =====================================================

        private int GetRequiredScore()
        {
            if (IsFarBehindTarget())
                return 1;


            if (IsBehindDailyTarget())
                return 2;


            return NormalMinimumScore;
        }


        // =====================================================
        // DAILY PACE
        // =====================================================

        private bool IsBehindDailyTarget()
        {
            double minutesPassed =
                Server.Time.TimeOfDay.TotalMinutes;


            double expectedTrades =
                TargetTradesPerDay
                * (
                    minutesPassed
                    / 1440.0
                );


            return
                _tradesToday <
                expectedTrades - 3;
        }


        private bool IsFarBehindTarget()
        {
            double minutesPassed =
                Server.Time.TimeOfDay.TotalMinutes;


            double expectedTrades =
                TargetTradesPerDay
                * (
                    minutesPassed
                    / 1440.0
                );


            return
                _tradesToday <
                expectedTrades - 15;
        }


        // =====================================================
        // OPEN POSITION
        // =====================================================

        private void OpenTrade(
            TradeType tradeType,
            double atr
        )
        {
            if (_tradesToday >=
                TargetTradesPerDay)
            {
                return;
            }


            double volume =
                Symbol.QuantityToVolumeInUnits(
                    Lots
                );


            volume =
                Symbol.NormalizeVolumeInUnits(
                    volume,
                    RoundingMode.Down
                );


            if (
                volume <
                Symbol.VolumeInUnitsMin
            )
            {
                Print(
                    "Volume below broker minimum"
                );

                return;
            }


            // -------------------------------------------------
            // ATR STOP LOSS
            // -------------------------------------------------

            double atrPips =
                atr / Symbol.PipSize;


            double stopLossPips =
                atrPips
                * AtrStopMultiplier;


            if (
                stopLossPips <
                MinimumStopLossPips
            )
            {
                stopLossPips =
                    MinimumStopLossPips;
            }


            // -------------------------------------------------
            // EXECUTE
            // -------------------------------------------------

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


                Print(
                    "================================"
                );


                Print(
                    "OPENED: {0}",
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
                    "TRADE: {0}/{1}",
                    _tradesToday,
                    TargetTradesPerDay
                );


                Print(
                    "================================"
                );
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
        // MANAGE OPEN TRADES
        // =====================================================

        private void ManageOpenPositions()
        {
            var positions =
                Positions.FindAll(
                    BotLabel,
                    SymbolName
                );


            foreach (
                var position
                in positions
            )
            {
                // ---------------------------------------------
                // CLOSE AT POSITIVE NET PROFIT
                // ---------------------------------------------

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


                    if (
                        result.IsSuccessful
                    )
                    {
                        Print(
                            "PROFIT CLOSE | {0:F2}",
                            profit
                        );
                    }


                    continue;
                }


                // ---------------------------------------------
                // DON'T ALLOW POSITIONS TO BLOCK THE BOT FOREVER
                // ---------------------------------------------

                double minutesOpen =
                    (
                        Server.Time
                        - position.EntryTime
                    )
                    .TotalMinutes;


                if (
                    minutesOpen >=
                    MaximumHoldMinutes
                )
                {
                    double profit =
                        position.NetProfit;


                    var result =
                        ClosePosition(
                            position
                        );


                    if (
                        result.IsSuccessful
                    )
                    {
                        Print(
                            "TIME EXIT | P/L {0:F2}",
                            profit
                        );
                    }
                }
            }
        }


        // =====================================================
        // HIGHEST HIGH
        // =====================================================

        private double HighestHigh(
            int start,
            int end
        )
        {
            double highest =
                double.MinValue;


            for (
                int x = start;
                x <= end;
                x++
            )
            {
                if (
                    _m1Bars.HighPrices[x]
                    > highest
                )
                {
                    highest =
                        _m1Bars.HighPrices[x];
                }
            }


            return highest;
        }


        // =====================================================
        // LOWEST LOW
        // =====================================================

        private double LowestLow(
            int start,
            int end
        )
        {
            double lowest =
                double.MaxValue;


            for (
                int x = start;
                x <= end;
                x++
            )
            {
                if (
                    _m1Bars.LowPrices[x]
                    < lowest
                )
                {
                    lowest =
                        _m1Bars.LowPrices[x];
                }
            }


            return lowest;
        }


        // =====================================================
        // RESET DAILY COUNTER
        // =====================================================

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
                "================================"
            );

            Print(
                "NEW DAY - TRADE COUNTER RESET"
            );

            Print(
                "================================"
            );
        }


        // =====================================================
        // STOP
        // =====================================================

        protected override void OnStop()
        {
            Timer.Stop();

            Print(
                "AI SCALPING M1 500 STOPPED"
            );
        }
    }
}