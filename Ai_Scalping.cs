using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class XAUUSD_Hedge_Stop_Repeater : Robot
    {
        private const string Label = "XAU_HEDGE_REPEAT";

        [Parameter("Enable Trading", DefaultValue = false)]
        public bool EnableTrading { get; set; }

        [Parameter("Lot Size", DefaultValue = 0.01, MinValue = 0.01)]
        public double LotSize { get; set; }

        // 30 BUY + 30 SELL = 60 pending orders
        [Parameter("Stops Per Side", DefaultValue = 30, MinValue = 15, MaxValue = 100)]
        public int StopsPerSide { get; set; }

        // Distance from current market price
        [Parameter("Entry Distance Price", DefaultValue = 0.30, MinValue = 0.01)]
        public double EntryDistancePrice { get; set; }

        [Parameter("Minimum Net Profit", DefaultValue = 0.01, MinValue = 0)]
        public double MinimumNetProfit { get; set; }

        [Parameter("Maximum Spread Pips", DefaultValue = 100, MinValue = 0)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("Cancel Orders On Stop", DefaultValue = true)]
        public bool CancelOrdersOnStop { get; set; }

        private double _volume;

        private bool _cycleTriggered;
        private bool _creatingOrders;

        protected override void OnStart()
        {
            string clean =
                SymbolName
                .ToUpperInvariant()
                .Replace("/", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("_", "")
                .Replace(" ", "");

            bool isGold =
                clean.Contains("XAUUSD") ||
                clean.Contains("XAU") ||
                clean.Contains("GOLD");

            if (!isGold)
            {
                Print("ERROR: RUN THIS BOT ON XAUUSD / GOLD.");
                Stop();
                return;
            }

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

            Timer.Start(1);

            Print("=========================================");
            Print("XAUUSD HEDGE STOP REPEATER");
            Print("BUY STOPS: {0}", StopsPerSide);
            Print("SELL STOPS: {0}", StopsPerSide);
            Print("TOTAL STOPS: {0}", StopsPerSide * 2);
            Print("LOT SIZE: {0}", LotSize);
            Print("CLOSE: ANY POSITIVE NET PROFIT");
            Print("REPEAT: AUTOMATIC");
            Print("=========================================");

            if (EnableTrading)
                StartNewCycle();
        }

        protected override void OnTimer()
        {
            if (!EnableTrading)
                return;

            CloseProfitablePositions();

            Position[] positions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );

            // =============================================
            // ACTIVE TRADE CYCLE
            // =============================================

            if (positions.Length > 0)
            {
                _cycleTriggered = true;

                // Once something triggers, remove every
                // untriggered stop order.
                CancelAllPendingOrders();

                return;
            }

            // =============================================
            // PREVIOUS POSITIONS HAVE ALL CLOSED
            // =============================================

            if (_cycleTriggered)
            {
                CancelAllPendingOrders();

                _cycleTriggered = false;

                StartNewCycle();

                return;
            }

            // =============================================
            // MAKE SURE A FULL GRID EXISTS
            // =============================================

            int pending =
                PendingOrders.Count(
                    x =>
                        x.Label == Label &&
                        x.SymbolName == SymbolName
                );

            int required =
                StopsPerSide * 2;

            if (
                pending < required &&
                !_creatingOrders
            )
            {
                CancelAllPendingOrders();

                StartNewCycle();
            }
        }

        // =====================================================
        // START A NEW HEDGE CYCLE
        // =====================================================

        private void StartNewCycle()
        {
            if (_creatingOrders)
                return;

            if (
                Positions.FindAll(
                    Label,
                    SymbolName
                ).Length > 0
            )
                return;

            double spread =
                (
                    Symbol.Ask -
                    Symbol.Bid
                )
                /
                Symbol.PipSize;

            if (spread > MaximumSpreadPips)
            {
                Print(
                    "SPREAD TOO HIGH: {0:F2}",
                    spread
                );

                return;
            }

            _creatingOrders = true;

            CancelAllPendingOrders();

            double buyPrice =
                NormalizePrice(
                    Symbol.Ask +
                    EntryDistancePrice
                );

            double sellPrice =
                NormalizePrice(
                    Symbol.Bid -
                    EntryDistancePrice
                );

            Print("");
            Print("NEW HEDGE CYCLE");
            Print("CURRENT BID: {0}", Symbol.Bid);
            Print("CURRENT ASK: {0}", Symbol.Ask);
            Print("BUY STOP LEVEL: {0}", buyPrice);
            Print("SELL STOP LEVEL: {0}", sellPrice);

            int buyPlaced = 0;
            int sellPlaced = 0;

            // =================================================
            // 30 BUY STOPS AT SAME LEVEL
            // =================================================

            for (
                int i = 0;
                i < StopsPerSide;
                i++
            )
            {
                TradeResult result =
                    PlaceStopOrder(
                        TradeType.Buy,
                        SymbolName,
                        _volume,
                        buyPrice,
                        Label
                    );

                if (result.IsSuccessful)
                {
                    buyPlaced++;
                }
                else
                {
                    Print(
                        "BUY STOP #{0} FAILED: {1}",
                        i + 1,
                        result.Error
                    );
                }
            }

            // =================================================
            // 30 SELL STOPS AT SAME LEVEL
            // =================================================

            for (
                int i = 0;
                i < StopsPerSide;
                i++
            )
            {
                TradeResult result =
                    PlaceStopOrder(
                        TradeType.Sell,
                        SymbolName,
                        _volume,
                        sellPrice,
                        Label
                    );

                if (result.IsSuccessful)
                {
                    sellPlaced++;
                }
                else
                {
                    Print(
                        "SELL STOP #{0} FAILED: {1}",
                        i + 1,
                        result.Error
                    );
                }
            }

            _creatingOrders = false;

            Print(
                "READY | BUY STOPS {0} | SELL STOPS {1}",
                buyPlaced,
                sellPlaced
            );

            Print("");
        }

        // =====================================================
        // TRIGGER DETECTED
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

            Print(
                "TRIGGERED | {0} | ID {1} | ENTRY {2}",
                position.TradeType,
                position.Id,
                position.EntryPrice
            );

            // Remove opposite and remaining stops.
            CancelAllPendingOrders();
        }

        // =====================================================
        // CLOSE EVERY TRIGGERED POSITION AT ANY PROFIT
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

            // Timer checks whether all triggered positions
            // are gone. When they are, it immediately creates
            // a completely fresh BUY/SELL stop cycle.
        }

        // =====================================================
        // CANCEL ALL REMAINING STOPS
        // =====================================================

        private void CancelAllPendingOrders()
        {
            PendingOrder[] orders =
                PendingOrders
                .Where(
                    x =>
                        x.Label == Label &&
                        x.SymbolName == SymbolName
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
        // NORMALIZE PRICE TO BROKER TICK SIZE
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
        // STOP
        // =====================================================

        protected override void OnStop()
        {
            Timer.Stop();

            Positions.Opened -= OnPositionOpened;
            Positions.Closed -= OnPositionClosed;

            if (CancelOrdersOnStop)
                CancelAllPendingOrders();

            Print(
                "XAUUSD HEDGE STOP REPEATER STOPPED"
            );
        }
    }
}