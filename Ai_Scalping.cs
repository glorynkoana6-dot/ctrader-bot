using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class US100MultiTradeBot : Robot
    {
        // =====================================================
        // SETTINGS
        // =====================================================

        [Parameter("Enable Real Trading", DefaultValue = false)]
        public bool EnableRealTrading { get; set; }

        [Parameter("Volume In Units", DefaultValue = 1000, MinValue = 1)]
        public double VolumeInUnits { get; set; }

        [Parameter("Maximum Open Trades", DefaultValue = 10, MinValue = 1)]
        public int MaximumOpenTrades { get; set; }

        [Parameter("Minimum Entry Spacing Seconds", DefaultValue = 30, MinValue = 5)]
        public int EntrySpacingSeconds { get; set; }

        [Parameter("Minimum Signal Score", DefaultValue = 65, MinValue = 1)]
        public int MinimumSignalScore { get; set; }

        [Parameter("Risk Reward", DefaultValue = 2.5, MinValue = 0.5)]
        public double RiskReward { get; set; }

        [Parameter("SL ATR Multiplier", DefaultValue = 1.2, MinValue = 0.1)]
        public double SlAtrMultiplier { get; set; }

        [Parameter("Maximum Spread Pips", DefaultValue = 100)]
        public double MaximumSpreadPips { get; set; }

        // =====================================================
        // TRAILING STOP
        // =====================================================

        [Parameter("Enable Trailing Stop", DefaultValue = true)]
        public bool EnableTrailingStop { get; set; }

        [Parameter("Breakeven Trigger R", DefaultValue = 1.0, MinValue = 0.1)]
        public double BreakevenTriggerR { get; set; }

        [Parameter("Trailing ATR Multiplier", DefaultValue = 1.0, MinValue = 0.1)]
        public double TrailingAtrMultiplier { get; set; }

        [Parameter("Breakeven Buffer Pips", DefaultValue = 2)]
        public double BreakevenBufferPips { get; set; }

        // =====================================================
        // STATE
        // =====================================================

        private const string Label = "US100_MULTI_AI";

        private Bars _m1;
        private Bars _m5;
        private Bars _m15;

        private DateTime _lastEntryTime = DateTime.MinValue;

        private readonly Dictionary<long, double> _initialRisk =
            new Dictionary<long, double>();

        // =====================================================
        // START
        // =====================================================

        protected override void OnStart()
        {
            string symbol =
                SymbolName
                .ToUpperInvariant()
                .Replace("/", "")
                .Replace("-", "")
                .Replace(".", "");

            bool valid =
                symbol.Contains("US100") ||
                symbol.Contains("NAS100") ||
                symbol.Contains("USTEC") ||
                symbol.Contains("NASDAQ");

            if (!valid)
            {
                Print("ERROR: Run this bot on US100 / NAS100 / USTEC only.");
                Stop();
                return;
            }

            _m1 = MarketData.GetBars(
                TimeFrame.Minute,
                SymbolName
            );

            _m5 = MarketData.GetBars(
                TimeFrame.Minute5,
                SymbolName
            );

            _m15 = MarketData.GetBars(
                TimeFrame.Minute15,
                SymbolName
            );

            Positions.Closed += OnPositionClosed;

            Timer.Start(1);

            Print("==========================================");
            Print("US100 MULTI-TRADE BOT STARTED");
            Print("MAX POSITIONS: {0}", MaximumOpenTrades);
            Print("R:R: 1:{0}", RiskReward);
            Print("TRAILING STOP: {0}", EnableTrailingStop);
            Print("REAL TRADING: {0}", EnableRealTrading);
            Print("==========================================");

            AnalyseAndTrade();
        }

        // =====================================================
        // TIMER
        // =====================================================

        protected override void OnTimer()
        {
            ManageTrailingStops();

            AnalyseAndTrade();
        }

        // =====================================================
        // MAIN ENGINE
        // =====================================================

        private void AnalyseAndTrade()
        {
            if (
                _m1.Count < 220 ||
                _m5.Count < 220 ||
                _m15.Count < 220
            )
                return;

            Position[] openPositions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );

            if (
                openPositions.Length >=
                MaximumOpenTrades
            )
                return;

            if (
                (Server.Time - _lastEntryTime)
                .TotalSeconds <
                EntrySpacingSeconds
            )
                return;

            double spreadPips =
                (
                    Symbol.Ask -
                    Symbol.Bid
                )
                /
                Symbol.PipSize;

            if (
                spreadPips >
                MaximumSpreadPips
            )
                return;

            Analysis m1 =
                Analyse(
                    _m1,
                    _m1.Count - 2
                );

            Analysis m5 =
                Analyse(
                    _m5,
                    _m5.Count - 2
                );

            Analysis m15 =
                Analyse(
                    _m15,
                    _m15.Count - 2
                );

            int buyScore = 0;
            int sellScore = 0;

            // =================================================
            // M15 TREND
            // =================================================

            if (m15.Trend == "BUY")
                buyScore += 25;

            else if (m15.Trend == "SELL")
                sellScore += 25;

            // =================================================
            // M5 TREND
            // =================================================

            if (m5.Trend == "BUY")
                buyScore += 20;

            else if (m5.Trend == "SELL")
                sellScore += 20;

            // =================================================
            // M1 ENTRY
            // =================================================

            if (m1.Trend == "BUY")
                buyScore += 15;

            else if (m1.Trend == "SELL")
                sellScore += 15;

            // =================================================
            // FULL ALIGNMENT
            // =================================================

            if (
                m1.Trend == "BUY" &&
                m5.Trend == "BUY" &&
                m15.Trend == "BUY"
            )
                buyScore += 15;

            if (
                m1.Trend == "SELL" &&
                m5.Trend == "SELL" &&
                m15.Trend == "SELL"
            )
                sellScore += 15;

            // =================================================
            // RSI
            // =================================================

            if (
                m1.Rsi >= 52 &&
                m1.Rsi <= 72
            )
                buyScore += 10;

            else if (
                m1.Rsi >= 28 &&
                m1.Rsi <= 48
            )
                sellScore += 10;

            // =================================================
            // MOMENTUM
            // =================================================

            if (m1.Momentum > 0)
                buyScore += 10;

            else if (m1.Momentum < 0)
                sellScore += 10;

            // =================================================
            // CANDLE DIRECTION
            // =================================================

            int index =
                _m1.Count - 2;

            if (
                _m1.ClosePrices[index] >
                _m1.OpenPrices[index]
            )
                buyScore += 5;

            else if (
                _m1.ClosePrices[index] <
                _m1.OpenPrices[index]
            )
                sellScore += 5;

            string signal =
                "WAIT";

            if (
                buyScore >= MinimumSignalScore &&
                buyScore > sellScore + 10
            )
                signal = "BUY";

            else if (
                sellScore >= MinimumSignalScore &&
                sellScore > buyScore + 10
            )
                signal = "SELL";

            Print(
                "US100 | BUY {0} | SELL {1} | SIGNAL {2} | OPEN {3}/{4}",
                buyScore,
                sellScore,
                signal,
                openPositions.Length,
                MaximumOpenTrades
            );

            if (signal == "WAIT")
                return;

            if (!EnableRealTrading)
            {
                Print(
                    "Signal found but real trading is OFF."
                );

                return;
            }

            OpenTrade(
                signal,
                m1.Atr
            );
        }

        // =====================================================
        // OPEN TRADE
        // =====================================================

        private void OpenTrade(
            string signal,
            double atr
        )
        {
            if (atr <= 0)
                return;

            TradeType type =
                signal == "BUY"
                ? TradeType.Buy
                : TradeType.Sell;

            double stopDistance =
                atr *
                SlAtrMultiplier;

            double stopPips =
                stopDistance /
                Symbol.PipSize;

            double takePips =
                stopPips *
                RiskReward;

            if (stopPips < 1)
                return;

            double volume =
                Symbol.NormalizeVolumeInUnits(
                    VolumeInUnits,
                    RoundingMode.Down
                );

            volume =
                Math.Max(
                    volume,
                    Symbol.VolumeInUnitsMin
                );

            volume =
                Math.Min(
                    volume,
                    Symbol.VolumeInUnitsMax
                );

            TradeResult result =
                ExecuteMarketOrder(
                    type,
                    SymbolName,
                    volume,
                    Label,
                    stopPips,
                    takePips
                );

            if (!result.IsSuccessful)
            {
                Print(
                    "ORDER FAILED: {0}",
                    result.Error
                );

                return;
            }

            _lastEntryTime =
                Server.Time;

            Position position =
                result.Position;

            _initialRisk[position.Id] =
                stopPips *
                Symbol.PipSize;

            Print("");
            Print("🔥 US100 TRADE OPENED");
            Print("TYPE: {0}", signal);
            Print("ENTRY: {0}", position.EntryPrice);
            Print("SL: {0:F1} pips", stopPips);
            Print("TP: {0:F1} pips", takePips);
            Print(
                "OPEN POSITIONS: {0}/{1}",
                Positions.FindAll(
                    Label,
                    SymbolName
                ).Length,
                MaximumOpenTrades
            );
            Print("");
        }

        // =====================================================
        // TRAILING STOP
        // =====================================================

        private void ManageTrailingStops()
        {
            if (!EnableTrailingStop)
                return;

            if (_m1.Count < 30)
                return;

            double atr =
                ATR(
                    _m1,
                    _m1.Count - 2,
                    14
                );

            if (atr <= 0)
                return;

            Position[] positions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );

            foreach (
                Position position
                in positions
            )
            {
                if (
                    !_initialRisk
                    .ContainsKey(
                        position.Id
                    )
                )
                {
                    if (
                        position.StopLoss
                        .HasValue
                    )
                    {
                        double risk =
                            Math.Abs(
                                position.EntryPrice -
                                position.StopLoss.Value
                            );

                        if (risk > 0)
                            _initialRisk[
                                position.Id
                            ] = risk;
                    }
                }

                if (
                    !_initialRisk
                    .ContainsKey(
                        position.Id
                    )
                )
                    continue;

                double initialRisk =
                    _initialRisk[
                        position.Id
                    ];

                double trigger =
                    initialRisk *
                    BreakevenTriggerR;

                double buffer =
                    BreakevenBufferPips *
                    Symbol.PipSize;

                // =================================================
                // BUY
                // =================================================

                if (
                    position.TradeType ==
                    TradeType.Buy
                )
                {
                    double profit =
                        Symbol.Bid -
                        position.EntryPrice;

                    if (
                        profit <
                        trigger
                    )
                        continue;

                    double breakeven =
                        position.EntryPrice +
                        buffer;

                    double atrStop =
                        Symbol.Bid -
                        atr *
                        TrailingAtrMultiplier;

                    double newStop =
                        Math.Max(
                            breakeven,
                            atrStop
                        );

                    if (
                        newStop >=
                        Symbol.Bid
                    )
                        continue;

                    if (
                        position.StopLoss
                        .HasValue &&
                        newStop <=
                        position.StopLoss.Value
                    )
                        continue;

                    TradeResult result =
                        ModifyPosition(
                            position,
                            newStop,
                            position.TakeProfit
                        );

                    if (
                        result.IsSuccessful
                    )
                    {
                        Print(
                            "🔒 BUY SL TRAILED TO {0}",
                            newStop
                        );
                    }
                }

                // =================================================
                // SELL
                // =================================================

                else
                {
                    double profit =
                        position.EntryPrice -
                        Symbol.Ask;

                    if (
                        profit <
                        trigger
                    )
                        continue;

                    double breakeven =
                        position.EntryPrice -
                        buffer;

                    double atrStop =
                        Symbol.Ask +
                        atr *
                        TrailingAtrMultiplier;

                    double newStop =
                        Math.Min(
                            breakeven,
                            atrStop
                        );

                    if (
                        newStop <=
                        Symbol.Ask
                    )
                        continue;

                    if (
                        position.StopLoss
                        .HasValue &&
                        newStop >=
                        position.StopLoss.Value
                    )
                        continue;

                    TradeResult result =
                        ModifyPosition(
                            position,
                            newStop,
                            position.TakeProfit
                        );

                    if (
                        result.IsSuccessful
                    )
                    {
                        Print(
                            "🔒 SELL SL TRAILED TO {0}",
                            newStop
                        );
                    }
                }
            }
        }

        // =====================================================
        // ANALYSIS
        // =====================================================

        private Analysis Analyse(
            Bars bars,
            int index
        )
        {
            double[] closes =
                GetCloses(
                    bars,
                    index,
                    220
                );

            double ema20 =
                EMA(
                    closes,
                    20
                );

            double ema50 =
                EMA(
                    closes,
                    50
                );

            double ema200 =
                EMA(
                    closes,
                    200
                );

            double rsi =
                RSI(
                    closes,
                    14
                );

            double momentum =
                Momentum(
                    closes,
                    10
                );

            double atr =
                ATR(
                    bars,
                    index,
                    14
                );

            double price =
                closes[
                    closes.Length - 1
                ];

            int bullish = 0;
            int bearish = 0;

            if (
                ema20 >
                ema50
            )
                bullish += 2;
            else
                bearish += 2;

            if (
                ema50 >
                ema200
            )
                bullish += 3;
            else
                bearish += 3;

            if (
                price >
                ema200
            )
                bullish += 2;
            else
                bearish += 2;

            if (
                momentum >
                0
            )
                bullish += 1;
            else
                bearish += 1;

            if (
                rsi >
                50
            )
                bullish += 1;
            else
                bearish += 1;

            string trend;

            if (
                bullish >=
                bearish + 2
            )
                trend =
                    "BUY";

            else if (
                bearish >=
                bullish + 2
            )
                trend =
                    "SELL";

            else
                trend =
                    "NEUTRAL";

            return new Analysis
            {
                Trend =
                    trend,

                Rsi =
                    rsi,

                Momentum =
                    momentum,

                Atr =
                    atr
            };
        }

        // =====================================================
        // EMA
        // =====================================================

        private double EMA(
            double[] values,
            int period
        )
        {
            if (
                values.Length <
                period
            )
                return values[
                    values.Length - 1
                ];

            double ema =
                values
                .Take(period)
                .Average();

            double multiplier =
                2.0 /
                (
                    period +
                    1
                );

            for (
                int i = period;
                i < values.Length;
                i++
            )
            {
                ema =
                    (
                        values[i] -
                        ema
                    )
                    *
                    multiplier
                    +
                    ema;
            }

            return ema;
        }

        // =====================================================
        // RSI
        // =====================================================

        private double RSI(
            double[] closes,
            int period
        )
        {
            if (
                closes.Length <
                period + 1
            )
                return 50;

            double gain = 0;
            double loss = 0;

            for (
                int i = 1;
                i <= period;
                i++
            )
            {
                double change =
                    closes[i] -
                    closes[i - 1];

                if (change > 0)
                    gain += change;
                else
                    loss += -change;
            }

            gain /= period;
            loss /= period;

            for (
                int i =
                    period + 1;
                i < closes.Length;
                i++
            )
            {
                double change =
                    closes[i] -
                    closes[i - 1];

                double currentGain =
                    Math.Max(
                        change,
                        0
                    );

                double currentLoss =
                    Math.Max(
                        -change,
                        0
                    );

                gain =
                    (
                        gain *
                        (
                            period -
                            1
                        )
                        +
                        currentGain
                    )
                    /
                    period;

                loss =
                    (
                        loss *
                        (
                            period -
                            1
                        )
                        +
                        currentLoss
                    )
                    /
                    period;
            }

            if (loss == 0)
                return 100;

            double rs =
                gain /
                loss;

            return
                100 -
                (
                    100 /
                    (
                        1 +
                        rs
                    )
                );
        }

        // =====================================================
        // MOMENTUM
        // =====================================================

        private double Momentum(
            double[] closes,
            int period
        )
        {
            if (
                closes.Length <=
                period
            )
                return 0;

            double old =
                closes[
                    closes.Length -
                    period -
                    1
                ];

            double current =
                closes[
                    closes.Length -
                    1
                ];

            if (old == 0)
                return 0;

            return
                (
                    (
                        current -
                        old
                    )
                    /
                    old
                )
                *
                100;
        }

        // =====================================================
        // ATR
        // =====================================================

        private double ATR(
            Bars bars,
            int index,
            int period
        )
        {
            List<double> ranges =
                new List<double>();

            int start =
                Math.Max(
                    1,
                    index -
                    period *
                    4
                );

            for (
                int i = start;
                i <= index;
                i++
            )
            {
                double highLow =
                    bars.HighPrices[i] -
                    bars.LowPrices[i];

                double highClose =
                    Math.Abs(
                        bars.HighPrices[i] -
                        bars.ClosePrices[
                            i - 1
                        ]
                    );

                double lowClose =
                    Math.Abs(
                        bars.LowPrices[i] -
                        bars.ClosePrices[
                            i - 1
                        ]
                    );

                ranges.Add(
                    Math.Max(
                        highLow,
                        Math.Max(
                            highClose,
                            lowClose
                        )
                    )
                );
            }

            if (
                ranges.Count <
                period
            )
                return 0;

            double atr =
                ranges
                .Take(period)
                .Average();

            for (
                int i = period;
                i < ranges.Count;
                i++
            )
            {
                atr =
                    (
                        atr *
                        (
                            period -
                            1
                        )
                        +
                        ranges[i]
                    )
                    /
                    period;
            }

            return atr;
        }

        // =====================================================
        // CLOSE DATA
        // =====================================================

        private double[] GetCloses(
            Bars bars,
            int index,
            int amount
        )
        {
            int start =
                Math.Max(
                    0,
                    index -
                    amount +
                    1
                );

            List<double> data =
                new List<double>();

            for (
                int i = start;
                i <= index;
                i++
            )
            {
                data.Add(
                    bars.ClosePrices[i]
                );
            }

            return data.ToArray();
        }

        // =====================================================
        // POSITION CLOSED
        // =====================================================

        private void OnPositionClosed(
            PositionClosedEventArgs args
        )
        {
            if (
                _initialRisk
                .ContainsKey(
                    args.Position.Id
                )
            )
            {
                _initialRisk.Remove(
                    args.Position.Id
                );
            }
        }

        // =====================================================
        // STOP
        // =====================================================

        protected override void OnStop()
        {
            Timer.Stop();

            Positions.Closed -=
                OnPositionClosed;
        }

        // =====================================================
        // DATA
        // =====================================================

        private class Analysis
        {
            public string Trend;
            public double Rsi;
            public double Momentum;
            public double Atr;
        }
    }
}