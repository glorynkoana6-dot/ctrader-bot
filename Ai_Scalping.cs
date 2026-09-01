using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GoldAITradingBot : Robot
    {
        [Parameter("Enable Real Trading", DefaultValue = false)]
        public bool EnableRealTrading { get; set; }

        [Parameter("Volume In Units", DefaultValue = 1000, MinValue = 1)]
        public double VolumeInUnits { get; set; }

        [Parameter("Minimum Signal Score", DefaultValue = 70, MinValue = 50, MaxValue = 100)]
        public int MinimumSignalScore { get; set; }

        [Parameter("Risk Reward", DefaultValue = 2.0, MinValue = 0.5)]
        public double RiskReward { get; set; }

        [Parameter("SL ATR Multiplier", DefaultValue = 1.0, MinValue = 0.2)]
        public double StopAtrMultiplier { get; set; }

        [Parameter("Maximum Spread Pips", DefaultValue = 50, MinValue = 1)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("One Trade At A Time", DefaultValue = true)]
        public bool OneTradeAtATime { get; set; }

        [Parameter("Enable Trailing Stop", DefaultValue = true)]
        public bool EnableTrailingStop { get; set; }

        [Parameter("Breakeven Trigger R", DefaultValue = 1.0, MinValue = 0.2)]
        public double BreakevenTriggerR { get; set; }

        [Parameter("Trailing ATR Multiplier", DefaultValue = 1.0, MinValue = 0.2)]
        public double TrailingAtrMultiplier { get; set; }

        private const string BotLabel = "GOLD_AI_BOT";

        private Bars _m5;
        private Bars _m15;
        private Bars _h1;

        private ExponentialMovingAverage _m5Ema20;
        private ExponentialMovingAverage _m5Ema50;
        private ExponentialMovingAverage _m15Ema20;
        private ExponentialMovingAverage _m15Ema50;
        private ExponentialMovingAverage _h1Ema50;
        private ExponentialMovingAverage _h1Ema200;

        private RelativeStrengthIndex _m5Rsi;
        private MacdHistogram _m5Macd;
        private AverageTrueRange _m5Atr;

        private DateTime _lastProcessedCandle = DateTime.MinValue;

        protected override void OnStart()
        {
            string cleanSymbol = SymbolName
                .ToUpperInvariant()
                .Replace("/", "")
                .Replace("-", "");

            if (!cleanSymbol.Contains("XAU") || !cleanSymbol.Contains("USD"))
            {
                Print("ERROR: Attach this bot to an XAUUSD/Gold chart.");
                Stop();
                return;
            }

            _m5 = MarketData.GetBars(TimeFrame.Minute5, SymbolName);
            _m15 = MarketData.GetBars(TimeFrame.Minute15, SymbolName);
            _h1 = MarketData.GetBars(TimeFrame.Hour, SymbolName);

            _m5Ema20 = Indicators.ExponentialMovingAverage(_m5.ClosePrices, 20);
            _m5Ema50 = Indicators.ExponentialMovingAverage(_m5.ClosePrices, 50);

            _m15Ema20 = Indicators.ExponentialMovingAverage(_m15.ClosePrices, 20);
            _m15Ema50 = Indicators.ExponentialMovingAverage(_m15.ClosePrices, 50);

            _h1Ema50 = Indicators.ExponentialMovingAverage(_h1.ClosePrices, 50);
            _h1Ema200 = Indicators.ExponentialMovingAverage(_h1.ClosePrices, 200);

            _m5Rsi = Indicators.RelativeStrengthIndex(_m5.ClosePrices, 14);
            _m5Macd = Indicators.MacdHistogram(_m5.ClosePrices, 26, 12, 9);
            _m5Atr = Indicators.AverageTrueRange(_m5, 14, MovingAverageType.Exponential);

            Timer.Start(1);

            Chart.DrawStaticText(
                "BOT_STATUS",
                EnableRealTrading
                    ? "GOLD AI BOT\nREAL TRADING ENABLED"
                    : "GOLD AI BOT\nSIGNAL/DEMO MODE",
                VerticalAlignment.Bottom,
                HorizontalAlignment.Left,
                EnableRealTrading ? Color.Lime : Color.Yellow
            );

            Print("Gold AI Trading Bot started.");
            Print("Real trading: {0}", EnableRealTrading);
            Print("Minimum score: {0}", MinimumSignalScore);
        }

        protected override void OnTimer()
        {
            if (!HasEnoughData())
                return;

            int m5Index = _m5.Count - 2;
            DateTime candleTime = _m5.OpenTimes[m5Index];

            if (candleTime != _lastProcessedCandle)
            {
                _lastProcessedCandle = candleTime;
                AnalyseAndTrade(m5Index);
            }

            ManageTrailingStop();
        }

        private bool HasEnoughData()
        {
            return _m5 != null &&
                   _m15 != null &&
                   _h1 != null &&
                   _m5.Count >= 210 &&
                   _m15.Count >= 60 &&
                   _h1.Count >= 210;
        }

        private void AnalyseAndTrade(int m5Index)
        {
            int m15Index = _m15.Count - 2;
            int h1Index = _h1.Count - 2;

            int buyScore = 0;
            int sellScore = 0;

            double price = _m5.ClosePrices[m5Index];
            double atr = _m5Atr.Result[m5Index];
            double rsi = _m5Rsi.Result[m5Index];
            double macd = _m5Macd.Histogram[m5Index];

            // H1 main trend
            if (_h1Ema50.Result[h1Index] > _h1Ema200.Result[h1Index] &&
                _h1.ClosePrices[h1Index] > _h1Ema200.Result[h1Index])
            {
                buyScore += 25;
            }
            else if (_h1Ema50.Result[h1Index] < _h1Ema200.Result[h1Index] &&
                     _h1.ClosePrices[h1Index] < _h1Ema200.Result[h1Index])
            {
                sellScore += 25;
            }

            // M15 confirmation
            if (_m15Ema20.Result[m15Index] > _m15Ema50.Result[m15Index] &&
                _m15.ClosePrices[m15Index] > _m15Ema20.Result[m15Index])
            {
                buyScore += 20;
            }
            else if (_m15Ema20.Result[m15Index] < _m15Ema50.Result[m15Index] &&
                     _m15.ClosePrices[m15Index] < _m15Ema20.Result[m15Index])
            {
                sellScore += 20;
            }

            // M5 trend
            if (_m5Ema20.Result[m5Index] > _m5Ema50.Result[m5Index] &&
                price > _m5Ema20.Result[m5Index])
            {
                buyScore += 15;
            }
            else if (_m5Ema20.Result[m5Index] < _m5Ema50.Result[m5Index] &&
                     price < _m5Ema20.Result[m5Index])
            {
                sellScore += 15;
            }

            // RSI momentum
            if (rsi >= 52 && rsi <= 70)
                buyScore += 10;
            else if (rsi >= 30 && rsi <= 48)
                sellScore += 10;

            // MACD
            if (macd > 0)
                buyScore += 10;
            else if (macd < 0)
                sellScore += 10;

            // Three-candle confirmation
            if (ThreeBullishCandles(m5Index))
                buyScore += 15;

            if (ThreeBearishCandles(m5Index))
                sellScore += 15;

            // Breakout
            double previousHigh = HighestHigh(m5Index - 1, 20);
            double previousLow = LowestLow(m5Index - 1, 20);

            if (price > previousHigh)
                buyScore += 10;

            if (price < previousLow)
                sellScore += 10;

            string signal = "WAIT";
            int confidence = Math.Max(buyScore, sellScore);

            if (buyScore >= MinimumSignalScore && buyScore >= sellScore + 10)
                signal = "BUY";
            else if (sellScore >= MinimumSignalScore && sellScore >= buyScore + 10)
                signal = "SELL";

            DrawSignal(signal, buyScore, sellScore, confidence, price);

            Print(
                "{0} | Signal: {1} | Buy: {2} | Sell: {3} | RSI: {4:F1} | ATR: {5:F2}",
                _m5.OpenTimes[m5Index],
                signal,
                buyScore,
                sellScore,
                rsi,
                atr
            );

            if (signal == "WAIT" || atr <= 0)
                return;

            if (!EnableRealTrading)
            {
                Print("Signal detected, but real trading is disabled.");
                return;
            }

            if (OneTradeAtATime &&
                Positions.FindAll(BotLabel, SymbolName).Length > 0)
            {
                Print("Trade blocked: an existing bot position is open.");
                return;
            }

            double spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;

            if (spreadPips > MaximumSpreadPips)
            {
                Print("Trade blocked: spread is {0:F1} pips.", spreadPips);
                return;
            }

            ExecuteTrade(signal, atr);
        }

        private void ExecuteTrade(string signal, double atr)
        {
            TradeType tradeType =
                signal == "BUY" ? TradeType.Buy : TradeType.Sell;

            double stopLossPips = Math.Max(
                (atr * StopAtrMultiplier) / Symbol.PipSize,
                1
            );

            double takeProfitPips = Math.Max(
                stopLossPips * RiskReward,
                1
            );

            double volume = Symbol.NormalizeVolumeInUnits(
                VolumeInUnits,
                RoundingMode.Down
            );

            volume = Math.Max(volume, Symbol.VolumeInUnitsMin);
            volume = Math.Min(volume, Symbol.VolumeInUnitsMax);

            TradeResult result = ExecuteMarketOrder(
                tradeType,
                SymbolName,
                volume,
                BotLabel,
                stopLossPips,
                takeProfitPips
            );

            if (result.IsSuccessful)
            {
                Print(
                    "TRADE EXECUTED | {0} | Entry: {1} | SL: {2:F1} pips | TP: {3:F1} pips",
                    signal,
                    result.Position.EntryPrice,
                    stopLossPips,
                    takeProfitPips
                );
            }
            else
            {
                Print("ORDER FAILED: {0}", result.Error);
            }
        }

        private void ManageTrailingStop()
        {
            if (!EnableTrailingStop || _m5Atr == null || _m5.Count < 20)
                return;

            int index = _m5.Count - 2;
            double atr = _m5Atr.Result[index];

            if (atr <= 0)
                return;

            Position[] positions = Positions.FindAll(BotLabel, SymbolName);

            foreach (Position position in positions)
            {
                if (!position.StopLoss.HasValue)
                    continue;

                double initialRisk = Math.Abs(
                    position.EntryPrice - position.StopLoss.Value
                );

                if (initialRisk <= 0)
                    continue;

                if (position.TradeType == TradeType.Buy)
                {
                    double profitDistance = Symbol.Bid - position.EntryPrice;

                    if (profitDistance < initialRisk * BreakevenTriggerR)
                        continue;

                    double breakeven = position.EntryPrice + Symbol.PipSize;
                    double trailing = Symbol.Bid - atr * TrailingAtrMultiplier;
                    double newStop = Math.Max(breakeven, trailing);

                    if (newStop >= Symbol.Bid)
                        continue;

                    if (newStop > position.StopLoss.Value)
                        ModifyPosition(position, newStop, position.TakeProfit);
                }
                else
                {
                    double profitDistance = position.EntryPrice - Symbol.Ask;

                    if (profitDistance < initialRisk * BreakevenTriggerR)
                        continue;

                    double breakeven = position.EntryPrice - Symbol.PipSize;
                    double trailing = Symbol.Ask + atr * TrailingAtrMultiplier;
                    double newStop = Math.Min(breakeven, trailing);

                    if (newStop <= Symbol.Ask)
                        continue;

                    if (newStop < position.StopLoss.Value)
                        ModifyPosition(position, newStop, position.TakeProfit);
                }
            }
        }

        private bool ThreeBullishCandles(int index)
        {
            if (index < 2)
                return false;

            return IsBullish(index) &&
                   IsBullish(index - 1) &&
                   IsBullish(index - 2);
        }

        private bool ThreeBearishCandles(int index)
        {
            if (index < 2)
                return false;

            return IsBearish(index) &&
                   IsBearish(index - 1) &&
                   IsBearish(index - 2);
        }

        private bool IsBullish(int index)
        {
            return _m5.ClosePrices[index] > _m5.OpenPrices[index];
        }

        private bool IsBearish(int index)
        {
            return _m5.ClosePrices[index] < _m5.OpenPrices[index];
        }

        private double HighestHigh(int endIndex, int lookback)
        {
            double highest = double.MinValue;
            int start = Math.Max(0, endIndex - lookback + 1);

            for (int i = start; i <= endIndex; i++)
                highest = Math.Max(highest, _m5.HighPrices[i]);

            return highest;
        }

        private double LowestLow(int endIndex, int lookback)
        {
            double lowest = double.MaxValue;
            int start = Math.Max(0, endIndex - lookback + 1);

            for (int i = start; i <= endIndex; i++)
                lowest = Math.Min(lowest, _m5.LowPrices[i]);

            return lowest;
        }

        private void DrawSignal(
            string signal,
            int buyScore,
            int sellScore,
            int confidence,
            double price
        )
        {
            Color color = Color.Yellow;
            string heading = "WAIT";

            if (signal == "BUY")
            {
                color = Color.Lime;
                heading = "BULL BUY SIGNAL";
            }
            else if (signal == "SELL")
            {
                color = Color.Red;
                heading = "BEAR SELL SIGNAL";
            }

            Chart.DrawStaticText(
                "LIVE_SIGNAL",
                heading +
                "\nBUY SCORE: " + buyScore +
                "\nSELL SCORE: " + sellScore +
                "\nCONFIDENCE: " + confidence + "%" +
                "\nPRICE: " + price.ToString("F2"),
                VerticalAlignment.Top,
                HorizontalAlignment.Right,
                color
            );
        }

        protected override void OnStop()
        {
            Timer.Stop();
            Chart.RemoveObject("LIVE_SIGNAL");
            Chart.RemoveObject("BOT_STATUS");
            Print("Gold AI Trading Bot stopped.");
        }
    }
}