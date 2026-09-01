using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class EURUSD_10S_30S_1M_HyperScalper : Robot
    {
        [Parameter("Enable Real Trading", DefaultValue = false)]
        public bool EnableRealTrading { get; set; }

        [Parameter("Volume Lots", DefaultValue = 0.01, MinValue = 0.01)]
        public double VolumeLots { get; set; }

        [Parameter("Maximum Trades Per Day", DefaultValue = 1000, MinValue = 1)]
        public int MaximumTradesPerDay { get; set; }

        [Parameter("Maximum Open Positions", DefaultValue = 50, MinValue = 1)]
        public int MaximumOpenPositions { get; set; }

        [Parameter("Orders Per Batch", DefaultValue = 3, MinValue = 1, MaxValue = 20)]
        public int OrdersPerBatch { get; set; }

        [Parameter("Minimum Batch Spacing Seconds", DefaultValue = 2, MinValue = 1)]
        public int MinimumBatchSpacingSeconds { get; set; }

        [Parameter("One Batch Per 10s Bar", DefaultValue = true)]
        public bool OneBatchPer10SecondBar { get; set; }

        [Parameter("Minimum Signal Score", DefaultValue = 70, MinValue = 1)]
        public int MinimumSignalScore { get; set; }

        [Parameter("Minimum Score Difference", DefaultValue = 12, MinValue = 1)]
        public int MinimumScoreDifference { get; set; }

        [Parameter("Close Profit Pips", DefaultValue = 1.0, MinValue = 0.1)]
        public double CloseProfitPips { get; set; }

        [Parameter("Minimum Net Profit", DefaultValue = 0.01, MinValue = 0)]
        public double MinimumNetProfit { get; set; }

        [Parameter("Maximum Hold Minutes", DefaultValue = 20, MinValue = 1)]
        public int MaximumHoldMinutes { get; set; }

        [Parameter("Emergency SL ATR", DefaultValue = 3.0, MinValue = 0.5)]
        public double EmergencyStopAtrMultiplier { get; set; }

        [Parameter("Minimum Emergency SL Pips", DefaultValue = 5, MinValue = 1)]
        public double MinimumEmergencyStopPips { get; set; }

        [Parameter("Maximum Spread Pips", DefaultValue = 2.0, MinValue = 0)]
        public double MaximumSpreadPips { get; set; }

        private const string Label = "EURUSD_10S_30S_HYPER";

        private Bars _m1;

        private SyntheticBar _current10s;
        private SyntheticBar _current30s;

        private readonly List<SyntheticBar> _bars10s =
            new List<SyntheticBar>();

        private readonly List<SyntheticBar> _bars30s =
            new List<SyntheticBar>();

        private DateTime _lastBatchTime = DateTime.MinValue;
        private DateTime _currentTradingDay;

        private DateTime _lastAnalysisTime = DateTime.MinValue;
        private DateTime _lastBatch10SecondBar = DateTime.MinValue;
        private DateTime _lastPrinted10SecondBar = DateTime.MinValue;

        private int _tradesToday;

        protected override void OnStart()
        {
            string cleanSymbol =
                SymbolName
                .ToUpperInvariant()
                .Replace("/", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("_", "");

            if (!cleanSymbol.Contains("EURUSD"))
            {
                Print("ERROR: EURUSD ONLY");
                Stop();
                return;
            }

            _m1 =
                MarketData.GetBars(
                    TimeFrame.Minute,
                    SymbolName
                );

            _currentTradingDay =
                Server.Time.Date;

            _tradesToday =
                CountTradesToday();

            Timer.Start(1);

            Print("==============================================");
            Print("EURUSD 10S / 30S / 1M HYPER SCALPER");
            Print("1M  = MAIN DIRECTION");
            Print("30S = CONFIRMATION");
            Print("10S = ENTRY");
            Print("LOTS = {0}", VolumeLots);
            Print("MAX DAILY = {0}", MaximumTradesPerDay);
            Print("MAX OPEN = {0}", MaximumOpenPositions);
            Print("REAL TRADING = {0}", EnableRealTrading);
            Print("==============================================");
        }

        protected override void OnTick()
        {
            ResetDailyCounter();

            double midPrice =
                (Symbol.Bid + Symbol.Ask) / 2.0;

            UpdateSyntheticBar(
                10,
                ref _current10s,
                _bars10s,
                Server.Time,
                midPrice
            );

            UpdateSyntheticBar(
                30,
                ref _current30s,
                _bars30s,
                Server.Time,
                midPrice
            );

            if (
                _lastAnalysisTime != DateTime.MinValue &&
                (Server.Time - _lastAnalysisTime)
                .TotalMilliseconds < 250
            )
                return;

            _lastAnalysisTime =
                Server.Time;

            ManageOpenPositions();

            AnalyseAndTrade();
        }

        protected override void OnTimer()
        {
            ResetDailyCounter();
            ManageOpenPositions();
        }

        private void UpdateSyntheticBar(
            int seconds,
            ref SyntheticBar current,
            List<SyntheticBar> completedBars,
            DateTime time,
            double price
        )
        {
            long intervalTicks =
                TimeSpan
                .FromSeconds(seconds)
                .Ticks;

            long bucketTicks =
                time.Ticks -
                (time.Ticks % intervalTicks);

            DateTime bucketStart =
                new DateTime(
                    bucketTicks,
                    time.Kind
                );

            if (current == null)
            {
                current =
                    CreateSyntheticBar(
                        bucketStart,
                        price
                    );

                return;
            }

            if (bucketStart > current.Start)
            {
                completedBars.Add(current);

                if (completedBars.Count > 300)
                    completedBars.RemoveAt(0);

                current =
                    CreateSyntheticBar(
                        bucketStart,
                        price
                    );

                return;
            }

            current.High =
                Math.Max(
                    current.High,
                    price
                );

            current.Low =
                Math.Min(
                    current.Low,
                    price
                );

            current.Close =
                price;

            current.Ticks++;
        }

        private SyntheticBar CreateSyntheticBar(
            DateTime start,
            double price
        )
        {
            return new SyntheticBar
            {
                Start = start,
                Open = price,
                High = price,
                Low = price,
                Close = price,
                Ticks = 1
            };
        }

        private void AnalyseAndTrade()
        {
            if (_m1 == null)
                return;

            if (_m1.Count < 70)
                return;

            if (
                _bars30s.Count < 2 ||
                _bars10s.Count < 4 ||
                _current30s == null ||
                _current10s == null
            )
                return;

            if (_tradesToday >= MaximumTradesPerDay)
                return;

            Position[] positions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );

            if (
                positions.Length >=
                MaximumOpenPositions
            )
                return;

            if (
                (Server.Time - _lastBatchTime)
                .TotalSeconds <
                MinimumBatchSpacingSeconds
            )
                return;

            if (
                OneBatchPer10SecondBar &&
                _lastBatch10SecondBar ==
                _current10s.Start
            )
                return;

            double spreadPips =
                (Symbol.Ask - Symbol.Bid) /
                Symbol.PipSize;

            if (
                spreadPips >
                MaximumSpreadPips
            )
                return;

            MainAnalysis m1 =
                AnalyseOneMinute();

            FastAnalysis s30 =
                AnalyseFastBars(
                    _bars30s,
                    _current30s,
                    8
                );

            FastAnalysis s10 =
                AnalyseFastBars(
                    _bars10s,
                    _current10s,
                    15
                );

            int buyScore = 0;
            int sellScore = 0;

            if (m1.Direction == "BUY")
                buyScore += 40;
            else if (m1.Direction == "SELL")
                sellScore += 40;

            if (s30.Direction == "BUY")
                buyScore += 30;
            else if (s30.Direction == "SELL")
                sellScore += 30;

            if (s10.Direction == "BUY")
                buyScore += 25;
            else if (s10.Direction == "SELL")
                sellScore += 25;

            if (s10.Breakout == "BUY")
                buyScore += 10;
            else if (s10.Breakout == "SELL")
                sellScore += 10;

            if (s30.Breakout == "BUY")
                buyScore += 5;
            else if (s30.Breakout == "SELL")
                sellScore += 5;

            if (
                m1.Direction == "BUY" &&
                s30.Direction == "BUY" &&
                s10.Direction == "BUY"
            )
            {
                buyScore += 15;
            }

            if (
                m1.Direction == "SELL" &&
                s30.Direction == "SELL" &&
                s10.Direction == "SELL"
            )
            {
                sellScore += 15;
            }

            double range10 =
                _current10s.High -
                _current10s.Low;

            if (range10 > 0)
            {
                double body10 =
                    _current10s.Close -
                    _current10s.Open;

                double strength10 =
                    Math.Abs(body10) /
                    range10;

                if (
                    body10 > 0 &&
                    strength10 >= 0.60
                )
                    buyScore += 8;

                else if (
                    body10 < 0 &&
                    strength10 >= 0.60
                )
                    sellScore += 8;
            }

            string signal =
                "WAIT";

            if (
                buyScore >= MinimumSignalScore &&
                buyScore >=
                sellScore +
                MinimumScoreDifference
            )
            {
                signal = "BUY";
            }

            else if (
                sellScore >= MinimumSignalScore &&
                sellScore >=
                buyScore +
                MinimumScoreDifference
            )
            {
                signal = "SELL";
            }

            if (
                _lastPrinted10SecondBar !=
                _current10s.Start
            )
            {
                _lastPrinted10SecondBar =
                    _current10s.Start;

                Print(
                    "1M {0} | 30S {1} | 10S {2} | BUY {3} | SELL {4} | {5} | OPEN {6}/{7} | DAY {8}/{9}",
                    m1.Direction,
                    s30.Direction,
                    s10.Direction,
                    buyScore,
                    sellScore,
                    signal,
                    positions.Length,
                    MaximumOpenPositions,
                    _tradesToday,
                    MaximumTradesPerDay
                );
            }

            if (signal == "WAIT")
                return;

            if (!EnableRealTrading)
            {
                Print(
                    "SIGNAL {0} - REAL TRADING OFF",
                    signal
                );

                return;
            }

            OpenBatch(
                signal,
                m1.Atr
            );
        }

        private MainAnalysis AnalyseOneMinute()
        {
            int index =
                _m1.Count - 2;

            double[] closes =
                GetCloses(
                    _m1,
                    index,
                    120
                );

            double current =
                closes[
                    closes.Length - 1
                ];

            double ema9 =
                EMA(closes, 9);

            double ema20 =
                EMA(closes, 20);

            double ema50 =
                EMA(closes, 50);

            double rsi =
                RSI(closes, 14);

            double momentum =
                Momentum(closes, 10);

            double atr =
                ATR(
                    _m1,
                    index,
                    14
                );

            int buy = 0;
            int sell = 0;

            if (ema9 > ema20)
                buy += 20;
            else
                sell += 20;

            if (ema20 > ema50)
                buy += 25;
            else
                sell += 25;

            if (current > ema20)
                buy += 15;
            else
                sell += 15;

            if (
                rsi >= 52 &&
                rsi <= 75
            )
                buy += 15;

            else if (
                rsi >= 25 &&
                rsi <= 48
            )
                sell += 15;

            if (momentum > 0)
                buy += 15;

            else if (momentum < 0)
                sell += 15;

            double candleOpen =
                _m1.OpenPrices[index];

            double candleClose =
                _m1.ClosePrices[index];

            if (candleClose > candleOpen)
                buy += 10;

            else if (candleClose < candleOpen)
                sell += 10;

            string direction =
                "NEUTRAL";

            if (buy >= sell + 15)
                direction = "BUY";

            else if (sell >= buy + 15)
                direction = "SELL";

            return new MainAnalysis
            {
                Direction = direction,
                BuyScore = buy,
                SellScore = sell,
                Rsi = rsi,
                Momentum = momentum,
                Atr = atr
            };
        }

        private FastAnalysis AnalyseFastBars(
            List<SyntheticBar> completed,
            SyntheticBar current,
            int lookback
        )
        {
            List<SyntheticBar> bars =
                new List<SyntheticBar>();

            int start =
                Math.Max(
                    0,
                    completed.Count -
                    lookback
                );

            for (
                int i = start;
                i < completed.Count;
                i++
            )
            {
                bars.Add(
                    completed[i]
                );
            }

            if (current != null)
                bars.Add(current);

            if (bars.Count < 3)
            {
                return new FastAnalysis
                {
                    Direction = "NEUTRAL",
                    Breakout = "NONE"
                };
            }

            int buy = 0;
            int sell = 0;

            SyntheticBar latest =
                bars[
                    bars.Count - 1
                ];

            int momentumIndex =
                Math.Max(
                    0,
                    bars.Count - 4
                );

            double oldClose =
                bars[
                    momentumIndex
                ].Close;

            double momentum =
                latest.Close -
                oldClose;

            if (momentum > 0)
                buy += 30;

            else if (momentum < 0)
                sell += 30;

            int averageStart =
                Math.Max(
                    0,
                    bars.Count - 5
                );

            double average = 0;
            int averageCount = 0;

            for (
                int i = averageStart;
                i < bars.Count;
                i++
            )
            {
                average +=
                    bars[i].Close;

                averageCount++;
            }

            if (averageCount > 0)
            {
                average /=
                    averageCount;

                if (latest.Close > average)
                    buy += 25;

                else if (latest.Close < average)
                    sell += 25;
            }

            double body =
                latest.Close -
                latest.Open;

            double range =
                latest.High -
                latest.Low;

            if (range > 0)
            {
                double strength =
                    Math.Abs(body) /
                    range;

                if (body > 0)
                {
                    buy += 10;

                    if (strength >= 0.60)
                        buy += 15;
                }

                else if (body < 0)
                {
                    sell += 10;

                    if (strength >= 0.60)
                        sell += 15;
                }
            }

            int bullish = 0;
            int bearish = 0;

            for (
                int i =
                    Math.Max(
                        0,
                        bars.Count - 3
                    );
                i < bars.Count;
                i++
            )
            {
                if (
                    bars[i].Close >
                    bars[i].Open
                )
                    bullish++;

                else if (
                    bars[i].Close <
                    bars[i].Open
                )
                    bearish++;
            }

            if (bullish >= 2)
                buy += 15;

            if (bearish >= 2)
                sell += 15;

            string breakout =
                "NONE";

            if (bars.Count >= 4)
            {
                double previousHigh =
                    double.MinValue;

                double previousLow =
                    double.MaxValue;

                int breakoutStart =
                    Math.Max(
                        0,
                        bars.Count - 6
                    );

                for (
                    int i = breakoutStart;
                    i < bars.Count - 1;
                    i++
                )
                {
                    previousHigh =
                        Math.Max(
                            previousHigh,
                            bars[i].High
                        );

                    previousLow =
                        Math.Min(
                            previousLow,
                            bars[i].Low
                        );
                }

                if (
                    latest.Close >
                    previousHigh
                )
                {
                    breakout = "BUY";
                    buy += 15;
                }

                else if (
                    latest.Close <
                    previousLow
                )
                {
                    breakout = "SELL";
                    sell += 15;
                }
            }

            string direction =
                "NEUTRAL";

            if (buy >= sell + 10)
                direction = "BUY";

            else if (sell >= buy + 10)
                direction = "SELL";

            return new FastAnalysis
            {
                Direction = direction,
                Breakout = breakout,
                BuyScore = buy,
                SellScore = sell
            };
        }

        private void OpenBatch(
            string signal,
            double atr
        )
        {
            if (atr <= 0)
                return;

            int open =
                Positions.FindAll(
                    Label,
                    SymbolName
                ).Length;

            int availableSlots =
                MaximumOpenPositions -
                open;

            int remainingDaily =
                MaximumTradesPerDay -
                _tradesToday;

            int orders =
                Math.Min(
                    OrdersPerBatch,
                    Math.Min(
                        availableSlots,
                        remainingDaily
                    )
                );

            if (orders <= 0)
                return;

            TradeType type =
                signal == "BUY"
                ? TradeType.Buy
                : TradeType.Sell;

            double emergencyPips =
                (
                    atr *
                    EmergencyStopAtrMultiplier
                )
                /
                Symbol.PipSize;

            emergencyPips =
                Math.Max(
                    emergencyPips,
                    MinimumEmergencyStopPips
                );

            double volume =
                Symbol.QuantityToVolumeInUnits(
                    VolumeLots
                );

            volume =
                Symbol.NormalizeVolumeInUnits(
                    volume,
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

            int opened = 0;

            for (
                int i = 0;
                i < orders;
                i++
            )
            {
                TradeResult result =
                    ExecuteMarketOrder(
                        type,
                        SymbolName,
                        volume,
                        Label,
                        emergencyPips,
                        null
                    );

                if (result.IsSuccessful)
                {
                    opened++;

                    _tradesToday++;

                    Print(
                        "{0} OPEN | ID {1} | ENTRY {2} | LOTS {3} | DAY {4}/{5}",
                        signal,
                        result.Position.Id,
                        result.Position.EntryPrice,
                        VolumeLots,
                        _tradesToday,
                        MaximumTradesPerDay
                    );
                }
                else
                {
                    Print(
                        "ORDER FAILED: {0}",
                        result.Error
                    );
                }

                if (
                    _tradesToday >=
                    MaximumTradesPerDay
                )
                    break;

                if (
                    Positions.FindAll(
                        Label,
                        SymbolName
                    ).Length >=
                    MaximumOpenPositions
                )
                    break;
            }

            if (opened > 0)
            {
                _lastBatchTime =
                    Server.Time;

                if (_current10s != null)
                {
                    _lastBatch10SecondBar =
                        _current10s.Start;
                }
            }
        }

        private void ManageOpenPositions()
        {
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
                    position.Pips >=
                    CloseProfitPips
                    &&
                    position.NetProfit >=
                    MinimumNetProfit
                )
                {
                    TradeResult close =
                        ClosePosition(
                            position
                        );

                    if (close.IsSuccessful)
                    {
                        Print(
                            "PROFIT CLOSED | ID {0} | PIPS {1:F2} | NET {2:F2}",
                            position.Id,
                            position.Pips,
                            position.NetProfit
                        );
                    }

                    continue;
                }

                double ageMinutes =
                    (
                        Server.Time -
                        position.EntryTime
                    ).TotalMinutes;

                if (
                    ageMinutes >=
                    MaximumHoldMinutes
                    &&
                    position.NetProfit >=
                    MinimumNetProfit
                )
                {
                    ClosePosition(
                        position
                    );
                }
            }
        }

        private double EMA(
            double[] values,
            int period
        )
        {
            if (values.Length < period)
                return values[values.Length - 1];

            double value =
                values
                .Take(period)
                .Average();

            double multiplier =
                2.0 /
                (period + 1);

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
                        (period - 1)
                        +
                        currentGain
                    )
                    /
                    period;

                loss =
                    (
                        loss *
                        (period - 1)
                        +
                        currentLoss
                    )
                    /
                    period;
            }

            if (loss == 0)
                return 100;

            double rs =
                gain / loss;

            return
                100 -
                (
                    100 /
                    (1 + rs)
                );
        }

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

            double previous =
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

            if (previous == 0)
                return 0;

            return
                (
                    (current - previous)
                    /
                    previous
                )
                *
                100;
        }

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

            List<double> ranges =
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
                        (period - 1)
                        +
                        ranges[i]
                    )
                    /
                    period;
            }

            return atr;
        }

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

            List<double> result =
                new List<double>();

            for (
                int i = start;
                i <= index;
                i++
            )
            {
                result.Add(
                    bars.ClosePrices[i]
                );
            }

            return result.ToArray();
        }

        private int CountTradesToday()
        {
            DateTime today =
                Server.Time.Date;

            int closed =
                History.Count(
                    trade =>
                        trade.Label ==
                        Label
                        &&
                        trade.SymbolName ==
                        SymbolName
                        &&
                        trade.EntryTime.Date ==
                        today
                );

            int open =
                Positions.Count(
                    position =>
                        position.Label ==
                        Label
                        &&
                        position.SymbolName ==
                        SymbolName
                        &&
                        position.EntryTime.Date ==
                        today
                );

            return closed + open;
        }

        private void ResetDailyCounter()
        {
            if (
                Server.Time.Date ==
                _currentTradingDay
            )
                return;

            _currentTradingDay =
                Server.Time.Date;

            _tradesToday =
                CountTradesToday();

            _lastBatchTime =
                DateTime.MinValue;

            _lastBatch10SecondBar =
                DateTime.MinValue;
        }

        protected override void OnStop()
        {
            Timer.Stop();

            Print(
                "EURUSD scalper stopped."
            );
        }

        private class SyntheticBar
        {
            public DateTime Start;
            public double Open;
            public double High;
            public double Low;
            public double Close;
            public int Ticks;
        }

        private class MainAnalysis
        {
            public string Direction;
            public int BuyScore;
            public int SellScore;
            public double Rsi;
            public double Momentum;
            public double Atr;
        }

        private class FastAnalysis
        {
            public string Direction;
            public string Breakout;
            public int BuyScore;
            public int SellScore;
        }
    }
}