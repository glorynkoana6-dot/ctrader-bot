using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class XAUUSD_Video_Stop_Ladder : Robot
    {
        private const string Label = "XAU_VIDEO_LADDER";

        // =====================================================
        // SETTINGS
        // =====================================================

        [Parameter("Enable Trading", DefaultValue = false)]
        public bool EnableTrading { get; set; }

        [Parameter("Lot Size", DefaultValue = 0.01, MinValue = 0.01)]
        public double LotSize { get; set; }

        // 30 BUY + 30 SELL = 60 pending orders
        [Parameter("Stops Per Side", DefaultValue = 30, MinValue = 15, MaxValue = 100)]
        public int StopsPerSide { get; set; }

        // First order sits close to current market
        [Parameter("First Stop Distance", DefaultValue = 0.30, MinValue = 0.01)]
        public double FirstStopDistance { get; set; }

        // Distance between each ladder level
        [Parameter("Stop Spacing", DefaultValue = 0.20, MinValue = 0.01)]
        public double StopSpacing { get; set; }

        // Close as soon as net profit is above this
        [Parameter("Minimum Net Profit", DefaultValue = 0.00, MinValue = 0)]
        public double MinimumNetProfit { get; set; }

        [Parameter("Maximum Spread Pips", DefaultValue = 100, MinValue = 0)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("Cancel Orders When Bot Stops", DefaultValue = true)]
        public bool CancelOrdersWhenBotStops { get; set; }

        // =====================================================
        // INTERNAL STATE
        // =====================================================

        private double _volume;

        private bool _cycleTriggered;
        private bool _buildingCycle;

        // =====================================================
        // START
        // =====================================================

        protected override void OnStart()
        {
            string cleanSymbol =
                SymbolName
                .ToUpperInvariant()
                .Replace("/", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("_", "")
                .Replace(" ", "");

            bool gold =
                cleanSymbol.Contains("XAUUSD") ||
                cleanSymbol.Contains("XAU") ||
                cleanSymbol.Contains("GOLD");

            if (!gold)
            {
                Print("ERROR: RUN THIS BOT ON XAUUSD / GOLD.");
                Stop();
                return;
            }

            // Convert 0.01 lots into broker volume units
            _volume =
                Symbol.QuantityToVolumeInUnits(
                    LotSize
                );

            _volume =
                Symbol.NormalizeVolumeInUnits(
                    _volume,
                    RoundingMode.Down
                );

            _volume =
                Math.Max(
                    _volume,
                    Symbol.VolumeInUnitsMin
                );

            _volume =
                Math.Min(
                    _volume,
                    Symbol.VolumeInUnitsMax
                );

            Positions.Opened += OnPositionOpened;
            Positions.Closed += OnPositionClosed;

            // Run every second
            Timer.Start(1);

            Print("================================================");
            Print("XAUUSD VIDEO STYLE STOP LADDER");
            Print("BUY STOPS: {0}", StopsPerSide);
            Print("SELL STOPS: {0}", StopsPerSide);
            Print("TOTAL STOPS: {0}", StopsPerSide * 2);
            Print("LOT SIZE: {0}", LotSize);
            Print("FIRST DISTANCE: {0}", FirstStopDistance);
            Print("SPACING: {0}", StopSpacing);
            Print("EXIT: ANY POSITIVE NET PROFIT");
            Print("REFRESH: EVERY SECOND");
            Print("================================================");

            if (EnableTrading)
            {
                StartNewCycle();
            }
        }

        // =====================================================
        // EVERY SECOND
        // =====================================================

        protected override void OnTimer()
        {
            if (!EnableTrading)
                return;

            // First try to close triggered trades
            CloseProfitablePositions();

            Position[] positions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );

            // =================================================
            // THERE IS A LIVE POSITION
            // =================================================

            if (positions.Length > 0)
            {
                _cycleTriggered = true;

                // Video-style behavior:
                // once something triggers,
                // remove remaining pending stops.
                CancelAllPendingOrders();

                return;
            }

            // =================================================
            // TRIGGERED POSITION HAS NOW CLOSED
            // =================================================

            if (_cycleTriggered)
            {
                CancelAllPendingOrders();

                _cycleTriggered = false;

                StartNewCycle();

                return;
            }

            // =================================================
            // CHECK GRID STILL EXISTS
            // =================================================

            int pendingCount =
                PendingOrders.Count(
                    order =>
                        order.Label == Label &&
                        order.SymbolName == SymbolName
                );

            int required =
                StopsPerSide * 2;

            if (
                pendingCount < required &&
                !_buildingCycle
            )
            {
                CancelAllPendingOrders();

                StartNewCycle();
            }
        }

        // =====================================================
        // BUILD BUY + SELL LADDER
        // =====================================================

        private void StartNewCycle()
        {
            if (_buildingCycle)
                return;

            if (
                Positions.FindAll(
                    Label,
                    SymbolName
                ).Length > 0
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
            {
                Print(
                    "SPREAD TOO HIGH: {0:F2}",
                    spreadPips
                );

                return;
            }

            _buildingCycle = true;

            CancelAllPendingOrders();

            double baseBuy =
                Symbol.Ask +
                FirstStopDistance;

            double baseSell =
                Symbol.Bid -
                FirstStopDistance;

            int buyPlaced = 0;
            int sellPlaced = 0;

            Print("");
            Print("NEW LADDER");
            Print("BID: {0}", Symbol.Bid);
            Print("ASK: {0}", Symbol.Ask);

            // =================================================
            // BUY STOP LADDER ABOVE MARKET
            // =================================================

            for (
                int i = 0;
                i < StopsPerSide;
                i++
            )
            {
                double targetPrice =
                    baseBuy +
                    (
                        i *
                        StopSpacing
                    );

                targetPrice =
                    NormalizePrice(
                        targetPrice
                    );

                if (
                    targetPrice <=
                    Symbol.Ask
                )
                    continue;

                TradeResult result =
                    PlaceStopOrder(
                        TradeType.Buy,
                        SymbolName,
                        _volume,
                        targetPrice,
                        Label
                    );

                if (result.IsSuccessful)
                {
                    buyPlaced++;

                    Print(
                        "BUY STOP #{0} | {1} | {2} LOT",
                        i + 1,
                        targetPrice,
                        LotSize
                    );
                }
                else
                {
                    Print(
                        "BUY STOP #{0} FAILED | {1}",
                        i + 1,
                        result.Error
                    );
                }
            }

            // =================================================
            // SELL STOP LADDER BELOW MARKET
            // =================================================

            for (
                int i = 0;
                i < StopsPerSide;
                i++
            )
            {
                double targetPrice =
                    baseSell -
                    (
                        i *
                        StopSpacing
                    );

                targetPrice =
                    NormalizePrice(
                        targetPrice
                    );

                if (
                    targetPrice >=
                    Symbol.Bid
                )
                    continue;

                TradeResult result =
                    PlaceStopOrder(
                        TradeType.Sell,
                        SymbolName,
                        _volume,
                        targetPrice,
                        Label
                    );

                if (result.IsSuccessful)
                {
                    sellPlaced++;

                    Print(
                        "SELL STOP #{0} | {1} | {2} LOT",
                        i + 1,
                        targetPrice,
                        LotSize
                    );
                }
                else
                {
                    Print(
                        "SELL STOP #{0} FAILED | {1}",
                        i + 1,
                        result.Error
                    );
                }
            }

            _buildingCycle = false;

            Print("");
            Print(
                "LADDER READY | BUY {0}/{1} | SELL {2}/{3}",
                buyPlaced,
                StopsPerSide,
                sellPlaced,
                StopsPerSide
            );

            Print("");
        }

        // =====================================================
        // WHEN A STOP GETS TRIGGERED
        // =====================================================

        private void OnPositionOpened(
            PositionOpenedEventArgs args
        )
        {
            Position position =
                args.Position;

            if (
                position.Label != Label ||
                position.SymbolName != SymbolName
            )
                return;

            _cycleTriggered = true;

            Print("");
            Print(
                "TRIGGERED | {0} | ID {1} | ENTRY {2}",
                position.TradeType,
                position.Id,
                position.EntryPrice
            );

            // Remove all remaining BUY/SELL stops
            CancelAllPendingOrders();
        }

        // =====================================================
        // CLOSE AT ANY POSITIVE PROFIT
        // =====================================================

        private void CloseProfitablePositions()
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
                // NetProfit includes trading costs
                if (
                    position.NetProfit <=
                    MinimumNetProfit
                )
                    continue;

                TradeResult result =
                    ClosePosition(
                        position
                    );

                if (result.IsSuccessful)
                {
                    Print(
                        "PROFIT CLOSED | {0} | ID {1} | NET {2:F2} | PIPS {3:F2}",
                        position.TradeType,
                        position.Id,
                        position.NetProfit,
                        position.Pips
                    );
                }
                else
                {
                    Print(
                        "CLOSE FAILED | ID {0} | {1}",
                        position.Id,
                        result.Error
                    );
                }
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

            Print(
                "CLOSED | {0} | ID {1} | NET {2:F2}",
                position.TradeType,
                position.Id,
                position.NetProfit
            );

            // Timer will detect that all positions are closed
            // and rebuild a completely new ladder.
        }

        // =====================================================
        // CANCEL EVERY PENDING ORDER FROM THIS BOT
        // =====================================================

        private void CancelAllPendingOrders()
        {
            PendingOrder[] orders =
                PendingOrders
                .Where(
                    order =>
                        order.Label == Label &&
                        order.SymbolName == SymbolName
                )
                .ToArray();

            foreach (
                PendingOrder order
                in orders
            )
            {
                CancelPendingOrder(
                    order
                );
            }
        }

        // =====================================================
        // PRICE NORMALIZATION
        // =====================================================

        private double NormalizePrice(
            double price
        )
        {
            double ticks =
                Math.Round(
                    price /
                    Symbol.TickSize
                );

            return Math.Round(
                ticks *
                Symbol.TickSize,
                Symbol.Digits
            );
        }

        // =====================================================
        // STOP BOT
        // =====================================================

        protected override void OnStop()
        {
            Timer.Stop();

            Positions.Opened -=
                OnPositionOpened;

            Positions.Closed -=
                OnPositionClosed;

            if (
                CancelOrdersWhenBotStops
            )
            {
                CancelAllPendingOrders();
            }

            Print(
                "XAUUSD VIDEO LADDER BOT STOPPED"
            );
        }
    }
}