using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class XAUUSD_MTF_Analysis_Bot : Robot
    {
        // =====================================================
        // SETTINGS
        // =====================================================

        [Parameter("Enable Real Trading", DefaultValue = false)]
        public bool EnableRealTrading { get; set; }

        [Parameter("Volume In Units", DefaultValue = 1000, MinValue = 1)]
        public double VolumeInUnits { get; set; }

        [Parameter("Risk Reward", DefaultValue = 3.0, MinValue = 0.5)]
        public double RiskReward { get; set; }

        [Parameter("Minimum Signal Score", DefaultValue = 70)]
        public int MinimumSignalScore { get; set; }

        [Parameter("Ranging Minimum Score", DefaultValue = 80)]
        public int RangingMinScore { get; set; }

        [Parameter("SL ATR Multiplier", DefaultValue = 1.0)]
        public double SlAtrMultiplier { get; set; }

        [Parameter("Maximum Spread Pips", DefaultValue = 50)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("One Position At A Time", DefaultValue = true)]
        public bool OnePositionAtATime { get; set; }

        [Parameter("Analyse Immediately", DefaultValue = true)]
        public bool AnalyseImmediately { get; set; }


        // =====================================================
        // TRAILING STOP SETTINGS
        // =====================================================

        [Parameter("Enable Trailing Stop", DefaultValue = true)]
        public bool EnableTrailingStop { get; set; }

        [Parameter("Breakeven Trigger R", DefaultValue = 1.0, MinValue = 0.1)]
        public double BreakevenTriggerR { get; set; }

        [Parameter("Trailing ATR Multiplier", DefaultValue = 1.0, MinValue = 0.1)]
        public double TrailingAtrMultiplier { get; set; }

        [Parameter("Breakeven Extra Pips", DefaultValue = 2.0, MinValue = 0)]
        public double BreakevenExtraPips { get; set; }


        // =====================================================
        // STATE
        // =====================================================

        private const string Label = "XAUUSD_MTF_AI";

        private Bars _m5;
        private Bars _m15;
        private Bars _h1;

        private DateTime _lastProcessedCandle = DateTime.MinValue;
        private DateTime _lastTradeCandle = DateTime.MinValue;

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
                .Replace("-", "");

            if (!symbol.Contains("XAU") || !symbol.Contains("USD"))
            {
                Print("ERROR: This bot is for XAUUSD / GOLD only.");

                Stop();

                return;
            }

            _m5 =
                MarketData.GetBars(
                    TimeFrame.Minute5,
                    SymbolName
                );

            _m15 =
                MarketData.GetBars(
                    TimeFrame.Minute15,
                    SymbolName
                );

            _h1 =
                MarketData.GetBars(
                    TimeFrame.Hour,
                    SymbolName
                );

            Positions.Closed += OnPositionClosed;

            RestoreInitialRisk();

            Timer.Start(5);

            Print("==================================================");
            Print("XAUUSD MULTI TIMEFRAME ROBOT");
            Print("5M ENTRY");
            Print("15M CONFIRMATION");
            Print("1H MAIN TREND");
            Print("RISK / REWARD = 1:{0}", RiskReward);

            Print(
                "REAL TRADING = {0}",
                EnableRealTrading ? "ON" : "OFF"
            );

            Print(
                "TRAILING STOP = {0}",
                EnableTrailingStop ? "ON" : "OFF"
            );

            Print(
                "BREAKEVEN AT +{0:F1}R",
                BreakevenTriggerR
            );

            Print(
                "TRAIL = {0:F1} ATR",
                TrailingAtrMultiplier
            );

            Print("==================================================");

            if (AnalyseImmediately)
                AnalyseMarket(true);
        }


        // =====================================================
        // TIMER
        // =====================================================

        protected override void OnTimer()
        {
            ManageTrailingStops();

            AnalyseMarket(false);
        }


        // =====================================================
        // RESTORE EXISTING POSITION RISK
        // =====================================================

        private void RestoreInitialRisk()
        {
            Position[] positions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );

            foreach (Position position in positions)
            {
                if (_initialRisk.ContainsKey(position.Id))
                    continue;

                if (!position.StopLoss.HasValue)
                    continue;

                double risk =
                    Math.Abs(
                        position.EntryPrice -
                        position.StopLoss.Value
                    );

                if (risk > 0)
                    _initialRisk[position.Id] = risk;
            }
        }


        // =====================================================
        // POSITION CLOSED
        // =====================================================

        private void OnPositionClosed(
            PositionClosedEventArgs args
        )
        {
            Position position =
                args.Position;

            if (
                position.Label != Label ||
                position.SymbolName != SymbolName
            )
                return;

            if (_initialRisk.ContainsKey(position.Id))
                _initialRisk.Remove(position.Id);
        }


        // =====================================================
        // TRAILING STOP
        // =====================================================

        private void ManageTrailingStops()
        {
            if (!EnableTrailingStop)
                return;

            if (_m5 == null || _m5.Count < 30)
                return;

            int index =
                _m5.Count - 2;

            double atr =
                ATR(
                    _m5,
                    index,
                    14
                );

            if (atr <= 0)
                return;

            Position[] positions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );

            foreach (Position position in positions)
            {
                if (!_initialRisk.ContainsKey(position.Id))
                {
                    if (position.StopLoss.HasValue)
                    {
                        double risk =
                            Math.Abs(
                                position.EntryPrice -
                                position.StopLoss.Value
                            );

                        if (risk > 0)
                            _initialRisk[position.Id] = risk;
                    }
                }

                if (!_initialRisk.ContainsKey(position.Id))
                    continue;

                double initialRisk =
                    _initialRisk[position.Id];

                if (initialRisk <= 0)
                    continue;


                // =============================================
                // BUY
                // =============================================

                if (position.TradeType == TradeType.Buy)
                {
                    double currentPrice =
                        Symbol.Bid;

                    double profitDistance =
                        currentPrice -
                        position.EntryPrice;

                    double triggerDistance =
                        initialRisk *
                        BreakevenTriggerR;

                    if (profitDistance < triggerDistance)
                        continue;


                    // Breakeven + small profit

                    double breakeven =
                        position.EntryPrice +
                        (
                            BreakevenExtraPips *
                            Symbol.PipSize
                        );


                    // ATR trailing level

                    double trailingStop =
                        currentPrice -
                        (
                            atr *
                            TrailingAtrMultiplier
                        );


                    // Never put trailing stop below breakeven

                    double newStop =
                        Math.Max(
                            breakeven,
                            trailingStop
                        );


                    // Stop must remain below live Bid

                    if (newStop >= currentPrice)
                        continue;


                    bool shouldMove =
                        !position.StopLoss.HasValue ||
                        newStop >
                        position.StopLoss.Value;


                    if (!shouldMove)
                        continue;


                    TradeResult result =
                        ModifyPosition(
                            position,
                            newStop,
                            position.TakeProfit
                        );


                    if (result.IsSuccessful)
                    {
                        Print(
                            "🔒 BUY SL MOVED | New SL: {0}",
                            newStop
                        );
                    }
                    else
                    {
                        Print(
                            "BUY trailing SL error: {0}",
                            result.Error
                        );
                    }
                }


                // =============================================
                // SELL
                // =============================================

                else if (
                    position.TradeType ==
                    TradeType.Sell
                )
                {
                    double currentPrice =
                        Symbol.Ask;

                    double profitDistance =
                        position.EntryPrice -
                        currentPrice;

                    double triggerDistance =
                        initialRisk *
                        BreakevenTriggerR;

                    if (profitDistance < triggerDistance)
                        continue;


                    // Breakeven + small profit

                    double breakeven =
                        position.EntryPrice -
                        (
                            BreakevenExtraPips *
                            Symbol.PipSize
                        );


                    // ATR trailing stop

                    double trailingStop =
                        currentPrice +
                        (
                            atr *
                            TrailingAtrMultiplier
                        );


                    // Never move above breakeven

                    double newStop =
                        Math.Min(
                            breakeven,
                            trailingStop
                        );


                    if (newStop <= currentPrice)
                        continue;


                    bool shouldMove =
                        !position.StopLoss.HasValue ||
                        newStop <
                        position.StopLoss.Value;


                    if (!shouldMove)
                        continue;


                    TradeResult result =
                        ModifyPosition(
                            position,
                            newStop,
                            position.TakeProfit
                        );


                    if (result.IsSuccessful)
                    {
                        Print(
                            "🔒 SELL SL MOVED | New SL: {0}",
                            newStop
                        );
                    }
                    else
                    {
                        Print(
                            "SELL trailing SL error: {0}",
                            result.Error
                        );
                    }
                }
            }
        }


        // =====================================================
        // MAIN MARKET ANALYSIS
        // =====================================================

        private void AnalyseMarket(bool force)
        {
            int m5Index =
                _m5.Count - 2;

            int m15Index =
                _m15.Count - 2;

            int h1Index =
                _h1.Count - 2;


            if (
                m5Index < 210 ||
                m15Index < 210 ||
                h1Index < 210
            )
            {
                Print(
                    "Waiting for enough historical data..."
                );

                return;
            }


            DateTime candleTime =
                _m5.OpenTimes[m5Index];


            if (
                !force &&
                candleTime ==
                _lastProcessedCandle
            )
                return;


            _lastProcessedCandle =
                candleTime;


            TfAnalysis entry =
                AnalyseTimeframe(
                    _m5,
                    m5Index
                );


            TfAnalysis confirmation =
                AnalyseTimeframe(
                    _m15,
                    m15Index
                );


            TfAnalysis trend =
                AnalyseTimeframe(
                    _h1,
                    h1Index
                );


            SignalData signal =
                CreateSignal(
                    entry,
                    confirmation,
                    trend
                );


            TradeLevels levels =
                CalculateTradeLevels(
                    signal,
                    entry
                );


            PrintReport(
                entry,
                confirmation,
                trend,
                signal,
                levels
            );


            if (
                signal.Signal ==
                "WAIT"
            )
                return;


            if (!EnableRealTrading)
            {
                Print(
                    "SIGNAL MODE: Real order not sent."
                );

                return;
            }


            if (
                _lastTradeCandle ==
                candleTime
            )
                return;


            if (
                OnePositionAtATime &&
                Positions.FindAll(
                    Label,
                    SymbolName
                ).Length > 0
            )
            {
                Print(
                    "Existing position already open."
                );

                return;
            }


            double spread =
                (
                    Symbol.Ask -
                    Symbol.Bid
                )
                /
                Symbol.PipSize;


            if (
                spread >
                MaximumSpreadPips
            )
            {
                Print(
                    "Spread too high: {0:F1} pips",
                    spread
                );

                return;
            }


            ExecuteSignal(
                signal,
                levels,
                candleTime
            );
        }


        // =====================================================
        // TIMEFRAME ANALYSIS
        // =====================================================

        private TfAnalysis AnalyseTimeframe(
            Bars bars,
            int index
        )
        {
            double[] closes =
                GetCloses(
                    bars,
                    index,
                    260
                );


            double current =
                closes[
                    closes.Length - 1
                ];


            double ema9 =
                EMA(
                    closes,
                    9
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


            double macdHistogram =
                MACDHistogram(
                    closes
                );


            double atr =
                ATR(
                    bars,
                    index,
                    14
                );


            double momentum =
                Momentum(
                    closes,
                    10
                );


            double volatility =
                Volatility(
                    closes,
                    20
                );


            double vwap =
                VWAP(
                    bars,
                    index,
                    120
                );


            double relativeVolume =
                RelativeVolume(
                    bars,
                    index,
                    20
                );


            StructureResult structure =
                AnalyseStructure(
                    bars,
                    index
                );


            string breakout =
                AnalyseBreakout(
                    bars,
                    index,
                    20
                );


            string falseBreakout =
                FalseBreakout(
                    bars,
                    index,
                    20
                );


            string candlePattern =
                CandlePattern(
                    bars,
                    index
                );


            int bull =
                0;

            int bear =
                0;


            // EMA 9 / EMA 20

            if (ema9 > ema20)
                bull += 1;

            else
                bear += 1;


            // EMA 20 / EMA 50

            if (ema20 > ema50)
                bull += 2;

            else
                bear += 2;


            // EMA 50 / EMA 200

            if (ema50 > ema200)
                bull += 3;

            else
                bear += 3;


            // Price vs EMA 200

            if (current > ema200)
                bull += 2;

            else
                bear += 2;


            // Structure

            if (
                structure.Trend ==
                "BULLISH"
            )
                bull += 2;


            else if (
                structure.Trend ==
                "BEARISH"
            )
                bear += 2;


            // MACD

            if (
                macdHistogram > 0
            )
                bull += 2;

            else if (
                macdHistogram < 0
            )
                bear += 2;


            // Momentum

            if (
                momentum > 0
            )
                bull += 1;

            else if (
                momentum < 0
            )
                bear += 1;


            // VWAP

            if (vwap > 0)
            {
                if (
                    current > vwap
                )
                    bull += 1;

                else
                    bear += 1;
            }


            string direction;


            if (
                bull >=
                bear + 3
            )
                direction =
                    "BULLISH";


            else if (
                bear >=
                bull + 3
            )
                direction =
                    "BEARISH";


            else
                direction =
                    "NEUTRAL";


            string regime;


            if (
                volatility < 0.03
            )
                regime =
                    "RANGING";


            else if (
                volatility > 0.30
            )
                regime =
                    "HIGH VOLATILITY";


            else if (
                breakout !=
                "NONE"
            )
                regime =
                    "BREAKOUT";


            else
                regime =
                    direction +
                    " TREND";


            return new TfAnalysis
            {
                Price =
                    current,

                Ema9 =
                    ema9,

                Ema20 =
                    ema20,

                Ema50 =
                    ema50,

                Ema200 =
                    ema200,

                Rsi =
                    rsi,

                MacdHistogram =
                    macdHistogram,

                Atr =
                    atr,

                Momentum =
                    momentum,

                Volatility =
                    volatility,

                Vwap =
                    vwap,

                RelativeVolume =
                    relativeVolume,

                Structure =
                    structure,

                Breakout =
                    breakout,

                FalseBreakout =
                    falseBreakout,

                CandlePattern =
                    candlePattern,

                Trend =
                    direction,

                Regime =
                    regime,

                BullPoints =
                    bull,

                BearPoints =
                    bear
            };
        }


        // =====================================================
        // SIGNAL ENGINE
        // =====================================================

        private SignalData CreateSignal(
            TfAnalysis entry,
            TfAnalysis confirm,
            TfAnalysis trend
        )
        {
            int buy =
                0;

            int sell =
                0;


            // 1H trend

            if (
                trend.Trend ==
                "BULLISH"
            )
                buy += 20;


            else if (
                trend.Trend ==
                "BEARISH"
            )
                sell += 20;


            // 15M confirmation

            if (
                confirm.Trend ==
                "BULLISH"
            )
                buy += 15;


            else if (
                confirm.Trend ==
                "BEARISH"
            )
                sell += 15;


            // 5M entry trend

            if (
                entry.Trend ==
                "BULLISH"
            )
                buy += 15;


            else if (
                entry.Trend ==
                "BEARISH"
            )
                sell += 15;


            // Full bullish alignment

            if (
                trend.Trend == "BULLISH" &&
                confirm.Trend == "BULLISH" &&
                entry.Trend == "BULLISH"
            )
                buy += 10;


            // Full bearish alignment

            if (
                trend.Trend == "BEARISH" &&
                confirm.Trend == "BEARISH" &&
                entry.Trend == "BEARISH"
            )
                sell += 10;


            // Market structure

            if (
                entry.Structure.Trend ==
                "BULLISH"
            )
                buy += 10;


            else if (
                entry.Structure.Trend ==
                "BEARISH"
            )
                sell += 10;


            // Break of structure

            if (
                entry.Structure.Bos ==
                "BULLISH BOS"
            )
                buy += 8;


            else if (
                entry.Structure.Bos ==
                "BEARISH BOS"
            )
                sell += 8;


            // Breakout

            if (
                entry.Breakout ==
                "BULLISH BREAKOUT"
            )
                buy += 12;


            else if (
                entry.Breakout ==
                "BEARISH BREAKOUT"
            )
                sell += 12;


            // False breakout penalty

            if (
                entry.FalseBreakout ==
                "BULLISH TRAP"
            )
                buy -= 15;


            else if (
                entry.FalseBreakout ==
                "BEARISH TRAP"
            )
                sell -= 15;


            // RSI

            if (
                entry.Rsi >= 52 &&
                entry.Rsi <= 70
            )
                buy += 8;


            else if (
                entry.Rsi >= 30 &&
                entry.Rsi <= 48
            )
                sell += 8;


            else if (
                entry.Rsi > 75
            )
                sell += 4;


            else if (
                entry.Rsi < 25
            )
                buy += 4;


            // MACD

            if (
                entry.MacdHistogram > 0
            )
                buy += 8;


            else if (
                entry.MacdHistogram < 0
            )
                sell += 8;


            // VWAP

            if (
                entry.Vwap > 0
            )
            {
                if (
                    entry.Price >
                    entry.Vwap
                )
                    buy += 5;


                else
                    sell += 5;
            }


            // Momentum

            if (
                entry.Momentum > 0
            )
                buy += 5;


            else if (
                entry.Momentum < 0
            )
                sell += 5;


            // Candlestick pattern

            if (
                entry.CandlePattern ==
                    "BULLISH ENGULFING" ||
                entry.CandlePattern ==
                    "BULLISH REJECTION"
            )
                buy += 5;


            else if (
                entry.CandlePattern ==
                    "BEARISH ENGULFING" ||
                entry.CandlePattern ==
                    "BEARISH REJECTION"
            )
                sell += 5;


            // Volume confirmation

            if (
                entry.RelativeVolume >=
                1.20
            )
            {
                int completed =
                    _m5.Count - 2;


                if (
                    _m5.ClosePrices[completed] >
                    _m5.OpenPrices[completed]
                )
                    buy += 7;


                else if (
                    _m5.ClosePrices[completed] <
                    _m5.OpenPrices[completed]
                )
                    sell += 7;
            }


            int required =
                MinimumSignalScore;


            if (
                entry.Regime ==
                "RANGING"
            )
                required =
                    RangingMinScore;


            string signal =
                "WAIT";


            if (
                buy >= required &&
                buy >
                sell + 10
            )
                signal =
                    "BUY";


            else if (
                sell >= required &&
                sell >
                buy + 10
            )
                signal =
                    "SELL";


            // ATR must exist

            if (
                entry.Atr <= 0
            )
                signal =
                    "WAIT";


            // Block false breakout

            if (
                entry.FalseBreakout !=
                "NONE"
            )
                signal =
                    "WAIT";


            return new SignalData
            {
                Signal =
                    signal,

                BuyScore =
                    buy,

                SellScore =
                    sell,

                RequiredScore =
                    required,

                Confidence =
                    Math.Min(
                        99,
                        Math.Max(
                            buy,
                            sell
                        )
                    )
            };
        }


        // =====================================================
        // CALCULATE TRADE LEVELS
        // =====================================================

        private TradeLevels CalculateTradeLevels(
            SignalData signal,
            TfAnalysis entry
        )
        {
            if (
                signal.Signal ==
                "WAIT"
            )
                return new TradeLevels();


            double risk =
                entry.Atr *
                SlAtrMultiplier;


            if (
                risk <= 0
            )
                return new TradeLevels();


            double entryPrice =
                entry.Price;


            double stopLoss;

            double takeProfit;


            // BUY

            if (
                signal.Signal ==
                "BUY"
            )
            {
                stopLoss =
                    entryPrice -
                    risk;


                if (
                    entry.Structure.LastLow.HasValue &&
                    entry.Structure.LastLow.Value <
                    entryPrice
                )
                {
                    double structureStop =
                        entry.Structure.LastLow.Value
                        -
                        entry.Atr *
                        0.15;


                    stopLoss =
                        Math.Min(
                            stopLoss,
                            structureStop
                        );
                }


                risk =
                    entryPrice -
                    stopLoss;


                takeProfit =
                    entryPrice +
                    risk *
                    RiskReward;
            }


            // SELL

            else
            {
                stopLoss =
                    entryPrice +
                    risk;


                if (
                    entry.Structure.LastHigh.HasValue &&
                    entry.Structure.LastHigh.Value >
                    entryPrice
                )
                {
                    double structureStop =
                        entry.Structure.LastHigh.Value
                        +
                        entry.Atr *
                        0.15;


                    stopLoss =
                        Math.Max(
                            stopLoss,
                            structureStop
                        );
                }


                risk =
                    stopLoss -
                    entryPrice;


                takeProfit =
                    entryPrice -
                    risk *
                    RiskReward;
            }


            return new TradeLevels
            {
                Entry =
                    entryPrice,

                StopLoss =
                    stopLoss,

                TakeProfit =
                    takeProfit,

                Tp1 =
                    signal.Signal == "BUY"
                    ?
                    entryPrice + risk
                    :
                    entryPrice - risk,

                Tp2 =
                    signal.Signal == "BUY"
                    ?
                    entryPrice + risk * 2
                    :
                    entryPrice - risk * 2,

                RiskDistance =
                    risk
            };
        }


        // =====================================================
        // EXECUTE SIGNAL
        // =====================================================

        private void ExecuteSignal(
            SignalData signal,
            TradeLevels levels,
            DateTime candleTime
        )
        {
            if (
                !levels.StopLoss.HasValue ||
                !levels.TakeProfit.HasValue
            )
                return;


            TradeType type =
                signal.Signal ==
                "BUY"
                ?
                TradeType.Buy
                :
                TradeType.Sell;


            double liveEntry =
                type ==
                TradeType.Buy
                ?
                Symbol.Ask
                :
                Symbol.Bid;


            double slDistance =
                Math.Abs(
                    liveEntry -
                    levels.StopLoss.Value
                );


            if (
                slDistance <= 0
            )
                return;


            double stopPips =
                Math.Max(
                    slDistance /
                    Symbol.PipSize,
                    1
                );


            double takePips =
                Math.Max(
                    stopPips *
                    RiskReward,
                    1
                );


            double volume =
                Symbol
                .NormalizeVolumeInUnits(
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


            if (
                result.IsSuccessful
            )
            {
                _lastTradeCandle =
                    candleTime;


                Position position =
                    result.Position;


                _initialRisk[position.Id] =
                    stopPips *
                    Symbol.PipSize;


                Print("");

                Print(
                    "🔥 TRADE EXECUTED"
                );


                Print(
                    "TYPE: {0}",
                    signal.Signal
                );


                Print(
                    "ENTRY: {0}",
                    position.EntryPrice
                );


                Print(
                    "SL: {0:F1} pips",
                    stopPips
                );


                Print(
                    "TP: {0:F1} pips",
                    takePips
                );


                Print(
                    "BREAKEVEN STARTS AT: +{0:F1}R",
                    BreakevenTriggerR
                );


                Print(
                    "TRAILING DISTANCE: {0:F1} ATR",
                    TrailingAtrMultiplier
                );


                Print("");
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
        // REPORT
        // =====================================================

        private void PrintReport(
            TfAnalysis entry,
            TfAnalysis confirm,
            TfAnalysis trend,
            SignalData signal,
            TradeLevels levels
        )
        {
            Print("");

            Print(
                "=================================================="
            );

            Print(
                "🤖 XAUUSD MARKET ANALYSIS"
            );

            Print(
                "=================================================="
            );


            Print(
                "PRICE: {0}",
                entry.Price
            );


            Print("");


            Print(
                "5M TREND: {0}",
                entry.Trend
            );


            Print(
                "15M TREND: {0}",
                confirm.Trend
            );


            Print(
                "1H TREND: {0}",
                trend.Trend
            );


            Print("");


            Print(
                "REGIME: {0}",
                entry.Regime
            );


            Print(
                "RSI: {0:F2}",
                entry.Rsi
            );


            Print(
                "MACD HIST: {0:F4}",
                entry.MacdHistogram
            );


            Print(
                "ATR: {0:F2}",
                entry.Atr
            );


            Print(
                "MOMENTUM: {0:F3}%",
                entry.Momentum
            );


            Print(
                "VWAP: {0:F2}",
                entry.Vwap
            );


            Print(
                "RELATIVE VOLUME: {0:F2}x",
                entry.RelativeVolume
            );


            Print("");


            Print(
                "STRUCTURE: {0}",
                entry.Structure.Trend
            );


            Print(
                "BOS: {0}",
                entry.Structure.Bos
            );


            Print(
                "BREAKOUT: {0}",
                entry.Breakout
            );


            Print(
                "FALSE BREAKOUT: {0}",
                entry.FalseBreakout
            );


            Print(
                "CANDLE: {0}",
                entry.CandlePattern
            );


            Print("");


            Print(
                "BUY SCORE: {0}",
                signal.BuyScore
            );


            Print(
                "SELL SCORE: {0}",
                signal.SellScore
            );


            Print(
                "REQUIRED SCORE: {0}",
                signal.RequiredScore
            );


            Print(
                "CONFIDENCE: {0}%",
                signal.Confidence
            );


            Print("");


            Print(
                "🎯 SIGNAL: {0}",
                signal.Signal
            );


            if (
                levels.Entry.HasValue
            )
            {
                Print(
                    "ENTRY: {0:F2}",
                    levels.Entry.Value
                );


                Print(
                    "SL: {0:F2}",
                    levels.StopLoss.Value
                );


                Print(
                    "TP1 (+1R): {0:F2}",
                    levels.Tp1.Value
                );


                Print(
                    "TP2 (+2R): {0:F2}",
                    levels.Tp2.Value
                );


                Print(
                    "FINAL TP (+{0}R): {1:F2}",
                    RiskReward,
                    levels.TakeProfit.Value
                );


                Print(
                    "TRAIL START: +{0:F1}R",
                    BreakevenTriggerR
                );


                Print(
                    "TRAIL DISTANCE: {0:F1} ATR",
                    TrailingAtrMultiplier
                );
            }


            Print(
                "=================================================="
            );

            Print("");
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


            double value =
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
                value =
                    (
                        values[i] -
                        value
                    )
                    *
                    multiplier
                    +
                    value;
            }


            return value;
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


            double gain =
                0;

            double loss =
                0;


            for (
                int i = 1;
                i <= period;
                i++
            )
            {
                double change =
                    closes[i] -
                    closes[i - 1];


                if (
                    change > 0
                )
                    gain +=
                        change;


                else
                    loss +=
                        -change;
            }


            gain /=
                period;

            loss /=
                period;


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


            if (
                loss == 0
            )
                return 100;


            double rs =
                gain /
                loss;


            return 100 -
                (
                    100 /
                    (
                        1 +
                        rs
                    )
                );
        }


        // =====================================================
        // MACD
        // =====================================================

        private double MACDHistogram(
            double[] closes
        )
        {
            if (
                closes.Length <
                40
            )
                return 0;


            List<double> macd =
                new List<double>();


            for (
                int i = 26;
                i < closes.Length;
                i++
            )
            {
                double[] section =
                    closes
                    .Take(
                        i + 1
                    )
                    .ToArray();


                macd.Add(
                    EMA(
                        section,
                        12
                    )
                    -
                    EMA(
                        section,
                        26
                    )
                );
            }


            if (
                macd.Count <
                9
            )
                return 0;


            double signal =
                EMA(
                    macd.ToArray(),
                    9
                );


            return
                macd[
                    macd.Count - 1
                ]
                -
                signal;
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
            if (
                index <
                period + 1
            )
                return 0;


            List<double> tr =
                new List<double>();


            int start =
                Math.Max(
                    1,
                    index -
                    period * 4
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


                tr.Add(
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
                tr.Count <
                period
            )
                return 0;


            double atr =
                tr
                .Take(period)
                .Average();


            for (
                int i = period;
                i < tr.Count;
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
                        tr[i]
                    )
                    /
                    period;
            }


            return atr;
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


            if (
                old == 0
            )
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
        // VOLATILITY
        // =====================================================

        private double Volatility(
            double[] closes,
            int period
        )
        {
            if (
                closes.Length <
                period + 1
            )
                return 0;


            List<double> returns =
                new List<double>();


            for (
                int i =
                    closes.Length -
                    period;
                i < closes.Length;
                i++
            )
            {
                double previous =
                    closes[
                        i - 1
                    ];


                if (
                    previous == 0
                )
                    continue;


                returns.Add(
                    (
                        (
                            closes[i] -
                            previous
                        )
                        /
                        previous
                    )
                    *
                    100
                );
            }


            if (
                returns.Count < 2
            )
                return 0;


            double average =
                returns.Average();


            double variance =
                returns
                .Select(
                    x =>
                    Math.Pow(
                        x -
                        average,
                        2
                    )
                )
                .Average();


            return
                Math.Sqrt(
                    variance
                );
        }


        // =====================================================
        // VWAP
        // =====================================================

        private double VWAP(
            Bars bars,
            int index,
            int lookback
        )
        {
            int start =
                Math.Max(
                    0,
                    index -
                    lookback +
                    1
                );


            double total =
                0;

            double volumeTotal =
                0;


            for (
                int i = start;
                i <= index;
                i++
            )
            {
                double volume =
                    bars.TickVolumes[i];


                double typical =
                    (
                        bars.HighPrices[i] +
                        bars.LowPrices[i] +
                        bars.ClosePrices[i]
                    )
                    /
                    3.0;


                total +=
                    typical *
                    volume;


                volumeTotal +=
                    volume;
            }


            if (
                volumeTotal <= 0
            )
                return 0;


            return
                total /
                volumeTotal;
        }


        // =====================================================
        // RELATIVE VOLUME
        // =====================================================

        private double RelativeVolume(
            Bars bars,
            int index,
            int period
        )
        {
            if (
                index <
                period + 1
            )
                return 1;


            double average =
                0;


            for (
                int i =
                    index - period;
                i < index;
                i++
            )
            {
                average +=
                    bars.TickVolumes[i];
            }


            average /=
                period;


            if (
                average <= 0
            )
                return 1;


            return
                bars.TickVolumes[index] /
                average;
        }


        // =====================================================
        // MARKET STRUCTURE
        // =====================================================

        private StructureResult AnalyseStructure(
            Bars bars,
            int index
        )
        {
            List<double> highs =
                new List<double>();

            List<double> lows =
                new List<double>();


            int start =
                Math.Max(
                    3,
                    index - 100
                );


            for (
                int i = start;
                i < index - 2;
                i++
            )
            {
                bool swingHigh =
                    bars.HighPrices[i] >
                    bars.HighPrices[i - 1]
                    &&
                    bars.HighPrices[i] >
                    bars.HighPrices[i - 2]
                    &&
                    bars.HighPrices[i] >
                    bars.HighPrices[i + 1]
                    &&
                    bars.HighPrices[i] >
                    bars.HighPrices[i + 2];


                bool swingLow =
                    bars.LowPrices[i] <
                    bars.LowPrices[i - 1]
                    &&
                    bars.LowPrices[i] <
                    bars.LowPrices[i - 2]
                    &&
                    bars.LowPrices[i] <
                    bars.LowPrices[i + 1]
                    &&
                    bars.LowPrices[i] <
                    bars.LowPrices[i + 2];


                if (swingHigh)
                {
                    highs.Add(
                        bars.HighPrices[i]
                    );
                }


                if (swingLow)
                {
                    lows.Add(
                        bars.LowPrices[i]
                    );
                }
            }


            string trend =
                "NEUTRAL";

            string bos =
                "NONE";


            double? lastHigh =
                highs.Count > 0
                ?
                highs[
                    highs.Count - 1
                ]
                :
                (double?)null;


            double? lastLow =
                lows.Count > 0
                ?
                lows[
                    lows.Count - 1
                ]
                :
                (double?)null;


            if (
                highs.Count >= 2 &&
                lows.Count >= 2
            )
            {
                double previousHigh =
                    highs[
                        highs.Count - 2
                    ];


                double currentHigh =
                    highs[
                        highs.Count - 1
                    ];


                double previousLow =
                    lows[
                        lows.Count - 2
                    ];


                double currentLow =
                    lows[
                        lows.Count - 1
                    ];


                if (
                    currentHigh >
                    previousHigh
                    &&
                    currentLow >
                    previousLow
                )
                    trend =
                        "BULLISH";


                else if (
                    currentHigh <
                    previousHigh
                    &&
                    currentLow <
                    previousLow
                )
                    trend =
                        "BEARISH";


                else
                    trend =
                        "RANGING";


                double close =
                    bars.ClosePrices[index];


                if (
                    close >
                    currentHigh
                )
                    bos =
                        "BULLISH BOS";


                else if (
                    close <
                    currentLow
                )
                    bos =
                        "BEARISH BOS";
            }


            return new StructureResult
            {
                Trend =
                    trend,

                Bos =
                    bos,

                LastHigh =
                    lastHigh,

                LastLow =
                    lastLow
            };
        }


        // =====================================================
        // BREAKOUT
        // =====================================================

        private string AnalyseBreakout(
            Bars bars,
            int index,
            int lookback
        )
        {
            if (
                index <
                lookback
            )
                return "NONE";


            double highest =
                double.MinValue;

            double lowest =
                double.MaxValue;


            for (
                int i =
                    index - lookback;
                i < index;
                i++
            )
            {
                highest =
                    Math.Max(
                        highest,
                        bars.HighPrices[i]
                    );


                lowest =
                    Math.Min(
                        lowest,
                        bars.LowPrices[i]
                    );
            }


            double close =
                bars.ClosePrices[index];


            if (
                close >
                highest
            )
                return
                    "BULLISH BREAKOUT";


            if (
                close <
                lowest
            )
                return
                    "BEARISH BREAKOUT";


            return "NONE";
        }


        // =====================================================
        // FALSE BREAKOUT
        // =====================================================

        private string FalseBreakout(
            Bars bars,
            int index,
            int lookback
        )
        {
            if (
                index <
                lookback + 2
            )
                return "NONE";


            double highest =
                double.MinValue;

            double lowest =
                double.MaxValue;


            for (
                int i =
                    index -
                    lookback -
                    1;
                i < index - 1;
                i++
            )
            {
                highest =
                    Math.Max(
                        highest,
                        bars.HighPrices[i]
                    );


                lowest =
                    Math.Min(
                        lowest,
                        bars.LowPrices[i]
                    );
            }


            if (
                bars.HighPrices[
                    index - 1
                ]
                >
                highest
                &&
                bars.ClosePrices[index]
                <
                highest
            )
                return
                    "BULLISH TRAP";


            if (
                bars.LowPrices[
                    index - 1
                ]
                <
                lowest
                &&
                bars.ClosePrices[index]
                >
                lowest
            )
                return
                    "BEARISH TRAP";


            return "NONE";
        }


        // =====================================================
        // CANDLE PATTERN
        // =====================================================

        private string CandlePattern(
            Bars bars,
            int index
        )
        {
            if (
                index < 1
            )
                return "NONE";


            double open =
                bars.OpenPrices[index];

            double close =
                bars.ClosePrices[index];

            double high =
                bars.HighPrices[index];

            double low =
                bars.LowPrices[index];


            double previousOpen =
                bars.OpenPrices[
                    index - 1
                ];

            double previousClose =
                bars.ClosePrices[
                    index - 1
                ];


            // Bullish engulfing

            if (
                previousClose <
                previousOpen
                &&
                close >
                open
                &&
                close >=
                previousOpen
                &&
                open <=
                previousClose
            )
                return
                    "BULLISH ENGULFING";


            // Bearish engulfing

            if (
                previousClose >
                previousOpen
                &&
                close <
                open
                &&
                close <=
                previousOpen
                &&
                open >=
                previousClose
            )
                return
                    "BEARISH ENGULFING";


            double body =
                Math.Abs(
                    close -
                    open
                );


            double lowerWick =
                Math.Min(
                    open,
                    close
                )
                -
                low;


            double upperWick =
                high
                -
                Math.Max(
                    open,
                    close
                );


            if (
                body > 0 &&
                lowerWick >
                body * 2
            )
                return
                    "BULLISH REJECTION";


            if (
                body > 0 &&
                upperWick >
                body * 2
            )
                return
                    "BEARISH REJECTION";


            return "NONE";
        }


        // =====================================================
        // GET CLOSES
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


            List<double> values =
                new List<double>();


            for (
                int i = start;
                i <= index;
                i++
            )
            {
                values.Add(
                    bars.ClosePrices[i]
                );
            }


            return
                values.ToArray();
        }


        // =====================================================
        // STOP
        // =====================================================

        protected override void OnStop()
        {
            Positions.Closed -=
                OnPositionClosed;

            Timer.Stop();

            Print(
                "XAUUSD bot stopped."
            );
        }


        // =====================================================
        // DATA CLASSES
        // =====================================================

        private class TfAnalysis
        {
            public double Price;

            public double Ema9;

            public double Ema20;

            public double Ema50;

            public double Ema200;

            public double Rsi;

            public double MacdHistogram;

            public double Atr;

            public double Momentum;

            public double Volatility;

            public double Vwap;

            public double RelativeVolume;

            public StructureResult Structure;

            public string Breakout;

            public string FalseBreakout;

            public string CandlePattern;

            public string Trend;

            public string Regime;

            public int BullPoints;

            public int BearPoints;
        }


        private class StructureResult
        {
            public string Trend;

            public string Bos;

            public double? LastHigh;

            public double? LastLow;
        }


        private class SignalData
        {
            public string Signal;

            public int BuyScore;

            public int SellScore;

            public int RequiredScore;

            public int Confidence;
        }


        private class TradeLevels
        {
            public double? Entry;

            public double? StopLoss;

            public double? TakeProfit;

            public double? Tp1;

            public double? Tp2;

            public double RiskDistance;
        }
    }
}