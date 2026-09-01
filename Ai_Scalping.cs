using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class USTEC_Spider_Grid_Bot : Robot
    {
        private const string Label = "USTEC_SPIDER";

        [Parameter("Enable Trading", DefaultValue = false)]
        public bool EnableTrading { get; set; }

        [Parameter("Lot Size", DefaultValue = 0.01, MinValue = 0.01)]
        public double LotSize { get; set; }

        [Parameter("Grid Levels Per Side", DefaultValue = 20, MinValue = 1, MaxValue = 50)]
        public int GridLevelsPerSide { get; set; }

        [Parameter("Grid Spacing Price", DefaultValue = 1.50, MinValue = 0.01)]
        public double GridSpacingPrice { get; set; }

        [Parameter("Entry Buffer Price", DefaultValue = 0.20, MinValue = 0)]
        public double EntryBufferPrice { get; set; }

        [Parameter("Maximum Open Positions", DefaultValue = 40, MinValue = 1)]
        public int MaximumOpenPositions { get; set; }

        [Parameter("Close At Any Net Profit", DefaultValue = true)]
        public bool CloseAtAnyProfit { get; set; }

        [Parameter("Minimum Net Profit", DefaultValue = 0.0, MinValue = 0)]
        public double MinimumNetProfit { get; set; }

        [Parameter("Maximum Spread Pips", DefaultValue = 100, MinValue = 0)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("Cancel Pending When Stopped", DefaultValue = true)]
        public bool CancelPendingWhenStopped { get; set; }

        private double _volume;

        protected override void OnStart()
        {
            string symbol =
                SymbolName
                .ToUpperInvariant()
                .Replace("/", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("_", "")
                .Replace(" ", "");

            bool validSymbol =
                symbol.Contains("USTEC") ||
                symbol.Contains("US100") ||
                symbol.Contains("NAS100") ||
                symbol.Contains("NASDAQ");

            if (!validSymbol)
            {
                Print("Run this cBot on USTEC / US100 / NAS100.");
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

            if (_volume < Symbol.VolumeInUnitsMin)
                _volume = Symbol.VolumeInUnitsMin;

            if (_volume > Symbol.VolumeInUnitsMax)
                _volume = Symbol.VolumeInUnitsMax;

            Timer.Start(1);

            Print("==========================================");
            Print("USTEC SPIDER GRID BOT");
            Print("LOT SIZE: {0}", LotSize);
            Print("GRID LEVELS: {0} BUY + {0} SELL", GridLevelsPerSide);
            Print("GRID SPACING: {0}", GridSpacingPrice);
            Print("CLOSE: ANY POSITIVE NET PROFIT");
            Print("CHECK SPEED: EVERY 1 SECOND");
            Print("TRADING ENABLED: {0}", EnableTrading);
            Print("==========================================");

            if (EnableTrading)
                MaintainGrid();
        }

        protected override void OnTimer()
        {
            if (!EnableTrading)
                return;

            CloseProfitablePositions();

            MaintainGrid();
        }

        // =====================================================
        // CLOSE EVERY POSITION AS SOON AS NET PROFIT > 0
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
                bool profitable;

                if (CloseAtAnyProfit)
                {
                    profitable =
                        position.NetProfit >
                        MinimumNetProfit;
                }
                else
                {
                    profitable = false;
                }

                if (!profitable)
                    continue;

                TradeResult result =
                    ClosePosition(
                        position
                    );

                if (result.IsSuccessful)
                {
                    Print(
                        "PROFIT CLOSED | ID {0} | NET {1:F2} | PIPS {2:F2}",
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
        // KEEP SPIDER GRID ACTIVE
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

            if (spreadPips > MaximumSpreadPips)
            {
                Print(
                    "SPREAD TOO HIGH: {0:F2}",
                    spreadPips
                );

                return;
            }

            Position[] openPositions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );

            int openCount =
                openPositions.Length;

            int availableCapacity =
                MaximumOpenPositions -
                openCount;

            if (availableCapacity <= 0)
            {
                CancelAllPendingOrders();

                return;
            }

            int maximumPending =
                Math.Min(
                    GridLevelsPerSide * 2,
                    availableCapacity
                );

            int desiredBuyCount =
                Math.Min(
                    GridLevelsPerSide,
                    (maximumPending + 1) / 2
                );

            int desiredSellCount =
                Math.Min(
                    GridLevelsPerSide,
                    maximumPending / 2
                );

            List<double> desiredBuys =
                BuildBuyLevels(
                    desiredBuyCount
                );

            List<double> desiredSells =
                BuildSellLevels(
                    desiredSellCount
                );

            RemoveUnwantedPendingOrders(
                desiredBuys,
                desiredSells
            );

            AddMissingOrders(
                TradeType.Buy,
                desiredBuys
            );

            AddMissingOrders(
                TradeType.Sell,
                desiredSells
            );
        }

        // =====================================================
        // BUY STOP LEVELS
        // =====================================================

        private List<double> BuildBuyLevels(
            int count
        )
        {
            List<double> levels =
                new List<double>();

            if (count <= 0)
                return levels;

            double minimumPrice =
                Symbol.Ask +
                EntryBufferPrice;

            double firstLevel =
                Math.Floor(
                    minimumPrice /
                    GridSpacingPrice
                )
                *
                GridSpacingPrice;

            if (firstLevel <= minimumPrice)
                firstLevel += GridSpacingPrice;

            for (int i = 0; i < count; i++)
            {
                double price =
                    firstLevel +
                    (
                        i *
                        GridSpacingPrice
                    );

                levels.Add(
                    NormalizePrice(
                        price
                    )
                );
            }

            return levels;
        }

        // =====================================================
        // SELL STOP LEVELS
        // =====================================================

        private List<double> BuildSellLevels(
            int count
        )
        {
            List<double> levels =
                new List<double>();

            if (count <= 0)
                return levels;

            double maximumPrice =
                Symbol.Bid -
                EntryBufferPrice;

            double firstLevel =
                Math.Ceiling(
                    maximumPrice /
                    GridSpacingPrice
                )
                *
                GridSpacingPrice;

            if (firstLevel >= maximumPrice)
                firstLevel -= GridSpacingPrice;

            for (int i = 0; i < count; i++)
            {
                double price =
                    firstLevel -
                    (
                        i *
                        GridSpacingPrice
                    );

                levels.Add(
                    NormalizePrice(
                        price
                    )
                );
            }

            return levels;
        }

        // =====================================================
        // REMOVE OLD GRID ORDERS
        // =====================================================

        private void RemoveUnwantedPendingOrders(
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

            foreach (PendingOrder order in orders)
            {
                List<double> desired =
                    order.TradeType == TradeType.Buy
                    ? desiredBuys
                    : desiredSells;

                bool keep =
                    desired.Any(
                        price =>
                            PricesMatch(
                                price,
                                order.TargetPrice
                            )
                    );

                if (keep)
                    continue;

                TradeResult result =
                    CancelPendingOrder(
                        order
                    );

                if (result.IsSuccessful)
                {
                    Print(
                        "REMOVED OLD {0} STOP @ {1}",
                        order.TradeType,
                        order.TargetPrice
                    );
                }
            }
        }

        // =====================================================
        // ADD MISSING BUY/SELL STOPS
        // =====================================================

        private void AddMissingOrders(
            TradeType tradeType,
            List<double> desiredLevels
        )
        {
            foreach (double targetPrice in desiredLevels)
            {
                if (
                    PendingExists(
                        tradeType,
                        targetPrice
                    )
                )
                    continue;

                if (
                    Positions.FindAll(
                        Label,
                        SymbolName
                    ).Length >=
                    MaximumOpenPositions
                )
                    return;

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
                        "{0} STOP PLACED | {1} | LOT {2}",
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
        // CHECK FOR DUPLICATE ORDER
        // =====================================================

        private bool PendingExists(
            TradeType tradeType,
            double targetPrice
        )
        {
            return PendingOrders.Any(
                order =>
                    order.Label == Label &&
                    order.SymbolName == SymbolName &&
                    order.TradeType == tradeType &&
                    PricesMatch(
                        order.TargetPrice,
                        targetPrice
                    )
            );
        }

        // =====================================================
        // PRICE COMPARISON
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
        // CANCEL BOT'S PENDING ORDERS
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

            foreach (PendingOrder order in orders)
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

            if (CancelPendingWhenStopped)
            {
                CancelAllPendingOrders();
            }

            Print(
                "USTEC SPIDER GRID BOT STOPPED"
            );
        }
    }
}