using System;
using System.Linq;
using cAlgo.API;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class XAUUSD_OneShot_Straddle : Robot
    {
        private const string Label = "XAU_STRADDLE";

        // =====================================================
        // SETTINGS
        // =====================================================

        [Parameter("Enable Trading", DefaultValue = false)]
        public bool EnableTrading { get; set; }

        [Parameter("Lot Size", DefaultValue = 0.01, MinValue = 0.01)]
        public double LotSize { get; set; }

        [Parameter("Entry Distance Price", DefaultValue = 0.50, MinValue = 0.01)]
        public double EntryDistancePrice { get; set; }

        [Parameter("Minimum Net Profit", DefaultValue = 0.01, MinValue = 0)]
        public double MinimumNetProfit { get; set; }

        [Parameter("Maximum Spread Pips", DefaultValue = 100, MinValue = 0)]
        public double MaximumSpreadPips { get; set; }

        [Parameter("Reposition Pending Orders", DefaultValue = true)]
        public bool RepositionPendingOrders { get; set; }

        [Parameter("Reposition Distance Price", DefaultValue = 0.25, MinValue = 0.01)]
        public double RepositionDistancePrice { get; set; }

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
            string symbol =
                SymbolName
                .ToUpperInvariant()
                .Replace("/", "")
                .Replace("-", "")
                .Replace(".", "")
                .Replace("_", "")
                .Replace(" ", "");

            bool gold =
                symbol.Contains("XAUUSD") ||
                symbol.Contains("XAU") ||
                symbol.Contains("GOLD");

            if (!gold)
            {
                Print("ERROR: XAUUSD / GOLD ONLY.");
                Stop();
                return;
            }


            // =================================================
            // 0.01 LOT -> BROKER VOLUME UNITS
            // =================================================

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


            // =================================================
            // EVENTS
            // =================================================

            Positions.Opened +=
                OnPositionOpened;

            Positions.Closed +=
                OnPositionClosed;


            // =================================================
            // RUN EVERY SECOND
            // =================================================

            Timer.Start(1);


            Print("==========================================");
            Print("XAUUSD BUY STOP / SELL STOP BOT");
            Print("LOT SIZE: {0}", LotSize);
            Print("CHECK SPEED: 1 SECOND");
            Print("BUY STOP ABOVE PRICE");
            Print("SELL STOP BELOW PRICE");
            Print("WHEN ONE TRIGGERS -> OTHER SIDE CANCELLED");
            Print("POSITION CLOSES AT POSITIVE NET PROFIT");
            Print("TRADING ENABLED: {0}", EnableTrading);
            Print("==========================================");


            if (EnableTrading)
            {
                RunEngine();
            }
        }


        // =====================================================
        // EVERY SECOND
        // =====================================================

        protected override void OnTimer()
        {
            if (!EnableTrading)
                return;

            RunEngine();
        }


        // =====================================================
        // MAIN ENGINE
        // =====================================================

        private void RunEngine()
        {
            // ---------------------------------------------
            // 1. CLOSE TRADES THAT ARE PROFITABLE
            // ---------------------------------------------

            CloseProfitablePositions();


            // ---------------------------------------------
            // 2. CHECK WHETHER A POSITION IS STILL OPEN
            // ---------------------------------------------

            Position[] positions =
                Positions.FindAll(
                    Label,
                    SymbolName
                );


            if (positions.Length > 0)
            {
                // A pending order has triggered.
                // Do not leave the opposite pending order active.

                CancelAllPendingOrders();

                return;
            }


            // ---------------------------------------------
            // 3. NO POSITION -> KEEP STRADDLE READY
            // ---------------------------------------------

            MaintainStraddle();
        }


        // =====================================================
        // CLOSE AT ANY POSITIVE NET PROFIT
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
        // KEEP ONE BUY STOP + ONE SELL STOP
        // =====================================================

        private void MaintainStraddle()
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


            double desiredBuyPrice =
                NormalizePrice(
                    Symbol.Ask +
                    EntryDistancePrice
                );


            double desiredSellPrice =
                NormalizePrice(
                    Symbol.Bid -
                    EntryDistancePrice
                );


            PendingOrder buyOrder =
                FindPendingOrder(
                    TradeType.Buy
                );


            PendingOrder sellOrder =
                FindPendingOrder(
                    TradeType.Sell
                );


            // =================================================
            // MOVE BUY STOP IF PRICE MOVED TOO FAR
            // =================================================

            if (
                buyOrder != null &&
                RepositionPendingOrders
            )
            {
                double difference =
                    Math.Abs(
                        buyOrder.TargetPrice -
                        desiredBuyPrice
                    );


                if (
                    difference >=
                    RepositionDistancePrice
                )
                {
                    CancelPendingOrder(
                        buyOrder
                    );

                    buyOrder = null;
                }
            }


            // =================================================
            // MOVE SELL STOP IF PRICE MOVED TOO FAR
            // =================================================

            if (
                sellOrder != null &&
                RepositionPendingOrders
            )
            {
                double difference =
                    Math.Abs(
                        sellOrder.TargetPrice -
                        desiredSellPrice
                    );


                if (
                    difference >=
                    RepositionDistancePrice
                )
                {
                    CancelPendingOrder(
                        sellOrder
                    );

                    sellOrder = null;
                }
            }


            // =================================================
            // CREATE BUY STOP
            // =================================================

            if (buyOrder == null)
            {
                PlaceBuyStop(
                    desiredBuyPrice
                );
            }


            // =================================================
            // CREATE SELL STOP
            // =================================================

            if (sellOrder == null)
            {
                PlaceSellStop(
                    desiredSellPrice
                );
            }
        }


        // =====================================================
        // BUY STOP
        // =====================================================

        private void PlaceBuyStop(
            double targetPrice
        )
        {
            if (
                targetPrice <=
                Symbol.Ask
            )
                return;


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
                Print(
                    "BUY STOP PLACED | {0} | LOT {1}",
                    targetPrice,
                    LotSize
                );
            }
            else
            {
                Print(
                    "BUY STOP FAILED | {0}",
                    result.Error
                );
            }
        }


        // =====================================================
        // SELL STOP
        // =====================================================

        private void PlaceSellStop(
            double targetPrice
        )
        {
            if (
                targetPrice >=
                Symbol.Bid
            )
                return;


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
                Print(
                    "SELL STOP PLACED | {0} | LOT {1}",
                    targetPrice,
                    LotSize
                );
            }
            else
            {
                Print(
                    "SELL STOP FAILED | {0}",
                    result.Error
                );
            }
        }


        // =====================================================
        // WHEN ONE SIDE TRIGGERS
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


            Print(
                "TRIGGERED | {0} | ENTRY {1} | LOT {2}",
                position.TradeType,
                position.EntryPrice,
                LotSize
            );


            // Once BUY or SELL activates,
            // remove the opposite side immediately.

            CancelAllPendingOrders();
        }


        // =====================================================
        // WHEN TRADE CLOSES
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
                "TRADE CLOSED | {0} | NET {1:F2}",
                position.TradeType,
                position.NetProfit
            );


            // Next 1-second cycle automatically creates
            // a fresh BUY STOP + SELL STOP pair.
        }


        // =====================================================
        // FIND PENDING ORDER
        // =====================================================

        private PendingOrder FindPendingOrder(
            TradeType tradeType
        )
        {
            return PendingOrders
                .FirstOrDefault(
                    order =>
                        order.Label == Label &&
                        order.SymbolName == SymbolName &&
                        order.TradeType == tradeType
                );
        }


        // =====================================================
        // CANCEL PENDING ORDERS
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
                TradeResult result =
                    CancelPendingOrder(
                        order
                    );


                if (result.IsSuccessful)
                {
                    Print(
                        "CANCELLED {0} STOP @ {1}",
                        order.TradeType,
                        order.TargetPrice
                    );
                }
            }
        }


        // =====================================================
        // NORMALIZE PRICE
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
                CancelOrdersWhenStopped
            )
            {
                CancelAllPendingOrders();
            }


            Print(
                "XAUUSD STRADDLE BOT STOPPED"
            );
        }
    }
}