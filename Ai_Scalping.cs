using cAlgo.API;
using System.Linq;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class ThreeCandleBot : Robot
    {
        [Parameter("Volume (Lots)", DefaultValue = 0.01)]
        public double Lots { get; set; }

        [Parameter("Stop Loss (Pips)", DefaultValue = 20)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit (Pips)", DefaultValue = 10)]
        public double TakeProfitPips { get; set; }

        [Parameter("Close At Any Profit", DefaultValue = false)]
        public bool CloseAtAnyProfit { get; set; }

        private const string Label = "ThreeCandleBot";

        protected override void OnStart()
        {
            Print("3 Candle Bot Started");
        }

        protected override void OnBar()
        {
            if (Bars.Count < 5)
                return;

            // Previous 3 COMPLETED candles
            bool candle1Bullish =
                Bars.ClosePrices.Last(1) > Bars.OpenPrices.Last(1);

            bool candle2Bullish =
                Bars.ClosePrices.Last(2) > Bars.OpenPrices.Last(2);

            bool candle3Bullish =
                Bars.ClosePrices.Last(3) > Bars.OpenPrices.Last(3);


            bool candle1Bearish =
                Bars.ClosePrices.Last(1) < Bars.OpenPrices.Last(1);

            bool candle2Bearish =
                Bars.ClosePrices.Last(2) < Bars.OpenPrices.Last(2);

            bool candle3Bearish =
                Bars.ClosePrices.Last(3) < Bars.OpenPrices.Last(3);


            bool threeBullish =
                candle1Bullish &&
                candle2Bullish &&
                candle3Bullish;

            bool threeBearish =
                candle1Bearish &&
                candle2Bearish &&
                candle3Bearish;


            // ==========================
            // 3 BULLISH = BUY
            // ==========================

            if (threeBullish)
            {
                ClosePositions(TradeType.Sell);

                if (!HasPosition(TradeType.Buy))
                {
                    OpenTrade(TradeType.Buy);

                    Print("BUY: Previous 3 candles were bullish.");
                }
            }


            // ==========================
            // 3 BEARISH = SELL
            // ==========================

            else if (threeBearish)
            {
                ClosePositions(TradeType.Buy);

                if (!HasPosition(TradeType.Sell))
                {
                    OpenTrade(TradeType.Sell);

                    Print("SELL: Previous 3 candles were bearish.");
                }
            }
        }


        protected override void OnTick()
        {
            // Optional:
            // close position as soon as it becomes profitable

            if (!CloseAtAnyProfit)
                return;

            foreach (var position in Positions
                         .Where(p => p.Label == Label &&
                                     p.SymbolName == SymbolName)
                         .ToArray())
            {
                if (position.NetProfit > 0)
                {
                    ClosePosition(position);

                    Print(
                        "Closed in profit: {0}",
                        position.NetProfit
                    );
                }
            }
        }


        private void OpenTrade(TradeType tradeType)
        {
            double volume =
                Symbol.QuantityToVolumeInUnits(Lots);

            volume =
                Symbol.NormalizeVolumeInUnits(
                    volume,
                    RoundingMode.Down
                );

            ExecuteMarketOrder(
                tradeType,
                SymbolName,
                volume,
                Label,
                StopLossPips,
                TakeProfitPips
            );
        }


        private bool HasPosition(TradeType tradeType)
        {
            return Positions.Any(
                p =>
                    p.Label == Label &&
                    p.SymbolName == SymbolName &&
                    p.TradeType == tradeType
            );
        }


        private void ClosePositions(TradeType tradeType)
        {
            foreach (var position in Positions
                         .Where(
                             p =>
                                 p.Label == Label &&
                                 p.SymbolName == SymbolName &&
                                 p.TradeType == tradeType
                         )
                         .ToArray())
            {
                ClosePosition(position);
            }
        }
    }
}