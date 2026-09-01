using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class XAUUSD_Spider_Grid_Bot : Robot
    {
        private const string Label = "XAUUSD_SPIDER_GRID";

        // =====================================================
        // SETTINGS
        // =====================================================

        [Parameter("Enable Trading", DefaultValue = false)]
        public bool EnableTrading { get; set; }

        [Parameter("Lot Size", DefaultValue = 0.01, MinValue = 0.01)]
        public double LotSize { get; set; }

        [Parameter("Grid Levels Per Side", DefaultValue = 20, MinValue = 1, MaxValue = 50)]
        public int GridLevelsPerSide { get; set; }

        // Gold price spacing:
        // 0.50 = orders every $0.50
        [Parameter("Grid Spacing Price", DefaultValue = 0.50, MinValue = 0.01)]
        public double GridSpacingPrice { get; set; }

        [Parameter("First Order Distance", DefaultValue = 0.30, MinValue = 0.01)]
        public double FirstOrderDistance { get; set; }

        [Parameter("Maximum Open Positions", DefaultValue = 40, MinValue = 1, MaxValue = 100)]
        public int MaximumOpenPositions { get; set; }

        [Parameter("Minimum Net Profit", DefaultValue = 0.01, MinValue = 0)]
        public double MinimumNetProfit { get; set; }

        [Parameter("Maximum Spread Pips", DefaultValue = 100, MinValue = 0)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("Recenter Grid", DefaultValue = true)]
        public bool RecenterGrid { get; set; }

        [Parameter("Cancel Orders When Stopped", DefaultValue = true)]
        public bool CancelOrdersWhenStopped { get; set; }


        // =====================================================
        // INTERNAL
        // =====================================================

        private double _volume;


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
                cleanSymbol.Contains("GOLD") ||
                cleanSymbol.Contains("XAU");

            if (!gold)
            {
                Print("ERROR: RUN THIS BOT ON XAUUSD / GOLD ONLY.");
                Stop();
                return;
            }

            _volume =
                Symbol.QuantityToVolumeInUnits(LotSize);

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

            Timer.Start(1);

            Print("==========================================");
            Print("XAUUSD SPIDER GRID STARTED");
            Print("LOT SIZE: {0}", LotSize);
            Print("BUY STOP LEVELS: {0}", GridLevelsPerSide);
            Print("SELL STOP LEVELS: {0}", GridLevelsPerSide);
            Print("GRID SPACING: ${0}", GridSpacingPrice);
            Print("CHECKING EVERY: 1 SECOND");
            Print("CLOSE: ANY POSITIVE NET PROFIT");
            Print("TRADING ENABLED: {0}", EnableTrading);
            Print("==========================================");

            if (EnableTrading)
            {
                CloseProfitablePositions();
                MaintainGrid();
            }
        }


        // =====================================================
        // EVERY SECOND
        // =====================================================

        protected override void OnTimer()
        {
            if (!EnableTrading)
                return;

            // First close profitable triggered positions.
            CloseProfitablePositions();

            // Then rebuild / maintain spider grid.
            MaintainGrid();
        }


        // =====================================================
        // CLOSE TRIGGERED POSITION AT ANY PROFIT
        // =====================================================

        private void CloseProfitablePositions()
        {
            Position[] positions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );

            foreach (Position position in positions)
            {
                if (
                    position.NetProfit <=
                    MinimumNetProfit
                )
                    continue;

                TradeResult result =
                    ClosePosition(position);

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
        // MAINTAIN BUY STOP + SELL STOP SPIDER
        // =====================================================

        private void MaintainGrid()
        {
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


            // =================================================
            // OPEN POSITION LIMIT
            // =================================================

            int openPositions =
                Positions.FindAll(
                    Label,
                    SymbolName
                ).Length;

            int remainingCapacity =
                MaximumOpenPositions -
                openPositions;

            if (remainingCapacity <= 0)
            {
                CancelAllPendingOrders();
                return;
            }


            // =================================================
            // HOW MANY PENDING ORDERS ARE ALLOWED
            // =================================================

            int desiredTotal =
                Math.Min(
                    GridLevelsPerSide * 2,
                    remainingCapacity
                );

            int desiredBuys =
                Math.Min(
                    GridLevelsPerSide,
                    (desiredTotal + 1) / 2
                );

            int desiredSells =
                Math.Min(
                    GridLevelsPerSide,
                    desiredTotal / 2
                );


            // =================================================
            // BUILD PRICE LADDER
            // =================================================

            List<double> buyLevels =
                BuildBuyLevels(
                    desiredBuys
                );

            List<double> sellLevels =
                BuildSellLevels(
                    desiredSells
                );


            // =================================================
            // RECENTER ORDERS LIKE THE SPIDER PICTURE
            // =================================================

            if (RecenterGrid)
            {
                RemoveOrdersOutsideGrid(
                    buyLevels,
                    sellLevels
                );
            }


            // =================================================
            // REPLACE MISSING ORDERS
            // =================================================

            AddMissingOrders(
                TradeType.Buy,
                buyLevels
            );

            AddMissingOrders(
                TradeType.Sell,
                sellLevels
            );
        }


        // =====================================================
        // BUY STOPS ABOVE CURRENT PRICE
        // =====================================================

        private List<double> BuildBuyLevels(
            int count
        )
        {
            List<double> levels =
                new List<double>();

            if (count <= 0)
                return levels;

            double first =
                Symbol.Ask +
                FirstOrderDistance;

            first =
                AlignUpToGrid(first);

            for (
                int i = 0;
                i < count;
                i++
            )
            {
                double target =
                    first +
                    (
                        GridSpacingPrice *
                        i
                    );

                levels.Add(
                    NormalizePrice(target)
                );
            }

            return levels;
        }


        // =====================================================
        // SELL STOPS BELOW CURRENT PRICE
        // =====================================================

        private List<double> BuildSellLevels(
            int count
        )
        {
            List<double> levels =
                new List<double>();

            if (count <= 0)
                return levels;

            double first =
                Symbol.Bid -
                FirstOrderDistance;

            first =
                AlignDownToGrid(first);

            for (
                int i = 0;
                i < count;
                i++
            )
            {
                double target =
                    first -
                    (
                        GridSpacingPrice *
                        i
                    );

                levels.Add(
                    NormalizePrice(target)
                );
            }

            return levels;
        }


        // =====================================================
        // ADD MISSING PENDING ORDERS
        // =====================================================

        private void AddMissingOrders(
            TradeType tradeType,
            List<double> levels
        )
        {
            foreach (
                double targetPrice
                in levels
            )
            {
                if (
                    PendingExists(
                        tradeType,
                        targetPrice
                    )
                )
                    continue;


                int openCount =
                    Positions.FindAll(
                        Label,
                        SymbolName
                    ).Length;


                int pendingCount =
                    PendingOrders.Count(
                        order =>
                            order.Label == Label &&
                            order.SymbolName == SymbolName
                    );


                if (
                    openCount +
                    pendingCount >=
                    MaximumOpenPositions
                )
                    return;


                // BUY STOP must remain above Ask.
                if (
                    tradeType ==
                    TradeType.Buy &&
                    targetPrice <=
                    Symbol.Ask
                )
                    continue;


                // SELL STOP must remain below Bid.
                if (
                    tradeType ==
                    TradeType.Sell &&
                    targetPrice >=
                    Symbol.Bid
                )
                    continue;


                TradeResult result =
                    PlaceStopOrder(
                        tradeType,
                        SymbolName,
                        _volume,
                        targetPrice,
                        Label
                    );


                if (result.IsSuccessful)
                {
                    Print(
                        "{0} STOP | {1} | LOT {2}",
                        tradeType,
                        targetPrice,
                        LotSize
                    );
                }
                else
                {
                    Print(
                        "{0} STOP FAILED @ {1} | {2}",
                        tradeType,
                        targetPrice,
                        result.Error
                    );
                }
            }
        }


        // =====================================================
        // REMOVE OLD ORDERS WHEN PRICE MOVES
        // =====================================================

        private void RemoveOrdersOutsideGrid(
            List<double> desiredBuys,
            List<double> desiredSells
        )
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
                List<double> desiredLevels =
                    order.TradeType ==
                    TradeType.Buy
                        ? desiredBuys
                        : desiredSells;


                bool shouldRemain =
                    desiredLevels.Any(
                        target =>
                            PricesMatch(
                                target,
                                order.TargetPrice
                            )
                    );


                if (shouldRemain)
                    continue;


                TradeResult result =
                    CancelPendingOrder(
                        order
                    );


                if (result.IsSuccessful)
                {
                    Print(
                        "GRID RECENTER | REMOVED {0} @ {1}",
                        order.TradeType,
                        order.TargetPrice
                    );
                }
            }
        }


        // =====================================================
        // DUPLICATE PROTECTION
        // =====================================================

        private bool PendingExists(
            TradeType tradeType,
            double target
        )
        {
            return PendingOrders.Any(
                order =>
                    order.Label == Label &&
                    order.SymbolName == SymbolName &&
                    order.TradeType == tradeType &&
                    PricesMatch(
                        order.TargetPrice,
                        target
                    )
            );
        }


        // =====================================================
        // PRICE MATCHING
        // =====================================================

        private bool PricesMatch(
            double a,
            double b
        )
        {
            double tolerance =
                Math.Max(
                    Symbol.TickSize,
                    GridSpacingPrice * 0.05
                );

            return
                Math.Abs(a - b) <=
                tolerance;
        }


        // =====================================================
        // GRID ALIGNMENT
        // =====================================================

        private double AlignUpToGrid(
            double price
        )
        {
            double aligned =
                Math.Ceiling(
                    price /
                    GridSpacingPrice
                )
                *
                GridSpacingPrice;

            if (
                aligned <=
                Symbol.Ask
            )
            {
                aligned +=
                    GridSpacingPrice;
            }

            return
                NormalizePrice(
                    aligned
                );
        }


        private double AlignDownToGrid(
            double price
        )
        {
            double aligned =
                Math.Floor(
                    price /
                    GridSpacingPrice
                )
                *
                GridSpacingPrice;

            if (
                aligned >=
                Symbol.Bid
            )
            {
                aligned -=
                    GridSpacingPrice;
            }

            return
                NormalizePrice(
                    aligned
                );
        }


        // =====================================================
        // NORMALIZE GOLD PRICE
        // =====================================================

        private double NormalizePrice(
            double price
        )
        {
            return Math.Round(
                price,
                Symbol.Digits
            );
        }


        // =====================================================
        // CANCEL ALL BOT PENDING ORDERS
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
        // STOP
        // =====================================================

        protected override void OnStop()
        {
            Timer.Stop();

            if (
                CancelOrdersWhenStopped
            )
            {
                CancelAllPendingOrders();
            }

            Print(
                "XAUUSD SPIDER GRID STOPPED"
            );
        }
    }
}